namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Runs shell commands in the workspace.
/// </summary>
public interface IShellRunner
{
    /// <summary>
    /// Shell name for the system prompt, for example "bash".
    /// </summary>
    string Label { get; }

    /// <summary>
    /// Runs a command and waits up to 120 seconds.
    /// The result ends with "[exit code N in S.Ss]".
    /// </summary>
    Task<string> RunCommandAsync(string command, string workspaceRoot, CancellationToken cancellationToken);
}
