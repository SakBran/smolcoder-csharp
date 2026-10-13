using Microsoft.EntityFrameworkCore;
using Smolcoder.Infrastructure.Persistence.Entities;

namespace Smolcoder.Infrastructure.Persistence;

/// <summary>
/// The SQLite database at ~/.smolcoder/smolcoder.db.
/// </summary>
public sealed class SmolcoderDbContext : DbContext
{
    public SmolcoderDbContext(DbContextOptions<SmolcoderDbContext> options)
        : base(options)
    {
    }

    public DbSet<SettingEntity> Settings
    {
        get
        {
            return Set<SettingEntity>();
        }
    }

    public DbSet<WorkspaceEntity> Workspaces
    {
        get
        {
            return Set<WorkspaceEntity>();
        }
    }

    public DbSet<SessionEntity> Sessions
    {
        get
        {
            return Set<SessionEntity>();
        }
    }

    public DbSet<SessionEventEntity> SessionEvents
    {
        get
        {
            return Set<SessionEventEntity>();
        }
    }

    public DbSet<UploadEntity> Uploads
    {
        get
        {
            return Set<UploadEntity>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(setting => setting.Key);
        });

        modelBuilder.Entity<WorkspaceEntity>(entity =>
        {
            entity.ToTable("Workspaces");
            entity.HasKey(workspace => workspace.Key);
        });

        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(session => session.Id);
            entity.HasIndex(session => session.WorkspacePath);
            entity.HasMany(session => session.Events)
                .WithOne()
                .HasForeignKey(sessionEvent => sessionEvent.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionEventEntity>(entity =>
        {
            entity.ToTable("SessionEvents");
            entity.HasKey(sessionEvent => sessionEvent.Id);
            entity.HasIndex(sessionEvent => new { sessionEvent.SessionId, sessionEvent.Sequence });
        });

        modelBuilder.Entity<UploadEntity>(entity =>
        {
            entity.ToTable("Uploads");
            entity.HasKey(upload => new { upload.SessionId, upload.Id });
        });
    }
}
