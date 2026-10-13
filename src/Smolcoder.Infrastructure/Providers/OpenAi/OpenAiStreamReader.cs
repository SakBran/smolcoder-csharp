using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Infrastructure.Providers.OpenAi;

/// <summary>
/// Collects one OpenAI-style chat completion, streamed (SSE "data:" lines) or whole.
/// Reasoning arrives as delta.reasoning_content (DeepSeek, LM Studio) or delta.reasoning (vLLM, newer servers).
/// </summary>
public sealed class OpenAiStreamReader
{
    private readonly ChatRequestOptions options;
    private readonly StreamProgress progress;
    private readonly Stopwatch clock;
    private readonly StringBuilder content;
    private readonly StringBuilder thinking;
    private readonly SortedDictionary<int, PartialCall> partials;
    private int? promptTokens;
    private int? completionTokens;
    private int? cacheHitTokens;
    private bool truncated;
    private long? firstTokenAt;
    private long lastTokenAt;

    public OpenAiStreamReader(ChatRequestOptions options, StreamProgress progress, Stopwatch clock)
    {
        this.options = options;
        this.progress = progress;
        this.clock = clock;
        content = new StringBuilder();
        thinking = new StringBuilder();
        partials = new SortedDictionary<int, PartialCall>();
    }

    public bool Finished { get; private set; }

    public void HandleLine(string rawLine)
    {
        string line = rawLine.Trim();
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            return;
        }
        string payload = line.Substring(5).Trim();
        if (payload == "[DONE]")
        {
            return;
        }
        JsonObject chunk = JsonRead.ParseStreamChunk(payload);
        // Tokens may already be on screen now, so a retry is no longer safe.
        progress.Streaming = true;

        ReadUsage(chunk);
        JsonObject? choice = FirstChoice(chunk);
        if (choice == null)
        {
            return;
        }
        ReadFinish(choice);
        JsonObject? delta = JsonRead.Object(choice, "delta");
        if (delta == null)
        {
            return;
        }

