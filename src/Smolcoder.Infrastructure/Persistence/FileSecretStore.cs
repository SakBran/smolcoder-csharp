using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Persistence;

namespace Smolcoder.Infrastructure.Persistence;

/// <summary>
/// Secrets in ~/.smolcoder/secrets.json, readable by this user only (0600 on macOS and Linux).
/// Writes go to a private temp file first and replace the old one in a single move.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly object gate = new object();

    public static string SecretsPath
    {
        get
        {
            return Path.Combine(AppPaths.DataDirectory, "secrets.json");
        }
    }

    public string? Get(string name)
    {
        lock (gate)
        {
            JsonObject secrets = Read();
            string? value = secrets[name]?.GetValue<string>();
            if (value == null || value.Length == 0)
            {
                return null;
            }
            return value;
        }
    }

    public void Set(string name, string? value)
    {
        lock (gate)
        {
            JsonObject secrets = Read();
            if (value == null || value.Trim().Length == 0)
            {
                if (!secrets.ContainsKey(name))
                {
                    return;
                }
                secrets.Remove(name);
            }
            else
            {
                secrets[name] = value.Trim();
            }
            Write(secrets);
        }
    }

    private static JsonObject Read()
    {
        string path = SecretsPath;
        try
        {
            if (!File.Exists(path))
            {
                return new JsonObject();
            }
            if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != PrivateFile)
            {
                // Someone widened it, or an old copy was restored. Close it again.
                File.SetUnixFileMode(path, PrivateFile);
            }
            JsonObject? parsed = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (parsed == null)
            {
                return new JsonObject();
            }
            return parsed;
        }
        catch (Exception)
        {
            return new JsonObject();
        }
    }

    private static void Write(JsonObject secrets)
    {
        string directory = AppPaths.DataDirectory;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, PrivateDirectory);
        }
        string path = SecretsPath;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        FileStreamOptions options = new FileStreamOptions();
        options.Mode = FileMode.CreateNew;
        options.Access = FileAccess.Write;
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFile;
        }
        try
        {
            using (FileStream stream = new FileStream(temporary, options))
            {
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.Write(secrets.ToJsonString());
                }
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
