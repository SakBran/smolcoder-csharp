using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Infrastructure.Providers.Ollama;

/// <summary>
/// Collects one /api/chat reply: newline-delimited JSON when streamed, one object otherwise.
/// </summary>
public sealed class OllamaStreamReader
{
    private readonly ChatRequestOptions options;
    private readonly StreamProgress progress;
    private readonly Stopwatch clock;
    private readonly StringBuilder content;
    private readonly StringBuilder thinking;
    private readonly List<ToolCall> toolCalls;
    private int? promptTokens;
    private int? completionTokens;
    private double? generatedPerSecond;
    private bool truncated;
    private long? firstTokenAt;

    public OllamaStreamReader(ChatRequestOptions options, StreamProgress progress, Stopwatch clock)
    {
        this.options = options;
        this.progress = progress;
        this.clock = clock;
        content = new StringBuilder();
        thinking = new StringBuilder();
        toolCalls = new List<ToolCall>();
    }

    public bool Finished { get; private set; }

    public void HandleLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }
        HandleChunk(JsonRead.ParseStreamChunk(trimmed));
    }

    public void HandleChunk(JsonObject chunk)
    {
        // Committed: a re-request now would show tokens twice.
        progress.Streaming = true;
        JsonObject? message = JsonRead.Object(chunk, "message");
        string? thought = JsonRead.String(message, "thinking");
        if (thought != null && thought.Length > 0)
        {
            thinking.Append(thought);
            if (options.OnThinking != null)
            {
                options.OnThinking(thought);
            }
            MarkToken();
        }
        string? text = JsonRead.String(message, "content");
        if (text != null && text.Length > 0)
        {
            content.Append(text);
            if (options.OnToken != null)
            {
                options.OnToken(text);
            }
            MarkToken();
        }
        JsonArray? calls = JsonRead.Array(message, "tool_calls");
        if (calls != null)
        {
            MarkToken();
            foreach (JsonNode? node in calls)
            {
                JsonObject? function = JsonRead.Object(node, "function");
                string name = JsonRead.String(function, "name") ?? string.Empty;
                JsonNode? arguments = function == null ? null : function["arguments"];
                string raw = string.Empty;
                if (arguments is JsonObject)
                {
                    raw = arguments.ToJsonString();
                }
                else if (JsonRead.String(function, "arguments") is string argumentText)
                {
                    raw = argumentText;
                }
                // Ollama sends no call ids; the parser makes them.
                toolCalls.Add(ToolCallParser.Build(string.Empty, name, raw));
            }
        }
        if (JsonRead.Bool(chunk, "done") == true)
        {
            Finished = true;
            promptTokens = JsonRead.Int(chunk, "prompt_eval_count");
            completionTokens = JsonRead.Int(chunk, "eval_count");
            long? evalNanoseconds = JsonRead.Long(chunk, "eval_duration");
            if (evalNanoseconds.HasValue && evalNanoseconds.Value > 0 && completionTokens.HasValue)
            {
                generatedPerSecond = completionTokens.Value / (evalNanoseconds.Value / 1e9);
            }
            if (JsonRead.String(chunk, "done_reason") == "length")
            {
                truncated = true;
            }
        }
    }

    public ChatResult ToResult()
    {
        ChatResult result = new ChatResult(content.ToString(), toolCalls);
        string thought = thinking.ToString();
        if (thought.Length > 0)
        {
            result.Thinking = thought;
        }
        result.PromptTokens = promptTokens;
        // This turn's reasoning is sent back in the next request, so the whole eval count travels.
        result.CompletionTokens = completionTokens;
        if (!completionTokens.HasValue)
        {
            result.CompletionTokens = ToolCallParser.EstimateReplayTokens(result.Content, toolCalls) + (int)Math.Ceiling(thought.Length / 4.0);
        }
        result.GeneratedTokens = completionTokens;
        result.GeneratedTokensPerSecond = generatedPerSecond;
        result.Truncated = truncated;
        if (firstTokenAt.HasValue)
        {
            result.TimeToFirstTokenMilliseconds = firstTokenAt.Value;
        }
        return result;
    }

    private void MarkToken()
    {
        if (!firstTokenAt.HasValue)
        {
            firstTokenAt = clock.ElapsedMilliseconds;
        }
    }
}
