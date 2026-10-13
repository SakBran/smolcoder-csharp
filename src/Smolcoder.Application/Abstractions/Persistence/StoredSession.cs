using Smolcoder.Domain.Sessions;

namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// A saved session with its snapshot and the UI events to replay.
/// </summary>
public sealed class StoredSession
{
    public StoredSession(ChatSession session, SessionSnapshot snapshot, List<string> events)
    {
        Session = session;
        Snapshot = snapshot;
        Events = events;
    }

    public ChatSession Session { get; }

    public SessionSnapshot Snapshot { get; }

    /// <summary>
    /// UI events as JSON text, oldest first.
    /// </summary>
    public List<string> Events { get; }
}
