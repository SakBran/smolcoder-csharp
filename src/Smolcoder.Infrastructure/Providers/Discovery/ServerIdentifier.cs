using System.Text.Json;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Infrastructure.Providers.OpenAiCompatible;

namespace Smolcoder.Infrastructure.Providers.Discovery;

/// <summary>
/// Asks an address what it is. The ANSWER decides the backend, never the port,
/// so Ollama and LM Studio are found the same way wherever they listen.
/// </summary>
public sealed class ServerIdentifier
{
    public const string NotLoadedNote = "not loaded yet. LM Studio loads it on first use, likely at a small default context. For longer sessions, load it in LM Studio with a bigger context first.";

    /// <summary>
    /// LM Studio's usual context when it loads a model on demand.
    /// </summary>
    public const int LmStudioOnDemandGuess = 4096;

    private readonly IHttpClientFactory httpClientFactory;

    public ServerIdentifier(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Ollama or LM Studio at this address, or null. Both native listings are requested together,
    /// so a dead address costs one timeout, not one per backend.
    /// </summary>
    public async Task<ServerInfo?> IdentifyAsync(string baseUrl, TimeSpan timeout, CancellationToken token)
    {
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        Task<ProbeResult> tagsTask = LocalHttp.GetJsonAsync(client, baseUrl + "/api/tags", timeout, null, token);
        Task<ProbeResult> v1Task = LocalHttp.GetJsonAsync(client, baseUrl + "/api/v1/models", timeout, null, token);
        ProbeResult tags = await tagsTask;
        ProbeResult v1 = await v1Task;
        if (!tags.Reached && !v1.Reached)
        {
            return null;
        }
        // LM Studio first: its listing names models by "key", which nothing else does.
        if (IsKeyedListing(v1.Data))
        {
            return new ServerInfo(Backends.LmStudio, baseUrl, ParseLmStudioV1(v1.Data, baseUrl) ?? new List<ModelDescriptor>());
        }
        List<ModelDescriptor>? ollama = ParseOllamaTags(tags.Data, baseUrl);
        if (ollama != null)
        {
            return new ServerInfo(Backends.Ollama, baseUrl, ollama);
        }
        List<ModelDescriptor>? lmV1 = ParseLmStudioV1(v1.Data, baseUrl);
        if (lmV1 != null)
        {
            return new ServerInfo(Backends.LmStudio, baseUrl, lmV1);
        }
        JsonNode? v0Data = await LocalHttp.TryGetJsonAsync(client, baseUrl + "/api/v0/models", timeout, token);
        List<ModelDescriptor>? v0 = ParseLmStudioV0(v0Data, baseUrl);
        if (v0 != null)
        {
            return new ServerInfo(Backends.LmStudio, baseUrl, v0);
        }
        // Older LM Studio builds only have the OpenAI-compatible listing, without context sizes.
        JsonNode? compat = await LocalHttp.TryGetJsonAsync(client, baseUrl + "/v1/models", timeout, token);
        List<ModelDescriptor>? models = ParseOpenAiModels(compat, baseUrl, Backends.LmStudio);
        if (models != null)
        {
            foreach (ModelDescriptor model in models)
            {
                model.Note = "context window unknown (older LM Studio). Assuming 4096 to be safe.";
            }
            return new ServerInfo(Backends.LmStudio, baseUrl, models);
        }
        return null;
    }

    /// <summary>
    /// A custom OpenAI-compatible server: GET {apiRoot}/models with the key, if any.
    /// </summary>
    public async Task<ServerInfo?> IdentifyOpenAiAsync(string apiRoot, string? apiKey, TimeSpan timeout, CancellationToken token)
    {
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        ProbeResult listing = await LocalHttp.GetJsonAsync(client, apiRoot + "/models", timeout, apiKey, token);
        if (!listing.Reached || !listing.Status.HasValue || listing.Status.Value >= 300)
        {
            return null;
        }
        List<ModelDescriptor>? models = ParseOpenAiModels(listing.Data, apiRoot, Backends.OpenAi);
        if (models == null)
        {
            return null;
        }
        return new ServerInfo(Backends.OpenAi, apiRoot, models);
    }

    public static bool IsKeyedListing(JsonNode? data)
    {
        JsonArray? models = JsonRead.Array(data, "models");
        if (models == null || models.Count == 0)
        {
            return false;
        }
        foreach (JsonNode? model in models)
        {
            if (JsonRead.String(model, "key") == null)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Ollama's /api/tags. Embedding models cannot chat, so they are left out.
    /// </summary>
    public static List<ModelDescriptor>? ParseOllamaTags(JsonNode? data, string baseUrl)
    {
        JsonArray? items = JsonRead.Array(data, "models");
        if (items == null)
        {
            return null;
        }
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        foreach (JsonNode? item in items)
        {
            string? name = JsonRead.String(item, "name");
            if (name == null)
            {
                return null;
            }
            JsonObject? details = JsonRead.Object(item, "details");
            string? family = JsonRead.String(details, "family");
            if (name.Contains("embed", StringComparison.OrdinalIgnoreCase) || (family != null && family.Contains("bert", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            // The window is worked out when the model is chosen (/api/show and /api/ps).
            ModelDescriptor model = new ModelDescriptor(name, Backends.Ollama, baseUrl, 0);
            model.SizeBytes = JsonRead.Long(item, "size");
            model.ParameterSize = JsonRead.String(details, "parameter_size");
            model.Quantization = JsonRead.String(details, "quantization_level");
            models.Add(model);
        }
        return models;
    }

    /// <summary>
    /// LM Studio's /api/v1/models: loaded instances, context sizes and reasoning levels.
    /// </summary>
    public static List<ModelDescriptor>? ParseLmStudioV1(JsonNode? data, string baseUrl)
    {
        JsonArray? items = JsonRead.Array(data, "models");
        if (items == null)
        {
            return null;
        }
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        foreach (JsonNode? item in items)
        {
            string? type = JsonRead.String(item, "type");
            if (type != null && type != "llm" && type != "vlm")
            {
                continue;
            }
            JsonArray? instances = JsonRead.Array(item, "loaded_instances");
            JsonNode? instance = instances != null && instances.Count > 0 ? instances[0] : null;
            string? id = JsonRead.String(instance, "id") ?? JsonRead.String(item, "key");
            if (id == null)
            {
                continue;
            }
            int? maximum = JsonRead.Int(item, "max_context_length");
            int? loadedContext = JsonRead.Int(JsonRead.Object(instance, "config"), "context_length");
            bool loaded = instance != null;
            int window = Math.Min(maximum ?? LmStudioOnDemandGuess, LmStudioOnDemandGuess);
            if (loaded && loadedContext.HasValue)
            {
                window = loadedContext.Value;
            }
            ModelDescriptor model = new ModelDescriptor(id, Backends.LmStudio, baseUrl, window);
            model.MaxContext = maximum;
            model.Loaded = loaded;
            model.SupportsVision = Vision(type);
            model.SizeBytes = JsonRead.Long(item, "size_bytes");
            model.ParameterSize = JsonRead.String(item, "params_string");
            JsonObject? reasoning = JsonRead.Object(JsonRead.Object(item, "capabilities"), "reasoning");
            JsonArray? allowed = JsonRead.Array(reasoning, "allowed_options");
            if (allowed != null)
            {
                List<string> options = new List<string>();
                foreach (JsonNode? option in allowed)
                {
                    if (option != null)
                    {
                        options.Add(option.GetValueKind() == JsonValueKind.String ? option.GetValue<string>() : option.ToJsonString());
                    }
                }
                model.ReasoningOptions = options;
                model.ReasoningDefault = JsonRead.String(reasoning, "default");
            }
            JsonObject? capabilities = JsonRead.Object(item, "capabilities");
            bool? tools = JsonRead.Bool(capabilities, "trained_for_tool_use");
            if (tools.HasValue)
            {
                model.SupportsTools = tools;
            }
            if (!loaded || !loadedContext.HasValue)
            {
                model.Note = NotLoadedNote;
            }
            models.Add(model);
        }
        return models;
    }

    /// <summary>
    /// LM Studio's older /api/v0/models.
    /// </summary>
    public static List<ModelDescriptor>? ParseLmStudioV0(JsonNode? data, string baseUrl)
    {
        JsonArray? items = JsonRead.Array(data, "data");
        if (items == null)
        {
            return null;
        }
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        foreach (JsonNode? item in items)
        {
            string? type = JsonRead.String(item, "type");
            if (type != null && type != "llm" && type != "vlm")
            {
                continue;
            }
            string? id = JsonRead.String(item, "id");
            if (id == null)
            {
                continue;
            }
            bool loaded = JsonRead.String(item, "state") == "loaded";
            int? maximum = JsonRead.Int(item, "max_context_length");
            int? loadedContext = JsonRead.Int(item, "loaded_context_length");
            ModelDescriptor model = new ModelDescriptor(id, Backends.LmStudio, baseUrl, Math.Min(maximum ?? LmStudioOnDemandGuess, LmStudioOnDemandGuess));
            model.MaxContext = maximum;
            model.Loaded = loaded;
            model.SupportsVision = Vision(type);
            if (loaded && loadedContext.HasValue)
            {
                model.ContextWindow = loadedContext.Value;
            }
            else
            {
                model.Note = NotLoadedNote;
            }
            models.Add(model);
        }
        return models;
    }

    /// <summary>
    /// An OpenAI-style /v1/models listing. vLLM adds max_model_len; others give no context size.
    /// </summary>
    public static List<ModelDescriptor>? ParseOpenAiModels(JsonNode? data, string baseUrl, string backend)
    {
        JsonArray? items = JsonRead.Array(data, "data");
        if (items == null)
        {
            return null;
        }
        List<ModelDescriptor> models = new List<ModelDescriptor>();
        foreach (JsonNode? item in items)
        {
            string? id = JsonRead.String(item, "id");
            if (id == null || id.Contains("embed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int? maximum = JsonRead.Int(item, "max_model_len");
            ModelDescriptor model = new ModelDescriptor(id, backend, baseUrl, maximum ?? GenericOpenAiBackend.UnknownContext);
            model.MaxContext = maximum;
            if (!maximum.HasValue && backend == Backends.OpenAi)
            {
                model.Note = "this server did not say its context size. Assuming 4096; set a bigger one with --ctx.";
            }
            models.Add(model);
        }
        return models;
    }

    private static bool? Vision(string? type)
    {
        if (type == "vlm")
        {
            return true;
        }
        if (type == "llm")
        {
            return false;
        }
        return null;
    }
}
