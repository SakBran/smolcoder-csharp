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
using Smolcoder.Infrastructure.Providers;
using Smolcoder.Infrastructure.Providers.DeepSeek;
using Smolcoder.Infrastructure.Providers.Discovery;
using Smolcoder.Infrastructure.Providers.LmStudio;
using Smolcoder.Infrastructure.Providers.Ollama;
using Smolcoder.Infrastructure.Providers.OpenAiCompatible;
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

        services.AddSingleton<ISecretStore, FileSecretStore>();

        services.AddHttpClient("deepseek", client =>
        {
            // Deadlines are handled per request (idle and total), not here.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient(LocalHttp.ClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() =>
        {
            SocketsHttpHandler handler = new SocketsHttpHandler();
            // A system or office proxy would swallow requests to this computer and the local network.
            handler.UseProxy = false;
            handler.ConnectTimeout = TimeSpan.FromSeconds(3);
            handler.PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60);
            return handler;
        });
        services.AddSingleton<ServerIdentifier>();
        services.AddSingleton<ServerProbe>();
        services.AddSingleton<IServerProbe>(provider => provider.GetRequiredService<ServerProbe>());
        services.AddSingleton<INetworkScanner, NetworkScanner>();
        services.AddSingleton<IModelDownloader, OllamaPuller>();
        services.AddSingleton<ILocalOllama, LocalOllama>();
        services.AddSingleton<ModelDiscovery>();
        services.AddSingleton<DeepSeekBackend>();
        services.AddSingleton<IModelBackend>(provider => provider.GetRequiredService<DeepSeekBackend>());
        services.AddSingleton<IModelBackend, OllamaBackend>();
        services.AddSingleton<IModelBackend, LmStudioBackend>();
        services.AddSingleton<IModelBackend, GenericOpenAiBackend>();
        services.AddSingleton<IModelCatalog, CompositeModelCatalog>();

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
