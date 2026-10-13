using Smolcoder.Domain.Messages;

namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// What one model call produced.
/// </summary>
public sealed class ChatResult
{
    public ChatResult(string content, List<ToolCall> toolCalls)
    {
        Content = content;
        ToolCalls = toolCalls;
    }

    public string Content { get; set; }

    public List<ToolCall> ToolCalls { get; set; }

    /// <summary>
    /// The reasoning trace, when the backend streamed one.
    /// </summary>
    public string? Thinking { get; set; }

    /// <summary>
    /// Real prompt size reported by the backend.
    /// </summary>
    public int? PromptTokens { get; set; }

    /// <summary>
    /// Tokens of this reply that will be sent again in the next prompt.
    /// </summary>
    public int? CompletionTokens { get; set; }

    /// <summary>
    /// All tokens generated, reasoning included.
    /// </summary>
    public int? GeneratedTokens { get; set; }

    public double? GeneratedTokensPerSecond { get; set; }

    /// <summary>
    /// Prompt tokens served from the backend's prefix cache.
    /// </summary>
    public int? CacheHitTokens { get; set; }

    public long? TimeToFirstTokenMilliseconds { get; set; }

    /// <summary>
    /// True when the reply hit the output-token cap and was cut off.
    /// </summary>
    public bool Truncated { get; set; }
}
