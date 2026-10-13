using System.CommandLine;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Cli;

/// <summary>
/// The smol command line: options, help text and parsing into CliOptions.
/// </summary>
public sealed class CommandLineDefinition
{
    public const int DefaultWebPort = 7433;

    public CommandLineDefinition()
    {
        WorkspaceArgument = new Argument<string?>("workspace");
        WorkspaceArgument.Description = "Project folder (default: the current folder)";
        WorkspaceArgument.Arity = ArgumentArity.ZeroOrOne;

        ModeOption = new Option<string?>("--mode", "-m");
        ModeOption.Description = "ro: read files only. edit: read/write files and run commands inside the workspace; anything reaching outside it asks. bypass: no approvals at all.";

        BypassOption = new Option<bool>("--bypass", "--bypass-permissions", "--yolo");
        BypassOption.Description = "Same as --mode bypass";

        ModelOption = new Option<string?>("--model");
        ModelOption.Description = "Pick a model by (partial) name, for example deepseek-flash";

        ContextOption = new Option<int?>("--ctx");
        ContextOption.Description = "Context window to budget against, in tokens (at least 1024)";

        EffortOption = new Option<string?>("--effort");
        EffortOption.Description = "Reasoning effort: off, low, medium, high, default";

        PrintOption = new Option<string?>("--print", "-p");
        PrintOption.Description = "Headless: run a single prompt and exit";

        VerifyOption = new Option<string?>("--verify");
        VerifyOption.Description = "Headless acceptance command; failures are repaired automatically";

        VerifyAttemptsOption = new Option<int?>("--verify-attempts");
        VerifyAttemptsOption.Description = "Maximum acceptance checks (default 6; requires --verify)";

        WebOption = new Option<int?>("--web");
        WebOption.Description = "Browser UI on 127.0.0.1 (default port " + DefaultWebPort + ")";
        WebOption.Arity = ArgumentArity.ZeroOrOne;

        Root = new RootCommand("smolcoder — a smol coding agent. Uses DeepSeek's cloud API (set DEEPSEEK_API_KEY).");
        Root.Arguments.Add(WorkspaceArgument);
        Root.Options.Add(ModeOption);
        Root.Options.Add(BypassOption);
        Root.Options.Add(ModelOption);
        Root.Options.Add(ContextOption);
        Root.Options.Add(EffortOption);
        Root.Options.Add(PrintOption);
        Root.Options.Add(VerifyOption);
        Root.Options.Add(VerifyAttemptsOption);
        Root.Options.Add(WebOption);
    }

    public RootCommand Root { get; }

    public Argument<string?> WorkspaceArgument { get; }

    public Option<string?> ModeOption { get; }

    public Option<bool> BypassOption { get; }

    public Option<string?> ModelOption { get; }

    public Option<int?> ContextOption { get; }

    public Option<string?> EffortOption { get; }

    public Option<string?> PrintOption { get; }

    public Option<string?> VerifyOption { get; }

    public Option<int?> VerifyAttemptsOption { get; }

    public Option<int?> WebOption { get; }

    /// <summary>
    /// Turns the parse result into CliOptions. Throws ArgumentException with a clear message on bad values.
    /// </summary>
    public CliOptions Read(ParseResult parseResult)
    {
        string workspace = Directory.GetCurrentDirectory();
        string? givenWorkspace = parseResult.GetValue(WorkspaceArgument);
        bool workspaceGiven = false;
        if (givenWorkspace != null)
        {
            workspace = Path.GetFullPath(givenWorkspace);
            workspaceGiven = true;
        }
        CliOptions options = new CliOptions(workspace);
        options.WorkspaceGiven = workspaceGiven;

        string? mode = parseResult.GetValue(ModeOption);
        if (mode != null)
        {
            PermissionMode parsedMode;
            if (!PermissionModes.TryParse(mode, out parsedMode))
            {
                throw new ArgumentException("Unknown mode \"" + mode + "\". Use ro, edit, or bypass.");
            }
            options.Mode = parsedMode;
        }
        if (parseResult.GetValue(BypassOption))
        {
            options.Mode = PermissionMode.Bypass;
        }

        options.Model = parseResult.GetValue(ModelOption);

        int? context = parseResult.GetValue(ContextOption);
        if (context.HasValue && context.Value < 1024)
        {
            throw new ArgumentException("--ctx must be a whole number of at least 1024 tokens.");
        }
        options.ContextWindow = context;

        string? effort = parseResult.GetValue(EffortOption);
        if (effort != null)
        {
            Effort? parsedEffort;
            if (!Efforts.TryParse(effort, out parsedEffort))
            {
                throw new ArgumentException("Unknown effort \"" + effort + "\". Use off, low, medium, high, or default.");
            }
            options.EffortGiven = true;
            options.Effort = parsedEffort;
        }

        options.Print = parseResult.GetValue(PrintOption);
        string? verify = parseResult.GetValue(VerifyOption);
        if (verify != null && verify.Trim().Length == 0)
        {
            throw new ArgumentException("--verify needs an acceptance command");
        }
        options.Verify = verify;
        int? attempts = parseResult.GetValue(VerifyAttemptsOption);
        if (attempts.HasValue && attempts.Value < 1)
        {
            throw new ArgumentException("--verify-attempts must be a positive whole number");
        }
        options.VerifyAttempts = attempts;

        if (parseResult.GetResult(WebOption) != null)
        {
            options.Web = true;
            options.WebPort = parseResult.GetValue(WebOption);
        }

        if (options.Verify != null && (options.Print == null || options.Web))
        {
            throw new ArgumentException("--verify requires a headless -p run");
        }
        if (options.VerifyAttempts.HasValue && options.Verify == null)
        {
            throw new ArgumentException("--verify-attempts requires --verify");
        }
        return options;
    }
}
