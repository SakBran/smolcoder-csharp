namespace Smolcoder.Infrastructure.Persistence.Entities;

public sealed class WorkspaceEntity
{
    /// <summary>
    /// The resolved path, used as the key (lowercase on Windows).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; }

    public DateTime LastOpened { get; set; }
}
