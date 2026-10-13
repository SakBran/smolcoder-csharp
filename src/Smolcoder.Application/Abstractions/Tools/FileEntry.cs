namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// One file or folder in a listing for the user interface.
/// </summary>
public sealed class FileEntry
{
    public FileEntry(string name, string relativePath, bool isDirectory)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
    }

    public string Name { get; }

    /// <summary>
    /// Path relative to the workspace, with "/" separators.
    /// </summary>
    public string RelativePath { get; }

    public bool IsDirectory { get; }
}
