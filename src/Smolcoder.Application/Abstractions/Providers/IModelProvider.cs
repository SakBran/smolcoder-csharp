using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// One model on one backend.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// True when earlier reasoning is sent back to the model in later requests.
    /// </summary>
    bool ReplaysThinking { get; }

    string Label { get; }

    string ModelId { get; }

    int ContextWindow { get; }

    /// <summary>
    /// Tokens reserved for the model's reply within the window.
    /// </summary>
    int MaxOutputTokens { get; }

    /// <summary>
    /// Whether the model accepts images. Null when the backend did not say.
    /// </summary>
    bool? SupportsVision { get; }

    void SetEffort(Effort? effort);

    /// <summary>
    /// What the effort setting really does on this model. Null when the plain name is enough.
    /// </summary>
    string? EffortLabel();

    /// <summary>
    /// The context the server really loaded, when it can tell. Null otherwise.
    /// </summary>
    Task<int?> LoadedContextWindowAsync(CancellationToken cancellationToken);

    Task<ChatResult> ChatAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options);
}
