namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Creates one task manager per session.
/// </summary>
public interface IBackgroundTaskManagerFactory
{
    IBackgroundTaskManager Create(string workspaceRoot);
}
