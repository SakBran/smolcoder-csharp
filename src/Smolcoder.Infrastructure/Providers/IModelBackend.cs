using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// One kind of model server. The composite catalog routes a model to the backend named on it.
/// </summary>
public interface IModelBackend
{
    /// <summary>
    /// One of <see cref="Backends"/>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The window to budget against. The override is already checked to be at least 1024.
    /// </summary>
    Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken);

    IModelProvider CreateProvider(ModelDescriptor model);
}
