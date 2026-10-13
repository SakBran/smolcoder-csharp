using System.CommandLine;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Smolcoder.Application;
using Smolcoder.Cli;
using Smolcoder.Cli.Headless;
using Smolcoder.Infrastructure;
using Smolcoder.Infrastructure.Persistence;
using Smolcoder.Web.Hosting;

// smol — the smolcoder entry point.
// Headless: smol -p "prompt" for people and scripts.
// Web: smol --web serves the browser UI with workspaces and sessions side by side.

Console.OutputEncoding = Encoding.UTF8;
CommandLineDefinition definition = new CommandLineDefinition();

definition.Root.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
{
    CliOptions options;
    try
    {
        options = definition.Read(parseResult);
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
    if (!Directory.Exists(options.Workspace))
    {
        Console.Error.WriteLine("Workspace folder does not exist: " + options.Workspace);
        return 1;
    }

    if (options.Web)
    {
        return await WebHubLauncher.RunAsync(options.Workspace, options.WorkspaceGiven, WebPreferences(options), options.WebPort, cancellationToken);
    }
    if (options.Print == null)
    {
        Console.Error.WriteLine("Interactive terminal mode is not available. Use the browser UI with: smol --web");
        Console.Error.WriteLine("For a single headless prompt, run: smol -p \"your prompt\"");
        return 1;
    }

    ServiceCollection services = new ServiceCollection();
    services.AddApplication();
    services.AddInfrastructure();
    services.AddSingleton<HeadlessRunner>();
    using (ServiceProvider provider = services.BuildServiceProvider())
    {
        provider.GetRequiredService<IDatabaseInitializer>().Initialize();
        HeadlessRunner runner = provider.GetRequiredService<HeadlessRunner>();
        return await runner.RunAsync(options, cancellationToken);
    }
});

ParseResult parsed = definition.Root.Parse(args);
return await parsed.InvokeAsync();

static Smolcoder.Application.Sessions.SessionPreferences WebPreferences(CliOptions options)
{
    Smolcoder.Application.Sessions.SessionPreferences preferences = new Smolcoder.Application.Sessions.SessionPreferences();
    preferences.Mode = options.Mode;
    preferences.Model = options.Model;
    preferences.ContextWindow = options.ContextWindow;
    preferences.EffortGiven = options.EffortGiven;
    preferences.Effort = options.Effort;
    return preferences;
}
