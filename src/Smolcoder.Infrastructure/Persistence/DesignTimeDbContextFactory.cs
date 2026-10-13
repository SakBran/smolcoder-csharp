using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Smolcoder.Infrastructure.Persistence;

/// <summary>
/// Lets "dotnet ef" create migrations without starting the app.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmolcoderDbContext>
{
    public SmolcoderDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SmolcoderDbContext> builder = new DbContextOptionsBuilder<SmolcoderDbContext>();
        builder.UseSqlite("Data Source=" + AppPaths.DatabasePath);
        return new SmolcoderDbContext(builder.Options);
    }
}
