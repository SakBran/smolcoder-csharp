namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Reads project files the harness needs.
/// </summary>
public interface IProjectInspector
{
    /// <summary>
    /// The build and test command from package.json, or null when there is none.
    /// </summary>
    string? ProjectVerificationCommand(string workspaceRoot);

    /// <summary>
    /// AGENTS.md from the workspace root, capped at 8000 characters. Null when missing.
    /// </summary>
    string? LoadAgentsMd(string workspaceRoot);
}
