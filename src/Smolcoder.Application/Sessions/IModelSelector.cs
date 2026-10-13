using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Sessions;

public interface IModelSelector
{
    /// <summary>
    /// Finds the available models, picks one and resolves its context window.
    /// Returns null when no model is available.
    /// </summary>
    Task<ModelDescriptor?> PrepareModelAsync(SessionPreferences preferences, AppSettings settings, Action<string>? progress, CancellationToken cancellationToken);

    ModelDescriptor AutoPick(IReadOnlyList<ModelDescriptor> models, string? wanted, string? remembered, string? url);

    /// <summary>
    /// Message shown when no model is available.
    /// </summary>
    string NoModelsMessage();
}
