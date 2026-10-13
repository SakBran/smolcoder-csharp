using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Agents;
using Smolcoder.Application.Context;
using Smolcoder.Application.Events;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Planning;
using Smolcoder.Domain.Text;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// One session: one workspace, one agent and one user interface,
/// plus the input loop and the slash commands that drive it.
/// </summary>
public sealed class InteractiveSession
{
    public static readonly IReadOnlyList<SlashCommand> SlashCommands = new List<SlashCommand>
    {
        new SlashCommand("models", "Switch model"),
        new SlashCommand("mode", "Set mode (ro / edit / bypass)"),
        new SlashCommand("effort", "Set reasoning effort"),
        new SlashCommand("plan", "Show the agent's plan"),
        new SlashCommand("context", "Show context usage"),
        new SlashCommand("compact", "Compact the conversation now"),
        new SlashCommand("tasks", "List background tasks"),
        new SlashCommand("logs", "Show task output — /logs t1"),
        new SlashCommand("stop", "Stop a background task — /stop t1"),
        new SlashCommand("clear", "Reset the conversation"),
        new SlashCommand("help", "Show help"),
        new SlashCommand("exit", "Close this session")
    };

    private const string HelpText =
        "Slash commands:\n"
        + "  /models     switch model         /tasks        background tasks\n"
        + "  /mode       set mode             /logs <id>    task output\n"
        + "  /effort     reasoning effort     /stop <id>    stop a task\n"
        + "  /plan       show the plan        /clear        reset conversation\n"
        + "  /context    context usage        /exit         close this session\n"
        + "  /compact    compact now\n\n"
        + "Keys: Enter sends · Shift+Enter new line · Shift+Tab cycles the mode · Esc stops the running turn.";

    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    private readonly ISessionInteraction ui;
    private readonly AgentSessionParts parts;
    private readonly IAgentSessionBuilder sessionBuilder;
    private readonly IModelCatalog modelCatalog;
    private readonly ISettingsStore settingsStore;
    private readonly SessionPreferences preferences;
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private bool ended;

    public InteractiveSession(
        ISessionInteraction ui,
        string workspace,
        ModelDescriptor chosen,
        SessionPreferences preferences,
        AppSettings settings,
        IAgentSessionBuilder sessionBuilder,
        IModelCatalog modelCatalog,
        ISettingsStore settingsStore)
    {
        this.ui = ui;
        this.sessionBuilder = sessionBuilder;
        this.modelCatalog = modelCatalog;
        this.settingsStore = settingsStore;
        this.preferences = preferences;
        Workspace = workspace;
        Chosen = chosen;

        PermissionMode mode = PermissionMode.Edit;
        if (preferences.Mode.HasValue)
        {
            mode = preferences.Mode.Value;
        }
        else if (settings.LastMode.HasValue)
        {
            mode = settings.LastMode.Value;
        }
        if (preferences.EffortGiven)
        {
            Effort = preferences.Effort;
        }
        else
        {
            Effort = settings.Effort;
        }

        AgentSessionRequest request = new AgentSessionRequest(workspace, chosen, mode, ui);
        request.Effort = Effort;
        request.Interactive = true;
        // The step cap stops runaway loops only. The stop button is the real kill switch.
        request.MaxSteps = 1000;
        parts = sessionBuilder.Build(request);

        parts.EventBus.On(AgentEventKind.PostCompact, payload =>
        {
            ReportCompaction(payload as CompactionReport);
            return Task.CompletedTask;
        });
        parts.EventBus.On(AgentEventKind.ContextUpdate, payload =>
        {
            ui.Refresh();
            return Task.CompletedTask;
        });
        Persist();
    }

    public string Workspace { get; }

    public ModelDescriptor Chosen { get; private set; }

    public Effort? Effort { get; private set; }

    public IAgentService Agent
    {
        get
        {
            return parts.Agent;
        }
    }

    public AgentSessionParts Parts
    {
        get
        {
            return parts;
        }
    }

    /// <summary>
    /// Called after each completed user turn (the web hub names the session after the first one).
    /// </summary>
    public Func<Task>? OnTurnDone { get; set; }

    /// <summary>
    /// Called once when the session has shut down.
    /// </summary>
    public Action? OnExit { get; set; }

    /// <summary>
    /// The opening lines: model note and effort advice.
    /// </summary>
    public void Announce()
    {
        if (Chosen.Note != null)
        {
            ui.Warn("  " + Chosen.Note);
        }
        string? advice = EffortAdvice();
        if (advice != null)
        {
            ui.Warn("  " + advice);
        }
    }

