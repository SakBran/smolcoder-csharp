using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Infrastructure.Providers.OpenAi;

namespace Smolcoder.Infrastructure.Providers.Ollama;

/// <summary>
/// Ollama through its NATIVE /api/chat, not the OpenAI-compatible one: only the native API
/// takes num_ctx per request, accepts reasoning back, and reports real timings.
///
/// Reasoning: the "think" field is a boolean for most models; gpt-oss alone takes a level.
/// A server that rejects levels gets a boolean, and one that rejects "think" gets nothing.
/// </summary>
public sealed class OllamaProvider : IModelProvider
{
    private static readonly Regex LevelModels = new Regex("gpt-oss", RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IInferenceScheduler scheduler;
    private readonly string baseUrl;
    private readonly int? numCtx;
    private Effort? effort;
    private bool thinkUnsupported;
    private bool levelsSupported;

    public OllamaProvider(
        IHttpClientFactory httpClientFactory,
        IInferenceScheduler scheduler,
        string baseUrl,
        string modelId,
        int contextWindow,
        int? numCtx,
        int maxOutputTokens,
        bool? supportsVision)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
        this.baseUrl = baseUrl;
        this.numCtx = numCtx;
        ModelId = modelId;
        ContextWindow = contextWindow;
        MaxOutputTokens = maxOutputTokens;
        SupportsVision = supportsVision;
        Label = "ollama · " + modelId;
        // Only gpt-oss is documented to take levels; everything else gets a boolean straight away.
        levelsSupported = LevelModels.IsMatch(modelId);
    }

