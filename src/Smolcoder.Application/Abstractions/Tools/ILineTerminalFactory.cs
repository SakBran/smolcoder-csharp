namespace Smolcoder.Application.Abstractions.Tools;

public interface ILineTerminalFactory
{
    /// <summary>
    /// Starts a terminal. onOutput gets text as it arrives; onDone gets the exit code and folder after each line.
    /// </summary>
    ILineTerminal Create(string id, string workingDirectory, Action<string> onOutput, Action<int, string> onDone);
}
