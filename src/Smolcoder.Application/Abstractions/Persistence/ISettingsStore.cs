namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Loads and saves AppSettings.
/// </summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);

    /// <summary>
    /// Servers the user added. Kept apart from AppSettings so a session saving its last model
    /// never overwrites a server added in the meantime.
    /// </summary>
    IReadOnlyList<SavedServer> LoadServers();

    void SaveServers(IReadOnlyList<SavedServer> servers);
}
