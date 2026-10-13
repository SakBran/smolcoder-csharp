using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Tools;

/// <summary>
/// Eight tools with flat parameters and an example call in every description.
/// Small models copy examples better than they infer from schemas.
/// The mode decides which schemas are sent. The agent checks the mode again when a tool runs.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    public IReadOnlyList<ToolSpec> BuildToolSpecs(PermissionMode mode)
    {
        List<ToolSpec> tools = new List<ToolSpec>();

        tools.Add(new ToolSpec(
            ToolNames.ReadFile,
            "Read a text file in the workspace. Example: {\"path\": \"src/app.js\"}. Long files are returned in chunks; pass \"offset\" (a line number) to continue reading.",
            Schema(
                new JsonObject
                {
                    ["path"] = Property("string", "File path relative to the workspace"),
                    ["offset"] = Property("number", "Line number to start from (optional)"),
                    ["limit"] = Property("number", "Max lines to return (optional)")
                },
                new string[] { "path" })));

        tools.Add(new ToolSpec(
            ToolNames.ListFiles,
            "List files and folders in the workspace. Example: {} for everything, or {\"path\": \"src\"} for one folder. Folders end with \"/\".",
            Schema(
                new JsonObject
                {
                    ["path"] = Property("string", "Folder to list (optional, default: whole workspace)")
                },
                null)));

        tools.Add(new ToolSpec(
            ToolNames.Search,
            "Search inside files for a pattern (regular expression; plain text also works). Example: {\"pattern\": \"TODO\"}. Returns file:line: matching text.",
            Schema(
                new JsonObject
                {
                    ["pattern"] = Property("string", "Text or regex to find"),
                    ["path"] = Property("string", "Folder to search in (optional)")
                },
                new string[] { "pattern" })));

        tools.Add(new ToolSpec(
            ToolNames.Plan,
            "Plan runnable increments, kept across compaction. Create: {\"action\":\"set\",\"steps\":\"wire entry point; run build; add movement and test\"}. Finish current step: {\"action\":\"done\"} (or supply step). Save exact APIs, error and next edit before a long investigation: {\"action\":\"checkpoint\",\"text\":\"...\"} (max 1000 chars, replaces current step notes). Append: {\"action\":\"add\",\"text\":\"...\"}. Show: {\"action\":\"show\"}.",
            Schema(
                new JsonObject
                {
                    ["action"] = EnumProperty(new string[] { "set", "done", "add", "show", "checkpoint" }),
                    ["steps"] = Property("string", "The steps, one per line; semicolon lists also accepted (only for \"set\")"),
                    ["step"] = Property("number", "Step number to mark done (optional, for \"done\")"),
                    ["text"] = Property("string", "Step to append or working checkpoint")
                },
                new string[] { "action" })));

        if (mode == PermissionMode.ReadOnly)
        {
            return tools;
        }

        tools.Add(new ToolSpec(
            ToolNames.WriteFile,
            "Create a new file or completely overwrite an existing one. Example: {\"path\": \"src/new.js\", \"content\": \"...\"}. Parent folders are created automatically. To change part of an existing file, prefer edit_file.",
            Schema(
                new JsonObject
                {
                    ["path"] = Property("string", "File path relative to the workspace"),
                    ["content"] = Property("string", "The full file content")
                },
                new string[] { "path", "content" })));

        tools.Add(new ToolSpec(
            ToolNames.EditFile,
            "Replace text inside an existing file. Copy old_text EXACTLY from the file (a few lines, enough to be unique), and give the replacement as new_text. Example: {\"path\": \"src/app.js\", \"old_text\": \"const x = 1;\", \"new_text\": \"const x = 2;\"}",
            Schema(
                new JsonObject
                {
                    ["path"] = Property("string", "File path relative to the workspace"),
                    ["old_text"] = Property("string", "Exact text currently in the file"),
                    ["new_text"] = Property("string", "Text to replace it with")
                },
                new string[] { "path", "old_text", "new_text" })));

        tools.Add(new ToolSpec(
            ToolNames.RunCommand,
            "Run a shell command in the workspace and wait for it to finish. Example: {\"command\": \"npm test\"}. Times out after 120s — for servers or watchers use the task tool instead.",
            Schema(
                new JsonObject
                {
                    ["command"] = Property("string", "The command to run")
                },
                new string[] { "command" })));

        tools.Add(new ToolSpec(
            ToolNames.Task,
            "Manage background tasks (things that keep running, like dev servers). action \"start\" runs a command in the background: {\"action\": \"start\", \"command\": \"npm run dev\"}. action \"logs\" shows recent output: {\"action\": \"logs\", \"task_id\": \"t1\"}. action \"list\" shows all tasks. action \"stop\" kills one: {\"action\": \"stop\", \"task_id\": \"t1\"}.",
            Schema(
                new JsonObject
                {
                    ["action"] = EnumProperty(new string[] { "start", "list", "logs", "stop" }),
                    ["command"] = Property("string", "Command to run (only for \"start\")"),
                    ["task_id"] = Property("string", "Task id like \"t1\" (for \"logs\" and \"stop\")")
                },
                new string[] { "action" })));

        return tools;
    }

    private static JsonObject Schema(JsonObject properties, string[]? required)
    {
        JsonObject schema = new JsonObject();
        schema["type"] = "object";
        schema["properties"] = properties;
        if (required != null)
        {
            JsonArray requiredArray = new JsonArray();
            foreach (string name in required)
            {
                requiredArray.Add(name);
            }
            schema["required"] = requiredArray;
        }
        return schema;
    }

    private static JsonObject Property(string type, string description)
    {
        JsonObject property = new JsonObject();
        property["type"] = type;
        property["description"] = description;
        return property;
    }

    private static JsonObject EnumProperty(string[] values)
    {
        JsonArray options = new JsonArray();
        foreach (string value in values)
        {
            options.Add(value);
        }
        JsonObject property = new JsonObject();
        property["type"] = "string";
        property["enum"] = options;
        return property;
    }
}
