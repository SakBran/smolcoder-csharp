using Smolcoder.Application.Sessions;

namespace Smolcoder.Web.Services;

/// <summary>
/// Settings the hub was started with.
/// </summary>
public sealed class HubOptions
{
    public HubOptions(SessionPreferences preferences, int port, string token, string version)
    {
        Preferences = preferences;
        Port = port;
        Token = token;
        Version = version;
    }

    /// <summary>
    /// Command-line choices that every new session starts from.
    /// </summary>
    public SessionPreferences Preferences { get; }

    public int Port { get; }

    /// <summary>
    /// The secret key in the printed URL.
    /// </summary>
    public string Token { get; }

    public string Version { get; }
}
