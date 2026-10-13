namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// A shell for the web terminal panel. No PTY: it runs one line at a time,
/// keeps its working folder and environment between lines, and streams output back.
/// </summary>
public interface ILineTerminal : IDisposable
{
    string Id { get; }

    string WorkingDirectory { get; }

    /// <summary>
    /// Recent output, for showing again when a page reconnects.
    /// </summary>
    string Buffer { get; }

    void Write(string line);

    /// <summary>
    /// Kills what is running and starts a fresh shell in the last folder.
    /// </summary>
    void Interrupt();
}
