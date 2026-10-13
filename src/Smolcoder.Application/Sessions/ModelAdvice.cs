using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// One-line tips shown after a model or effort is chosen, or after an error.
/// </summary>
public static class ModelAdvice
{
    /// <summary>
    /// A warning about what the current effort will cost on this model, or null.
    /// </summary>
    /// <param name="flagStyle">True for the command line (--effort), false for the slash command (/effort).</param>
    public static string? Effort(ModelDescriptor model, Effort? effort, bool flagStyle)
    {
        string command = flagStyle ? "--effort off" : "/effort off";
        if (model.Backend == Backends.DeepSeek && effort != Domain.Modes.Effort.Off)
        {
            return "DeepSeek thinks before every tool call and bills those tokens as output. " + command + " is cheapest for long tool loops.";
        }
        if (model.Backend == Backends.LmStudio && !effort.HasValue && (model.ReasoningDefault == "high" || model.ReasoningDefault == "xhigh"))
        {
            return "This model thinks at \"" + model.ReasoningDefault + "\" by default, which is slow for tool loops. Try " + command + " or a lower level.";
        }
        if (model.SupportsTools == false)
        {
            return ToolsHint(model.Id);
        }
        return null;
    }

    /// <summary>
    /// A plain explanation for a known error, or null.
    /// </summary>
    public static string? ForError(string message)
    {
        if (message.Contains("does not support tools", StringComparison.OrdinalIgnoreCase))
        {
            return "This model cannot call tools, so it cannot read or edit files. Pick another model with /models (for example qwen3, llama3.1 or mistral-nemo).";
        }
        return null;
    }

    private static string ToolsHint(string id)
    {
        return id + " cannot call tools, so it cannot read or edit files. Pick a tool-capable model such as qwen3, llama3.1 or mistral-nemo.";
    }
}
