using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Domain.Text;

namespace Smolcoder.Application.Tools;

/// <summary>
/// Runs the tool the model asked for.
/// </summary>
public sealed class ToolDispatcher : IToolDispatcher
{
    /// <summary>
    /// Final safety net over the per-tool caps.
    /// </summary>
    private const int ToolResultCap = 10000;

    private readonly IFileSystemTools fileSystemTools;
    private readonly IShellRunner shellRunner;
    private readonly IWorkspaceSandbox sandbox;
    private readonly ISyntaxChecker syntaxChecker;

    public ToolDispatcher(IFileSystemTools fileSystemTools, IShellRunner shellRunner, IWorkspaceSandbox sandbox, ISyntaxChecker syntaxChecker)
    {
        this.fileSystemTools = fileSystemTools;
        this.shellRunner = shellRunner;
        this.sandbox = sandbox;
        this.syntaxChecker = syntaxChecker;
    }

    public async Task<string> ExecuteAsync(string name, JsonObject arguments, ToolContext context, CancellationToken cancellationToken)
    {
        try
        {
            string result;
            if (name == ToolNames.ReadFile)
            {
                int? maxChars = null;
                if (context.ResultCharLimit.HasValue)
                {
                    maxChars = context.ResultCharLimit.Value - 256;
                }
                result = fileSystemTools.ReadFile(context.Workspace, arguments, maxChars);
            }
            else if (name == ToolNames.ListFiles)
            {
                result = fileSystemTools.ListFiles(context.Workspace, arguments);
            }
            else if (name == ToolNames.Search)
            {
                result = await fileSystemTools.SearchAsync(context.Workspace, arguments, cancellationToken);
            }
            else if (name == ToolNames.Plan)
            {
                result = RunPlan(arguments, context);
                if (result.StartsWith("Error: action must be", StringComparison.Ordinal))
                {
                    return result;
                }
            }
            else if (name == ToolNames.WriteFile)
            {
                result = fileSystemTools.WriteFile(context.Workspace, arguments);
                if (!result.StartsWith("Error", StringComparison.Ordinal))
                {
                    string path = JsonArguments.GetText(arguments, "path");
                    context.AddFileTouched(path);
                    result = result + AfterWrite(context.Workspace, path);
                }
            }
            else if (name == ToolNames.EditFile)
            {
                result = fileSystemTools.EditFile(context.Workspace, arguments);
                if (!result.StartsWith("Error", StringComparison.Ordinal))
                {
                    string path = JsonArguments.GetText(arguments, "path");
                    context.AddFileTouched(path);
                    result = result + AfterWrite(context.Workspace, path);
                }
            }
            else if (name == ToolNames.RunCommand)
            {
                string? command = JsonArguments.GetString(arguments, "command");
                if (command == null || command.Trim().Length == 0)
                {
                    return "Error: command is required. Example: {\"command\": \"npm test\"}";
                }
                result = await shellRunner.RunCommandAsync(command, context.Workspace, cancellationToken);
                string[] lines = result.Split('\n');
                context.CommandsRun.Add(command + " → " + lines[lines.Length - 1]);
                if (context.CommandsRun.Count > 50)
                {
                    context.CommandsRun.RemoveRange(0, context.CommandsRun.Count - 50);
                }
            }
            else if (name == ToolNames.Task)
            {
                string? action = JsonArguments.GetString(arguments, "action");
                if (action == "start")
                {
                    string? command = JsonArguments.GetString(arguments, "command");
                    if (command == null || command.Trim().Length == 0)
                    {
                        return "Error: \"start\" needs a command. Example: {\"action\": \"start\", \"command\": \"npm run dev\"}";
                    }
                    context.CommandsRun.Add("[bg] " + command);
                    result = await context.TaskManager.StartWithEarlyOutputAsync(command, cancellationToken);
                }
                else if (action == "logs")
                {
                    int lines = 50;
                    double? requested = JsonArguments.GetNumber(arguments, "lines");
                    if (requested.HasValue && requested.Value != 0)
                    {
                        lines = (int)requested.Value;
                    }
                    result = context.TaskManager.Logs(JsonArguments.GetText(arguments, "task_id"), lines);
                }
                else if (action == "stop")
                {
                    result = context.TaskManager.Stop(JsonArguments.GetText(arguments, "task_id"));
                }
                else if (action == "list")
                {
                    result = context.TaskManager.List();
                }
                else
                {
                    return "Error: action must be one of \"start\", \"list\", \"logs\", \"stop\". Example: {\"action\": \"list\"}";
                }
            }
            else
            {
                return "Error: unknown tool \"" + name + "\". Available tools are listed in your tool definitions — use one of those.";
            }
            return TextTruncation.Middle(result, ToolResultCap);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return "Error: " + exception.Message;
        }
    }

    public string? CommandOf(string name, JsonObject arguments)
    {
        if (name == ToolNames.RunCommand)
        {
            return JsonArguments.GetText(arguments, "command");
        }
        if (name == ToolNames.Task && JsonArguments.GetString(arguments, "action") == "start")
        {
            return JsonArguments.GetText(arguments, "command");
        }
        return null;
    }

    /// <summary>
    /// The plan action the call means. A call with only "steps" means "set".
    /// </summary>
    public static string? PlanAction(JsonObject arguments)
    {
        if (JsonArguments.Has(arguments, "action"))
        {
            return JsonArguments.GetText(arguments, "action");
        }
        if (JsonArguments.GetString(arguments, "steps") != null)
        {
            return "set";
        }
        return null;
    }

    private static string RunPlan(JsonObject arguments, ToolContext context)
    {
        string? action = PlanAction(arguments);
        if (action == "set")
        {
            string? steps = JsonArguments.GetString(arguments, "steps");
            if (steps == null)
            {
                steps = string.Empty;
            }
            return context.Plan.Set(steps);
        }
        if (action == "done")
        {
            int? stepNumber = null;
            double? requested = JsonArguments.GetNumber(arguments, "step");
            if (requested.HasValue)
            {
                stepNumber = (int)requested.Value;
            }
            return context.Plan.MarkDone(stepNumber);
        }
        if (action == "add")
        {
            return context.Plan.Add(JsonArguments.GetText(arguments, "text"));
        }
        if (action == "checkpoint")
        {
            return context.Plan.Checkpoint(JsonArguments.GetText(arguments, "text"));
        }
        if (action == "show")
        {
            return context.Plan.ModelView();
        }
        return "Error: action must be one of \"set\", \"done\", \"add\", \"show\", \"checkpoint\". Example: {\"action\": \"done\"}";
    }

    /// <summary>
    /// Checks what was just written and warns about the first syntax error.
    /// </summary>
    private string AfterWrite(string workspace, string relativePath)
    {
        try
        {
            string absolutePath = sandbox.ResolveInWorkspace(workspace, relativePath);
            string? warning = syntaxChecker.Check(absolutePath, relativePath);
            if (warning == null)
            {
                return string.Empty;
            }
            return "\nWarning: " + warning + " Fix this before moving on (use edit_file).";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
