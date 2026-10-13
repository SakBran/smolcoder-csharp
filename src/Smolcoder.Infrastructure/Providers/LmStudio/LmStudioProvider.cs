using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Infrastructure.Providers.OpenAi;

namespace Smolcoder.Infrastructure.Providers.LmStudio;

/// <summary>
/// LM Studio: OpenAI-compatible /v1/chat/completions with SSE. The context window is whatever
/// LM Studio loaded the model with; it cannot change per request.
/// </summary>
public sealed class LmStudioProvider : OpenAiCompatibleProvider
{
    private static readonly Regex QwenModels = new Regex("qwen", RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IReadOnlyList<string>? reasoningOptions;
    private readonly string? reasoningDefault;
    private bool effortUnsupported;

    public LmStudioProvider(
        IHttpClientFactory httpClientFactory,
        IInferenceScheduler scheduler,
        string baseUrl,
        string modelId,
        int contextWindow,
        int maxOutputTokens,
        IReadOnlyList<string>? reasoningOptions,
        string? reasoningDefault,
        bool? supportsVision)
        : base(httpClientFactory, LocalHttp.ClientName, scheduler, baseUrl, null, modelId, contextWindow, maxOutputTokens, supportsVision)
    {
        this.httpClientFactory = httpClientFactory;
        this.reasoningOptions = reasoningOptions;
        this.reasoningDefault = reasoningDefault;
        Label = "lmstudio · " + modelId;
    }

    public override bool ReplaysThinking
    {
        get
        {
            return false;
        }
    }

    public override string Label { get; }

    protected override string BackendName
    {
        get
        {
            return "LM Studio";
        }
    }

    protected override string CompletionsUrl
    {
        get
        {
            return BaseUrl + "/v1/chat/completions";
        }
    }

    public override void SetEffort(Effort? effort)
    {
        base.SetEffort(effort);
        effortUnsupported = false;
    }

    public override string? EffortLabel()
    {
        if (effortUnsupported)
        {
            if (CurrentEffort.HasValue)
            {
                return Efforts.Name(CurrentEffort) + " (ignored by this server)";
            }
            return null;
        }
        if (!CurrentEffort.HasValue)
        {
            if (reasoningDefault != null)
            {
                return "default → " + reasoningDefault;
            }
            return null;
        }
        string? wire = LmStudioReasoning.MapEffort(CurrentEffort, reasoningOptions);
        string name = Efforts.Name(CurrentEffort);
        if (wire != null && wire != name && wire != "none")
        {
            return name + " → " + wire;
        }
        return null;
    }

    public override async Task<int?> LoadedContextWindowAsync(CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        JsonNode? data = await LocalHttp.TryGetJsonAsync(client, BaseUrl + "/api/v1/models", TimeSpan.FromMilliseconds(1500), cancellationToken);
        JsonArray? models = JsonRead.Array(data, "models");
        if (models == null)
        {
            return null;
        }
        foreach (JsonNode? model in models)
        {
            JsonArray? instances = JsonRead.Array(model, "loaded_instances");
            if (instances == null || instances.Count == 0)
            {
                continue;
            }
            JsonNode? match = null;
            foreach (JsonNode? instance in instances)
            {
                if (JsonRead.String(instance, "id") == ModelId)
                {
                    match = instance;
                    break;
                }
            }
            if (match == null && JsonRead.String(model, "key") == ModelId)
            {
                match = instances[0];
            }
            int? length = JsonRead.Int(JsonRead.Object(match, "config"), "context_length");
            if (length.HasValue)
            {
                return length;
            }
        }
        return null;
    }

    protected override async Task<ChatResult> ChatScheduledAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, CancellationToken token)
    {
        Effort? requested = CurrentEffort;
        if (options.ForceThinkingOff)
        {
            requested = Effort.Off;
        }
        IReadOnlyList<Message> wireMessages = messages;
        string? wireEffort = LmStudioReasoning.MapEffort(requested, reasoningOptions);
        bool sendEffort = wireEffort != null && !effortUnsupported;
        if (!sendEffort && requested == Effort.Off && QwenModels.IsMatch(ModelId))
        {
            // Older LM Studio builds without reasoning_effort: qwen's per-turn /no_think switch.
            wireMessages = WithNoThink(messages);
        }
        JsonObject body = BaseBody(OpenAiWireFormat.Messages(wireMessages), tools, options);
        if (sendEffort)
        {
            body["reasoning_effort"] = wireEffort;
        }

        StreamProgress progress = new StreamProgress();
        try
        {
            return await SendWithShapeFallbackAsync(body, options, token, progress, "reasoning", "effort");
        }
        catch (BackendHttpException exception) when (sendEffort && !progress.Streaming && exception.IsClientError && exception.Mentions("reasoning", "effort"))
        {
            // Only an actual rejection of the parameter turns it off, never a transient error.
            effortUnsupported = true;
            body.Remove("reasoning_effort");
            return await SendWithShapeFallbackAsync(body, options, token, progress);
        }
    }

    private static IReadOnlyList<Message> WithNoThink(IReadOnlyList<Message> messages)
    {
        List<Message> copy = new List<Message>(messages);
        for (int index = copy.Count - 1; index >= 0; index--)
        {
            if (copy[index].Role == MessageRole.User)
            {
                Message changed = copy[index].Clone();
                changed.Content = changed.Content + " /no_think";
                copy[index] = changed;
                break;
            }
        }
        return copy;
    }
}
