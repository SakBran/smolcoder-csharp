using System.Text.Json;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Infrastructure.Providers.Ollama;

/// <summary>
/// Ollama context handling. By default the server's own context setting (the Ollama app's
/// Context Length, or OLLAMA_CONTEXT_LENGTH) is respected and no num_ctx is sent: the model is
/// preloaded and /api/ps says what was really allocated. num_ctx is sent only for a --ctx
/// override, or for an old Ollama that does not report context, whose tiny default silently truncates.
/// </summary>
public sealed class OllamaBackend : IModelBackend
{
    /// <summary>
    /// Ceiling for old servers, to avoid a surprise memory blowup on models with huge windows.
    /// </summary>
    public const int OldServerContextCap = 32768;

    /// <summary>
    /// Below this, the user is told how to give the model more room.
    /// </summary>
    private const int SmallContext = 16384;

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IInferenceScheduler scheduler;

    public OllamaBackend(IHttpClientFactory httpClientFactory, IInferenceScheduler scheduler)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
    }

    public string Name
    {
        get
        {
            return Backends.Ollama;
        }
    }

    public async Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        ModelDescriptor resolved = model.Copy();
        JsonNode? info = await ShowAsync(client, model.BaseUrl, model.Id, TimeSpan.FromSeconds(10), cancellationToken);
        ApplyShow(resolved, info);
        int? maximum = resolved.MaxContext;

        if (contextOverride.HasValue)
        {
            int window = contextOverride.Value;
            if (maximum.HasValue)
            {
                window = Math.Min(window, maximum.Value);
            }
            resolved.ContextWindow = window;
            resolved.NumCtx = window;
            return resolved;
        }

        // Preload (an empty chat is Ollama's documented load call), then read what the server allocated.
        JsonObject preload = new JsonObject();
        preload["model"] = model.Id;
        preload["messages"] = new JsonArray();
        preload["keep_alive"] = OllamaProvider.KeepAlive;
        await LocalHttp.TryPostJsonAsync(client, model.BaseUrl + "/api/chat", preload, TimeSpan.FromSeconds(180), cancellationToken);
        JsonNode? running = await LocalHttp.TryGetJsonAsync(client, model.BaseUrl + "/api/ps", TimeSpan.FromSeconds(3), cancellationToken);
        int? loaded = RunningContextLength(running, model.Id);
        if (loaded.HasValue && loaded.Value > 0)
        {
            resolved.ContextWindow = loaded.Value;
            resolved.NumCtx = null;
            if (loaded.Value < SmallContext && (!maximum.HasValue || maximum.Value > loaded.Value))
            {
                resolved.Note = "Ollama is set to a " + loaded.Value.ToString("N0") + "-token context, which a coding session fills quickly. Raise \"Context length\" in Ollama's settings, or start smallcode with --ctx 32768.";
            }
            return resolved;
        }

        int capped = OldServerContextCap;
        if (maximum.HasValue)
        {
            capped = Math.Min(maximum.Value, OldServerContextCap);
        }
        resolved.ContextWindow = capped;
        resolved.NumCtx = capped;
        resolved.Note = "older Ollama: setting the context to " + capped.ToString("N0") + " explicitly (change it with --ctx).";
        return resolved;
    }

    public IModelProvider CreateProvider(ModelDescriptor model)
    {
        return new OllamaProvider(
            httpClientFactory,
            scheduler,
            model.BaseUrl,
            model.Id,
            model.ContextWindow,
            model.NumCtx,
            Backends.LocalOutputBudget(model.ContextWindow),
            model.SupportsVision);
    }

    public static Task<JsonNode?> ShowAsync(HttpClient client, string baseUrl, string modelId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        JsonObject request = new JsonObject();
        request["model"] = modelId;
        return LocalHttp.TryPostJsonAsync(client, baseUrl + "/api/show", request, timeout, cancellationToken);
    }

    /// <summary>
    /// Fills the maximum context, vision and tool support from an /api/show answer.
    /// Capabilities exist only on newer Ollama; without them the fields stay unknown.
    /// </summary>
    public static void ApplyShow(ModelDescriptor model, JsonNode? info)
    {
        JsonArray? capabilities = JsonRead.Array(info, "capabilities");
        if (capabilities != null)
        {
            bool vision = false;
            bool tools = false;
            foreach (JsonNode? capability in capabilities)
            {
                if (capability == null || capability.GetValueKind() != JsonValueKind.String)
                {
                    continue;
                }
                string name = capability.GetValue<string>();
                if (name == "vision")
                {
                    vision = true;
                }
                else if (name == "tools")
                {
                    tools = true;
                }
            }
            model.SupportsVision = vision;
            model.SupportsTools = tools;
        }
        JsonObject? modelInfo = JsonRead.Object(info, "model_info");
        if (modelInfo != null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in modelInfo)
            {
                if (pair.Key.EndsWith(".context_length", StringComparison.Ordinal) && pair.Value != null && pair.Value.GetValueKind() == JsonValueKind.Number)
                {
                    model.MaxContext = (int)pair.Value.GetValue<double>();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The context /api/ps reports for this model. Never another model's: if our preload failed
    /// while a different model is resident, its window would be the wrong budget.
    /// </summary>
    public static int? RunningContextLength(JsonNode? running, string modelId)
    {
        JsonArray? models = JsonRead.Array(running, "models");
        if (models == null)
        {
            return null;
        }
        foreach (JsonNode? entry in models)
        {
            if (JsonRead.String(entry, "name") == modelId || JsonRead.String(entry, "model") == modelId)
            {
                return JsonRead.Int(entry, "context_length");
            }
        }
        return null;
    }
}
