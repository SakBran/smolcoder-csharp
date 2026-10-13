using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Smolcoder.Application.Tools;

/// <summary>
/// A short, readable summary of a tool call for the user, like "src/app.js from line 40".
/// </summary>
public static class ToolCallSummary
{
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    public static string Summarize(string name, JsonObject arguments)
    {
        try
        {
            string? raw = JsonArguments.GetString(arguments, "__raw");
            if (raw != null)
            {
                return "(arguments could not be parsed: " + Whitespace.Replace(raw, " ") + "…)";
            }
            if (name == ToolNames.ReadFile)
            {
                string text = JsonArguments.GetText(arguments, "path");
                if (JsonArguments.Has(arguments, "offset"))
                {
                    text = text + " from line " + JsonArguments.GetText(arguments, "offset");
                }
                return text;
            }
            if (name == ToolNames.WriteFile)
            {
                int lines = JsonArguments.GetText(arguments, "content").Split('\n').Length;
                return JsonArguments.GetText(arguments, "path") + " (" + lines + " lines)";
            }
            if (name == ToolNames.EditFile)
            {
                return JsonArguments.GetText(arguments, "path");
            }
            if (name == ToolNames.ListFiles)
            {
                string path = JsonArguments.GetText(arguments, "path");
                if (path.Length == 0)
                {
                    return ".";
                }
                return path;
            }
            if (name == ToolNames.Search)
            {
                string text = "\"" + JsonArguments.GetText(arguments, "pattern") + "\"";
                string path = JsonArguments.GetText(arguments, "path");
                if (path.Length > 0)
                {
                    text = text + " in " + path;
                }
                return text;
            }
            if (name == ToolNames.RunCommand)
            {
                return JsonArguments.GetText(arguments, "command");
            }
            if (name == ToolNames.Task)
            {
                string action = JsonArguments.GetText(arguments, "action");
                string target = JsonArguments.GetText(arguments, "command");
                if (target.Length == 0)
                {
                    target = JsonArguments.GetText(arguments, "task_id");
                }
                return (action + " " + target).Trim();
            }
            if (name == ToolNames.Plan)
            {
                string action = JsonArguments.GetText(arguments, "action");
                if (action == "set")
                {
                    string steps = JsonArguments.GetText(arguments, "steps");
                    char separator = ';';
                    if (steps.Contains('\n'))
                    {
                        separator = '\n';
                    }
                    int count = steps.Split(separator).Count(line => line.Trim().Length > 0);
                    return "set (" + count + " steps)";
                }
                if (action == "done")
                {
                    string step = JsonArguments.GetText(arguments, "step");
                    if (step.Length > 0)
                    {
                        return "done " + step;
                    }
                    return "done";
                }
                if (action == "add")
                {
                    string text = JsonArguments.GetText(arguments, "text");
                    if (text.Length > 60)
                    {
                        text = text.Substring(0, 60);
                    }
                    return "add: " + text;
                }
                return action;
            }
            string json = arguments.ToJsonString();
            if (json.Length > 80)
            {
                json = json.Substring(0, 80);
            }
            return json;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
