using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Agents;
using Smolcoder.Application.Context;
using Smolcoder.Application.Events;
using Smolcoder.Application.Prompts;
using Smolcoder.Application.Tools;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// Puts one session together: provider, tools, context manager, event bus and agent.
/// </summary>
public sealed class AgentSessionBuilder : IAgentSessionBuilder
{
    private readonly IModelCatalog modelCatalog;
    private readonly IAgentFactory agentFactory;
    private readonly IBackgroundTaskManagerFactory taskManagerFactory;
    private readonly ISystemPromptBuilder systemPromptBuilder;
    private readonly IShellRunner shellRunner;
    private readonly IProjectInspector projectInspector;

    public AgentSessionBuilder(
        IModelCatalog modelCatalog,
        IAgentFactory agentFactory,
        IBackgroundTaskManagerFactory taskManagerFactory,
        ISystemPromptBuilder systemPromptBuilder,
        IShellRunner shellRunner,
        IProjectInspector projectInspector)
    {
        this.modelCatalog = modelCatalog;
        this.agentFactory = agentFactory;
        this.taskManagerFactory = taskManagerFactory;
        this.systemPromptBuilder = systemPromptBuilder;
        this.shellRunner = shellRunner;
        this.projectInspector = projectInspector;
    }

    public AgentSessionParts Build(AgentSessionRequest request)
    {
        IModelProvider provider = modelCatalog.CreateProvider(request.Model);
        provider.SetEffort(request.Effort);
        IBackgroundTaskManager taskManager = taskManagerFactory.Create(request.Workspace);
        ToolContext toolContext = new ToolContext(request.Workspace, taskManager, new Plan());
        IContextManager contextManager = new ContextManager(request.Model.ContextWindow, provider.MaxOutputTokens);
        IAgentEventBus eventBus = new AgentEventBus();

        AgentCreationOptions options = new AgentCreationOptions(
            provider,
            request.Mode,
            SystemPrompt(request.Workspace, request.Mode),
            toolContext,
            contextManager,
            eventBus,
            request.UserInteraction);
        options.Interactive = request.Interactive;
        options.MaxSteps = request.MaxSteps;
        options.CallerVerification = request.Verification;

        IAgentService agent = agentFactory.Create(options);
        return new AgentSessionParts(agent, provider, toolContext, contextManager, eventBus);
    }

    public string SystemPrompt(string workspace, PermissionMode mode)
    {
        string? agentsMd = projectInspector.LoadAgentsMd(workspace);
        return systemPromptBuilder.Build(workspace, mode, shellRunner.Label, agentsMd);
    }

    /// <summary>
    /// Big windows can afford whole-file writes. Tiny windows must stay careful.
    /// </summary>
    public int OutputBudget(int contextWindow)
    {
        return Math.Max(128, Math.Min(8192, contextWindow / 4));
    }
}
