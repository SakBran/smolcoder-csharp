using System.Text.RegularExpressions;
using Smolcoder.Infrastructure.Checks;

namespace Smolcoder.Infrastructure.Providers.Discovery;

/// <summary>
/// Model servers in Docker or Podman containers that publish their port on this computer.
/// </summary>
public static class DockerPorts
{
    private static readonly Regex Mapping = new Regex("(\\[[^\\]]+\\]|(?:\\d{1,3}\\.){3}\\d{1,3}|localhost):(\\d+)->(\\d+)/tcp\\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Host endpoints in "docker ps --format {{.Ports}}" output that publish one of the container ports.
    /// Unpublished ports are skipped: they cannot be reached from here.
    /// </summary>
    public static List<string> ParseBaseUrls(string output, IReadOnlyCollection<int> containerPorts)
    {
        List<string> urls = new List<string>();
        foreach (Match match in Mapping.Matches(output))
        {
            int containerPort = int.Parse(match.Groups[3].Value);
            if (!containerPorts.Contains(containerPort))
            {
                continue;
            }
            string host = match.Groups[1].Value.ToLowerInvariant();
            if (host == "0.0.0.0")
            {
                host = "127.0.0.1";
            }
            else if (host == "[::]")
            {
                host = "[::1]";
            }
            string url = "http://" + host + ":" + match.Groups[2].Value;
            if (!urls.Contains(url))
            {
                urls.Add(url);
            }
        }
        return urls;
    }

    /// <summary>
    /// Asks Docker, then Podman when Docker is absent. Two seconds at most each.
    /// </summary>
    public static Task<List<string>> FindAsync(IReadOnlyCollection<int> containerPorts)
    {
        return Task.Run(() =>
        {
            foreach (string cli in new string[] { "docker", "podman" })
            {
                ExternalToolResult? result = ExternalTool.Run(cli, new string[] { "ps", "--format", "{{.Ports}}" }, TimeSpan.FromSeconds(2));
                if (result != null && result.ExitCode == 0)
                {
                    return ParseBaseUrls(result.StandardOutput, containerPorts);
                }
            }
            return new List<string>();
        });
    }
}
