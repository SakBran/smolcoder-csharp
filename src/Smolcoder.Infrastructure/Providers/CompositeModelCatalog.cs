using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Infrastructure.Providers.DeepSeek;
using Smolcoder.Infrastructure.Providers.Discovery;

namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// Every backend behind one catalog: discovery finds the models, and each model is routed
/// to the backend named on it.
/// </summary>
public sealed class CompositeModelCatalog : IModelCatalog
{
    private readonly Dictionary<string, IModelBackend> backends;
    private readonly ModelDiscovery discovery;
    private readonly DeepSeekBackend deepSeek;

    public CompositeModelCatalog(IEnumerable<IModelBackend> backends, ModelDiscovery discovery, DeepSeekBackend deepSeek)
    {
        this.backends = new Dictionary<string, IModelBackend>();
        foreach (IModelBackend backend in backends)
        {
            this.backends[backend.Name] = backend;
        }
        this.discovery = discovery;
        this.deepSeek = deepSeek;
    }

    public Task<IReadOnlyList<ModelDescriptor>> DetectAsync(ModelDetectOptions? options, CancellationToken cancellationToken)
    {
        ModelDetectOptions chosen = options ?? new ModelDetectOptions();
        return discovery.DetectAsync(chosen, deepSeek.Models(), cancellationToken);
    }

    public Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
    {
        if (contextOverride.HasValue && contextOverride.Value < 1024)
        {
            throw new ArgumentException("Context window must be a whole number of at least 1024 tokens.");
        }
        return Backend(model).ResolveContextWindowAsync(model, contextOverride, cancellationToken);
    }

    public IModelProvider CreateProvider(ModelDescriptor model)
    {
        return Backend(model).CreateProvider(model);
    }

    public string ResolveAlias(string name)
    {
        return DeepSeekBackend.ResolveAlias(name);
    }

    private IModelBackend Backend(ModelDescriptor model)
    {
        IModelBackend? backend;
        if (!backends.TryGetValue(model.Backend, out backend))
        {
            throw new InvalidOperationException("No backend called \"" + model.Backend + "\".");
        }
        return backend;
    }
}
