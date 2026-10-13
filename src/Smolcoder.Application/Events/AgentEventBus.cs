namespace Smolcoder.Application.Events;

public sealed class AgentEventBus : IAgentEventBus
{
    private readonly Dictionary<AgentEventKind, List<Func<object?, Task>>> handlers = new Dictionary<AgentEventKind, List<Func<object?, Task>>>();

    public void On(AgentEventKind kind, Func<object?, Task> handler)
    {
        List<Func<object?, Task>>? list;
        if (!handlers.TryGetValue(kind, out list))
        {
            list = new List<Func<object?, Task>>();
            handlers[kind] = list;
        }
        list.Add(handler);
    }

    public async Task EmitAsync(AgentEventKind kind, object? payload)
    {
        List<Func<object?, Task>>? list;
        if (!handlers.TryGetValue(kind, out list))
        {
            return;
        }
        foreach (Func<object?, Task> handler in list.ToList())
        {
            await handler(payload);
        }
    }
}
