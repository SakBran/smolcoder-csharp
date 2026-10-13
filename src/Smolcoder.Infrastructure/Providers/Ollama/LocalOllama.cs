using System.Diagnostics;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Infrastructure.Providers.Discovery;

namespace Smolcoder.Infrastructure.Providers.Ollama;

/// <summary>
/// Ollama on this computer. Installing is left to the user (the page shows the command);
/// starting it is one click.
/// </summary>
public sealed class LocalOllama : ILocalOllama
{
    private readonly IHttpClientFactory httpClientFactory;

    public LocalOllama(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public string BaseUrl
    {
        get
        {
            return LocalEndpoints.NormalizeOllamaBase(Environment.GetEnvironmentVariable("OLLAMA_HOST"));
        }
    }

    public async Task<OllamaInstallState> CheckAsync(CancellationToken cancellationToken)
    {
        if (await AnswersAsync(cancellationToken))
        {
            return OllamaInstallState.Running;
        }
        if (AppPath() != null || BinaryPath() != null)
        {
            return OllamaInstallState.Stopped;
        }
        return OllamaInstallState.NotInstalled;
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (await AnswersAsync(cancellationToken))
        {
            return true;
        }
        string? app = AppPath();
        string? binary = BinaryPath();
        try
        {
            if (OperatingSystem.IsMacOS() && app != null)
            {
                ProcessStartInfo open = new ProcessStartInfo("open");
                open.ArgumentList.Add("-a");
                open.ArgumentList.Add(app);
                open.UseShellExecute = false;
                using (Process.Start(open))
                {
                }
            }
            else if (binary != null)
            {
                // Detached: the server keeps running after this process ends.
                ProcessStartInfo serve = new ProcessStartInfo(binary);
                serve.ArgumentList.Add("serve");
                serve.UseShellExecute = false;
                serve.RedirectStandardOutput = true;
                serve.RedirectStandardError = true;
                serve.CreateNoWindow = true;
                using (Process.Start(serve))
                {
                }
            }
            else
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            if (await AnswersAsync(cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<bool> AnswersAsync(CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        JsonNode? tags = await LocalHttp.TryGetJsonAsync(client, BaseUrl + "/api/tags", TimeSpan.FromSeconds(1), cancellationToken);
        return tags != null;
    }

    private static string? AppPath()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string candidate in new string[] { "/Applications/Ollama.app", Path.Combine(home, "Applications", "Ollama.app") })
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// The ollama command. A process started from the Dock may not have Homebrew on its PATH,
    /// so the usual install places are checked by hand.
    /// </summary>
    private static string? BinaryPath()
    {
        List<string> candidates = new List<string> { "/opt/homebrew/bin/ollama", "/usr/local/bin/ollama", "/usr/bin/ollama" };
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path != null)
        {
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(directory, OperatingSystem.IsWindows() ? "ollama.exe" : "ollama"));
            }
        }
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
