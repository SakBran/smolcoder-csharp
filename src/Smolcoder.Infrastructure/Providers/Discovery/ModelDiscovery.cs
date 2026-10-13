using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Models;
using Smolcoder.Infrastructure.Providers.Ollama;

namespace Smolcoder.Infrastructure.Providers.Discovery;

/// <summary>
/// Zero-config detection. Builds the list of places a model server could be (loopback on the
/// usual ports, OLLAMA_HOST, LM Studio's configured port, ports published by containers, saved
/// servers) and asks each one what it is, all at once. Startup can stop as soon as the model it
/// wants turns up, so a saved machine that is switched off does not hold it up.
/// </summary>
public sealed class ModelDiscovery
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// An added machine that is switched off must not hold up startup for long.
    /// </summary>
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly ServerIdentifier identifier;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;

    public ModelDiscovery(ServerIdentifier identifier, IHttpClientFactory httpClientFactory, ISettingsStore settingsStore, ISecretStore secretStore)
    {
        this.identifier = identifier;
        this.httpClientFactory = httpClientFactory;
        this.settingsStore = settingsStore;
        this.secretStore = secretStore;
    }

    public async Task<IReadOnlyList<ModelDescriptor>> DetectAsync(ModelDetectOptions options, IReadOnlyList<ModelDescriptor> cloud, CancellationToken token)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        int? lmStudioPort = LocalEndpoints.ReadLmStudioPort(home);
        List<string> localUrls = new List<string>();
        localUrls.AddRange(LocalEndpoints.OllamaBaseUrls(Environment.GetEnvironmentVariable("OLLAMA_HOST")));
        localUrls.AddRange(LocalEndpoints.LmStudioBaseUrls(lmStudioPort));
        List<ServerGroup> local = LocalEndpoints.GroupServers(localUrls, LocalTimeout, null);
        HashSet<string> covered = new HashSet<string>(localUrls);
        List<int> containerPorts = new List<int> { ServerAddress.OllamaPort, ServerAddress.LmStudioPort };
        if (lmStudioPort.HasValue && !containerPorts.Contains(lmStudioPort.Value))
        {
            containerPorts.Add(lmStudioPort.Value);
        }

        // One slot per source, in display order: this computer, its containers, each saved server, the cloud.
        List<Task<List<ModelDescriptor>>> slots = new List<Task<List<ModelDescriptor>>>();
        foreach (ServerGroup group in local)
        {
            slots.Add(ProbeGroupAsync(group, token));
        }
        slots.Add(ContainersAsync(containerPorts, covered, token));
        if (options.IncludeSavedServers)
        {
            foreach (SavedServer server in settingsStore.LoadServers())
            {
                slots.Add(SavedServerAsync(server, token));
            }
        }
        slots.Add(Task.FromResult(new List<ModelDescriptor>(cloud)));

        List<Task<List<ModelDescriptor>>> pending = new List<Task<List<ModelDescriptor>>>(slots);
        while (pending.Count > 0)
        {
            Task<List<ModelDescriptor>> finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            if (options.Until != null && finished.IsCompletedSuccessfully && finished.Result.Any(options.Until))
            {
                break;
            }
        }

        List<ModelDescriptor> merged = Merge(slots);
        if (options.IncludeDetails)
        {
            await AddOllamaDetailsAsync(merged, token);
        }
        return merged;
    }

    /// <summary>
    /// What a saved server is serving right now. Empty when nothing answered.
    /// </summary>
    public async Task<List<ServerInfo>> ProbeServerAsync(SavedServer server, CancellationToken token)
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
            string root = ServerAddress.OpenAiRoot(server.Address);
            ServerInfo? info = await identifier.IdentifyOpenAiAsync(root, secretStore.Get(server.SecretName), NetworkTimeout, token);
            if (info != null)
            {
                SetHost(info.Models, host);
                found.Add(info);
            }
            return found;
        }
        List<Task<ServerInfo?>> probes = new List<Task<ServerInfo?>>();
        foreach (string url in address.Urls)
        {
            probes.Add(identifier.IdentifyAsync(url, NetworkTimeout, token));
        }
        foreach (Task<ServerInfo?> probe in probes)
        {
            ServerInfo? info = await probe;
            if (info != null)
            {
                SetHost(info.Models, host);
                found.Add(info);
            }
        }
        return found;
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

    private async Task<List<ModelDescriptor>> SavedServerAsync(SavedServer server, CancellationToken token)
    {
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        foreach (ServerInfo info in await ProbeServerAsync(server, token))
        {
            models.AddRange(info.Models);
        }
        return models;
    }

    private async Task<List<ModelDescriptor>> ProbeGroupAsync(ServerGroup group, CancellationToken token)
    {
        foreach (string url in group.Urls)
        {
            ServerInfo? info = await identifier.IdentifyAsync(url, group.Timeout, token);
            if (info != null)
            {
                SetHost(info.Models, group.Host);
                return info.Models;
            }
        }
        return new List<ModelDescriptor>();
    }

    private async Task<List<ModelDescriptor>> ContainersAsync(IReadOnlyCollection<int> ports, HashSet<string> covered, CancellationToken token)
    {
        List<string> urls = await DockerPorts.FindAsync(ports);
        List<string> fresh = urls.Where(url => !covered.Contains(url)).ToList();
        List<Task<List<ModelDescriptor>>> probes = new List<Task<List<ModelDescriptor>>>();
        foreach (ServerGroup group in LocalEndpoints.GroupServers(fresh, LocalTimeout, null))
        {
            probes.Add(ProbeGroupAsync(group, token));
        }
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        foreach (Task<List<ModelDescriptor>> probe in probes)
        {
            models.AddRange(await probe);
        }
        return models;
    }

    /// <summary>
    /// Finished slots in slot order, without the same model on the same server twice.
    /// Loopback spellings of one server count as the same server.
    /// </summary>
    private static List<ModelDescriptor> Merge(List<Task<List<ModelDescriptor>>> slots)
    {
        HashSet<string> seen = new HashSet<string>();
        List<ModelDescriptor> merged = new List<ModelDescriptor>();
        foreach (Task<List<ModelDescriptor>> slot in slots)
        {
            if (!slot.IsCompletedSuccessfully)
            {
                continue;
            }
            foreach (ModelDescriptor model in slot.Result)
            {
                string key = ServerKey(model.BaseUrl) + "|" + model.Id;
                if (seen.Add(key))
                {
                    merged.Add(model);
                }
            }
        }
        return merged;
    }

    public static string ServerKey(string url)
    {
        Uri? uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return url.ToLowerInvariant();
        }
        string host = uri.Host.ToLowerInvariant();
        if (LocalEndpoints.IsLoopback(url))
        {
            host = "loopback";
        }
        return host + ":" + uri.Port;
    }

    /// <summary>
    /// Context size, tool and vision support for each Ollama model, one second each, all at once.
    /// </summary>
    private async Task AddOllamaDetailsAsync(List<ModelDescriptor> models, CancellationToken token)
    {
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        List<Task> lookups = new List<Task>();
        foreach (ModelDescriptor model in models)
        {
            if (model.Backend != Backends.Ollama)
            {
                continue;
            }
            ModelDescriptor target = model;
            lookups.Add(Task.Run(async () =>
            {
                JsonNode? info = await OllamaBackend.ShowAsync(client, target.BaseUrl, target.Id, TimeSpan.FromSeconds(1), token);
                OllamaBackend.ApplyShow(target, info);
            }, token));
        }
        try
        {
            await Task.WhenAll(lookups);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // Details are a nicety; a model without them is still listed.
        }
    }

    private static void SetHost(List<ModelDescriptor> models, string? host)
    {
        foreach (ModelDescriptor model in models)
        {
            model.Host = host;
        }
    }
}
