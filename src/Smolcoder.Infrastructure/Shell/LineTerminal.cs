using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Shell;

/// <summary>
/// A persistent shell that reads commands from a pipe. After every command a sentinel
/// reports the exit code and the folder. No TTY means interactive programs do not work.
/// </summary>
public sealed class LineTerminal : ILineTerminal
{
    private const char RecordSeparator = '\u001e';
    private const int BufferCap = 200000;
    private const int HoldCap = 600;

    private static readonly Regex Sentinel = new Regex("\u001e(-?\\d+)\u001e([^\u001e]*)\u001e\r?\n?", RegexOptions.Compiled);
    private static readonly Regex OscSequence = new Regex("\u001b\\][^\u0007\u001b]*(?:\u0007|\u001b\\\\)", RegexOptions.Compiled);
    private static readonly Regex NonColorCsi = new Regex("\u001b\\[[0-9;?]*[ -/]*[@-ln-~]", RegexOptions.Compiled);
    private static readonly Regex OtherEscape = new Regex("\u001b[^\\[\\]]", RegexOptions.Compiled);
    private static readonly Regex ControlCharacters = new Regex("[\u0000-\u0008\u000b\u000c\u000e-\u001a\u001c-\u001f]", RegexOptions.Compiled);
    private static readonly Regex MsysPath = new Regex("^/([a-zA-Z])(/.*)?$", RegexOptions.Compiled);

    private readonly object gate = new object();
    private readonly ShellInfo shell;
    private readonly bool powerShell;
    private readonly Action<string> onOutput;
    private readonly Action<int, string> onDone;
    private readonly StringBuilder buffer = new StringBuilder();
    private Process? process;
    private string pending = string.Empty;
    private bool interrupting;
    private bool closed;
    private DateTime spawnedAt;
    private int quickExits;

    public LineTerminal(ShellInfo shell, string id, string workingDirectory, Action<string> onOutput, Action<int, string> onDone)
    {
        this.shell = shell;
        this.onOutput = onOutput;
        this.onDone = onDone;
        Id = id;
        WorkingDirectory = workingDirectory;
        powerShell = shell.Executable.ToLowerInvariant().Contains("powershell") || shell.Executable.ToLowerInvariant().Contains("pwsh");
        Emit("\u001b[2m" + shell.Label + " · no TTY: interactive programs will not work · ctrl+c interrupts\u001b[0m\n");
        Spawn();
    }

    public string Id { get; }

    public string WorkingDirectory { get; private set; }

    public string Buffer
    {
        get
        {
            lock (gate)
            {
                return buffer.ToString();
            }
        }
    }

    public void Write(string line)
    {
        if (closed)
        {
            return;
        }
        string command = line.TrimEnd('\r', '\n');
        Emit("\u001b[36m❯\u001b[0m " + command + "\n");
        Process? current = process;
        if (current == null || current.HasExited)
        {
            Emit("\u001b[31m[no shell running]\u001b[0m\n");
            return;
        }
        string payload;
        if (powerShell)
        {
            payload = command + "\nWrite-Output ([string][char]0x1e + $(if ($?) {0} else {1}) + [char]0x1e + (Get-Location).Path + [char]0x1e)\n";
        }
        else
        {
            // The group redirect keeps a command that reads stdin from eating the next command.
            payload = "{\n" + command + "\n} </dev/null 2>&1\nprintf '\u001e%s\u001e%s\u001e\\n' \"$?\" \"$PWD\"\n";
        }
        try
        {
            current.StandardInput.Write(payload);
            current.StandardInput.Flush();
        }
        catch (Exception exception)
        {
            Emit("[could not write to the shell: " + exception.Message + "]\n");
        }
    }

    public void Interrupt()
    {
        if (closed)
        {
            return;
        }
        Process? current = process;
        if (current == null)
        {
            Spawn();
            return;
        }
        interrupting = true;
        KillTree(current);
    }

    public void Dispose()
    {
        closed = true;
        Process? current = process;
        process = null;
        if (current != null)
        {
            KillTree(current);
        }
    }

