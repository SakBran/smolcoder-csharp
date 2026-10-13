namespace Smolcoder.Application.Context;

/// <summary>
/// What context management did, with the estimated size before and after.
/// </summary>
public sealed class CompactionReport
{
    public CompactionReport(CompactionAction action, int before, int after)
    {
        Action = action;
        Before = before;
        After = after;
    }

    public CompactionAction Action { get; }

    public int Before { get; }

    public int After { get; }
}
