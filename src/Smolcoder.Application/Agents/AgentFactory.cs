using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Tools;

namespace Smolcoder.Application.Agents;

public sealed class AgentFactory : IAgentFactory
{
    private readonly IToolRegistry toolRegistry;
    private readonly IToolDispatcher toolDispatcher;
    private readonly IWorkspaceSandbox sandbox;
    private readonly IShellRunner shellRunner;
    private readonly IProjectInspector projectInspector;
    private readonly IAttachmentStore attachmentStore;

    public AgentFactory(
        IToolRegistry toolRegistry,
        IToolDispatcher toolDispatcher,
        IWorkspaceSandbox sandbox,
        IShellRunner shellRunner,
        IProjectInspector projectInspector,
        IAttachmentStore attachmentStore)
    {
        this.toolRegistry = toolRegistry;
        this.toolDispatcher = toolDispatcher;
        this.sandbox = sandbox;
        this.shellRunner = shellRunner;
        this.projectInspector = projectInspector;
        this.attachmentStore = attachmentStore;
    }

    public IAgentService Create(AgentCreationOptions options)
    {
        return new AgentService(options, toolRegistry, toolDispatcher, sandbox, shellRunner, projectInspector, attachmentStore);
    }
}
