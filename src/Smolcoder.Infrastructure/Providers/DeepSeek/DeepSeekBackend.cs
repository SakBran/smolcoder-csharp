using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Infrastructure.Providers.DeepSeek;

/// <summary>
/// The DeepSeek cloud models. The key comes from DEEPSEEK_API_KEY, or else from a key pasted
/// into the web UI (the secret store). It is read on every call, so a pasted key works at once.
/// </summary>
public sealed class DeepSeekBackend : IModelBackend
{
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
    private readonly ISecretStore secretStore;

    public DeepSeekBackend(IHttpClientFactory httpClientFactory, IInferenceScheduler scheduler, ISecretStore secretStore)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
        this.secretStore = secretStore;
    }

    public string Name
    {
        get
        {
            return Backends.DeepSeek;
        }
    }

    public IReadOnlyList<ModelDescriptor> Models()
    {
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        if (ApiKey() != null)
        {
            ModelDescriptor flash = new ModelDescriptor("deepseek-flash", Backends.DeepSeek, BaseUrl(), DefaultContextWindow);
            flash.Host = "cloud";
            flash.MaxContext = MaxContextWindow;
            flash.SupportsVision = true;
            flash.SupportsTools = true;
            models.Add(flash);
        }
        return models;
    }

    public Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
    {
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
            throw new InvalidOperationException("No DeepSeek API key. Set DEEPSEEK_API_KEY or paste a key in the web UI.");
        }
        int maxOutput = Math.Min(CloudOutputBudget, model.ContextWindow / 4);
        return new DeepSeekProvider(httpClientFactory, scheduler, model.BaseUrl, key, model.Id, model.ContextWindow, maxOutput, model.SupportsVision);
    }

    public static string ResolveAlias(string name)
    {
        string? current;
        if (ModelAliases.TryGetValue(name, out current))
        {
            return current;
        }
        return name;
    }

    /// <summary>
    /// True when DEEPSEEK_API_KEY is set. A key in the environment wins over a pasted one.
    /// </summary>
    public static bool KeyFromEnvironment
    {
        get
        {
            return EnvironmentKey() != null;
        }
    }

    public string? ApiKey()
    {
        string? key = EnvironmentKey();
        if (key != null)
        {
            return key;
        }
        return secretStore.Get(SecretNames.DeepSeek);
    }

    public static string BaseUrl()
    {
        string? configured = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");
        string url = DeepSeekProvider.DefaultBaseUrl;
        if (configured != null && configured.Trim().Length > 0)
        {
            url = configured.Trim();
        }
        return url.TrimEnd('/');
    }

    private static string? EnvironmentKey()
    {
        string? key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (key == null || key.Trim().Length == 0)
        {
            return null;
        }
        return key.Trim();
    }
}
