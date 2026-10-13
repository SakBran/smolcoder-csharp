using Microsoft.EntityFrameworkCore;

namespace Smolcoder.Infrastructure.Persistence;

/// <summary>
/// Creates the data folder and applies database migrations.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<SmolcoderDbContext> contextFactory;

    public DatabaseInitializer(IDbContextFactory<SmolcoderDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        using (SmolcoderDbContext context = contextFactory.CreateDbContext())
        {
            context.Database.Migrate();
        }
    }
}
