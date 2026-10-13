namespace Smolcoder.Infrastructure;

/// <summary>
/// Where smolcoder keeps its data. SMOLCODER_HOME moves it (useful for tests).
/// </summary>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            string? overridePath = Environment.GetEnvironmentVariable("SMOLCODER_HOME");
            if (overridePath != null && overridePath.Trim().Length > 0)
            {
                return overridePath;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".smolcoder");
        }
    }

    public static string DatabasePath
    {
        get
        {
            return Path.Combine(DataDirectory, "smolcoder.db");
        }
    }

    public static string UploadsDirectory
    {
        get
        {
            return Path.Combine(DataDirectory, "uploads");
        }
    }

    /// <summary>
    /// Record of the running web hub, so a second "smol --web" can hand its folder over.
    /// </summary>
    public static string HubRecordPath
    {
        get
        {
            return Path.Combine(DataDirectory, "hub.json");
        }
    }
}
