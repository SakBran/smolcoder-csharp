namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Loads and saves AppSettings.
/// </summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
