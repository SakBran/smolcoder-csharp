using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Application.Tools;

/// <summary>
/// Session state the tools work with.
/// </summary>
public sealed class ToolContext
{
    public ToolContext(string workspace, IBackgroundTaskManager taskManager, Plan plan)
    {
        Workspace = workspace;
        TaskManager = taskManager;
        Plan = plan;
        FilesTouched = new List<string>();
        CommandsRun = new List<string>();
    }

    public string Workspace { get; }

    public IBackgroundTaskManager TaskManager { get; }

    public Plan Plan { get; }

    /// <summary>
    /// Files created or changed, in first-touched order. Used by the compaction note.
    /// </summary>
    public List<string> FilesTouched { get; }

    /// <summary>
    /// Recent commands with their last output line. At most 50 are kept.
    /// </summary>
    public List<string> CommandsRun { get; }

    /// <summary>
    /// Internal cap on a tool result. Never set by the model.
    /// </summary>
    public int? ResultCharLimit { get; set; }

    public void AddFileTouched(string path)
    {
        if (!FilesTouched.Contains(path))
        {
            FilesTouched.Add(path);
        }
    }
}
