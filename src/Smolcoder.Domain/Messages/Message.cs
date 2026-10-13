namespace Smolcoder.Domain.Messages;

/// <summary>
/// One message in the conversation.
/// Each provider converts it to its own wire format.
/// </summary>
public sealed class Message
{
    public Message(MessageRole role, string content)
    {
        Role = role;
        Content = content;
        Images = new List<ImageReference>();
        ToolCalls = new List<ToolCall>();
    }

    public MessageRole Role { get; set; }

    public string Content { get; set; }

    /// <summary>
    /// Images attached to a user turn.
    /// </summary>
    public List<ImageReference> Images { get; set; }

    public List<ToolCall> ToolCalls { get; set; }

    /// <summary>
    /// For a tool message: the id of the call it answers.
    /// </summary>
    public string? ToolCallId { get; set; }

    public string? ToolName { get; set; }

    /// <summary>
    /// The assistant's reasoning trace, when the model streamed one.
    /// </summary>
    public string? Thinking { get; set; }

    /// <summary>
    /// True when the body of a tool result was removed to save context.
    /// </summary>
    public bool Evicted { get; set; }

    /// <summary>
    /// True for the hand-over note made by compaction.
    /// </summary>
    public bool CompactNote { get; set; }

    /// <summary>
    /// True for harness records of earlier tools. It is never a real user turn.
    /// </summary>
    public bool HistoryNote { get; set; }

    public bool HasToolCalls
    {
        get
        {
            return ToolCalls.Count > 0;
        }
    }

    /// <summary>
    /// True for a real user turn (not a compaction note or history record).
    /// </summary>
    public bool IsPlainUserTurn
    {
        get
        {
            return Role == MessageRole.User && !CompactNote && !HistoryNote;
        }
    }

    public static Message System(string content)
    {
        return new Message(MessageRole.System, content);
    }

    public static Message User(string content)
    {
        return new Message(MessageRole.User, content);
    }

    public static Message Assistant(string content)
    {
        return new Message(MessageRole.Assistant, content);
    }

    public static Message ToolResult(string toolCallId, string toolName, string content)
    {
        Message message = new Message(MessageRole.Tool, content);
        message.ToolCallId = toolCallId;
        message.ToolName = toolName;
        return message;
    }

    /// <summary>
    /// Makes a deep copy, so background work never changes the live transcript.
    /// </summary>
    public Message Clone()
    {
        Message copy = new Message(Role, Content);
        copy.Images = new List<ImageReference>(Images);
        copy.ToolCalls = ToolCalls.Select(call => call.Clone()).ToList();
        copy.ToolCallId = ToolCallId;
        copy.ToolName = ToolName;
        copy.Thinking = Thinking;
        copy.Evicted = Evicted;
        copy.CompactNote = CompactNote;
        copy.HistoryNote = HistoryNote;
        return copy;
    }

    /// <summary>
    /// Index of the latest real user turn, or -1.
    /// </summary>
    public static int LastUserIndex(IReadOnlyList<Message> messages)
    {
        for (int index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].IsPlainUserTurn)
            {
                return index;
            }
        }
        return -1;
    }
}
