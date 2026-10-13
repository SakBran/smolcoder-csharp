using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Infrastructure.Providers.DeepSeek;

/// <summary>
/// The DeepSeek cloud models available through DEEPSEEK_API_KEY.
/// The key stays in the environment. It is never saved.
/// </summary>
public sealed class DeepSeekModelCatalog : IModelCatalog
{
    public const string BackendName = "deepseek";

    /// <summary>
    /// Default budget. The model's 1M window would let a session grow (and bill) without ever compacting.
    /// --ctx raises it up to the maximum.
    /// </summary>
    public const int DefaultContextWindow = 131072;
    public const int MaxContextWindow = 1048576;

    /// <summary>
    /// Thinking counts against max_tokens, so cloud replies get more room than local ones.
    /// </summary>
    private const int CloudOutputBudget = 16384;

    private static readonly Dictionary<string, string> ModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["deepseek-v4-flash"] = "deepseek-flash"
    };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IInferenceScheduler scheduler;

    public DeepSeekModelCatalog(IHttpClientFactory httpClientFactory, IInferenceScheduler scheduler)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
    }

    public Task<IReadOnlyList<ModelDescriptor>> DetectAsync(CancellationToken cancellationToken)
    {
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        if (ApiKey() != null)
        {
            ModelDescriptor flash = new ModelDescriptor("deepseek-flash", BackendName, BaseUrl(), DefaultContextWindow);
            flash.Host = "cloud";
            flash.MaxContext = MaxContextWindow;
            flash.SupportsVision = true;
            models.Add(flash);
        }
        IReadOnlyList<ModelDescriptor> result = models;
        return Task.FromResult(result);
    }

    public Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
    {
        if (contextOverride.HasValue && contextOverride.Value < 1024)
        {
            throw new ArgumentException("Context window must be a whole number of at least 1024 tokens.");
        }
        ModelDescriptor resolved = model.Copy();
        if (contextOverride.HasValue)
        {
            int maximum = contextOverride.Value;
            if (model.MaxContext.HasValue)
            {
                maximum = model.MaxContext.Value;
            }
            resolved.ContextWindow = Math.Min(contextOverride.Value, maximum);
        }
        return Task.FromResult(resolved);
    }

    public IModelProvider CreateProvider(ModelDescriptor model)
    {
        string? key = ApiKey();
        if (key == null)
        {
            throw new InvalidOperationException("DEEPSEEK_API_KEY is not set.");
        }
        int maxOutput = Math.Min(CloudOutputBudget, model.ContextWindow / 4);
        return new DeepSeekProvider(httpClientFactory, scheduler, model.BaseUrl, key, model.Id, model.ContextWindow, maxOutput, model.SupportsVision);
    }

    public string ResolveAlias(string name)
    {
        string? current;
        if (ModelAliases.TryGetValue(name, out current))
        {
            return current;
        }
        return name;
    }

    private static string? ApiKey()
    {
        string? key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (key == null || key.Trim().Length == 0)
        {
            return null;
        }
        return key.Trim();
    }

    private static string BaseUrl()
    {
        string? configured = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");
        string url = DeepSeekProvider.DefaultBaseUrl;
        if (configured != null && configured.Trim().Length > 0)
        {
            url = configured.Trim();
        }
        return url.TrimEnd('/');
    }
}
