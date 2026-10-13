using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Settings remembered between runs.
/// </summary>
public sealed class AppSettings
{
    public string? LastModel { get; set; }

    /// <summary>
    /// Server the last model ran on.
    /// </summary>
    public string? LastModelUrl { get; set; }

    /// <summary>
    /// Last permission mode. Bypass is never remembered.
    /// </summary>
    public PermissionMode? LastMode { get; set; }

    public Effort? Effort { get; set; }
}
