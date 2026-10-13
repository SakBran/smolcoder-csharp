using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Agents;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Sessions;

public sealed class AgentSessionRequest
{
    public AgentSessionRequest(string workspace, ModelDescriptor model, PermissionMode mode, IUserInteraction userInteraction)
    {
        Workspace = workspace;
        Model = model;
        Mode = mode;
        UserInteraction = userInteraction;
        MaxSteps = 30;
    }

    public string Workspace { get; }

    public ModelDescriptor Model { get; }

    public PermissionMode Mode { get; }

    public Effort? Effort { get; set; }

    public IUserInteraction UserInteraction { get; }

    public bool Interactive { get; set; }

    public int MaxSteps { get; set; }

    public VerificationSettings? Verification { get; set; }
}
