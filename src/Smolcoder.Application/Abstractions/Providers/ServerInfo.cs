namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// One model server that answered, and what it serves.
/// </summary>
public sealed class ServerInfo
{
    public ServerInfo(string backend, string baseUrl, List<ModelDescriptor> models)
    {
        Backend = backend;
        BaseUrl = baseUrl;
        Models = models;
    }

    /// <summary>
    /// One of <see cref="Backends"/>.
    /// </summary>
    public string Backend { get; }

    public string BaseUrl { get; }

    public List<ModelDescriptor> Models { get; }
}
