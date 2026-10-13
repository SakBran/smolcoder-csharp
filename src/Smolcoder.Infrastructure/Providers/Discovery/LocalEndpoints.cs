using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Smolcoder.Application.Models;

namespace Smolcoder.Infrastructure.Providers.Discovery;

/// <summary>
/// Spellings of ONE server, tried in order until one answers.
/// </summary>
public sealed class ServerGroup
{
    public ServerGroup(TimeSpan timeout, string? host)
    {
        Urls = new List<string>();
        Timeout = timeout;
        Host = host;
    }

    public List<string> Urls { get; }

    public TimeSpan Timeout { get; }

    /// <summary>
    /// Display name of the machine. Null means this computer.
    /// </summary>
    public string? Host { get; }
}

/// <summary>
/// Where a model server on this computer could be: OLLAMA_HOST, the port LM Studio says it uses,
/// and the default ports, each under every loopback spelling.
/// </summary>
public static class LocalEndpoints
{
    public static readonly string DefaultOllamaBase = "http://127.0.0.1:" + ServerAddress.OllamaPort;

    private static readonly Regex Loopback = new Regex("^(https?://)(localhost|127(?:\\.\\d+){3}|\\[::1\\])(?=[:/]|$)", RegexOptions.IgnoreCase);
    private static readonly Regex AnyAddress = new Regex("^(https?://)(0\\.0\\.0\\.0|\\[::\\]|::)(?=[:/]|$)");
    private static readonly Regex BareLoopback = new Regex("^https?://(localhost|127(?:\\.\\d+){3}|\\[::1\\])$", RegexOptions.IgnoreCase);

    /// <summary>
    /// OLLAMA_HOST as a client address. 0.0.0.0 is how the server is exposed on the network, but
    /// it is not an address to connect to, so it becomes loopback. A bare local host means port 11434.
    /// </summary>
    public static string NormalizeOllamaBase(string? environmentValue)
    {
        string value = (environmentValue ?? string.Empty).Trim();
        string url;
        if (value.Length == 0)
        {
            url = DefaultOllamaBase;
        }
        else if (value.StartsWith("http://", StringComparison.Ordinal) || value.StartsWith("https://", StringComparison.Ordinal))
        {
            url = value.TrimEnd('/');
        }
        else
        {
            url = "http://" + value.TrimEnd('/');
        }
        url = AnyAddress.Replace(url, "${1}127.0.0.1");
        if (BareLoopback.IsMatch(url))
        {
            url = url + ":" + ServerAddress.OllamaPort;
        }
        return url;
    }

    /// <summary>
    /// The configured endpoint first, then every loopback spelling, so a stale OLLAMA_HOST
    /// does not hide a healthy local server.
    /// </summary>
    public static List<string> OllamaBaseUrls(string? environmentValue)
    {
        List<string> urls = new List<string>();
        AddDistinct(urls, LoopbackAliases(NormalizeOllamaBase(environmentValue)));
        AddDistinct(urls, LoopbackAliases(DefaultOllamaBase));
        return urls;
    }

    /// <summary>
    /// The port LM Studio's settings name (it can be changed in the app), then the default.
    /// </summary>
    public static List<string> LmStudioBaseUrls(int? configuredPort)
    {
        List<int> ports = new List<int>();
        if (configuredPort.HasValue)
        {
            ports.Add(configuredPort.Value);
        }
        if (!ports.Contains(ServerAddress.LmStudioPort))
        {
            ports.Add(ServerAddress.LmStudioPort);
        }
        List<string> urls = new List<string>();
        foreach (int port in ports)
        {
            AddDistinct(urls, LoopbackAliases("http://127.0.0.1:" + port));
        }
        return urls;
    }

    public static List<string> LoopbackAliases(string baseUrl)
    {
        Match match = Loopback.Match(baseUrl);
        if (!match.Success)
        {
            return new List<string> { baseUrl };
        }
        string tail = baseUrl.Substring(match.Length);
        List<string> urls = new List<string>();
        foreach (string host in new string[] { match.Groups[2].Value, "127.0.0.1", "localhost", "[::1]" })
        {
            string url = match.Groups[1].Value + host + tail;
            if (!urls.Contains(url))
            {
                urls.Add(url);
            }
        }
        return urls;
    }

    /// <summary>
    /// Loopback spellings of the same port are one server; every other URL is its own.
    /// Order of first appearance is kept.
    /// </summary>
    public static List<ServerGroup> GroupServers(IEnumerable<string> urls, TimeSpan timeout, string? host)
    {
        List<ServerGroup> groups = new List<ServerGroup>();
        Dictionary<string, ServerGroup> byKey = new Dictionary<string, ServerGroup>();
        foreach (string url in urls)
        {
            Match loop = Loopback.Match(url);
            string key = url;
            if (loop.Success)
            {
                key = "loopback" + url.Substring(loop.Length);
            }
            ServerGroup? group;
            if (!byKey.TryGetValue(key, out group))
            {
                group = new ServerGroup(timeout, host);
                byKey[key] = group;
                groups.Add(group);
            }
            if (!group.Urls.Contains(url))
            {
                group.Urls.Add(url);
            }
        }
        return groups;
    }

    public static bool IsLoopback(string url)
    {
        return Loopback.IsMatch(url);
    }

    /// <summary>
    /// The port in LM Studio's http-server-config.json.
    /// </summary>
    public static int? ParseLmStudioServerPort(string text)
    {
        try
        {
            int? port = JsonRead.Int(JsonNode.Parse(text), "port");
            if (port.HasValue && port.Value > 0 && port.Value < 65536)
            {
                return port;
            }
        }
        catch (Exception)
        {
            // Not JSON.
        }
        return null;
    }

    /// <summary>
    /// LM Studio keeps its live server settings in its home folder, which a pointer file relocates.
    /// Missing or unreadable means the default port.
    /// </summary>
    public static int? ReadLmStudioPort(string home)
    {
        List<string> homes = new List<string>();
        try
        {
            string pointer = Path.Combine(home, ".lmstudio-home-pointer");
            if (File.Exists(pointer))
            {
                string pointed = File.ReadAllText(pointer).Trim();
                if (pointed.Length > 0)
                {
                    homes.Add(pointed);
                }
            }
        }
        catch (Exception)
        {
            // Not relocated.
        }
        homes.Add(Path.Combine(home, ".lmstudio"));
        homes.Add(Path.Combine(home, ".cache", "lm-studio"));
        foreach (string directory in homes)
        {
            try
            {
                string config = Path.Combine(directory, ".internal", "http-server-config.json");
                if (!File.Exists(config))
                {
                    continue;
                }
                int? port = ParseLmStudioServerPort(File.ReadAllText(config));
                if (port.HasValue)
                {
                    return port;
                }
            }
            catch (Exception)
            {
                // Try the next location.
            }
        }
        return null;
    }

    private static void AddDistinct(List<string> target, IEnumerable<string> urls)
    {
        foreach (string url in urls)
        {
            if (!target.Contains(url))
            {
                target.Add(url);
            }
        }
    }
}
