using System.Text;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Web.Services;

/// <summary>
/// One terminal tab and its output.
/// </summary>
public sealed class TerminalView : IDisposable
{
    private const int MaxChars = 200000;
    private readonly object gate = new object();
    private readonly StringBuilder output = new StringBuilder();
    private ILineTerminal? terminal;

    public TerminalView(string id)
    {
        Id = id;
        WorkingDirectory = string.Empty;
    }

    public string Id { get; }

    public string WorkingDirectory { get; set; }

    public int? LastExitCode { get; set; }

    public string Output
    {
        get
        {
            lock (gate)
            {
                return output.ToString();
            }
        }
    }

    public void Attach(ILineTerminal attached)
    {
        terminal = attached;
        WorkingDirectory = attached.WorkingDirectory;
    }

    public void Append(string text)
    {
        lock (gate)
        {
            output.Append(text);
            if (output.Length > MaxChars)
            {
                output.Remove(0, output.Length - (int)(MaxChars * 0.8));
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            output.Clear();
        }
    }

    public void Write(string line)
    {
        if (terminal != null)
        {
            terminal.Write(line);
        }
    }

    public void Interrupt()
    {
        if (terminal != null)
        {
            terminal.Interrupt();
        }
    }

    public void Dispose()
    {
        if (terminal != null)
        {
            terminal.Dispose();
            terminal = null;
        }
    }
}
