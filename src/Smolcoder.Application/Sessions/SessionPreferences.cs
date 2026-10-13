using Smolcoder.Domain.Modes;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// Choices from the command line for a session. Unset values fall back to remembered settings.
/// </summary>
public sealed class SessionPreferences
{
    public PermissionMode? Mode { get; set; }

    public string? Model { get; set; }

    public string? Backend { get; set; }

    /// <summary>
    /// Server of the wanted model, when the same id exists on several servers.
    /// </summary>
    public string? BaseUrl { get; set; }

    public int? ContextWindow { get; set; }

    /// <summary>
    /// True when the effort was given explicitly. Then Effort (null = "default") is used.
    /// </summary>
    public bool EffortGiven { get; set; }

    public Effort? Effort { get; set; }

    public SessionPreferences Copy()
    {
        SessionPreferences copy = new SessionPreferences();
        copy.Mode = Mode;
        copy.Model = Model;
        copy.Backend = Backend;
        copy.BaseUrl = BaseUrl;
        copy.ContextWindow = ContextWindow;
        copy.EffortGiven = EffortGiven;
        copy.Effort = Effort;
        return copy;
    }
}
