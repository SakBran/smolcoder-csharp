using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Infrastructure.Providers.OpenAi;

namespace Smolcoder.Infrastructure.Providers.DeepSeek;

/// <summary>
/// DeepSeek cloud API: OpenAI-compatible /chat/completions with streaming and a bearer key.
///
/// Two DeepSeek rules shape the request:
///  - Thinking is on by default. thinking.type switches it; reasoning_effort sets its depth (low or high).
///  - With tools in the request, every earlier assistant turn must send its reasoning_content back,
///    or the API answers 400. Old reasoning that context management removed is sent empty;
///    if the API still refuses, the request is retried once with thinking off.
/// </summary>
public sealed class DeepSeekProvider : OpenAiCompatibleProvider
{
    public const string DefaultBaseUrl = "https://api.deepseek.com";

    public DeepSeekProvider(
        IHttpClientFactory httpClientFactory,
        IInferenceScheduler scheduler,
        string baseUrl,
        string apiKey,
        string modelId,
        int contextWindow,
        int maxOutputTokens,
        bool? supportsVision)
        : base(httpClientFactory, "deepseek", scheduler, baseUrl, apiKey, modelId, contextWindow, maxOutputTokens, supportsVision)
    {
        Label = "deepseek · " + modelId;
    }

    public override bool ReplaysThinking
    {
        get
        {
            return true;
        }
    }

    public override string Label { get; }

    protected override string BackendName
    {
        get
        {
            return "DeepSeek";
        }
    }

    public override string? EffortLabel()
    {
        if (!CurrentEffort.HasValue)
        {
            return "default → high";
        }
        if (CurrentEffort.Value == Effort.Medium)
        {
            return "medium → high";
        }
        return null;
    }

    /// <summary>
    /// What an effort sends. Null thinking means "leave the server default" (thinking on, effort high).
    /// </summary>
    public static void ApplyEffort(JsonObject body, Effort? requested)
    {
        if (!requested.HasValue)
        {
            return;
        }
        JsonObject thinking = new JsonObject();
        if (requested.Value == Effort.Off)
        {
            thinking["type"] = "disabled";
            body["thinking"] = thinking;
            return;
        }
        thinking["type"] = "enabled";
        body["thinking"] = thinking;
        if (requested.Value == Effort.Low)
        {
            body["reasoning_effort"] = "low";
        }
        else
        {
            body["reasoning_effort"] = "high";
        }
    }

    /// <summary>
    /// The shared wire shape, plus reasoning_content on every assistant turn while thinking is on.
    /// </summary>
    public static JsonArray WireMessages(IReadOnlyList<Message> messages, bool thinking)
    {
        JsonArray wire = OpenAiWireFormat.Messages(messages);
        if (!thinking)
        {
            return wire;
        }
        for (int index = 0; index < messages.Count; index++)
        {
            if (messages[index].Role != MessageRole.Assistant)
            {
                continue;
            }
            JsonObject? item = wire[index] as JsonObject;
            if (item == null)
            {
                continue;
            }
            string? thought = messages[index].Thinking;
            string reasoning = string.Empty;
            if (thought != null)
            {
                reasoning = thought;
            }
            item["reasoning_content"] = reasoning;
        }
        return wire;
    }

    protected override string ErrorHint(int status)
    {
        if (status == 401)
        {
            return " (check the DeepSeek API key)";
        }
        if (status == 402)
        {
            return " (account balance is empty)";
        }
        return string.Empty;
    }

    /// <summary>
    /// Reasoning is sent again while thinking stays on, so it counts too.
    /// </summary>
    protected override bool CountThinkingInReplay(JsonObject body)
    {
        return JsonRead.String(JsonRead.Object(body, "thinking"), "type") != "disabled";
    }

    protected override async Task<ChatResult> ChatScheduledAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, CancellationToken token)
    {
        Effort? requested = CurrentEffort;
        if (options.ForceThinkingOff)
        {
            requested = Effort.Off;
        }
        bool thinking = !requested.HasValue || requested.Value != Effort.Off;

        JsonObject body = Streaming(BaseBody(WireMessages(messages, thinking), tools, options), true);
        ApplyEffort(body, requested);

        StreamProgress progress = new StreamProgress();
        try
        {
            return await SendAsync(body, options, token, progress);
        }
        catch (BackendHttpException exception) when (!progress.Streaming && thinking && exception.Status == 400 && exception.Mentions("reasoning"))
        {
            // Reasoning that was already removed cannot come back. Answer this one request without thinking.
            JsonObject plain = (JsonObject)body.DeepClone();
            plain["messages"] = WireMessages(messages, false);
            JsonObject disabled = new JsonObject();
            disabled["type"] = "disabled";
            plain["thinking"] = disabled;
            plain.Remove("reasoning_effort");
            return await SendAsync(plain, options, token, progress);
        }
    }
}
