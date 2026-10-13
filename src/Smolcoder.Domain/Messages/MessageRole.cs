namespace Smolcoder.Domain.Messages;

/// <summary>
/// Who wrote a message in the conversation.
/// </summary>
public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}
