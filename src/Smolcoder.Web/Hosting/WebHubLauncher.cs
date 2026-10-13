using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Smolcoder.Application;
using Smolcoder.Application.Sessions;
using Smolcoder.Infrastructure;
using Smolcoder.Infrastructure.Persistence;
using Smolcoder.Web.Components;
using Smolcoder.Web.Services;

namespace Smolcoder.Web.Hosting;

/// <summary>
/// smol --web: serves the browser UI on 127.0.0.1. A second "smol --web" hands its folder
/// to the hub that is already running instead of starting another one.
/// </summary>
public static class WebHubLauncher
{
    public const int DefaultPort = 7433;

    public static async Task<int> RunAsync(string workspace, bool workspaceGiven, SessionPreferences preferences, int? port, CancellationToken cancellationToken)
    {
        int chosenPort = DefaultPort;
        if (port.HasValue)
        {
            chosenPort = port.Value;
        }
        string version = Version();
        Console.WriteLine("smolcoder v" + version + " · web");
        bool autoStart = workspaceGiven || !IsHomeOrRoot(workspace);

        HubRecord? running = HubRecord.Read();
        if (running != null && (!port.HasValue || running.Port == chosenPort))
        {
            string? handedOver = await HandOverAsync(running, workspace, autoStart);
            if (handedOver != null)
            {
                Console.WriteLine();
                Console.WriteLine("  added " + workspace + " to the running web UI:");
                Console.WriteLine("  " + handedOver);
                Console.WriteLine();
                return 0;
            }
        }

        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        HubOptions options = new HubOptions(preferences, chosenPort, token, version);

        WebApplicationOptions applicationOptions = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        };
        WebApplicationBuilder builder = WebApplication.CreateBuilder(applicationOptions);
        builder.WebHost.UseStaticWebAssets();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, chosenPort);
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // A port in use is reported in one plain line below, not as a stack trace.
        builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.None);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure();
        builder.Services.AddSingleton<HubOptions>(options);
        builder.Services.AddSingleton<SessionHub>();
        builder.Services.AddSingleton<MarkdownRenderer>();
        builder.Services.AddScoped<ModelDialogs>();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        WebApplication app = builder.Build();
        app.Services.GetRequiredService<IDatabaseInitializer>().Initialize();
        SessionHub hub = app.Services.GetRequiredService<SessionHub>();
        await hub.InitializeAsync(cancellationToken);

        app.UseMiddleware<TokenAuthMiddleware>();
        app.UseAntiforgery();
        app.MapStaticAssets();
        ApiEndpoints.Map(app, options);
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Port " + chosenPort + " is already in use (" + exception.Message + "). Pick another with: smol --web " + (chosenPort + 1));
            return 1;
        }

        HubRecord record = new HubRecord(chosenPort, token, Environment.ProcessId);
        record.Write();

        string? firstSession = null;
        if (autoStart)
        {
            try
            {
                firstSession = await hub.OpenSessionAsync(workspace);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Could not open " + workspace + ": " + exception.Message);
            }
        }

        string url = "http://127.0.0.1:" + chosenPort + "/?k=" + token;
        if (firstSession != null)
        {
            url = url + "#" + firstSession;
        }
        Console.WriteLine();
        Console.WriteLine("  smolcoder web UI:  " + url);
        if (autoStart)
        {
            Console.WriteLine("  workspace " + workspace + " · ctrl+c stops the server");
        }
        else
        {
            Console.WriteLine("  pick a workspace in the sidebar · ctrl+c stops the server");
        }
        Console.WriteLine();

        try
        {
            await app.WaitForShutdownAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // ctrl+c
        }
        finally
        {
            await hub.SaveAllAsync();
            hub.Dispose();
            HubRecord.RemoveIfOwned();
            await app.StopAsync();
        }
        return 0;
    }

    /// <summary>
    /// Gives the folder to a hub that is already running. Returns its URL, or null when no hub answered.
    /// </summary>
    private static async Task<string?> HandOverAsync(HubRecord running, string workspace, bool start)
    {
        using (HttpClient client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(3);
            string baseUrl = "http://127.0.0.1:" + running.Port;
            string key = Uri.EscapeDataString(running.Token);
            try
            {
                HttpResponseMessage ping = await client.GetAsync(baseUrl + "/api/ping?k=" + key);
                if (!ping.IsSuccessStatusCode)
                {
                    return null;
                }
                JsonObject request = new JsonObject();
                request["path"] = workspace;
                request["start"] = start;
                HttpResponseMessage response = await client.PostAsync(baseUrl + "/api/workspaces/add?k=" + key, JsonContent.Create(request));
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                JsonObject? result = await response.Content.ReadFromJsonAsync<JsonObject>();
                string url = baseUrl + "/?k=" + running.Token;
                if (result != null)
                {
                    JsonNode? id = result["id"];
                    if (id != null)
                    {
                        url = url + "#" + id.GetValue<string>();
                    }
                }
                return url;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The home folder or a drive root is not a project. Launching there just opens the hub.
    /// </summary>
    private static bool IsHomeOrRoot(string path)
    {
        string full = Path.GetFullPath(path);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), home.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        string? root = Path.GetPathRoot(full);
        return root != null && full == root;
    }

    private static string Version()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        if (entry == null)
        {
            return "0.0.0";
        }
        AssemblyInformationalVersionAttribute? attribute = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attribute == null)
        {
            return "0.0.0";
        }
        string text = attribute.InformationalVersion;
        int plus = text.IndexOf('+');
        if (plus > 0)
        {
            text = text.Substring(0, plus);
        }
        return text;
    }
}
