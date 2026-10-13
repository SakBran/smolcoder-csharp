using Smolcoder.Domain.Modes;

namespace Smolcoder.Infrastructure.Providers.LmStudio;

/// <summary>
/// LM Studio accepts reasoning_effort none|minimal|low|medium|high|xhigh, but each model supports
/// only some of them. An unsupported value silently becomes the model's default, which for
/// current qwen builds is xhigh, the maximum. So "off" is sent as "none" (which really disables
/// thinking) and every other level snaps to the nearest level the model has.
/// </summary>
public static class LmStudioReasoning
{
    private static readonly string[] WireLevels = new string[] { "low", "medium", "high", "xhigh" };

    /// <summary>
    /// The wire value for an effort, or null to leave it to the server.
    /// </summary>
    public static string? MapEffort(Effort? effort, IReadOnlyList<string>? allowed)
    {
        if (!effort.HasValue)
        {
            return null;
        }
        if (effort.Value == Effort.Off)
        {
            return "none";
        }
        string wanted = Efforts.Name(effort);
        if (allowed == null || allowed.Count == 0)
        {
            return wanted;
        }
        // The model list also uses "off"/"on", which are not wire values.
        List<string> candidates = new List<string>();
        foreach (string level in WireLevels)
        {
            if (allowed.Contains(level))
            {
                candidates.Add(level);
            }
        }
        if (candidates.Count == 0 || candidates.Contains(wanted))
        {
            return wanted;
        }
        int want = Rank(wanted);
        string best = candidates[0];
        int bestDistance = int.MaxValue;
        foreach (string candidate in candidates)
        {
            int distance = Math.Abs(Rank(candidate) - want);
            // Ties go to the lower level: on a local model the cheaper step wins.
            if (distance < bestDistance || (distance == bestDistance && Rank(candidate) < Rank(best)))
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static int Rank(string level)
    {
        if (level == "low")
        {
            return 1;
        }
        if (level == "medium")
        {
            return 2;
        }
        if (level == "high")
        {
            return 3;
        }
        return 4;
    }
}
