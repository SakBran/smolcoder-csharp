using Smolcoder.Domain.Sessions;

namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Saved sessions.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// The session list (without snapshots), for the sidebar.
    /// </summary>
    Task<IReadOnlyList<ChatSession>> ListAsync(CancellationToken cancellationToken);

    Task<StoredSession?> LoadAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the session, its snapshot and its events, replacing any earlier copy.
    /// </summary>
    Task SaveAsync(StoredSession session, CancellationToken cancellationToken);

    Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken);

    Task DeleteAsync(string sessionId, CancellationToken cancellationToken);

    Task DeleteByWorkspaceAsync(string workspacePath, CancellationToken cancellationToken);
}
