using Smolcoder.Application.Abstractions.Persistence;

namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// One progress report from an Ollama download.
/// </summary>
public sealed class PullProgress
{
    public PullProgress(string status, long? completed, long? total)
    {
        Status = status;
        Completed = completed;
        Total = total;
    }

    /// <summary>
    /// Ollama's own words, for example "pulling manifest" or "verifying sha256 digest".
    /// </summary>
    public string Status { get; }

    public long? Completed { get; }

    public long? Total { get; }
}

/// <summary>
/// Downloads models into an Ollama server.
/// </summary>
public interface IModelDownloader
{
    /// <summary>
    /// Pulls the model, reporting progress. Throws with Ollama's message when the name does not exist.
    /// </summary>
    Task PullAsync(string ollamaBaseUrl, string model, IProgress<PullProgress> progress, CancellationToken cancellationToken);
}

public enum OllamaInstallState
{
    NotInstalled,
    Stopped,
    Running
}

/// <summary>
/// Ollama on this computer: is it installed, is it running, start it.
/// </summary>
public interface ILocalOllama
{
    /// <summary>
    /// Where this computer's Ollama listens (OLLAMA_HOST, or the default).
    /// </summary>
    string BaseUrl { get; }

    Task<OllamaInstallState> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts the Ollama app (or "ollama serve") and waits up to ten seconds for it to answer.
    /// </summary>
    Task<bool> StartAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Talks to servers the user is adding or keys the user pasted, before they are saved.
/// </summary>
public interface IServerProbe
{
    /// <summary>
    /// What a server is serving right now. Empty when nothing answered.
    /// </summary>
    Task<List<ServerInfo>> ProbeAsync(SavedServer server, string? apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Null when DeepSeek accepts the key, otherwise why not.
    /// </summary>
    Task<string?> CheckDeepSeekKeyAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// One machine on the local network that runs a model server.
/// </summary>
public sealed class FoundHost
{
    public FoundHost(string address, string hostName, List<ServerInfo> servers)
    {
        Address = address;
        HostName = hostName;
        Servers = servers;
    }

    /// <summary>
    /// The IP address.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// A name the network confirms for it, or the IP again.
    /// </summary>
    public string HostName { get; }

    public List<ServerInfo> Servers { get; }
}

public sealed class ScanProgress
{
    public ScanProgress(int checkedCount, int total)
    {
        Checked = checkedCount;
        Total = total;
    }

    public int Checked { get; }

    public int Total { get; }
}

/// <summary>
/// Finds model servers on the local network. Runs only when asked.
/// </summary>
public interface INetworkScanner
{
    Task<NetworkScanResult> ScanAsync(IProgress<ScanProgress> progress, CancellationToken cancellationToken);
}

public sealed class NetworkScanResult
{
    public NetworkScanResult(List<FoundHost> hosts, int addressesChecked, bool everyProbeFailed)
    {
        Hosts = hosts;
        AddressesChecked = addressesChecked;
        EveryProbeFailed = everyProbeFailed;
    }

    public List<FoundHost> Hosts { get; }

    public int AddressesChecked { get; }

    /// <summary>
    /// True when no connection worked at all, not even a refusal. On macOS that usually means
    /// the terminal has not been allowed to use the local network yet.
    /// </summary>
    public bool EveryProbeFailed { get; }
}
