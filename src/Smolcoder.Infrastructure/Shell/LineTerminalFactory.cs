using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Shell;

public sealed class LineTerminalFactory : ILineTerminalFactory
{
    private readonly IShellLocator shellLocator;

    public LineTerminalFactory(IShellLocator shellLocator)
    {
        this.shellLocator = shellLocator;
    }

    public ILineTerminal Create(string id, string workingDirectory, Action<string> onOutput, Action<int, string> onDone)
    {
        return new LineTerminal(shellLocator.Shell, id, workingDirectory, onOutput, onDone);
    }
}
