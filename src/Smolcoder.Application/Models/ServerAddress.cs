using System.Text.RegularExpressions;

namespace Smolcoder.Application.Models;

/// <summary>
/// A server address as people type it: "192.168.1.50", "gpu-box.local", "box:1234",
/// "https://llm.example.com". A bare host means "try both usual ports"; anything more
/// specific names exactly one server.
/// </summary>
public sealed class ServerAddress
{
    public const int OllamaPort = 11434;
    public const int LmStudioPort = 1234;

    private static readonly Regex Scheme = new Regex("^([a-z][a-z0-9+.-]*)://", RegexOptions.IgnoreCase);
    private static readonly Regex TypedPort = new Regex(":\\d+$");

    private ServerAddress(string address, string hostName, IReadOnlyList<string> urls)
    {
        Address = address;
        HostName = hostName;
        Urls = urls;
    }

    /// <summary>
    /// The normalized form to save.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// The host part, for display.
    /// </summary>
    public string HostName { get; }

    /// <summary>
    /// Server roots to try, without a trailing slash.
    /// </summary>
    public IReadOnlyList<string> Urls { get; }

    /// <summary>
    /// Parses what someone typed. Throws ArgumentException with a plain-English reason.
    /// </summary>
    public static ServerAddress Parse(string? input)
    {
        string text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Type the address of the machine that runs your models.");
        }
        if (text.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("An address cannot contain spaces.");
        }
        Match scheme = Scheme.Match(text);
        if (scheme.Success && !Regex.IsMatch(scheme.Groups[1].Value, "^https?$", RegexOptions.IgnoreCase))
        {
            throw new ArgumentException("Only http:// and https:// addresses work here.");
        }
        if (scheme.Success && text.Length == scheme.Length)
        {
            throw new ArgumentException("The address needs a host name or IP.");
        }
        text = text.TrimEnd('/');
        // A bare IPv6 address needs brackets before it can carry a port.
        if (!scheme.Success && !text.StartsWith("[", StringComparison.Ordinal) && text.Count(character => character == ':') > 1)
        {
            text = "[" + text + "]";
        }
        Uri? uri;
        string withScheme = scheme.Success ? text : "http://" + text;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out uri) || uri.Host.Length == 0)
        {
            throw new ArgumentException("\"" + (input ?? string.Empty).Trim() + "\" is not an address I can connect to. Try an IP like 192.168.1.50 or a name like gpu-box.local.");
        }
        if (uri.UserInfo.Length > 0)
        {
            throw new ArgumentException("Leave user names and passwords out of the address.");
        }
        string host = uri.Host;
        if (host == "0.0.0.0" || host == "[::]" || host == "::")
        {
            throw new ArgumentException("0.0.0.0 is what a server listens on, not where to reach it. Use that machine's IP or name.");
        }
        string hostName = host.Trim('[', ']');
        string path = uri.AbsolutePath.TrimEnd('/');
        string authority = uri.IsDefaultPort && !TypedPort.IsMatch(StripBracketedHost(text)) ? host : host + ":" + uri.Port;
        string root = uri.Scheme.ToLowerInvariant() + "://" + authority;
        bool portTyped = TypedPort.IsMatch(StripBracketedHost(text));
        if (scheme.Success || portTyped || path.Length > 0)
        {
            string url = root + path;
            return new ServerAddress(url, hostName, new List<string> { url });
        }
        List<string> both = new List<string>
        {
            "http://" + host + ":" + OllamaPort,
            "http://" + host + ":" + LmStudioPort
        };
        return new ServerAddress(host.ToLowerInvariant(), hostName, both);
    }

    /// <summary>
    /// The OpenAI API root for a custom server: the URL as given, with /v1 added when it has no path.
    /// llama.cpp, vLLM and Jan all serve under /v1.
    /// </summary>
    public static string OpenAiRoot(string address)
    {
        ServerAddress parsed = Parse(address);
        string url = parsed.Urls[0];
        if (parsed.Urls.Count > 1)
        {
            // A bare host for a custom server: plain http on port 80 is unlikely, but it is what was typed.
            url = "http://" + parsed.Address;
        }
        Uri uri = new Uri(url);
        if (uri.AbsolutePath.Trim('/').Length == 0)
        {
            return url.TrimEnd('/') + "/v1";
        }
        return url.TrimEnd('/');
    }

    /// <summary>
    /// True for addresses that stay inside a home or office network, and for names we cannot judge.
    /// Used to warn before sending code over plain http to somewhere on the internet.
    /// </summary>
    public static bool IsPrivateHost(string hostName)
    {
        string host = hostName.ToLowerInvariant().Trim('[', ']');
        Match v4 = Regex.Match(host, "^(\\d{1,3})\\.(\\d{1,3})\\.(\\d{1,3})\\.(\\d{1,3})$");
        if (v4.Success)
        {
            int a = int.Parse(v4.Groups[1].Value);
            int b = int.Parse(v4.Groups[2].Value);
            return a == 10 || a == 127 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) || (a == 169 && b == 254) || (a == 100 && b >= 64 && b <= 127);
        }
        if (host.Contains(':'))
        {
            return host == "::1" || Regex.IsMatch(host, "^f[cd]") || Regex.IsMatch(host, "^fe[89ab]");
        }
        // Single-label and .local/.lan/.home names resolve inside the network.
        return !host.Contains('.') || Regex.IsMatch(host, "\\.(local|lan|home|internal|localdomain|home\\.arpa|ts\\.net)$");
    }

    private static string StripBracketedHost(string text)
    {
        string withoutScheme = Scheme.Replace(text, string.Empty);
        int slash = withoutScheme.IndexOf('/');
        if (slash >= 0)
        {
            withoutScheme = withoutScheme.Substring(0, slash);
        }
        return Regex.Replace(withoutScheme, "^\\[[^\\]]*\\]", string.Empty);
    }
}
