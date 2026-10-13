namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Read-only file browsing for the user interface: the file tree, the file viewer
/// and the folder picker. Workspace paths stay inside the workspace.
/// </summary>
public interface IWorkspaceBrowser
{
    IReadOnlyList<FileEntry> ListDirectory(string workspaceRoot, string relativePath);

    FileView ReadForView(string workspaceRoot, string relativePath);

    FolderListing BrowseFolders(string? path);

    /// <summary>
    /// When a workspace file was last written, or null when it does not exist.
    /// </summary>
    DateTime? LastWriteUtc(string workspaceRoot, string relativePath);
}
