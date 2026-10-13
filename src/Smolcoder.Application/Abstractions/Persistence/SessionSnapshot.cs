using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Everything needed to resume a session after a restart.
/// </summary>
public sealed class SessionSnapshot
{
    public SessionSnapshot()
    {
        Messages = new List<Message>();
        PlanSteps = new List<PlanStep>();
        FilesTouched = new List<string>();
        CommandsRun = new List<string>();
        OriginalRequest = string.Empty;
        CurrentRequest = string.Empty;
        Mode = PermissionMode.Edit;
    }

    /// <summary>
    /// The transcript without its system message.
    /// </summary>
    public List<Message> Messages { get; set; }

    public List<PlanStep> PlanSteps { get; set; }

    public List<string> FilesTouched { get; set; }

    public List<string> CommandsRun { get; set; }

    public string OriginalRequest { get; set; }

    public string CurrentRequest { get; set; }

    public PermissionMode Mode { get; set; }

    public Effort? Effort { get; set; }

    public string? Model { get; set; }

    public string? Backend { get; set; }

    public string? BaseUrl { get; set; }
}
