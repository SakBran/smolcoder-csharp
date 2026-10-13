using System.Text;
using System.Text.Json;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Projects;

/// <summary>
/// Reads package.json scripts and AGENTS.md. Nothing in them is executed or interpolated here.
/// </summary>
public sealed class ProjectInspector : IProjectInspector
{
    private const int AgentsMdCapChars = 8000;

    private readonly IWorkspaceSandbox sandbox;

    public ProjectInspector(IWorkspaceSandbox sandbox)
    {
        this.sandbox = sandbox;
    }

    public string? ProjectVerificationCommand(string workspaceRoot)
    {
        string file;
        try
        {
            file = sandbox.ResolveInWorkspace(workspaceRoot, "package.json");
        }
        catch (Exception)
        {
            return null;
        }
        if (!File.Exists(file))
        {
            return null;
        }
        try
        {
            if (new FileInfo(file).Length > 1024 * 1024)
            {
                return "npm run build --if-present";
            }
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8)))
            {
                JsonElement scripts;
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("scripts", out scripts) || scripts.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                List<string> commands = new List<string>();
                foreach (string name in new string[] { "build", "test", "test:e2e" })
                {
                    JsonElement script;
                    if (!scripts.TryGetProperty(name, out script) || script.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    string? value = script.GetString();
                    if (value != null && value.Trim().Length > 0)
                    {
                        commands.Add("npm run " + name);
                    }
                }
                if (commands.Count == 0)
                {
                    return null;
                }
                return string.Join(" && ", commands);
            }
        }
        catch (Exception)
        {
            // npm reports broken package files clearly. Never treat them as fine.
            return "npm run build --if-present";
        }
    }

    public string? LoadAgentsMd(string workspaceRoot)
    {
        try
        {
            string path = Path.Combine(workspaceRoot, "AGENTS.md");
            if (!File.Exists(path))
            {
                return null;
            }
            string text = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (text.Length == 0)
            {
                return null;
            }
            if (text.Length > AgentsMdCapChars)
            {
                text = text.Substring(0, AgentsMdCapChars) + "\n[AGENTS.md was truncated here to save context]";
            }
            return text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
