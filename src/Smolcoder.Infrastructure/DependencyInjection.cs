using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Infrastructure.Attachments;
using Smolcoder.Infrastructure.Browsing;
using Smolcoder.Infrastructure.Checks;
using Smolcoder.Infrastructure.FileSystem;
using Smolcoder.Infrastructure.Persistence;
using Smolcoder.Infrastructure.Projects;
using Smolcoder.Infrastructure.Providers.DeepSeek;
using Smolcoder.Infrastructure.Sandbox;
using Smolcoder.Infrastructure.Shell;

namespace Smolcoder.Infrastructure;

/// <summary>
/// Registers the infrastructure adapters.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContextFactory<SmolcoderDbContext>(options =>
        {
            options.UseSqlite("Data Source=" + AppPaths.DatabasePath);
        });
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IWorkspaceRepository, WorkspaceRepository>();
        services.AddSingleton<IAttachmentStore, AttachmentStore>();

        services.AddHttpClient("deepseek", client =>
        {
            // Deadlines are handled per request (idle and total), not here.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IModelCatalog, DeepSeekModelCatalog>();

        services.AddSingleton<IWorkspaceSandbox, WorkspaceSandbox>();
        services.AddSingleton<IFileSystemTools, FileSystemTools>();
        services.AddSingleton<IShellLocator, ShellLocator>();
        services.AddSingleton<IShellRunner, ShellRunner>();
        services.AddSingleton<IBackgroundTaskManagerFactory, BackgroundTaskManagerFactory>();
        services.AddSingleton<ISyntaxChecker, SyntaxChecker>();
        services.AddSingleton<IProjectInspector, ProjectInspector>();
        services.AddSingleton<ILineTerminalFactory, LineTerminalFactory>();
        services.AddSingleton<IWorkspaceBrowser, WorkspaceBrowser>();
        return services;
    }
}