    public async Task RunAsync()
    {
        await parts.EventBus.EmitAsync(AgentEventKind.SessionStart, null);
        while (true)
        {
            UserInput input;
            try
            {
                input = await ui.ReadInputAsync(lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                await ShutdownAsync();
                return;
            }

            if (input.Attachments.Count == 0 && input.Text.StartsWith("/", StringComparison.Ordinal))
            {
                bool keepRunning = await HandleSlashCommandAsync(input.Text);
                ui.Refresh();
                if (!keepRunning)
                {
                    return;
                }
                continue;
            }

            try
            {
                await parts.Agent.RunTurnAsync(input.Text, input.Attachments);
                if (OnTurnDone != null)
                {
                    await OnTurnDone();
                }
            }
            catch (Exception exception)
            {
                ui.Error("\n" + exception.Message);
            }
            ui.Refresh();
        }
    }

    public void CycleMode()
    {
        PermissionMode next = PermissionModes.Next(parts.Agent.Mode);
        parts.Agent.SetMode(next, sessionBuilder.SystemPrompt(Workspace, next));
        Persist();
        ui.Refresh();
    }

    public void Cancel()
    {
        parts.Agent.Cancel();
    }

    /// <summary>
    /// Stops the session. Safe to call more than once.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (ended)
        {
            return;
        }
        ended = true;
        parts.Agent.Cancel();
        lifetime.Cancel();
        try
        {
            await parts.EventBus.EmitAsync(AgentEventKind.SessionEnd, null);
        }
        catch (Exception)
        {
            // Best effort.
        }
        parts.TaskManager.KillAll();
        if (OnExit != null)
        {
            OnExit();
        }
    }

    public SessionState State()
    {
        IAgentService agent = parts.Agent;
        Plan plan = parts.ToolContext.Plan;
        SessionState state = new SessionState(Workspace, Chosen.Id, Chosen.Backend, agent.ContextBudget(), agent.Mode);
        state.Host = Chosen.Host;
        state.SupportsVision = Chosen.SupportsVision;
        string? effortLabel = agent.Provider.EffortLabel();
        if (effortLabel == null)
        {
            effortLabel = Efforts.Name(Effort);
        }
        state.EffortLabel = effortLabel;
        state.ContextPercent = agent.ContextPercent();
        state.Outcome = agent.Outcome;
        state.LastError = agent.LastError;
        state.PlanSteps = plan.Steps.Select(step => CopyStep(step)).ToList();
        state.PlanCurrent = plan.CurrentIndex;
        state.RunningTasks = parts.TaskManager.RunningCount;
        state.Urls = parts.TaskManager.RecentUrls().ToList();
        return state;
    }

    public SessionSnapshot Snapshot()
    {
        SessionSnapshot snapshot = new SessionSnapshot();
        snapshot.Messages = parts.Agent.Messages.Skip(1).Select(message => message.Clone()).ToList();
        snapshot.PlanSteps = parts.ToolContext.Plan.Steps.Select(step => CopyStep(step)).ToList();
        snapshot.FilesTouched = new List<string>(parts.ToolContext.FilesTouched);
        snapshot.CommandsRun = new List<string>(parts.ToolContext.CommandsRun);
        snapshot.OriginalRequest = parts.Agent.OriginalRequest;
        snapshot.CurrentRequest = parts.Agent.CurrentRequest;
        snapshot.Mode = parts.Agent.Mode;
        snapshot.Effort = Effort;
        snapshot.Model = Chosen.Id;
        snapshot.Backend = Chosen.Backend;
        snapshot.BaseUrl = Chosen.BaseUrl;
        return snapshot;
    }

    /// <summary>
    /// Brings a saved transcript back. The system message is rebuilt; approvals are not restored.
    /// </summary>
    public void Restore(SessionSnapshot snapshot)
    {
        parts.Agent.RestoreTranscript(snapshot.Messages, snapshot.OriginalRequest, snapshot.CurrentRequest);
        parts.ToolContext.Plan.Restore(snapshot.PlanSteps);
        foreach (string file in snapshot.FilesTouched)
        {
            parts.ToolContext.AddFileTouched(file);
        }
        parts.ToolContext.CommandsRun.AddRange(snapshot.CommandsRun);
    }

