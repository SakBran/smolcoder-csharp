using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Models;

/// <summary>
/// Where a DeepSeek key comes from.
/// </summary>
public enum KeySource
{
    None,
    Environment,
    Saved
}

/// <summary>
/// What the setup screen needs to decide which steps to show.
/// </summary>
public sealed class SetupStatus
{
    public SetupStatus(OllamaInstallState ollama, string ollamaBaseUrl, long memoryBytes, RecommendedModel recommended, KeySource deepSeekKey, string? deepSeekKeyTail, int serverCount, int modelCount)
    {
        Ollama = ollama;
        OllamaBaseUrl = ollamaBaseUrl;
        MemoryBytes = memoryBytes;
        Recommended = recommended;
        DeepSeekKey = deepSeekKey;
        DeepSeekKeyTail = deepSeekKeyTail;
        ServerCount = serverCount;
        ModelCount = modelCount;
    }

    public OllamaInstallState Ollama { get; }

    public string OllamaBaseUrl { get; }

    public long MemoryBytes { get; }

    public RecommendedModel Recommended { get; }

    public KeySource DeepSeekKey { get; }

    /// <summary>
    /// The last four characters of a saved key. The key itself never reaches the page.
    /// </summary>
    public string? DeepSeekKeyTail { get; }

    public int ServerCount { get; }

    public int ModelCount { get; }
}

/// <summary>
/// A saved server and what it is serving right now.
/// </summary>
public sealed class ServerStatus
{
    public ServerStatus(SavedServer server, List<ServerInfo> found, bool hasKey)
    {
        Server = server;
        Found = found;
        HasKey = hasKey;
    }

    public SavedServer Server { get; }

    public List<ServerInfo> Found { get; }

    public bool HasKey { get; }

    public bool Online
    {
        get
        {
            return Found.Count > 0;
        }
    }

    public int ModelCount
    {
        get
        {
            return Found.Sum(info => info.Models.Count);
        }
    }
}

/// <summary>
/// Everything the web UI does with models and servers: list them, download, start Ollama,
/// save a DeepSeek key, add or remove servers, scan the network.
/// </summary>
public sealed class ModelLibrary
{
    private static readonly TimeSpan CacheLife = TimeSpan.FromSeconds(15);

    private readonly object gate = new object();
    private readonly IModelCatalog catalog;
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;
    private readonly ILocalOllama localOllama;
    private readonly IServerProbe serverProbe;
    private readonly INetworkScanner networkScanner;
    private Task<IReadOnlyList<ModelDescriptor>>? listing;
    private DateTime listedAt = DateTime.MinValue;

    public ModelLibrary(
        IModelCatalog catalog,
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        ILocalOllama localOllama,
        IServerProbe serverProbe,
        INetworkScanner networkScanner,
        ModelDownloads downloads)
    {
        this.catalog = catalog;
        this.settingsStore = settingsStore;
        this.secretStore = secretStore;
        this.localOllama = localOllama;
        this.serverProbe = serverProbe;
        this.networkScanner = networkScanner;
        Downloads = downloads;
        Downloads.Completed += _ => Invalidate();
    }

    public ModelDownloads Downloads { get; }

    /// <summary>
    /// Raised when the list of models or servers may have changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Every model, with details. Calls within a few seconds of each other share one detection.
    /// </summary>
    public Task<IReadOnlyList<ModelDescriptor>> ListAsync(bool refresh)
    {
        lock (gate)
        {
            bool stale = DateTime.UtcNow - listedAt > CacheLife;
            if (listing == null || refresh || stale || listing.IsFaulted)
            {
                ModelDetectOptions options = new ModelDetectOptions();
                options.IncludeDetails = true;
                listing = catalog.DetectAsync(options, CancellationToken.None);
                listedAt = DateTime.UtcNow;
            }
            return listing;
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            listing = null;
        }
        RaiseChanged();
    }

    public async Task<SetupStatus> SetupStatusAsync(CancellationToken cancellationToken)
    {
        Task<OllamaInstallState> ollama = localOllama.CheckAsync(cancellationToken);
        Task<IReadOnlyList<ModelDescriptor>> models = ListAsync(false);
        long memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        KeySource source = KeySource.None;
        string? tail = null;
        if (Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim().Length > 0)
        {
            source = KeySource.Environment;
        }
        else
        {
            string? saved = secretStore.Get(SecretNames.DeepSeek);
            if (saved != null)
            {
                source = KeySource.Saved;
                tail = saved.Length > 4 ? saved.Substring(saved.Length - 4) : saved;
            }
        }
        IReadOnlyList<ModelDescriptor> found = await models;
        return new SetupStatus(
            await ollama,
            localOllama.BaseUrl,
            memory,
            RecommendedModels.BestFor(memory),
            source,
            tail,
            settingsStore.LoadServers().Count,
            found.Count);
    }

