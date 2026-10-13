namespace Smolcoder.Domain.Sessions;

/// <summary>
/// A project folder the user works in.
/// </summary>
public sealed class Workspace
{
    public Workspace(string path, DateTime addedAt, DateTime lastOpened)
    {
        Path = path;
        AddedAt = addedAt;
        LastOpened = lastOpened;
    }

    public string Path { get; set; }

    public DateTime AddedAt { get; set; }

    public DateTime LastOpened { get; set; }
}
