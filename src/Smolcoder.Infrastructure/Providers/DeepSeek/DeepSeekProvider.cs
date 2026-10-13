using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
public sealed class DeepSeekProvider : IModelProvider
{
    public const string DefaultBaseUrl = "https://api.deepseek.com";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IInferenceScheduler scheduler;
    private readonly string baseUrl;
    private readonly string apiKey;
    private Effort? effort;

    public DeepSeekProvider(
        IHttpClientFactory httpClientFactory,
        IInferenceScheduler scheduler,
        string baseUrl,
        string apiKey,
        string modelId,
        int contextWindow,
        int maxOutputTokens,
        bool? supportsVision)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
        this.baseUrl = baseUrl;
        this.apiKey = apiKey;
        ModelId = modelId;
        ContextWindow = contextWindow;
        MaxOutputTokens = maxOutputTokens;
        SupportsVision = supportsVision;
        Label = "deepseek · " + modelId;
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
    }

    public string? EffortLabel()
    {
        if (!effort.HasValue)
        {
            return "default → high";
        }
        if (effort.Value == Effort.Medium)
        {
            return "medium → high";
        }
        return null;
    }

    public Task<int?> LoadedContextWindowAsync(CancellationToken cancellationToken)
    {
        int? unknown = null;
        return Task.FromResult(unknown);
    }

    public Task<ChatResult> ChatAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options)
    {
        return scheduler.RunAsync(baseUrl, options.Background, options.CancellationToken, token => ChatScheduledAsync(messages, tools, options, token));
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

    private async Task<ChatResult> ChatScheduledAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, CancellationToken token)
    {
        Effort? requested = effort;
        if (options.ForceThinkingOff)
        {
            requested = Effort.Off;
        }
        bool thinking = !requested.HasValue || requested.Value != Effort.Off;

        JsonObject body = new JsonObject();
        body["model"] = ModelId;
        body["messages"] = WireMessages(messages, thinking);
        if (tools.Count > 0)
        {
            body["tools"] = OpenAiWireFormat.Tools(tools);
        }
        int maxTokens = MaxOutputTokens;
        if (options.MaxTokens.HasValue)
        {
            maxTokens = options.MaxTokens.Value;
        }
        body["max_tokens"] = maxTokens;
        body["stream"] = true;
        JsonObject streamOptions = new JsonObject();
        streamOptions["include_usage"] = true;
        body["stream_options"] = streamOptions;
        ApplyEffort(body, requested);

        StreamProgress progress = new StreamProgress();
        try
        {
            return await RequestAsync(body, options, token, progress);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException))
        {
            if (progress.Streaming || !thinking)
            {
                throw;
            }
            bool reasoningRejected = exception.Message.Contains("returned 400") && exception.Message.Contains("reasoning", StringComparison.OrdinalIgnoreCase);
            if (!reasoningRejected)
            {
                throw;
            }
            // Reasoning that was already removed cannot come back. Answer this one request without thinking.
            JsonObject plain = (JsonObject)body.DeepClone();
            plain["messages"] = WireMessages(messages, false);
            JsonObject disabled = new JsonObject();
            disabled["type"] = "disabled";
            plain["thinking"] = disabled;
            plain.Remove("reasoning_effort");
            return await RequestAsync(plain, options, token, progress);
        }
    }

    private async Task<ChatResult> RequestAsync(JsonObject body, ChatRequestOptions options, CancellationToken token, StreamProgress progress)
    {
        using (RequestDeadline deadline = new RequestDeadline(token, options.Timeout, options.IdleTimeout))
        {
            try
            {
                return await ReadResponseAsync(body, options, deadline, progress);
            }
            catch (OperationCanceledException exception)
            {
                throw deadline.Translate(exception);
            }
            catch (IOException exception)
            {
                Exception translated = deadline.Translate(exception);
                if (translated == exception)
                {
                    throw;
                }
                throw translated;
            }
        }
    }

    private async Task<ChatResult> ReadResponseAsync(JsonObject body, ChatRequestOptions options, RequestDeadline deadline, StreamProgress progress)
    {
        Stopwatch clock = Stopwatch.StartNew();
        HttpClient client = httpClientFactory.CreateClient("deepseek");
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string text = string.Empty;
                    try
                    {
                        text = await response.Content.ReadAsStringAsync(deadline.Token);
                    }
                    catch (Exception)
                    {
                        text = string.Empty;
                    }
                    if (text.Length > 300)
                    {
                        text = text.Substring(0, 300);
                    }
                    int status = (int)response.StatusCode;
                    string hint = string.Empty;
                    if (status == 401)
                    {
                        hint = " (check DEEPSEEK_API_KEY)";
                    }
                    else if (status == 402)
                    {
                        hint = " (account balance is empty)";
                    }
                    throw new HttpRequestException("DeepSeek returned " + status + hint + ": " + text);
                }

                StreamAccumulator accumulator = new StreamAccumulator();
                using (Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token))
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        while (true)
                        {
                            string? line = await reader.ReadLineAsync(deadline.Token);
                            if (line == null)
                            {
                                break;
                            }
                            deadline.Activity();
                            if (line.Length > 4000000)
                            {
                                throw new IOException("Backend stream frame exceeded 4 MB");
                            }
                            HandleLine(line.Trim(), accumulator, options, progress, clock);
                        }
                    }
                }

                if (!accumulator.Finished)
                {
                    throw new IOException("DeepSeek stream ended before completion; the response was discarded");
                }
                return accumulator.ToResult(body, clock);
            }
        }
    }

    private static void HandleLine(string line, StreamAccumulator accumulator, ChatRequestOptions options, StreamProgress progress, Stopwatch clock)
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return;
        }
        string payload = line.Substring(5).Trim();
        if (payload == "[DONE]")
        {
            return;
        }
        JsonObject chunk = ParseChunk(payload);
        // Tokens may already be on screen now, so a retry is no longer safe.
        progress.Streaming = true;

        JsonObject? usage = chunk["usage"] as JsonObject;
        if (usage != null)
        {
            int? prompt = ReadInt(usage, "prompt_tokens");
            if (prompt.HasValue)
            {
                accumulator.PromptTokens = prompt;
            }
            int? completion = ReadInt(usage, "completion_tokens");
            if (completion.HasValue)
            {
                accumulator.CompletionTokens = completion;
            }
            int? cacheHit = ReadInt(usage, "prompt_cache_hit_tokens");
            if (cacheHit.HasValue)
            {
                accumulator.CacheHitTokens = cacheHit;
            }
        }

        JsonArray? choices = chunk["choices"] as JsonArray;
        if (choices == null || choices.Count == 0)
        {
            return;
        }
        JsonObject? choice = choices[0] as JsonObject;
        if (choice == null)
        {
            return;
        }
        string? finishReason = ReadString(choice, "finish_reason");
        if (finishReason != null)
        {
            accumulator.Finished = true;
            if (finishReason == "length")
            {
                accumulator.Truncated = true;
            }
        }
        JsonObject? delta = choice["delta"] as JsonObject;
        if (delta == null)
        {
            return;
        }

        bool sawToken = false;
        string? reasoning = ReadString(delta, "reasoning_content");
        if (reasoning != null && reasoning.Length > 0)
        {
            accumulator.Thinking.Append(reasoning);
            if (options.OnThinking != null)
            {
                options.OnThinking(reasoning);
            }
            sawToken = true;
        }
        string? content = ReadString(delta, "content");
        if (content != null && content.Length > 0)
        {
            accumulator.Content.Append(content);
            if (options.OnToken != null)
            {
                options.OnToken(content);
            }
            sawToken = true;
        }
        JsonArray? toolCalls = delta["tool_calls"] as JsonArray;
        if (toolCalls != null)
        {
            sawToken = true;
            foreach (JsonNode? node in toolCalls)
            {
                JsonObject? callDelta = node as JsonObject;
                if (callDelta == null)
                {
                    continue;
                }
                int index = 0;
                int? givenIndex = ReadInt(callDelta, "index");
                if (givenIndex.HasValue)
                {
                    index = givenIndex.Value;
                }
                PartialCall? partial;
                if (!accumulator.Partials.TryGetValue(index, out partial))
                {
                    partial = new PartialCall();
                    accumulator.Partials[index] = partial;
                }
                string? id = ReadString(callDelta, "id");
                if (id != null && id.Length > 0)
                {
                    partial.Id = id;
                }
                JsonObject? function = callDelta["function"] as JsonObject;
                if (function != null)
                {
                    string? name = ReadString(function, "name");
                    if (name != null)
                    {
                        partial.Name.Append(name);
                    }
                    string? arguments = ReadString(function, "arguments");
                    if (arguments != null)
                    {
                        partial.Arguments.Append(arguments);
                    }
                }
            }
        }
        if (sawToken)
        {
            long now = clock.ElapsedMilliseconds;
            if (!accumulator.FirstTokenAt.HasValue)
            {
                accumulator.FirstTokenAt = now;
            }
            accumulator.LastTokenAt = now;
        }
    }

    private static JsonObject ParseChunk(string payload)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            throw new IOException("Backend stream contained malformed JSON; the response was discarded");
        }
        JsonObject? chunk = node as JsonObject;
        if (chunk == null)
        {
            throw new IOException("Backend stream contained malformed JSON; the response was discarded");
        }
        JsonNode? error = chunk["error"];
        if (error != null)
        {
            string message = error.ToJsonString();
            if (error.GetValueKind() == JsonValueKind.String)
            {
                message = error.GetValue<string>();
            }
            else if (error is JsonObject errorObject)
            {
                string? inner = ReadString(errorObject, "message");
                if (inner != null)
                {
                    message = inner;
                }
            }
            throw new IOException("Backend stream error: " + message);
        }
        return chunk;
    }

    private static string? ReadString(JsonObject source, string key)
    {
        JsonNode? node = source[key];
        if (node == null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }
        return node.GetValue<string>();
    }

    private static int? ReadInt(JsonObject source, string key)
    {
        JsonNode? node = source[key];
        if (node == null || node.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }
        return (int)node.GetValue<double>();
    }

    private sealed class StreamProgress
    {
        /// <summary>
        /// True once a chunk arrived. After that, the request must not be sent again.
        /// </summary>
        public bool Streaming { get; set; }
    }

    private sealed class PartialCall
    {
        public PartialCall()
        {
            Id = string.Empty;
            Name = new StringBuilder();
            Arguments = new StringBuilder();
        }

        public string Id { get; set; }

        public StringBuilder Name { get; }

        public StringBuilder Arguments { get; }
    }

    private sealed class StreamAccumulator
    {
        public StreamAccumulator()
        {
            Content = new StringBuilder();
            Thinking = new StringBuilder();
            Partials = new SortedDictionary<int, PartialCall>();
        }

        public StringBuilder Content { get; }

        public StringBuilder Thinking { get; }

        public SortedDictionary<int, PartialCall> Partials { get; }

        public int? PromptTokens { get; set; }

        public int? CompletionTokens { get; set; }

        public int? CacheHitTokens { get; set; }

        public bool Truncated { get; set; }

        public bool Finished { get; set; }

        public long? FirstTokenAt { get; set; }

        public long LastTokenAt { get; set; }

        public ChatResult ToResult(JsonObject body, Stopwatch clock)
        {
            List<ToolCall> calls = new List<ToolCall>();
            foreach (KeyValuePair<int, PartialCall> pair in Partials)
            {
                calls.Add(ToolCallParser.Build(pair.Value.Id, pair.Value.Name.ToString(), pair.Value.Arguments.ToString()));
            }
            string content = Content.ToString();
            string thinking = Thinking.ToString();

            bool thinkingDisabled = false;
            JsonObject? thinkingSetting = body["thinking"] as JsonObject;
            if (thinkingSetting != null)
            {
                thinkingDisabled = ReadString(thinkingSetting, "type") == "disabled";
            }
            // Reasoning is sent again while thinking stays on, so it counts too.
            int replayed = ToolCallParser.EstimateReplayTokens(content, calls);
            if (!thinkingDisabled)
            {
                replayed += (int)Math.Ceiling(thinking.Length / 4.0);
            }

            ChatResult result = new ChatResult(content, calls);
            if (thinking.Length > 0)
            {
                result.Thinking = thinking;
            }
            result.PromptTokens = PromptTokens;
            result.CompletionTokens = replayed;
            result.GeneratedTokens = CompletionTokens;
            result.CacheHitTokens = CacheHitTokens;
            result.Truncated = Truncated;
            if (FirstTokenAt.HasValue)
            {
                result.TimeToFirstTokenMilliseconds = FirstTokenAt.Value;
                long generationMilliseconds = LastTokenAt - FirstTokenAt.Value;
                if (CompletionTokens.HasValue && generationMilliseconds > 0)
                {
                    result.GeneratedTokensPerSecond = CompletionTokens.Value / (generationMilliseconds / 1000.0);
                }
            }
            return result;
        }
    }
}
