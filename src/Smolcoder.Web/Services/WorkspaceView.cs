namespace Smolcoder.Web.Services;

/// <summary>
/// A workspace in the sidebar with its sessions.
/// </summary>
public sealed class WorkspaceView
{
    public WorkspaceView(string path, DateTime lastActivity)
    {
        Path = path;
        LastActivity = lastActivity;
        Sessions = new List<SessionView>();
        string name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        if (name.Length == 0)
        {
            name = path;
        }
        Name = name;
    }

    public string Path { get; }

    public string Name { get; }

    public DateTime LastActivity { get; set; }

    public List<SessionView> Sessions { get; }
}