    /// <summary>
    /// Keeps the model in memory between tool calls and while the user reads or approves.
    /// Ollama's own default (5 minutes) unloads mid-session, and reloading a large model costs seconds and a cold cache.
    /// </summary>
    public static string KeepAlive
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("SMOLCODER_KEEP_ALIVE");
            if (configured != null && configured.Trim().Length > 0)
            {
                return configured.Trim();
            }
            return "30m";
        }
    }

    public bool ReplaysThinking
    {
        get
        {
            return true;
        }
    }

    public string Label { get; }

    public string ModelId { get; }

    public int ContextWindow { get; }

    public int MaxOutputTokens { get; }

    public bool? SupportsVision { get; }

    public void SetEffort(Effort? newEffort)
    {
        effort = newEffort;
        thinkUnsupported = false;
    }

    public string? EffortLabel()
    {
        if (!effort.HasValue || effort.Value == Effort.Off)
        {
            return null;
        }
        string name = Efforts.Name(effort);
        if (thinkUnsupported)
        {
            return name + " (model has no thinking switch)";
        }
        if (!levelsSupported)
        {
            return name + " → thinking on";
        }
        return null;
    }

    public async Task<int?> LoadedContextWindowAsync(CancellationToken cancellationToken)
    {
        if (numCtx.HasValue)
        {
            return numCtx;
        }
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        JsonNode? running = await LocalHttp.TryGetJsonAsync(client, baseUrl + "/api/ps", TimeSpan.FromMilliseconds(1500), cancellationToken);
        return OllamaBackend.RunningContextLength(running, ModelId);
    }

    public Task<ChatResult> ChatAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options)
    {
        return scheduler.RunAsync(baseUrl, options.Background, options.CancellationToken, token => ChatScheduledAsync(messages, tools, options, token));
    }

    /// <summary>
    /// The think field for an effort: null leaves it out, false switches thinking off.
    /// </summary>
    public static JsonNode? ThinkValue(Effort? effort, bool levelsSupported, bool thinkUnsupported)
    {
        if (!effort.HasValue || thinkUnsupported)
        {
            return null;
        }
        if (effort.Value == Effort.Off)
        {
            return JsonValue.Create(false);
        }
        if (!levelsSupported)
        {
            return JsonValue.Create(true);
        }
        return JsonValue.Create(Efforts.Name(effort));
    }

    private async Task<ChatResult> ChatScheduledAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, CancellationToken token)
    {
        Effort? requested = effort;
        if (options.ForceThinkingOff)
        {
            requested = Effort.Off;
        }
        JsonArray wireMessages = OllamaWireFormat.Messages(messages);
        JsonNode? think = ThinkValue(requested, levelsSupported, thinkUnsupported);
        StreamProgress progress = new StreamProgress();
        try
        {
            return await SendAsync(Body(wireMessages, tools, options, true, think), options, token, progress);
        }
        catch (BackendHttpException exception) when (!progress.Streaming && exception.IsClientError)
        {
            if (think != null && exception.Mentions("think"))
            {
                if (think.GetValueKind() == System.Text.Json.JsonValueKind.String)
                {
                    // Levels rejected: this model takes a boolean. Same intent: on.
                    levelsSupported = false;
                    try
                    {
                        return await SendAsync(Body(wireMessages, tools, options, true, JsonValue.Create(true)), options, token, progress);
                    }
                    catch (BackendHttpException second) when (!progress.Streaming && second.IsClientError && second.Mentions("think"))
                    {
                        // Fall through to no think field at all.
                    }
                }
                thinkUnsupported = true;
                return await SendAsync(Body(wireMessages, tools, options, true, null), options, token, progress);
            }
            // Older Ollama versions reject stream and tools together. One whole-reply retry.
            return await SendAsync(Body(wireMessages, tools, options, false, think), options, token, progress);
        }
    }

    private JsonObject Body(JsonArray wireMessages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, bool stream, JsonNode? think)
    {
        JsonObject body = new JsonObject();
        body["model"] = ModelId;
        body["messages"] = wireMessages.DeepClone();
        if (tools.Count > 0)
        {
            body["tools"] = OpenAiWireFormat.Tools(tools);
        }
        body["stream"] = stream;
        body["keep_alive"] = KeepAlive;
        if (think != null)
        {
            body["think"] = think.DeepClone();
        }
        JsonObject modelOptions = new JsonObject();
        if (numCtx.HasValue)
        {
            modelOptions["num_ctx"] = numCtx.Value;
        }
        int maxTokens = MaxOutputTokens;
        if (options.MaxTokens.HasValue)
        {
            maxTokens = options.MaxTokens.Value;
        }
        modelOptions["num_predict"] = maxTokens;
        body["options"] = modelOptions;
        return body;
    }

    private Task<ChatResult> SendAsync(JsonObject body, ChatRequestOptions options, CancellationToken token, StreamProgress progress)
    {
        return RequestDeadline.RunAsync(token, options.Timeout, options.IdleTimeout, deadline => ReadResponseAsync(body, options, deadline, progress));
    }

    private async Task<ChatResult> ReadResponseAsync(JsonObject body, ChatRequestOptions options, RequestDeadline deadline, StreamProgress progress)
    {
        Stopwatch clock = Stopwatch.StartNew();
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/chat"))
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw await BackendHttpException.FromResponseAsync("Ollama", response, string.Empty, deadline.Token);
                }
                OllamaStreamReader reader = new OllamaStreamReader(options, progress, clock);
                if (JsonRead.Bool(body, "stream") != true)
                {
                    string text = await response.Content.ReadAsStringAsync(deadline.Token);
                    reader.HandleChunk(JsonRead.ParseStreamChunk(text));
                }
                else
                {
                    using (Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token))
                    {
                        using (StreamReader lines = new StreamReader(stream, Encoding.UTF8))
                        {
                            while (true)
                            {
                                string? line = await lines.ReadLineAsync(deadline.Token);
                                if (line == null)
                                {
                                    break;
                                }
                                deadline.Activity();
                                if (line.Length > 4000000)
                                {
                                    throw new IOException("Backend stream frame exceeded 4 MB");
                                }
                                reader.HandleLine(line);
                            }
                        }
                    }
                }
                if (!reader.Finished)
                {
                    throw new IOException("Ollama stream ended before completion; the response was discarded");
                }
                return reader.ToResult();
            }
        }
    }
}
