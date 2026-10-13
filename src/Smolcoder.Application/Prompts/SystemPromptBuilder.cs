using System.Runtime.InteropServices;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Prompts;

/// <summary>
/// Builds the system prompt. Short instructions; tool details live in the tool schemas.
/// </summary>
public sealed class SystemPromptBuilder : ISystemPromptBuilder
{
    public string Build(string workspace, PermissionMode mode, string shellLabel, string? agentsMd)
    {
        string operatingSystem = "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            operatingSystem = "Windows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            operatingSystem = "macOS";
        }

        string modeLine;
        if (mode == PermissionMode.ReadOnly)
        {
            modeLine = "You are in read-only mode: you can read and search files but not change anything.";
        }
        else if (mode == PermissionMode.Edit)
        {
            modeLine = "Read, edit and run commands inside the workspace. Commands reaching outside it need approval; keep scratch files in .scratch/.";
        }
        else
        {
            modeLine = "You have full access to files and commands; nothing asks the user for approval.";
        }

        string prompt =
            "You are smolcoder, a coding agent working in the workspace " + workspace + " on " + operatingSystem + ". "
            + "Commands run in " + shellLabel + " with the workspace as the working directory. "
            + "File paths are relative to the workspace; you cannot access files outside it. " + modeLine + "\n\n"
            + "For multi-step work, first set a short plan of runnable increments; mark each step done as you finish it. "
            + "The plan survives compaction. Use plan checkpoint to retain exact APIs, errors and your next edit during an investigation. "
            + "Establish a working entry point early and run the build after wiring modules. "
            + "Read relevant files before editing. Search narrowly and read small line ranges. "
            + "Inspect a local module's actual exports before importing it. "
            + "Make one tool call at a time; use small modules and write large files in parts. "
            + "Read errors and change your approach when a call fails. "
            + "Put test programs in files rather than long inline shell commands. "
            + "Verify changes with the relevant test or command; a failed check is not success. "
            + "Continue until the request is finished or explain the blocker. "
            + "Summarize the result and verification briefly.";

        if (agentsMd != null)
        {
            prompt = prompt + "\n\nWorkspace instructions from AGENTS.md — follow these:\n" + agentsMd;
        }
        return prompt;
    }
}
