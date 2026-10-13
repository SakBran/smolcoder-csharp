namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// A folder in the "add workspace" picker.
/// </summary>
public sealed class FolderListing
{
    public FolderListing(string path, string display)
    {
        Path = path;
        Display = display;
        Folders = new List<FolderChoice>();
        Roots = new List<string>();
    }

    public string Path { get; }

    /// <summary>
    /// The path with the home folder shown as "~".
    /// </summary>
    public string Display { get; }

    public string? Parent { get; set; }

    public List<FolderChoice> Folders { get; }

    public List<string> Roots { get; }

    /// <summary>
    /// True when the folder looks like a project (has .git, package.json and so on).
    /// </summary>
    public bool IsProject { get; set; }

    public string? Problem { get; set; }
}
