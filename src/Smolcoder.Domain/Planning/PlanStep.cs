namespace Smolcoder.Domain.Planning;

/// <summary>
/// One step of the agent's plan.
/// </summary>
public sealed class PlanStep
{
    public PlanStep(string text)
    {
        Text = text;
    }

    public string Text { get; set; }

    public bool Done { get; set; }

    /// <summary>
    /// Working checkpoint notes for this step, written by the agent.
    /// </summary>
    public string? Note { get; set; }
}
