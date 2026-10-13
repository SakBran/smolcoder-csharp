using System.Diagnostics;
using System.Text;

namespace Smolcoder.Infrastructure.Checks;

/// <summary>
/// Runs a small helper program and captures its output.
/// </summary>
public static class ExternalTool
{
    public static ExternalToolResult? Run(string executable, IEnumerable<string> arguments, TimeSpan timeout)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(executable);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
            using (Process? process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return null;
                }
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Kill(true);
                    return null;
                }
                process.WaitForExit();
                return new ExternalToolResult(process.ExitCode, standardOutput.Result, standardError.Result);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
