namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Keeps file access and commands inside the workspace.
/// </summary>
public interface IWorkspaceSandbox
{
    /// <summary>
    /// Returns the absolute path for a workspace-relative path.
    /// Throws SandboxException when the path leaves the workspace, symlinks included.
    /// </summary>
    string ResolveInWorkspace(string workspaceRoot, string? userPath);

    /// <summary>
    /// The workspace-relative path with "/" separators, or "." for the root.
    /// </summary>
    string RelativePath(string workspaceRoot, string absolutePath);

    /// <summary>
    /// Why a shell command reaches outside the workspace, or null when it stays inside.
    /// </summary>
    string? CommandEscapesWorkspace(string command, string workspaceRoot);
}
