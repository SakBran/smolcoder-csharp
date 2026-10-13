using System.Diagnostics;
using System.Text;

namespace Smolcoder.Infrastructure.Shell;

/// <summary>
/// Starts a shell command with stdout and stderr captured, and kills it with all its children.
/// </summary>
public sealed class ShellProcess : IDisposable
{
    private readonly Process process;
    private readonly Task stdoutReader;
    private readonly Task stderrReader;

    private ShellProcess(Process process, Action<string> onOutput)
    {
        this.process = process;
        stdoutReader = PumpAsync(process.StandardOutput.BaseStream, onOutput);
        stderrReader = PumpAsync(process.StandardError.BaseStream, onOutput);
    }

    public int Id
    {
        get
        {
            return process.Id;
        }
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int ExitCode
    {
        get
        {
            return process.ExitCode;
        }
    }

    /// <summary>
    /// Starts the command. stdin is closed at once. Output chunks go to onOutput.
    /// </summary>
    public static ShellProcess Start(ShellInfo shell, string command, string workingDirectory, Action<string> onOutput)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(shell.Executable);
        foreach (string argument in shell.ArgumentsBeforeCommand)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add(ManagedCommand(shell, command));
        startInfo.WorkingDirectory = workingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;

        Process process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        process.StandardInput.Close();
        return new ShellProcess(process, onOutput);
    }

    /// <summary>
    /// Keeps bash alive while background jobs still own its output pipes.
    /// </summary>
    public static string ManagedCommand(ShellInfo shell, string command)
    {
        if (shell.IsBash)
        {
            return command + "\n__smol_command_status=$?\nwait\nexit \"$__smol_command_status\"";
        }
        return command;
    }

    /// <summary>
    /// Waits for the process to exit and for its output to end.
    /// </summary>
    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutReader, stderrReader).WaitAsync(cancellationToken);
    }

    public void KillTree()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception)
        {
            // Already gone.
        }
    }

    public void Dispose()
    {
        process.Dispose();
    }

    private static async Task PumpAsync(Stream stream, Action<string> onOutput)
    {
        byte[] buffer = new byte[8192];
        Decoder decoder = Encoding.UTF8.GetDecoder();
        char[] characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }
                int count = decoder.GetChars(buffer, 0, read, characters, 0);
                if (count > 0)
                {
                    onOutput(new string(characters, 0, count));
                }
            }
        }
        catch (Exception)
        {
            // The stream closed because the process was killed.
        }
    }
}
