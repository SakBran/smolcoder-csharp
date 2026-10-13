using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Application.Agents;

/// <summary>
/// Decides whether a command may run without asking.
/// Commands that stay inside the workspace run freely. Others need approval.
/// </summary>
public sealed class CommandGate
{
    /// <summary>
    /// Shell characters that let one allowed program start others.
    /// "Always allow" never covers a command that has them.
    /// </summary>
    private static readonly Regex ShellMeta = new Regex(@"[;&|`$<>(){}\n\r\\]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    private readonly HashSet<string> alwaysAllowed = new HashSet<string>();
    private readonly IWorkspaceSandbox sandbox;

    public CommandGate(IWorkspaceSandbox sandbox)
    {
        this.sandbox = sandbox;
    }

    /// <summary>
    /// Returns null when the command may run, or the text to give the model instead.
    /// </summary>
    public async Task<string?> CheckAsync(string command, string workspace, bool interactive, IUserInteraction ui, CancellationToken cancellationToken)
    {
        string? reason = sandbox.CommandEscapesWorkspace(command, workspace);
        if (reason == null || IsAutoApproved(command))
        {
            return null;
        }
        if (!interactive)
        {
            return "Error: this command " + reason + ", which needs user approval, and this session is non-interactive. Keep every path inside the workspace (relative paths, a scratch folder in the workspace instead of /tmp), or the user can rerun smol with --mode bypass, or run this themselves: " + command;
        }
        CommandApproval answer = await ui.ConfirmCommandAsync(command, reason, cancellationToken);
        if (answer == CommandApproval.No)
        {
            return "The user declined to run this command. Continue without it, or ask the user what to do instead.";
        }
        if (answer == CommandApproval.Always)
        {
            string program = ProgramOf(command);
            if (program.Length > 0)
            {
                alwaysAllowed.Add(program);
            }
        }
        return null;
    }

    /// <summary>
    /// True when "always allow" covers this exact command.
    /// </summary>
    public bool IsAutoApproved(string command)
    {
        string trimmed = command.Trim();
        return alwaysAllowed.Contains(ProgramOf(command)) && !ShellMeta.IsMatch(trimmed);
    }

    private static string ProgramOf(string command)
    {
        string[] parts = Whitespace.Split(command.Trim());
        string program = parts[0];
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            program = program.ToLowerInvariant();
        }
        return program;
    }
}
