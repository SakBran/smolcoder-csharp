using Smolcoder.Application.Agents;
using Smolcoder.Application.Context;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// What the status bar shows for one session.
/// </summary>
public sealed class SessionState
{
    public SessionState(string workspace, string model, string backend, ContextBudget context, PermissionMode mode)
    {
        Workspace = workspace;
        Model = model;
        Backend = backend;
        Context = context;
        Mode = mode;
        PlanSteps = new List<PlanStep>();
        Urls = new List<string>();
    }

    public string Workspace { get; }

    public string Model { get; }

    public string Backend { get; }

    public string? Host { get; set; }

    public bool? SupportsVision { get; set; }

    public string? EffortLabel { get; set; }

    public ContextBudget Context { get; }

    public int ContextPercent { get; set; }

    public PermissionMode Mode { get; }

    public AgentOutcome Outcome { get; set; }

    public string? LastError { get; set; }

    public List<PlanStep> PlanSteps { get; set; }

    public int PlanCurrent { get; set; }

    public int RunningTasks { get; set; }

    public List<string> Urls { get; set; }
}
