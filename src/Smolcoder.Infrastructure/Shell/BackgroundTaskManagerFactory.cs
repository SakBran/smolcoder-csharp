using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Shell;

public sealed class BackgroundTaskManagerFactory : IBackgroundTaskManagerFactory
{
    private readonly IShellLocator shellLocator;

    public BackgroundTaskManagerFactory(IShellLocator shellLocator)
    {
        this.shellLocator = shellLocator;
    }

    public IBackgroundTaskManager Create(string workspaceRoot)
    {
        return new BackgroundTaskManager(shellLocator, workspaceRoot);
    }
}