    private void Spawn()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(shell.Executable);
        if (powerShell)
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("-");
        }
        else
        {
            startInfo.ArgumentList.Add("-l");
        }
        startInfo.WorkingDirectory = WorkingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        startInfo.Environment["TERM"] = "dumb";

        Process started = new Process();
        started.StartInfo = startInfo;
        started.EnableRaisingEvents = true;
        spawnedAt = DateTime.UtcNow;
        try
        {
            started.Start();
        }
        catch (Exception exception)
        {
            Emit("[could not start " + shell.Label + ": " + exception.Message + "]\n");
            return;
        }
        process = started;
        Task stdout = PumpAsync(started.StandardOutput.BaseStream);
        Task stderr = PumpAsync(started.StandardError.BaseStream);
        _ = WatchAsync(started, stdout, stderr);
    }

    private async Task WatchAsync(Process started, Task stdout, Task stderr)
    {
        try
        {
            await started.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Continue with the restart logic.
        }
        int code = 0;
        try
        {
            code = started.ExitCode;
        }
        catch (Exception)
        {
            code = -1;
        }
        started.Dispose();
        lock (gate)
        {
            if (process != started)
            {
                return;
            }
            process = null;
            if (pending.Length > 0)
            {
                string rest = pending;
                pending = string.Empty;
                EmitLocked(rest);
            }
        }
        if (closed)
        {
            return;
        }
        if (interrupting)
        {
            interrupting = false;
            Emit("\n\u001b[33m[interrupted]\u001b[0m\n");
            Spawn();
            return;
        }
        // A shell that dies at once, twice, is not going to work. Stop.
        bool quick = (DateTime.UtcNow - spawnedAt).TotalMilliseconds < 1500;
        if (quick)
        {
            quickExits++;
        }
        else
        {
            quickExits = 0;
        }
        if (quickExits >= 2)
        {
            Emit("\n\u001b[31m[" + shell.Label + " keeps exiting (code " + code + ") — close this tab and open a new terminal]\u001b[0m\n");
            return;
        }
        Emit("\n\u001b[2m[shell exited with code " + code + " — starting a new one]\u001b[0m\n");
        Spawn();
    }

    private async Task PumpAsync(Stream stream)
    {
        byte[] bytes = new byte[8192];
        Decoder decoder = Encoding.UTF8.GetDecoder();
        char[] characters = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(bytes, 0, bytes.Length);
                if (read == 0)
                {
                    return;
                }
                int count = decoder.GetChars(bytes, 0, read, characters, 0);
                if (count > 0)
                {
                    Ingest(new string(characters, 0, count));
                }
            }
        }
        catch (Exception)
        {
            // The shell was killed.
        }
    }

    private void Ingest(string text)
    {
        List<KeyValuePair<int, string>> finished = new List<KeyValuePair<int, string>>();
        lock (gate)
        {
            pending = pending + text;
            while (true)
            {
                Match match = Sentinel.Match(pending);
                if (!match.Success)
                {
                    break;
                }
                string before = pending.Substring(0, match.Index);
                if (before.Length > 0)
                {
                    EmitLocked(before);
                }
                pending = pending.Substring(match.Index + match.Length);
                string folder = ToOsPath(match.Groups[2].Value.Trim());
                if (folder.Length == 0)
                {
                    folder = WorkingDirectory;
                }
                WorkingDirectory = folder;
                finished.Add(new KeyValuePair<int, string>(int.Parse(match.Groups[1].Value), folder));
            }
            // Hold back a partial sentinel, but never for long.
            int separator = pending.IndexOf(RecordSeparator);
            if (separator < 0)
            {
                if (pending.Length > 0)
                {
                    EmitLocked(pending);
                }
                pending = string.Empty;
            }
            else
            {
                if (separator > 0)
                {
                    EmitLocked(pending.Substring(0, separator));
                }
                pending = pending.Substring(separator);
                if (pending.Length > HoldCap)
                {
                    EmitLocked(pending);
                    pending = string.Empty;
                }
            }
        }
        foreach (KeyValuePair<int, string> done in finished)
        {
            onDone(done.Key, done.Value);
        }
    }

    private void Emit(string text)
    {
        lock (gate)
        {
            EmitLocked(text);
        }
    }

    private void EmitLocked(string text)
    {
        string clean = StripControl(text);
        if (clean.Length == 0)
        {
            return;
        }
        buffer.Append(clean);
        if (buffer.Length > BufferCap)
        {
            int keep = (int)Math.Floor(BufferCap * 0.8);
            buffer.Remove(0, buffer.Length - keep);
        }
        onOutput(clean);
    }

    /// <summary>
    /// Keeps color codes and carriage returns. Drops cursor moves, screen clears and titles.
    /// </summary>
    public static string StripControl(string text)
    {
        string result = OscSequence.Replace(text, string.Empty);
        result = NonColorCsi.Replace(result, string.Empty);
        result = OtherEscape.Replace(result, string.Empty);
        result = ControlCharacters.Replace(result, string.Empty);
        return result;
    }

    private static string ToOsPath(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Match match = MsysPath.Match(path);
            if (match.Success)
            {
                string rest = "/";
                if (match.Groups[2].Success)
                {
                    rest = match.Groups[2].Value;
                }
                return match.Groups[1].Value.ToUpperInvariant() + ":" + rest.Replace('/', '\\');
            }
        }
        return path;
    }

    private static void KillTree(Process target)
    {
        try
        {
            if (!target.HasExited)
            {
                target.Kill(true);
            }
        }
        catch (Exception)
        {
            // Already gone.
        }
    }
}
