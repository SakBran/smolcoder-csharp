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
    /// Backend name, for example "deepseek".
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

    public bool? SupportsVision { get; set; }

    /// <summary>
    /// A note to show the user after the model is chosen.
    /// </summary>
    public string? Note { get; set; }

    public ModelDescriptor Copy()
    {
        ModelDescriptor copy = new ModelDescriptor(Id, Backend, BaseUrl, ContextWindow);
        copy.Host = Host;
        copy.MaxContext = MaxContext;
        copy.SupportsVision = SupportsVision;
        copy.Note = Note;
        return copy;
    }
}
