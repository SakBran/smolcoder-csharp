namespace Smolcoder.Web.Services;

/// <summary>
/// A folder chosen in the folder picker.
/// </summary>
public sealed class FolderPick
{
    public FolderPick(string path, bool startSession)
    {
        Path = path;
        StartSession = startSession;
    }

    public string Path { get; }

    public bool StartSession { get; }
}
