using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Models;

namespace Smolcoder.Infrastructure.Providers.OpenAiCompatible;

/// <summary>
/// Custom OpenAI-compatible servers. The context comes from vLLM's max_model_len (read at detection)
/// or llama.cpp's /props; otherwise 4096 is assumed, with a note to set --ctx.
/// </summary>
public sealed class GenericOpenAiBackend : IModelBackend
{
    public const int UnknownContext = 4096;

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IInferenceScheduler scheduler;
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secretStore;

    public GenericOpenAiBackend(IHttpClientFactory httpClientFactory, IInferenceScheduler scheduler, ISettingsStore settingsStore, ISecretStore secretStore)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
        this.settingsStore = settingsStore;
        this.secretStore = secretStore;
    }

    public string Name
    {
        get
        {
            return Backends.OpenAi;
        }
    }

    public async Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
    {
        ModelDescriptor resolved = model.Copy();
        if (!model.MaxContext.HasValue)
        {
            // llama.cpp's server reports its slot size at the server root, beside /v1.
            HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
            string serverRoot = model.BaseUrl.EndsWith("/v1", StringComparison.Ordinal) ? model.BaseUrl.Substring(0, model.BaseUrl.Length - 3) : model.BaseUrl;
            JsonNode? props = await LocalHttp.TryGetJsonAsync(client, serverRoot + "/props", TimeSpan.FromSeconds(2), cancellationToken);
            int? slot = JsonRead.Int(JsonRead.Object(props, "default_generation_settings"), "n_ctx");
            if (slot.HasValue && slot.Value >= 1024)
            {
                resolved.MaxContext = slot;
                resolved.ContextWindow = slot.Value;
                resolved.Note = null;
            }
        }
        if (contextOverride.HasValue)
        {
            int window = contextOverride.Value;
            if (resolved.MaxContext.HasValue)
            {
                window = Math.Min(window, resolved.MaxContext.Value);
            }
            resolved.ContextWindow = window;
            resolved.Note = null;
        }
        return resolved;
    }

    public IModelProvider CreateProvider(ModelDescriptor model)
    {
        return new GenericOpenAiProvider(
            httpClientFactory,
            scheduler,
            model.BaseUrl,
            ApiKeyFor(model.BaseUrl),
            model.Id,
            model.ContextWindow,
            Backends.LocalOutputBudget(model.ContextWindow),
            model.SupportsVision);
    }

    /// <summary>
    /// The key saved with the server whose API root is this URL.
    /// </summary>
    public string? ApiKeyFor(string apiRoot)
    {
        foreach (SavedServer server in settingsStore.LoadServers())
        {
            if (server.Kind != ServerKind.OpenAiCompatible)
            {
                continue;
            }
            string root;
            try
            {
                root = ServerAddress.OpenAiRoot(server.Address);
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (string.Equals(root, apiRoot, StringComparison.OrdinalIgnoreCase))
            {
                return secretStore.Get(server.SecretName);
            }
        }
        return null;
    }
}