    /// <summary>
    /// A model-written name for the session, or null to keep the fallback.
    /// One cheap call with thinking off and a tiny output cap.
    /// </summary>
    public async Task<string?> SuggestTitleAsync()
    {
        List<Message> messages = parts.Agent.Messages;
        Message? first = messages.FirstOrDefault(message => message.Role == MessageRole.User && !message.CompactNote);
        if (first == null)
        {
            return null;
        }
        Message? reply = messages.LastOrDefault(message => message.Role == MessageRole.Assistant && message.Content.Trim().Length > 0);
        string request = "First request:\n" + TextTruncation.End(first.Content, 600) + "\n\n";
        if (reply != null)
        {
            request = request + "Reply excerpt:\n" + TextTruncation.End(reply.Content, 400) + "\n\n";
        }
        request = request + "Title:";
        List<Message> prompt = new List<Message>();
        prompt.Add(Message.System("You name coding sessions. Reply with only the title: three to six words, plain text, no quotes, no trailing period."));
        prompt.Add(Message.User(request));
        ChatRequestOptions options = new ChatRequestOptions();
        options.ForceThinkingOff = true;
        options.MaxTokens = 30;
        options.Timeout = TimeSpan.FromSeconds(15);
        options.Background = true;
        try
        {
            ChatResult result = await parts.Agent.Provider.ChatAsync(prompt, new List<Abstractions.Providers.ToolSpec>(), options);
            return SessionTitles.Clean(result.Content);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns false when the session should end.
    /// </summary>
    private async Task<bool> HandleSlashCommandAsync(string text)
    {
        string[] words = Whitespace.Split(text.Substring(1).Trim());
        string command = words[0];
        string? argument = null;
        if (words.Length > 1)
        {
            argument = words[1];
        }

        if (command == "exit" || command == "quit" || command == "q")
        {
            await ShutdownAsync();
            return false;
        }
        if (command == "help")
        {
            ui.PrintLine(HelpText);
        }
        else if (command == "models" || command == "model")
        {
            await SwitchModelAsync();
        }
        else if (command == "mode")
        {
            await SetModeAsync(argument);
        }
        else if (command == "effort")
        {
            await SetEffortAsync(argument);
        }
        else if (command == "plan")
        {
            if (parts.ToolContext.Plan.Exists)
            {
                ui.PlanUpdated(parts.ToolContext.Plan);
            }
            else
            {
                ui.Status("· no plan yet — the agent creates one when it starts a multi-step task");
            }
        }
        else if (command == "tasks")
        {
            ui.PrintLine(parts.TaskManager.List());
        }
        else if (command == "logs")
        {
            int lines = 50;
            if (words.Length > 2)
            {
                int parsed;
                if (int.TryParse(words[2], out parsed) && parsed != 0)
                {
                    lines = parsed;
                }
            }
            string id = string.Empty;
            if (argument != null)
            {
                id = argument;
            }
            ui.PrintLine(parts.TaskManager.Logs(id, lines));
        }
        else if (command == "stop")
        {
            string id = string.Empty;
            if (argument != null)
            {
                id = argument;
            }
            ui.PrintLine(parts.TaskManager.Stop(id));
        }
        else if (command == "compact")
        {
            ui.StartSpinner("compacting");
            try
            {
                await parts.Agent.CompactNowAsync(true, false);
            }
            catch (Exception exception)
            {
                ui.Error(exception.Message);
            }
            finally
            {
                ui.StopSpinner();
            }
        }
        else if (command == "context")
        {
            ContextBudget budget = parts.Agent.ContextBudget();
            ui.Status(
                "Context " + Number(budget.Prompt) + " / " + Number(budget.Window) + " tokens (" + budget.Source + ")\n"
                + "Reply reserve " + Number(budget.Reserve) + " · available " + Number(budget.Available) + " · "
                + parts.Agent.Messages.Count + " messages · " + parts.Agent.Tools.Count + " tools");
        }
        else if (command == "clear")
        {
            parts.Agent.ResetTranscript();
            parts.ToolContext.Plan.Clear();
            ui.Status("· conversation cleared");
        }
        else
        {
            ui.Warn("Unknown command /" + command + " — try /help");
        }
        return true;
    }

    private async Task SwitchModelAsync()
    {
        ui.StartSpinner("looking for models");
        IReadOnlyList<ModelDescriptor> models;
        try
        {
            models = await modelCatalog.DetectAsync(lifetime.Token);
        }
        finally
        {
            ui.StopSpinner();
        }
        if (models.Count == 0)
        {
            ui.Warn("No model is available right now. Check DEEPSEEK_API_KEY.");
            return;
        }
        List<SelectOption> rows = new List<SelectOption>();
        foreach (ModelDescriptor model in models)
        {
            bool current = model.Id == Chosen.Id && model.Backend == Chosen.Backend && model.BaseUrl == Chosen.BaseUrl;
            rows.Add(new SelectOption(model.Id, model.Backend + " · cloud · ctx " + Number(model.ContextWindow), current));
        }
        int? index = await ui.SelectAsync("Select model", rows, lifetime.Token);
        if (!index.HasValue)
        {
            return;
        }

        ModelDescriptor next;
        ui.StartSpinner("loading " + models[index.Value].Id);
        try
        {
            next = await modelCatalog.ResolveContextWindowAsync(models[index.Value], preferences.ContextWindow, lifetime.Token);
        }
        catch (Exception exception)
        {
            ui.Error(exception.Message);
            return;
        }
        finally
        {
            ui.StopSpinner();
        }

        Chosen = next;
        IModelProvider provider = modelCatalog.CreateProvider(next);
        provider.SetEffort(Effort);
        parts.Agent.SetProvider(provider);
        parts.ContextManager.SetWindow(next.ContextWindow, provider.MaxOutputTokens);
        Persist();
        ui.PrintLine("● " + next.Backend + " · " + next.Id + " · ctx " + Number(next.ContextWindow) + " · " + PermissionModes.Label(parts.Agent.Mode) + " mode");
        if (next.Note != null)
        {
            ui.Warn("  " + next.Note);
        }
        string? advice = EffortAdvice();
        if (advice != null)
        {
            ui.Warn("  " + advice);
        }
    }

    private async Task SetModeAsync(string? argument)
    {
        PermissionMode next;
        if (argument != null)
        {
            if (!PermissionModes.TryParse(argument, out next))
            {
                ui.Warn("Unknown mode \"" + argument + "\". Use ro, edit, or bypass.");
                return;
            }
        }
        else
        {
            List<SelectOption> rows = new List<SelectOption>
            {
                new SelectOption("read-only", "read and search files only", parts.Agent.Mode == PermissionMode.ReadOnly),
                new SelectOption("edit", "edit files; run commands inside the workspace, ask y/n for anything outside it", parts.Agent.Mode == PermissionMode.Edit),
                new SelectOption("bypass permissions", "full access, never asks for approval", parts.Agent.Mode == PermissionMode.Bypass)
            };
            int? index = await ui.SelectAsync("Mode", rows, lifetime.Token);
            if (!index.HasValue)
            {
                return;
            }
            next = PermissionModes.CycleOrder[index.Value];
        }
        parts.Agent.SetMode(next, sessionBuilder.SystemPrompt(Workspace, next));
        Persist();
        ui.Status("· mode " + PermissionModes.Label(next));
    }

    private async Task SetEffortAsync(string? argument)
    {
        Effort? next;
        if (argument != null)
        {
            if (!Efforts.TryParse(argument, out next))
            {
                ui.Warn("Unknown effort \"" + argument + "\". Use default, off, low, medium, or high.");
                return;
            }
        }
        else
        {
            List<SelectOption> rows = new List<SelectOption>
            {
                new SelectOption("default", "let the model decide", !Effort.HasValue),
                new SelectOption("off", "no thinking — fastest and cheapest, best for long tool loops", Effort == Domain.Modes.Effort.Off),
                new SelectOption("low", "a little reasoning", Effort == Domain.Modes.Effort.Low),
                new SelectOption("medium", "more reasoning", Effort == Domain.Modes.Effort.Medium),
                new SelectOption("high", "the most reasoning — slowest", Effort == Domain.Modes.Effort.High)
            };
            int? index = await ui.SelectAsync("Reasoning effort", rows, lifetime.Token);
            if (!index.HasValue)
            {
                return;
            }
            string[] names = new string[] { "default", "off", "low", "medium", "high" };
            Efforts.TryParse(names[index.Value], out next);
        }
        Effort = next;
        parts.Agent.Provider.SetEffort(next);
        Persist();
        string? label = parts.Agent.Provider.EffortLabel();
        if (label == null)
        {
            label = Efforts.Name(next);
        }
        ui.Status("· effort " + label);
        string? advice = EffortAdvice();
        if (advice != null)
        {
            ui.Warn("  " + advice);
        }
    }

    private string? EffortAdvice()
    {
        if (Chosen.Backend == "deepseek" && Effort != Domain.Modes.Effort.Off)
        {
            return "DeepSeek thinks before every tool call and bills those tokens as output. /effort off is cheapest for long tool loops.";
        }
        return null;
    }

    private void Persist()
    {
        AppSettings settings = settingsStore.Load();
        settings.LastModel = Chosen.Id;
        settings.LastModelUrl = Chosen.BaseUrl;
        settings.LastMode = parts.Agent.Mode;
        settings.Effort = Effort;
        settingsStore.Save(settings);
    }

    private void ReportCompaction(CompactionReport? report)
    {
        if (report == null)
        {
            return;
        }
        string delta = report.Before + " → " + report.After + " tokens est.";
        if (report.Action == CompactionAction.Evicted)
        {
            ui.Status("· pruned context (" + delta + ")");
        }
        else if (report.Action == CompactionAction.Compacted)
        {
            ui.Status("· compacted the conversation into hand-over notes (" + delta + ")");
        }
        else if (report.Action == CompactionAction.Floor)
        {
            ui.Warn("· context is at its floor: system prompt + tools + the working tail no longer fit comfortably (" + delta + "). Consider a bigger context window.");
        }
    }

    private static PlanStep CopyStep(PlanStep step)
    {
        PlanStep copy = new PlanStep(step.Text);
        copy.Done = step.Done;
        copy.Note = step.Note;
        return copy;
    }

    private static string Number(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