    /// <summary>
    /// Starts downloading into an Ollama server; this computer's when none is named.
    /// </summary>
    public DownloadState StartDownload(string model, string? ollamaBaseUrl)
    {
        return Downloads.Start(model, ollamaBaseUrl ?? localOllama.BaseUrl);
    }

    public async Task<bool> StartOllamaAsync(CancellationToken cancellationToken)
    {
        bool started = await localOllama.StartAsync(cancellationToken);
        Invalidate();
        return started;
    }

    /// <summary>
    /// Checks the key with DeepSeek, then saves it. Throws with the reason when DeepSeek refuses it.
    /// </summary>
    public async Task SaveDeepSeekKeyAsync(string key, CancellationToken cancellationToken)
    {
        string clean = key.Trim();
        if (clean.Length == 0)
        {
            throw new InvalidOperationException("Paste the key first.");
        }
        string? problem = await serverProbe.CheckDeepSeekKeyAsync(clean, cancellationToken);
        if (problem != null)
        {
            throw new InvalidOperationException(problem);
        }
        secretStore.Set(SecretNames.DeepSeek, clean);
        Invalidate();
    }

    public void ForgetDeepSeekKey()
    {
        secretStore.Set(SecretNames.DeepSeek, null);
        Invalidate();
    }

    public IReadOnlyList<SavedServer> SavedServers()
    {
        return settingsStore.LoadServers();
    }

    public async Task<List<ServerStatus>> ServersAsync(CancellationToken cancellationToken)
    {
        List<Task<ServerStatus>> probes = new List<Task<ServerStatus>>();
        foreach (SavedServer server in settingsStore.LoadServers())
        {
            SavedServer target = server;
            string? key = secretStore.Get(target.SecretName);
            probes.Add(Task.Run(async () => new ServerStatus(target, await serverProbe.ProbeAsync(target, key, cancellationToken), key != null), cancellationToken));
        }
        List<ServerStatus> statuses = new List<ServerStatus>();
        foreach (Task<ServerStatus> probe in probes)
        {
            statuses.Add(await probe);
        }
        return statuses;
    }

    /// <summary>
    /// Checks the server, then saves it. Unless force is set, a server that does not answer is not saved.
    /// </summary>
    public async Task<ServerStatus> AddServerAsync(string address, string? name, ServerKind kind, string? apiKey, bool force, CancellationToken cancellationToken)
    {
        ServerAddress parsed = ServerAddress.Parse(address);
        SavedServer server = new SavedServer(parsed.Address, kind);
        string? cleanName = CleanName(name);
        server.Name = cleanName;
        string? key = apiKey == null || apiKey.Trim().Length == 0 ? null : apiKey.Trim();
        List<ServerInfo> found = await serverProbe.ProbeAsync(server, key, cancellationToken);
        if (found.Count == 0 && !force)
        {
            string what = kind == ServerKind.OpenAiCompatible ? "an OpenAI-compatible server" : "Ollama or LM Studio";
            throw new InvalidOperationException("Nothing answered as " + what + " at " + parsed.Address + ". Check that the server is running and reachable from this computer.");
        }
        List<SavedServer> servers = settingsStore.LoadServers().ToList();
        SavedServer? existing = servers.FirstOrDefault(entry => string.Equals(entry.Address, server.Address, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Kind = kind;
            if (cleanName != null)
            {
                existing.Name = cleanName;
            }
            server = existing;
        }
        else
        {
            servers.Add(server);
        }
        settingsStore.SaveServers(servers);
        if (key != null)
        {
            secretStore.Set(server.SecretName, key);
        }
        Invalidate();
        return new ServerStatus(server, found, key != null);
    }

    public void RemoveServer(string address)
    {
        List<SavedServer> servers = settingsStore.LoadServers().ToList();
        SavedServer? removed = servers.FirstOrDefault(entry => string.Equals(entry.Address, address, StringComparison.OrdinalIgnoreCase));
        if (removed == null)
        {
            return;
        }
        servers.Remove(removed);
        settingsStore.SaveServers(servers);
        secretStore.Set(removed.SecretName, null);
        Invalidate();
    }

    public void RenameServer(string address, string? name)
    {
        List<SavedServer> servers = settingsStore.LoadServers().ToList();
        SavedServer? target = servers.FirstOrDefault(entry => string.Equals(entry.Address, address, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return;
        }
        target.Name = CleanName(name);
        settingsStore.SaveServers(servers);
        Invalidate();
    }

    public Task<NetworkScanResult> ScanNetworkAsync(IProgress<ScanProgress> progress, CancellationToken cancellationToken)
    {
        return networkScanner.ScanAsync(progress, cancellationToken);
    }

    private static string? CleanName(string? name)
    {
        if (name == null)
        {
            return null;
        }
        string clean = string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > 40)
        {
            clean = clean.Substring(0, 40);
        }
        return clean.Length == 0 ? null : clean;
    }

    private void RaiseChanged()
    {
        Action? handler = Changed;
        if (handler != null)
        {
            handler();
        }
    }
}
