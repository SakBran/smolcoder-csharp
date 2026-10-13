using System.Text;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Tools;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Cli.Headless;

/// <summary>
/// Output for headless runs (smol -p). The model's answer goes to stdout.
/// Tool lines, status and warnings go to stderr, so scripts get a clean answer.
/// </summary>
public sealed class ConsoleInteraction : IUserInteraction, IDisposable
{
    private readonly object gate = new object();
    private bool atLineStart = true;
    private Timer? heartbeat;

    public ConsoleInteraction()
    {
        Console.OutputEncoding = Encoding.UTF8;
    }

    public void ResetResponse()
    {
        Meta(AnsiColors.Gray("· the reply above was interrupted; retrying"));
    }

    public void Token(string text)
    {
        lock (gate)
        {
            StopSpinner();
            Console.Out.Write(text);
            Console.Out.Flush();
            atLineStart = text.EndsWith("\n", StringComparison.Ordinal);
        }
    }

    public void Thinking(string text)
    {
        // Headless runs hide reasoning. Automations want the answer.
    }

    public void ToolCall(string name, JsonObject arguments)
    {
        Meta(AnsiColors.Cyan("→") + " " + AnsiColors.Bold(name) + " " + AnsiColors.Dim(ToolCallSummary.Summarize(name, arguments)));
    }

    public void ToolResult(string result)
    {
        string[] lines = result.Split('\n');
        string firstLine = lines[0];
        bool isError = firstLine.StartsWith("Error", StringComparison.Ordinal);
        string label;
        if (isError)
        {
            label = AnsiColors.Red(Shorten(firstLine, 120));
        }
        else
        {
            string more = string.Empty;
            if (lines.Length > 1)
            {
                more = " (+" + (lines.Length - 1) + " lines)";
            }
            label = AnsiColors.Dim(Shorten(firstLine, 100) + more);
        }
        string mark = AnsiColors.Green("✓");
        if (isError)
        {
            mark = AnsiColors.Red("✗");
        }
        Meta("  " + mark + " " + label);
    }

    public void PrintLine(string text)
    {
        lock (gate)
        {
            StopSpinner();
            EnsureLineStart();
            Console.Out.WriteLine(text);
            atLineStart = true;
        }
    }

    public void Status(string text)
    {
        Meta(AnsiColors.Gray(text));
    }

    public void Warn(string text)
    {
        Meta(AnsiColors.Yellow(text));
    }

    public void Error(string text)
    {
        Meta(AnsiColors.Red(text));
    }

    /// <summary>
    /// A heartbeat on stderr every 15 seconds, so a log follower can tell "working" from "hung".
    /// </summary>
    public void StartSpinner(string label)
    {
        lock (gate)
        {
            StopSpinner();
            DateTime started = DateTime.UtcNow;
            heartbeat = new Timer(_ =>
            {
                long seconds = (long)Math.Round((DateTime.UtcNow - started).TotalSeconds);
                Console.Error.WriteLine(AnsiColors.Gray("· " + label + "… " + seconds + "s"));
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }
    }

    public void StopSpinner()
    {
        lock (gate)
        {
            if (heartbeat != null)
            {
                heartbeat.Dispose();
                heartbeat = null;
            }
        }
    }

    /// <summary>
    /// Headless runs are non-interactive, so the agent never asks. This is only a safe default.
    /// </summary>
    public Task<CommandApproval> ConfirmCommandAsync(string command, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(CommandApproval.No);
    }

    public void TurnEnd(string label)
    {
        Meta(AnsiColors.Dim("■ " + label));
    }

    public void PlanUpdated(Plan plan)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("  ").Append(AnsiColors.Bold("Plan")).Append(' ').Append(AnsiColors.Dim(plan.DoneCount + "/" + plan.Steps.Count));
        int current = plan.CurrentIndex;
        for (int index = 0; index < plan.Steps.Count; index++)
        {
            PlanStep step = plan.Steps[index];
            builder.Append('\n');
            if (step.Done)
            {
                builder.Append("  ").Append(AnsiColors.Dim("✔ " + step.Text));
            }
            else if (index == current)
            {
                builder.Append("  ").Append(AnsiColors.Cyan("▶ " + step.Text));
            }
            else
            {
                builder.Append("  ").Append(AnsiColors.Gray("○ " + step.Text));
            }
        }
        Meta(builder.ToString());
    }

    public void Dispose()
    {
        StopSpinner();
    }

    private void Meta(string text)
    {
        lock (gate)
        {
            StopSpinner();
            EnsureLineStart();
            Console.Error.WriteLine(text);
        }
    }

    private void EnsureLineStart()
    {
        if (!atLineStart)
        {
            Console.Out.WriteLine();
            atLineStart = true;
        }
    }

    private static string Shorten(string text, int maxChars)
    {
        if (text.Length > maxChars)
        {
            return text.Substring(0, maxChars);
        }
        return text;
    }
}
