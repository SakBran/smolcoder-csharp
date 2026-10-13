using Smolcoder.Domain.Modes;

namespace Smolcoder.Cli;

/// <summary>
/// The parsed command line.
/// </summary>
public sealed class CliOptions
{
    public CliOptions(string workspace)
    {
        Workspace = workspace;
    }

    public string Workspace { get; }

    /// <summary>
    /// True when a folder was given (not the current folder by default).
    /// </summary>
    public bool WorkspaceGiven { get; set; }

    public PermissionMode? Mode { get; set; }

    public string? Model { get; set; }

    public int? ContextWindow { get; set; }

    public bool EffortGiven { get; set; }

    public Effort? Effort { get; set; }

    public string? Print { get; set; }

    public string? Verify { get; set; }

    public int? VerifyAttempts { get; set; }

    public bool Web { get; set; }

    public int? WebPort { get; set; }
}
