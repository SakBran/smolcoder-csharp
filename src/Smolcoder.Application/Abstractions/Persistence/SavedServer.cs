namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// How a saved server is asked for its models.
/// </summary>
public enum ServerKind
{
    /// <summary>
    /// Ollama or LM Studio: the answer decides which. A bare host name tries both usual ports.
    /// </summary>
    Auto,

    /// <summary>
    /// Any OpenAI-compatible server (llama.cpp, vLLM, Jan…), asked through /v1/models.
    /// </summary>
    OpenAiCompatible
}

/// <summary>
/// A model server on another machine, or a custom URL, that the user added.
/// Its API key, if any, lives in the secret store, not here.
/// </summary>
public sealed class SavedServer
{
    public SavedServer(string address, ServerKind kind)
    {
        Address = address;
        Kind = kind;
    }

    /// <summary>
    /// Normalized address: a bare host ("gpu-box.local") or a full URL.
    /// </summary>
    public string Address { get; set; }

    /// <summary>
    /// Display name. Null shows the host name.
    /// </summary>
    public string? Name { get; set; }

    public ServerKind Kind { get; set; }

    /// <summary>
    /// The secret store key for this server's API key.
    /// </summary>
    public string SecretName
    {
        get
        {
            return "server:" + Address.ToLowerInvariant();
        }
    }
}
