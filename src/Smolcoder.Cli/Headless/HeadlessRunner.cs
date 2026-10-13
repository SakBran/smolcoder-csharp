using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Agents;
using Smolcoder.Application.Context;
using Smolcoder.Application.Events;
using Smolcoder.Application.Sessions;
using Smolcoder.Domain.Attachments;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Cli.Headless;

/// <summary>
/// smol -p "prompt": runs one prompt, prints the transcript and exits.
/// </summary>
public sealed class HeadlessRunner
{
    private readonly IModelSelector modelSelector;
    private readonly IAgentSessionBuilder sessionBuilder;
    private readonly ISettingsStore settingsStore;

    public HeadlessRunner(IModelSelector modelSelector, IAgentSessionBuilder sessionBuilder, ISettingsStore settingsStore)
    {
        this.modelSelector = modelSelector;
        this.sessionBuilder = sessionBuilder;
        this.settingsStore = settingsStore;
    }

    public async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        using (ConsoleInteraction ui = new ConsoleInteraction())
        {
            AppSettings settings = settingsStore.Load();
            SessionPreferences preferences = new SessionPreferences();
            preferences.Mode = options.Mode;
            preferences.Model = options.Model;
            preferences.ContextWindow = options.ContextWindow;
            preferences.EffortGiven = options.EffortGiven;
            preferences.Effort = options.Effort;

            ModelDescriptor? chosen = await modelSelector.PrepareModelAsync(preferences, settings, null, cancellationToken);
            if (chosen == null)
            {
                ui.PrintLine(modelSelector.NoModelsMessage());
                return 1;
            }

            PermissionMode mode = PermissionMode.Edit;
            if (options.Mode.HasValue)
            {
                mode = options.Mode.Value;
            }
            else if (settings.LastMode.HasValue)
            {
                mode = settings.LastMode.Value;
            }
            Effort? effort = settings.Effort;
            if (options.EffortGiven)
            {
                effort = options.Effort;
            }

            AgentSessionRequest request = new AgentSessionRequest(options.Workspace, chosen, mode, ui);
            request.Effort = effort;
            request.Interactive = false;
            request.MaxSteps = 1000;
            if (options.Verify != null)
            {
                request.Verification = new VerificationSettings(options.Verify, options.VerifyAttempts, false);
            }
            AgentSessionParts parts = sessionBuilder.Build(request);
            ReportCompactions(parts.EventBus, ui);

            using (cancellationToken.Register(() => parts.Agent.Cancel()))
            {
                try
                {
                    ui.PrintLine("● " + ModelLabels.BackendLabel(chosen) + " · " + chosen.Id + " · ctx " + chosen.ContextWindow.ToString("N0") + " · " + PermissionModes.Label(mode) + " mode");
                    if (chosen.Note != null)
                    {
                        ui.Warn("  " + chosen.Note);
                    }
                    string? effortLabel = parts.Provider.EffortLabel();
                    if (effortLabel == null)
                    {
                        effortLabel = Efforts.Name(effort);
                    }
                    ui.Status("  effort " + effortLabel);
                    string? advice = ModelAdvice.Effort(chosen, effort, true);
                    if (advice != null)
                    {
                        ui.Warn("  " + advice);
                    }

                    int exitCode = 0;
                    try
                    {
                        string prompt = string.Empty;
                        if (options.Print != null)
                        {
                            prompt = options.Print;
                        }
                        await parts.Agent.RunTurnAsync(prompt, new List<Attachment>());
                        if (parts.Agent.Outcome != AgentOutcome.Completed)
                        {
                            exitCode = 1;
                        }
                    }
                    catch (Exception exception)
                    {
                        ui.Error("\n" + exception.Message);
                        exitCode = 1;
                    }
                    WriteStats(parts, chosen);
                    return exitCode;
                }
                finally
                {
                    parts.TaskManager.KillAll();
                }
            }
        }
    }

    /// <summary>
    /// A machine-readable summary on stderr, for scripts and cost comparisons.
    /// </summary>
    private static void WriteStats(AgentSessionParts parts, ModelDescriptor chosen)
    {
        TurnStats? stats = parts.Agent.LastTurnStats;
        if (stats == null)
        {
            return;
        }
        JsonObject summary = new JsonObject();
        summary["backend"] = chosen.Backend;
        summary["outcome"] = parts.Agent.Outcome.ToString().ToLowerInvariant();
        VerificationResult? verification = parts.Agent.LastVerification;
        if (verification != null)
        {
            JsonObject verificationJson = new JsonObject();
            verificationJson["attempts"] = verification.Attempts;
            verificationJson["passed"] = verification.Passed;
            summary["verification"] = verificationJson;
        }
        else
        {
            summary["verification"] = null;
        }
        summary["model"] = chosen.Id;
        summary["durationMs"] = stats.DurationMilliseconds;
        summary["modelCalls"] = stats.ModelCalls;
        summary["toolCalls"] = stats.ToolCalls;
        summary["generatedTokens"] = stats.GeneratedTokens;
        summary["thinkingTokensEst"] = (int)Math.Round(stats.ThinkingChars / 4.0);
        if (stats.GenerationSeconds > 0)
        {
            summary["genTokPerSec"] = (int)Math.Round(stats.GeneratedTokens / stats.GenerationSeconds);
        }
        else
        {
            summary["genTokPerSec"] = null;
        }
        summary["promptTokensLast"] = stats.PromptTokensLast;
        summary["promptTokensTotal"] = stats.PromptTokensTotal;
        if (stats.CacheHitTokens > 0)
        {
            summary["cacheHitTokens"] = stats.CacheHitTokens;
        }
        else
        {
            summary["cacheHitTokens"] = null;
        }
        summary["contextWindow"] = chosen.ContextWindow;
        if (parts.ToolContext.Plan.Exists)
        {
            summary["planDone"] = parts.ToolContext.Plan.DoneCount + "/" + parts.ToolContext.Plan.Steps.Count;
        }
        else
        {
            summary["planDone"] = null;
        }
        Console.Error.WriteLine("[stats] " + summary.ToJsonString());
    }

    private static void ReportCompactions(IAgentEventBus eventBus, ConsoleInteraction ui)
    {
        eventBus.On(AgentEventKind.PostCompact, payload =>
        {
            CompactionReport? report = payload as CompactionReport;
            if (report != null)
            {
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
            return Task.CompletedTask;
        });
    }
}
