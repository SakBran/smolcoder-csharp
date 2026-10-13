namespace Smolcoder.Application.Context;

/// <summary>
/// Facts the harness knows for sure. They go into the compaction note.
/// </summary>
public sealed class CompactState
{
    public CompactState(string originalRequest, string currentRequest, List<string> filesTouched, List<string> commandsRun)
    {
        OriginalRequest = originalRequest;
        CurrentRequest = currentRequest;
        FilesTouched = filesTouched;
        CommandsRun = commandsRun;
        VerificationLine = string.Empty;
    }

    public string OriginalRequest { get; }

    public string CurrentRequest { get; }

    public List<string> FilesTouched { get; }

    public List<string> CommandsRun { get; }

    public string? PlanLine { get; set; }

    public string VerificationLine { get; set; }

    /// <summary>
    /// A copy that background work can read while the session keeps changing.
    /// </summary>
    public CompactState Freeze()
    {
        CompactState copy = new CompactState(OriginalRequest, CurrentRequest, new List<string>(FilesTouched), new List<string>(CommandsRun));
        copy.PlanLine = PlanLine;
        copy.VerificationLine = VerificationLine;
        return copy;
    }
}
