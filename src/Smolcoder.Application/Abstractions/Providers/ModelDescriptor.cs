namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// A model the user can pick.
/// </summary>
public sealed class ModelDescriptor
{
    public ModelDescriptor(string id, string backend, string baseUrl, int contextWindow)
    {
        Id = id;
        Backend = backend;
        BaseUrl = baseUrl;
        ContextWindow = contextWindow;
    }

    public string Id { get; set; }

    /// <summary>
    /// Backend name, one of <see cref="Backends"/>.
    /// </summary>
    public string Backend { get; set; }

    public string BaseUrl { get; set; }

    /// <summary>
    /// Where the model runs. Null means this computer.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// The window we budget against.
    /// </summary>
    public int ContextWindow { get; set; }

    /// <summary>
    /// The model's largest possible window, when known.
    /// </summary>
    public int? MaxContext { get; set; }

    /// <summary>
    /// Ollama only: num_ctx to send with every request. Null keeps the server's own setting.
    /// </summary>
    public int? NumCtx { get; set; }

    /// <summary>
    /// LM Studio only: whether the model is in memory right now.
    /// </summary>
    public bool? Loaded { get; set; }

    /// <summary>
    /// LM Studio only: the reasoning levels the model accepts, and the one it uses when none is named.
    /// </summary>
    public IReadOnlyList<string>? ReasoningOptions { get; set; }

    public string? ReasoningDefault { get; set; }

    /// <summary>
    /// Download size on disk, when the server reports it.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// For example "8.2B".
    /// </summary>
    public string? ParameterSize { get; set; }

    /// <summary>
    /// For example "Q4_K_M".
    /// </summary>
    public string? Quantization { get; set; }

    public bool? SupportsVision { get; set; }

    /// <summary>
    /// Whether the model can call tools. Null when the backend did not say.
    /// </summary>
    public bool? SupportsTools { get; set; }

    /// <summary>
    /// A note to show the user after the model is chosen.
    /// </summary>
    public string? Note { get; set; }

    public ModelDescriptor Copy()
    {
        ModelDescriptor copy = new ModelDescriptor(Id, Backend, BaseUrl, ContextWindow);
        copy.Host = Host;
        copy.MaxContext = MaxContext;
        copy.NumCtx = NumCtx;
        copy.Loaded = Loaded;
        copy.ReasoningOptions = ReasoningOptions;
        copy.ReasoningDefault = ReasoningDefault;
        copy.SizeBytes = SizeBytes;
        copy.ParameterSize = ParameterSize;
        copy.Quantization = Quantization;
        copy.SupportsVision = SupportsVision;
        copy.SupportsTools = SupportsTools;
        copy.Note = Note;
        return copy;
    }
}
