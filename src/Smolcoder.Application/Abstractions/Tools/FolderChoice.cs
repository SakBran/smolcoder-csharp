namespace Smolcoder.Application.Abstractions.Tools;

public sealed class FolderChoice
{
    public FolderChoice(string name, string path, bool isProject)
    {
        Name = name;
        Path = path;
        IsProject = isProject;
    }

    public string Name { get; }

    public string Path { get; }

    public bool IsProject { get; }
}
