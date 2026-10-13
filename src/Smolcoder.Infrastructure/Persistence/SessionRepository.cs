using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Sessions;
using Smolcoder.Infrastructure.Persistence.Entities;

namespace Smolcoder.Infrastructure.Persistence;

public sealed class SessionRepository : ISessionRepository
{
    private readonly IDbContextFactory<SmolcoderDbContext> contextFactory;

    public SessionRepository(IDbContextFactory<SmolcoderDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<ChatSession>> ListAsync(CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            List<SessionEntity> rows = await context.Sessions.AsNoTracking().ToListAsync(cancellationToken);
            return rows.Select(ToDomain).ToList();
        }
    }

    public async Task<StoredSession?> LoadAsync(string sessionId, CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            SessionEntity? row = await context.Sessions.AsNoTracking().FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
            if (row == null)
            {
                return null;
            }
            List<string> events = await context.SessionEvents.AsNoTracking()
                .Where(sessionEvent => sessionEvent.SessionId == sessionId)
                .OrderBy(sessionEvent => sessionEvent.Sequence)
                .Select(sessionEvent => sessionEvent.Json)
                .ToListAsync(cancellationToken);
            return new StoredSession(ToDomain(row), MessageJson.DeserializeSnapshot(row.SnapshotJson), events);
        }
    }

    public async Task SaveAsync(StoredSession stored, CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken))
            {
                await SaveInContextAsync(context, stored, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
    }

    private static async Task SaveInContextAsync(SmolcoderDbContext context, StoredSession stored, CancellationToken cancellationToken)
    {
        {
            ChatSession session = stored.Session;
            SessionEntity? row = await context.Sessions.FirstOrDefaultAsync(existing => existing.Id == session.Id, cancellationToken);
            if (row == null)
            {
                row = new SessionEntity();
                row.Id = session.Id;
                context.Sessions.Add(row);
            }
            row.WorkspacePath = session.WorkspacePath;
            row.Title = session.Title;
            row.TitleByUser = session.TitleByUser;
            row.CreatedAt = session.CreatedAt;
            row.UpdatedAt = session.UpdatedAt;
            row.Mode = PermissionModes.ShortName(session.Mode);
            if (session.Effort.HasValue)
            {
                row.Effort = Efforts.Name(session.Effort);
            }
            else
            {
                row.Effort = null;
            }
            row.Model = session.Model;
            row.Backend = session.Backend;
            row.SnapshotJson = MessageJson.SerializeSnapshot(stored.Snapshot);

            await context.SessionEvents.Where(sessionEvent => sessionEvent.SessionId == session.Id).ExecuteDeleteAsync(cancellationToken);
            for (int index = 0; index < stored.Events.Count; index++)
            {
                SessionEventEntity sessionEvent = new SessionEventEntity();
                sessionEvent.SessionId = session.Id;
                sessionEvent.Sequence = index;
                sessionEvent.Json = stored.Events[index];
                context.SessionEvents.Add(sessionEvent);
            }
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RenameAsync(string sessionId, string title, CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            SessionEntity? row = await context.Sessions.FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
            if (row != null)
            {
                row.Title = title;
                row.TitleByUser = true;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await context.SessionEvents.Where(sessionEvent => sessionEvent.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);
            await context.Sessions.Where(session => session.Id == sessionId).ExecuteDeleteAsync(cancellationToken);
            await context.Uploads.Where(upload => upload.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);
        }
    }

    public async Task DeleteByWorkspaceAsync(string workspacePath, CancellationToken cancellationToken)
    {
        string resolved = WorkspaceRepository.Resolve(workspacePath);
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            List<string> ids = await context.Sessions
                .Where(session => session.WorkspacePath == resolved)
                .Select(session => session.Id)
                .ToListAsync(cancellationToken);
            foreach (string id in ids)
            {
                await context.SessionEvents.Where(sessionEvent => sessionEvent.SessionId == id).ExecuteDeleteAsync(cancellationToken);
                await context.Uploads.Where(upload => upload.SessionId == id).ExecuteDeleteAsync(cancellationToken);
            }
            await context.Sessions.Where(session => session.WorkspacePath == resolved).ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static ChatSession ToDomain(SessionEntity row)
    {
        ChatSession session = new ChatSession(row.Id, row.WorkspacePath, row.CreatedAt);
        session.Title = row.Title;
        session.TitleByUser = row.TitleByUser;
        session.UpdatedAt = row.UpdatedAt;
        PermissionMode mode;
        if (PermissionModes.TryParse(row.Mode, out mode))
        {
            session.Mode = mode;
        }
        Effort? effort;
        if (Efforts.TryParse(row.Effort, out effort))
        {
            session.Effort = effort;
        }
        session.Model = row.Model;
        session.Backend = row.Backend;
        return session;
    }
}
