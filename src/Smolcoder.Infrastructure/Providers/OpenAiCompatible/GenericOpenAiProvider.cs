using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Infrastructure.Providers.OpenAi;

namespace Smolcoder.Infrastructure.Providers.OpenAiCompatible;

/// <summary>
/// Any other OpenAI-compatible server (llama.cpp, vLLM, Jan…). The base URL is the API root, /v1 included.
/// reasoning_effort is sent only when an effort is set, and dropped for good if the server rejects it.
/// </summary>
public sealed class GenericOpenAiProvider : OpenAiCompatibleProvider
{
    private bool effortUnsupported;

    public GenericOpenAiProvider(
        IHttpClientFactory httpClientFactory,
        IInferenceScheduler scheduler,
        string apiRoot,
        string? apiKey,
        string modelId,
        int contextWindow,
        int maxOutputTokens,
        bool? supportsVision)
        : base(httpClientFactory, LocalHttp.ClientName, scheduler, apiRoot, apiKey, modelId, contextWindow, maxOutputTokens, supportsVision)
    {
        Label = "server · " + modelId;
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
            return "Server";
        }
    }

    public override void SetEffort(Effort? effort)
    {
        base.SetEffort(effort);
        effortUnsupported = false;
    }

    public override string? EffortLabel()
    {
        if (effortUnsupported && CurrentEffort.HasValue)
        {
            return Efforts.Name(CurrentEffort) + " (ignored by this server)";
        }
        return null;
    }

    protected override string ErrorHint(int status)
    {
        if (status == 401 || status == 403)
        {
            return " (check the server's API key)";
        }
        return string.Empty;
    }

    protected override async Task<ChatResult> ChatScheduledAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, CancellationToken token)
    {
        Effort? requested = CurrentEffort;
        if (options.ForceThinkingOff)
        {
            requested = Effort.Off;
        }
        JsonObject body = BaseBody(OpenAiWireFormat.Messages(messages), tools, options);
        bool sendEffort = requested.HasValue && !effortUnsupported;
        if (sendEffort)
        {
            body["reasoning_effort"] = requested == Effort.Off ? "none" : Efforts.Name(requested);
        }
        StreamProgress progress = new StreamProgress();
        try
        {
            return await SendWithShapeFallbackAsync(body, options, token, progress, "reasoning", "effort");
        }
        catch (BackendHttpException exception) when (sendEffort && !progress.Streaming && exception.IsClientError && exception.Mentions("reasoning", "effort"))
        {
            effortUnsupported = true;
            body.Remove("reasoning_effort");
            return await SendWithShapeFallbackAsync(body, options, token, progress);
        }
    }
}
