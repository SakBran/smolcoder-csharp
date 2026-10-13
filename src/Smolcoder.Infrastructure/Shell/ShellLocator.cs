using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Smolcoder.Infrastructure.Shell;

/// <summary>
/// Picks the shell once. On Windows, models write POSIX commands, so Git Bash wins when it exists.
/// WSL's System32 bash is skipped because it sees a different file system.
/// </summary>
public sealed class ShellLocator : IShellLocator
{
    private readonly object gate = new object();
    private ShellInfo? cached;

    public ShellInfo Shell
    {
        get
        {
            lock (gate)
            {
                if (cached == null)
                {
                    cached = Pick();
                }
                return cached;
            }
        }
    }

    private static ShellInfo Pick()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(EnvironmentOr("ProgramFiles", "C:\\Program Files"), "Git", "bin", "bash.exe"));
            candidates.Add(Path.Combine(EnvironmentOr("ProgramFiles(x86)", "C:\\Program Files (x86)"), "Git", "bin", "bash.exe"));
            string localAppData = EnvironmentOr("LOCALAPPDATA", string.Empty);
            if (localAppData.Length > 0)
            {
                candidates.Add(Path.Combine(localAppData, "Programs", "Git", "bin", "bash.exe"));
            }
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Bash(candidate, "bash (Git Bash)");
                }
            }
            string? onPath = FindBashOnPath();
            if (onPath != null)
            {
                return Bash(onPath, "bash (Git Bash)");
            }
            return new ShellInfo("powershell.exe", new List<string> { "-NoProfile", "-NonInteractive", "-Command" }, "PowerShell", false);
        }

        if (File.Exists("/bin/bash"))
        {
            return Bash("/bin/bash", "bash");
        }
        return new ShellInfo("/bin/sh", new List<string> { "-lc" }, "sh", false);
    }

    private static ShellInfo Bash(string executable, string label)
    {
        return new ShellInfo(executable, new List<string> { "-o", "pipefail", "-lc" }, label, true);
    }

    private static string EnvironmentOr(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (value == null || value.Length == 0)
        {
            return fallback;
        }
        return value;
    }

    private static string? FindBashOnPath()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("where.exe");
            startInfo.ArgumentList.Add("bash");
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            using (Process? process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return null;
                }
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                if (process.ExitCode != 0)
                {
                    return null;
                }
                foreach (string line in output.Split('\n'))
                {
                    string path = line.Trim();
                    if (path.Length > 0 && !path.ToLowerInvariant().Contains("system32"))
                    {
                        return path;
                    }
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
        return null;
    }
}
