using Smolcoder.Application.Abstractions.Interaction;

namespace Smolcoder.Web.Services;

/// <summary>
/// One row of the chat transcript. Saved as JSON so a session can be shown again after a restart.
/// </summary>
public sealed class UiEvent
{
    public UiEvent()
    {
        Text = string.Empty;
        Files = new List<UiFile>();
        Options = new List<SelectOption>();
        PlanSteps = new List<UiPlanStep>();
    }

    public UiEventKind Kind { get; set; }

    /// <summary>
    /// The main text: message, token stream, tool summary, status line, and so on.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// For Tool: the tool name. For Line: "status", "warn" or "error".
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// For Result: the full tool output.
    /// </summary>
    public string? Body { get; set; }

    public bool IsError { get; set; }

    public List<UiFile> Files { get; set; }

    /// <summary>
    /// For Confirm, Select and Prompt: the question id.
    /// </summary>
    public int AskId { get; set; }

    public string? Reason { get; set; }

    public List<SelectOption> Options { get; set; }

    public string? Placeholder { get; set; }

    /// <summary>
    /// True once a question was answered or cancelled.
    /// </summary>
    public bool Answered { get; set; }

    public string? Answer { get; set; }

    public List<UiPlanStep> PlanSteps { get; set; }

    public int PlanCurrent { get; set; }
}
