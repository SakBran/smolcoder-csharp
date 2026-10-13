namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// Finds the models that can be used and creates providers for them.
/// </summary>
public interface IModelCatalog
{
    Task<IReadOnlyList<ModelDescriptor>> DetectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Works out the context window to budget against. A --ctx override may change it.
    /// </summary>
    Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken);

    IModelProvider CreateProvider(ModelDescriptor model);

    /// <summary>
    /// The current name for a retired model name, or the name itself.
    /// </summary>
    string ResolveAlias(string name);
}
