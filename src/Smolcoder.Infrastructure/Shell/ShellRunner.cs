using System.Globalization;
using System.Text;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Domain.Text;

namespace Smolcoder.Infrastructure.Shell;

/// <summary>
/// run_command: one command in the workspace, waited for, with a 120-second limit.
/// </summary>
public sealed class ShellRunner : IShellRunner
{
    private const int OutputCap = 8000;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private readonly IShellLocator shellLocator;

    public ShellRunner(IShellLocator shellLocator)
    {
        this.shellLocator = shellLocator;
    }

    public string Label
    {
        get
        {
            return shellLocator.Shell.Label;
        }
    }

    public async Task<string> RunCommandAsync(string command, string workspaceRoot, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return "Error: command cancelled before starting";
        }

        object outputGate = new object();
        StringBuilder output = new StringBuilder();
        Action<string> append = chunk =>
        {
            lock (outputGate)
            {
                output.Append(chunk);
                // Keep the end of a long log: failures usually show up there.
                if (output.Length > OutputCap * 4)
                {
                    string kept = TextTruncation.Middle(output.ToString(), OutputCap * 4);
                    output.Clear();
                    output.Append(kept);
                }
            }
        };

        long startedAt = Environment.TickCount64;
        ShellProcess process;
        try
        {
            process = ShellProcess.Start(shellLocator.Shell, command, workspaceRoot, append);
        }
        catch (Exception exception)
        {
            return "Error: could not start command: " + exception.Message;
        }

        using (process)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(DefaultTimeout))
            {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
                {
                    try
                    {
                        await process.WaitForExitAsync(linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        process.KillTree();
                        string captured = Snapshot(output, outputGate);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            string before = string.Empty;
                            if (captured.Trim().Length > 0)
                            {
                                before = TextTruncation.Middle(captured, OutputCap) + "\n";
                            }
                            return before + "[command cancelled by the user before it finished]";
                        }
                        return "Error: " + TextTruncation.Middle(captured, OutputCap)
                            + "\n[command timed out after " + (int)DefaultTimeout.TotalSeconds + "s and was killed. For tests/builds, isolate the stuck test or phase and inspect its loop or initialization before rerunning. For a persistent dev server, use task {\"action\": \"start\"}.]";
                    }
                }
            }

            string text = Snapshot(output, outputGate);
            int exitCode = process.ExitCode;
            string seconds = ((Environment.TickCount64 - startedAt) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);
            string body = "(no output)";
            if (text.Trim().Length > 0)
            {
                body = TextTruncation.Middle(text, OutputCap);
            }
            string prefix = string.Empty;
            if (exitCode != 0)
            {
                prefix = "Error: command exited with code " + exitCode + "\n";
            }
            return prefix + body + "\n[exit code " + exitCode + " in " + seconds + "s]";
        }
    }

    private static string Snapshot(StringBuilder output, object outputGate)
    {
        lock (outputGate)
        {
            return output.ToString();
        }
    }
}