        bool sawToken = false;
        string? reasoning = JsonRead.String(delta, "reasoning_content");
        if (reasoning == null)
        {
            reasoning = JsonRead.String(delta, "reasoning");
        }
        if (reasoning != null && reasoning.Length > 0)
        {
            thinking.Append(reasoning);
            if (options.OnThinking != null)
            {
                options.OnThinking(reasoning);
            }
            sawToken = true;
        }
        string? text = JsonRead.String(delta, "content");
        if (text != null && text.Length > 0)
        {
            content.Append(text);
            if (options.OnToken != null)
            {
                options.OnToken(text);
            }
            sawToken = true;
        }
        JsonArray? toolCalls = JsonRead.Array(delta, "tool_calls");
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
                int? givenIndex = JsonRead.Int(callDelta, "index");
                if (givenIndex.HasValue)
                {
                    index = givenIndex.Value;
                }
                PartialCall? partial;
                if (!partials.TryGetValue(index, out partial))
                {
                    partial = new PartialCall();
                    partials[index] = partial;
                }
                string? id = JsonRead.String(callDelta, "id");
                if (id != null && id.Length > 0)
                {
                    partial.Id = id;
                }
                JsonObject? function = JsonRead.Object(callDelta, "function");
                if (function != null)
                {
                    string? name = JsonRead.String(function, "name");
                    if (name != null)
                    {
                        partial.Name.Append(name);
                    }
                    string? arguments = JsonRead.String(function, "arguments");
                    if (arguments != null)
                    {
                        partial.Arguments.Append(arguments);
                    }
                }
            }
        }
        if (sawToken)
        {
            MarkToken();
        }
    }

    /// <summary>
    /// A whole (stream: false) completion. Used when a server rejects streaming.
    /// </summary>
    public void HandleCompletion(JsonObject body)
    {
        JsonNode? error = body["error"];
        JsonObject? choice = FirstChoice(body);
        JsonObject? message = JsonRead.Object(choice, "message");
        if (error != null || message == null)
        {
            string detail = "missing message";
            string? errorText = JsonRead.String(error, "message");
            if (errorText != null)
            {
                detail = errorText;
            }
            throw new IOException("Backend returned an invalid completion: " + detail);
        }
        progress.Streaming = true;
        ReadUsage(body);
        ReadFinish(choice!);
        Finished = true;

        string? reasoning = JsonRead.String(message, "reasoning_content");
        if (reasoning == null)
        {
            reasoning = JsonRead.String(message, "reasoning");
        }
        if (reasoning != null && reasoning.Length > 0)
        {
            thinking.Append(reasoning);
            if (options.OnThinking != null)
            {
                options.OnThinking(reasoning);
            }
        }
        string? text = JsonRead.String(message, "content");
        if (text != null && text.Length > 0)
        {
            content.Append(text);
            if (options.OnToken != null)
            {
                options.OnToken(text);
            }
        }
        JsonArray? toolCalls = JsonRead.Array(message, "tool_calls");
        if (toolCalls != null)
        {
            int index = 0;
            foreach (JsonNode? node in toolCalls)
            {
                PartialCall partial = new PartialCall();
                string? id = JsonRead.String(node, "id");
                if (id != null)
                {
                    partial.Id = id;
                }
                JsonObject? function = JsonRead.Object(node, "function");
                string? name = JsonRead.String(function, "name");
                if (name != null)
                {
                    partial.Name.Append(name);
                }
                JsonNode? arguments = function == null ? null : function["arguments"];
                if (arguments is JsonObject argumentObject)
                {
                    partial.Arguments.Append(argumentObject.ToJsonString());
                }
                else
                {
                    string? argumentText = JsonRead.String(function, "arguments");
                    if (argumentText != null)
                    {
                        partial.Arguments.Append(argumentText);
                    }
                }
                partials[index] = partial;
                index++;
            }
        }
        MarkToken();
    }

    /// <param name="countThinkingInReplay">True when the reasoning is sent again in the next prompt.</param>
    public ChatResult ToResult(bool countThinkingInReplay)
    {
        List<ToolCall> calls = new List<ToolCall>();
        foreach (KeyValuePair<int, PartialCall> pair in partials)
        {
            calls.Add(ToolCallParser.Build(pair.Value.Id, pair.Value.Name.ToString(), pair.Value.Arguments.ToString()));
        }
        string text = content.ToString();
        string thought = thinking.ToString();

        int replayed = ToolCallParser.EstimateReplayTokens(text, calls);
        if (countThinkingInReplay)
        {
            replayed += (int)Math.Ceiling(thought.Length / 4.0);
        }

        ChatResult result = new ChatResult(text, calls);
        if (thought.Length > 0)
        {
            result.Thinking = thought;
        }
        result.PromptTokens = promptTokens;
        result.CompletionTokens = replayed;
        result.GeneratedTokens = completionTokens;
        result.CacheHitTokens = cacheHitTokens;
        result.Truncated = truncated;
        if (firstTokenAt.HasValue)
        {
            result.TimeToFirstTokenMilliseconds = firstTokenAt.Value;
            long generationMilliseconds = lastTokenAt - firstTokenAt.Value;
            if (completionTokens.HasValue && generationMilliseconds > 0)
            {
                result.GeneratedTokensPerSecond = completionTokens.Value / (generationMilliseconds / 1000.0);
            }
        }
        return result;
    }

    private static JsonObject? FirstChoice(JsonObject chunk)
    {
        JsonArray? choices = JsonRead.Array(chunk, "choices");
        if (choices == null || choices.Count == 0)
        {
            return null;
        }
        return choices[0] as JsonObject;
    }

    private void ReadUsage(JsonObject chunk)
    {
        JsonObject? usage = JsonRead.Object(chunk, "usage");
        if (usage == null)
        {
            return;
        }
        int? prompt = JsonRead.Int(usage, "prompt_tokens");
        if (prompt.HasValue)
        {
            promptTokens = prompt;
        }
        int? completion = JsonRead.Int(usage, "completion_tokens");
        if (completion.HasValue)
        {
            completionTokens = completion;
        }
        int? cacheHit = JsonRead.Int(usage, "prompt_cache_hit_tokens");
        if (cacheHit.HasValue)
        {
            cacheHitTokens = cacheHit;
        }
    }

    private void ReadFinish(JsonObject choice)
    {
        string? finishReason = JsonRead.String(choice, "finish_reason");
        if (finishReason == null)
        {
            return;
        }
        Finished = true;
        if (finishReason == "length")
        {
            truncated = true;
        }
    }

    private void MarkToken()
    {
        long now = clock.ElapsedMilliseconds;
        if (!firstTokenAt.HasValue)
        {
            firstTokenAt = now;
        }
        lastTokenAt = now;
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
}
