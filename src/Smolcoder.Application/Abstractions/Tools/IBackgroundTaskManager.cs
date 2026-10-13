namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Long-running commands such as dev servers, for one workspace.
/// </summary>
public interface IBackgroundTaskManager
{
    Task<string> StartWithEarlyOutputAsync(string command, CancellationToken cancellationToken);

    string Logs(string taskId, int lines);

    string Stop(string taskId);

    string List();

    /// <summary>
    /// Local URLs printed by running tasks, newest first.
    /// </summary>
    IReadOnlyList<string> RecentUrls();

    int RunningCount { get; }

    void KillAll();
}
