using System.Text;
using System.Text.RegularExpressions;

namespace Smolcoder.Domain.Planning;

/// <summary>
/// The agent's to-do list. It lives in the harness, not in the conversation,
/// so it survives compaction.
/// </summary>
public sealed class Plan
{
    public const int MaxSteps = 20;
    public const int MaxCheckpointChars = 1000;

    private static readonly Regex StepPrefix = new Regex(@"^\s*(?:[-*]|\d+[.)])?\s*(?:\[.\]\s*)?", RegexOptions.Compiled);
    private static readonly Regex SemicolonSplit = new Regex(@";\s*", RegexOptions.Compiled);

    private readonly List<PlanStep> steps = new List<PlanStep>();

    public IReadOnlyList<PlanStep> Steps
    {
        get
        {
            return steps;
        }
    }

    public bool Exists
    {
        get
        {
            return steps.Count > 0;
        }
    }

    public int DoneCount
    {
        get
        {
            return steps.Count(step => step.Done);
        }
    }

    /// <summary>
    /// Index of the first step that is not done, or -1.
    /// </summary>
    public int CurrentIndex
    {
        get
        {
            for (int index = 0; index < steps.Count; index++)
            {
                if (!steps[index].Done)
                {
                    return index;
                }
            }
            return -1;
        }
    }

    public void Clear()
    {
        steps.Clear();
    }

    /// <summary>
    /// Replaces the plan with restored steps (used when a session resumes).
    /// </summary>
    public void Restore(IEnumerable<PlanStep> restoredSteps)
    {
        steps.Clear();
        foreach (PlanStep step in restoredSteps)
        {
            if (step.Note != null && step.Note.Length > MaxCheckpointChars)
            {
                step.Note = step.Note.Substring(0, MaxCheckpointChars);
            }
            steps.Add(step);
        }
    }

    public string Set(string stepsText)
    {
        string[] lines;
        if (stepsText.Contains('\n'))
        {
            lines = stepsText.Split('\n');
        }
        else
        {
            lines = SemicolonSplit.Split(stepsText);
        }

        List<string> cleaned = new List<string>();
        foreach (string line in lines)
        {
            string text = StepPrefix.Replace(line, string.Empty, 1).Trim();
            if (text.Length > 0)
            {
                cleaned.Add(text);
            }
            if (cleaned.Count == MaxSteps)
            {
                break;
            }
        }

        if (cleaned.Count == 0)
        {
            return "Error: steps is required — one step per line. Example: {\"action\": \"set\", \"steps\": \"create index.html\\ncreate game.js\\ntest the page\"}";
        }

        steps.Clear();
        foreach (string text in cleaned)
        {
            steps.Add(new PlanStep(text));
        }
        return "Plan set (" + steps.Count + " steps). Current: 1. " + steps[0].Text;
    }

    public string MarkDone(int? stepNumber)
    {
        if (!Exists)
        {
            return "Error: no plan yet. Create one first with {\"action\": \"set\", \"steps\": \"...\"}";
        }

        int index;
        if (stepNumber.HasValue)
        {
            index = stepNumber.Value - 1;
            if (index < 0 || index >= steps.Count)
            {
                return "Error: step " + stepNumber.Value + " does not exist. The plan has " + steps.Count + " steps.";
            }
        }
        else
        {
            index = CurrentIndex;
            if (index < 0)
            {
                return "All steps are already done.";
            }
        }

        steps[index].Done = true;
        int next = CurrentIndex;
        if (next < 0)
        {
            return "Done: " + (index + 1) + ". All " + steps.Count + " steps complete.";
        }
        return "Done: " + (index + 1) + ". Next: " + (next + 1) + ". " + steps[next].Text;
    }

    public string Add(string? text)
    {
        string value = string.Empty;
        if (text != null)
        {
            value = text.Trim();
        }
        if (value.Length == 0)
        {
            return "Error: text is required. Example: {\"action\": \"add\", \"text\": \"fix the collision bug\"}";
        }
        if (steps.Count >= MaxSteps)
        {
            return "Error: the plan already has 20 steps — finish some first.";
        }
        steps.Add(new PlanStep(value));
        return "Added step " + steps.Count + ": " + value;
    }

    public string Checkpoint(string? text)
    {
        int index = CurrentIndex;
        if (index < 0)
        {
            return "Error: create a plan with an unfinished step before recording a checkpoint.";
        }
        string value = string.Empty;
        if (text != null)
        {
            value = text.Trim();
        }
        if (value.Length == 0 || value.Length > MaxCheckpointChars)
        {
            return "Error: checkpoint text must be 1–1000 characters. Keep exact APIs, the unresolved error and the next small edit.";
        }
        steps[index].Note = value;
        return "Checkpoint saved for step " + (index + 1) + ": " + value;
    }

    /// <summary>
    /// The checklist as the model sees it.
    /// </summary>
    public string ModelView()
    {
        if (!Exists)
        {
            return "No plan set.";
        }
        int current = CurrentIndex;
        StringBuilder builder = new StringBuilder();
        for (int index = 0; index < steps.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }
            string mark;
            if (steps[index].Done)
            {
                mark = "[x]";
            }
            else if (index == current)
            {
                mark = "[>]";
            }
            else
            {
                mark = "[ ]";
            }
            builder.Append(index + 1).Append('.').Append(mark).Append(' ').Append(steps[index].Text);
            if (index == current && steps[index].Note != null)
            {
                builder.Append("\nWorking checkpoint (agent notes; verify against files): ").Append(steps[index].Note);
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// The plan line kept in compaction notes, or null when there is no plan.
    /// </summary>
    public string? CompactLine()
    {
        if (!Exists)
        {
            return null;
        }
        return "Plan (" + DoneCount + "/" + steps.Count + " done):\n" + ModelView();
    }

    public string PendingSummary()
    {
        return string.Join("; ", steps.Where(step => !step.Done).Select(step => step.Text));
    }
}
