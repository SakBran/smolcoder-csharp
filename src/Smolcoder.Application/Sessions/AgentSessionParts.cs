using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Agents;
using Smolcoder.Application.Context;
using Smolcoder.Application.Events;
using Smolcoder.Application.Tools;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// The objects that make up one working session.
/// </summary>
public sealed class AgentSessionParts
{
    public AgentSessionParts(IAgentService agent, IModelProvider provider, ToolContext toolContext, IContextManager contextManager, IAgentEventBus eventBus)
    {
        Agent = agent;
        Provider = provider;
        ToolContext = toolContext;
        ContextManager = contextManager;
        EventBus = eventBus;
    }

    public IAgentService Agent { get; }

    public IModelProvider Provider { get; }

    public ToolContext ToolContext { get; }

    public IContextManager ContextManager { get; }

    public IAgentEventBus EventBus { get; }

    public IBackgroundTaskManager TaskManager
    {
        get
        {
            return ToolContext.TaskManager;
        }
    }
}
