namespace Smolcoder.Application.Events;

/// <summary>
/// A small event bus for one session. Handlers run one after another, in order.
/// </summary>
public interface IAgentEventBus
{
    void On(AgentEventKind kind, Func<object?, Task> handler);

    Task EmitAsync(AgentEventKind kind, object? payload);
}
