using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Models;
using Smolcoder.Infrastructure.Providers.DeepSeek;

namespace Smolcoder.Infrastructure.Providers.Discovery;

/// <summary>
/// Checks servers and keys before they are saved, and the saved ones for the servers list.
/// </summary>
public sealed class ServerProbe : IServerProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly ServerIdentifier identifier;
    private readonly IHttpClientFactory httpClientFactory;

    public ServerProbe(ServerIdentifier identifier, IHttpClientFactory httpClientFactory)
    {
        this.identifier = identifier;
        this.httpClientFactory = httpClientFactory;
    }

    public Task<List<ServerInfo>> ProbeAsync(SavedServer server, string? apiKey, CancellationToken cancellationToken)
    {
        return ProbeAsync(server, apiKey, Timeout, cancellationToken);
    }

    public async Task<List<ServerInfo>> ProbeAsync(SavedServer server, string? apiKey, TimeSpan timeout, CancellationToken cancellationToken)
    {
        List<ServerInfo> found = new List<ServerInfo>();
        ServerAddress address;
        try
        {
            address = ServerAddress.Parse(server.Address);
        }
        catch (ArgumentException)
        {
            return found;
        }
        string? host = HostLabel(server, address);
        if (server.Kind == ServerKind.OpenAiCompatible)
        {
            ServerInfo? info = await identifier.IdentifyOpenAiAsync(ServerAddress.OpenAiRoot(server.Address), apiKey, timeout, cancellationToken);
            if (info != null)
            {
                SetHost(info, host);
                found.Add(info);
            }
            return found;
        }
        List<Task<ServerInfo?>> probes = new List<Task<ServerInfo?>>();
        foreach (string url in address.Urls)
        {
            probes.Add(identifier.IdentifyAsync(url, timeout, cancellationToken));
        }
        foreach (Task<ServerInfo?> probe in probes)
        {
            ServerInfo? info = await probe;
            if (info != null)
            {
                SetHost(info, host);
                found.Add(info);
            }
        }
        return found;
    }

    public async Task<string?> CheckDeepSeekKeyAsync(string key, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient("deepseek");
        ProbeResult result = await LocalHttp.GetJsonAsync(client, DeepSeekBackend.BaseUrl() + "/models", TimeSpan.FromSeconds(10), key, cancellationToken);
        if (!result.Reached)
        {
            return "Could not reach DeepSeek to check the key. Check the internet connection and try again.";
        }
        if (result.Status == 401 || result.Status == 403)
        {
            return "DeepSeek refused that key. Copy it again from platform.deepseek.com/api_keys.";
        }
        if (!result.Status.HasValue || result.Status.Value >= 300)
        {
            return "DeepSeek answered " + result.Status + " while checking the key. Try again in a moment.";
        }
        return null;
    }

    /// <summary>
    /// Saved servers on this computer count as local; others show their name.
    /// </summary>
    public static string? HostLabel(SavedServer server, ServerAddress address)
    {
        if (server.Name != null && server.Name.Length > 0)
        {
            return server.Name;
        }
        foreach (string url in address.Urls)
        {
            if (LocalEndpoints.IsLoopback(url))
            {
                return null;
            }
        }
        return address.HostName;
    }

    private static void SetHost(ServerInfo info, string? host)
    {
        foreach (ModelDescriptor model in info.Models)
        {
            model.Host = host;
        }
    }
}
