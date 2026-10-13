namespace Smolcoder.Infrastructure.Persistence.Entities;

public sealed class SessionEntity
{
    public string Id { get; set; } = string.Empty;

    public string WorkspacePath { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool TitleByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string Mode { get; set; } = "edit";

    public string? Effort { get; set; }

    public string? Model { get; set; }

    public string? Backend { get; set; }

    /// <summary>
    /// The SessionSnapshot as JSON.
    /// </summary>
    public string SnapshotJson { get; set; } = "{}";

    public List<SessionEventEntity> Events { get; set; } = new List<SessionEventEntity>();
}
