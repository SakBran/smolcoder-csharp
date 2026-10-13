namespace Smolcoder.Application.Events;

/// <summary>
/// Points in the agent's life that other parts can listen to.
/// </summary>
public enum AgentEventKind
{
    SessionStart,
    PreRequest,
    PostTool,
    PostVerify,
    PostProgressCheck,
    ContextUpdate,
    PreCompact,
    PostCompact,
    SessionEnd
}
