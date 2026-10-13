namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// API keys pasted into the UI. Kept out of the session database, which holds every transcript.
/// </summary>
public interface ISecretStore
{
    string? Get(string name);

    /// <summary>
    /// Null or empty removes the secret.
    /// </summary>
    void Set(string name, string? value);
}

/// <summary>
/// Secret names used across layers.
/// </summary>
public static class SecretNames
{
    public const string DeepSeek = "deepseek";
}
