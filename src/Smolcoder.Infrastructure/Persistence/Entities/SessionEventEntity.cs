namespace Smolcoder.Infrastructure.Persistence.Entities;

public sealed class SessionEventEntity
{
    public long Id { get; set; }

    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Order within the session, starting at 0.
    /// </summary>
    public int Sequence { get; set; }

    public string Json { get; set; } = string.Empty;
}
