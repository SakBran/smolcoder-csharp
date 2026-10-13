using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Context;
using Smolcoder.Application.Events;
using Smolcoder.Application.Tools;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Agents;

/// <summary>
/// Everything that belongs to one session and is handed to a new agent.
/// </summary>
public sealed class AgentCreationOptions
{
    public AgentCreationOptions(
        IModelProvider provider,
        PermissionMode mode,
        string systemPrompt,
        ToolContext toolContext,
        IContextManager contextManager,
        IAgentEventBus eventBus,
        IUserInteraction userInteraction)
    {
        Provider = provider;
        Mode = mode;
        SystemPrompt = systemPrompt;
        ToolContext = toolContext;
        ContextManager = contextManager;
        EventBus = eventBus;
        UserInteraction = userInteraction;
        MaxSteps = 30;
    }

    public IModelProvider Provider { get; }

    public PermissionMode Mode { get; }

    public string SystemPrompt { get; }

    public ToolContext ToolContext { get; }

    public IContextManager ContextManager { get; }

    public IAgentEventBus EventBus { get; }

    public IUserInteraction UserInteraction { get; }

    /// <summary>
    /// True when a person can answer approval questions.
    /// </summary>
    public bool Interactive { get; set; }

    /// <summary>
    /// Model calls allowed per user turn.
    /// </summary>
    public int MaxSteps { get; set; }

    public VerificationSettings? CallerVerification { get; set; }
}
