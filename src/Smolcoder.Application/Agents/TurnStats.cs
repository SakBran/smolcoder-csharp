using System.Globalization;

namespace Smolcoder.Application.Agents;

/// <summary>
/// Speed and size figures for one turn.
/// </summary>
public sealed class TurnStats
{
    public int ModelCalls { get; set; }

    public int ToolCalls { get; set; }

    /// <summary>
    /// All tokens the model produced this turn, reasoning included.
    /// </summary>
    public int GeneratedTokens { get; set; }

    public double GenerationSeconds { get; set; }

    public int ThinkingChars { get; set; }

    /// <summary>
    /// Prompt size of the last request.
    /// </summary>
    public int PromptTokensLast { get; set; }

    /// <summary>
    /// Prompt tokens sent across every call this turn. This is what a cloud API bills.
    /// </summary>
    public int PromptTokensTotal { get; set; }

    public int CacheHitTokens { get; set; }

    public long DurationMilliseconds { get; set; }

    /// <summary>
    /// " · 12 tools · 4.1k tok @ 118 tok/s"
    /// </summary>
    public string Describe()
    {
        List<string> parts = new List<string>();
        if (ToolCalls > 0)
        {
            if (ToolCalls == 1)
            {
                parts.Add("1 tool");
            }
            else
            {
                parts.Add(ToolCalls + " tools");
            }
        }
        if (GeneratedTokens > 0)
        {
            string amount;
            if (GeneratedTokens >= 1000)
            {
                amount = (GeneratedTokens / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k";
            }
            else
            {
                amount = GeneratedTokens.ToString(CultureInfo.InvariantCulture);
            }
            string rate = string.Empty;
            if (GenerationSeconds > 0)
            {
                rate = " @ " + Math.Round(GeneratedTokens / GenerationSeconds) + " tok/s";
            }
            parts.Add(amount + " tok" + rate);
        }
        if (parts.Count == 0)
        {
            return string.Empty;
        }
        return " · " + string.Join(" · ", parts);
    }
}
