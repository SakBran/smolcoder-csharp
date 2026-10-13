using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Domain.Sessions;
using Smolcoder.Infrastructure.Persistence.Entities;

namespace Smolcoder.Infrastructure.Persistence;

public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly IDbContextFactory<SmolcoderDbContext> contextFactory;

    public WorkspaceRepository(IDbContextFactory<SmolcoderDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            List<WorkspaceEntity> rows = await context.Workspaces.AsNoTracking().ToListAsync(cancellationToken);
            return rows.Select(row => new Workspace(row.Path, row.AddedAt, row.LastOpened)).ToList();
        }
    }

    public async Task AddAsync(string path, CancellationToken cancellationToken)
    {
        string resolved = Resolve(path);
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            WorkspaceEntity? existing = await context.Workspaces.FindAsync(new object[] { KeyOf(resolved) }, cancellationToken);
            DateTime now = DateTime.UtcNow;
            if (existing == null)
            {
                WorkspaceEntity created = new WorkspaceEntity();
                created.Key = KeyOf(resolved);
                created.Path = resolved;
                created.AddedAt = now;
                created.LastOpened = now;
                context.Workspaces.Add(created);
            }
            else
            {
                existing.LastOpened = now;
            }
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task TouchAsync(string path, CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            WorkspaceEntity? existing = await context.Workspaces.FindAsync(new object[] { KeyOf(Resolve(path)) }, cancellationToken);
            if (existing != null)
            {
                existing.LastOpened = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task RemoveAsync(string path, CancellationToken cancellationToken)
    {
        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            WorkspaceEntity? existing = await context.Workspaces.FindAsync(new object[] { KeyOf(Resolve(path)) }, cancellationToken);
            if (existing != null)
            {
                context.Workspaces.Remove(existing);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// The full path without a trailing separator.
    /// </summary>
    public static string Resolve(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (full.Length > 1 && root != full)
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return full;
    }

    public static string KeyOf(string resolvedPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return resolvedPath.ToLowerInvariant();
        }
        return resolvedPath;
    }
}
