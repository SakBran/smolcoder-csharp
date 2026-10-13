using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Context;
using Smolcoder.Application.Events;
using Smolcoder.Application.Tools;
using Smolcoder.Application.Verification;
using Smolcoder.Domain.Attachments;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Planning;
using Smolcoder.Domain.Text;

namespace Smolcoder.Application.Agents;

/// <summary>
/// The agent loop. One tool call at a time; tool results go back to the model
/// until it answers in plain text. Broken calls come back as coaching errors,
/// so the model can retry instead of going off track.
/// </summary>
public sealed class AgentService : IAgentService
{
    private static readonly Regex TransientError = new Regex(@"fetch failed|econn|socket|network|timed?.?out|429|50[0-4]|stream ended|malformed JSON|stream error", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContextError = new Regex(@"context.{0,50}(exceed|overflow|full|length)|too (many|long).{0,30}tokens|prompt.{0,30}(too long|exceed)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IToolRegistry toolRegistry;
    private readonly IToolDispatcher toolDispatcher;
    private readonly IShellRunner shellRunner;
    private readonly IProjectInspector projectInspector;
    private readonly IAttachmentStore attachmentStore;
    private readonly ToolContext toolContext;
    private readonly IContextManager contextManager;
    private readonly IAgentEventBus eventBus;
    private readonly IUserInteraction ui;
    private readonly bool interactive;
    private readonly int maxSteps;
    private readonly VerificationSettings? callerVerification;
    private readonly CommandGate commandGate;

    private IModelProvider provider;
    private PermissionMode mode;
    private IReadOnlyList<ToolSpec> tools;
    private List<Message> messages;
    private bool planNudged;
    private CancellationTokenSource? turnCancellation;
    private VerificationSettings? verification;
    private VerificationResult? verificationResult;
    private string progressFailure = string.Empty;
    private int sameVerificationFailures;
    private bool canRefreshVerification;

    public AgentService(
        AgentCreationOptions options,
        IToolRegistry toolRegistry,
        IToolDispatcher toolDispatcher,
        IWorkspaceSandbox sandbox,
        IShellRunner shellRunner,
        IProjectInspector projectInspector,
        IAttachmentStore attachmentStore)
    {
        if (options.CallerVerification != null)
        {
            bool blankCommand = options.CallerVerification.Command.Trim().Length == 0;
            bool badLimit = options.CallerVerification.MaxAttempts.HasValue && options.CallerVerification.MaxAttempts.Value < 1;
            if (blankCommand || badLimit)
            {
                throw new ArgumentException("Verification needs a command and a positive attempt limit.");
            }
        }

        this.toolRegistry = toolRegistry;
        this.toolDispatcher = toolDispatcher;
        this.shellRunner = shellRunner;
        this.projectInspector = projectInspector;
        this.attachmentStore = attachmentStore;
        toolContext = options.ToolContext;
        contextManager = options.ContextManager;
        eventBus = options.EventBus;
        ui = options.UserInteraction;
        interactive = options.Interactive;
        maxSteps = options.MaxSteps;
        callerVerification = options.CallerVerification;
        verification = options.CallerVerification;
        commandGate = new CommandGate(sandbox);

        provider = options.Provider;
        mode = options.Mode;
        tools = toolRegistry.BuildToolSpecs(mode);
        messages = new List<Message>();
        messages.Add(Message.System(options.SystemPrompt));
        OriginalRequest = string.Empty;
        CurrentRequest = string.Empty;
        Outcome = AgentOutcome.Idle;
        contextManager.SetReplayThinking(provider.ReplaysThinking);
    }

    public List<Message> Messages
    {
        get
        {
            return messages;
        }
    }

    public IReadOnlyList<ToolSpec> Tools
    {
        get
        {
            return tools;
        }
    }

    public IModelProvider Provider
    {
        get
        {
            return provider;
        }
    }

    public PermissionMode Mode
    {
        get
        {
            return mode;
        }
    }

    public string OriginalRequest { get; private set; }

    public string CurrentRequest { get; private set; }

    public TurnStats? LastTurnStats { get; private set; }

    public AgentOutcome Outcome { get; private set; }

    public string? LastError { get; private set; }

    public VerificationResult? LastVerification
    {
        get
        {
            return verificationResult;
        }
    }

    public void SetMode(PermissionMode newMode, string systemPrompt)
    {
        contextManager.CancelBackground(true);
        contextManager.ResetAnchor();
        mode = newMode;
        tools = toolRegistry.BuildToolSpecs(newMode);
        messages[0] = Message.System(systemPrompt);
    }

    public void SetProvider(IModelProvider newProvider)
    {
        contextManager.CancelBackground(true);
        provider = newProvider;
        contextManager.SetReplayThinking(newProvider.ReplaysThinking);
    }

    public void ResetTranscript()
    {
        contextManager.CancelBackground(true);
        Message system = messages[0];
        messages = new List<Message>();
        messages.Add(system);
        OriginalRequest = string.Empty;
        CurrentRequest = string.Empty;
        planNudged = false;
        Outcome = AgentOutcome.Idle;
        LastError = null;
        verificationResult = null;
        verification = callerVerification;
        progressFailure = string.Empty;
        sameVerificationFailures = 0;
        // Facts from a cleared conversation would claim work the new task never did.
        toolContext.FilesTouched.Clear();
        toolContext.CommandsRun.Clear();
        contextManager.ResetAnchor();
    }

    /// <summary>
    /// Resumes a saved session: the transcript without its system message, and the two requests.
    /// </summary>
    public void RestoreTranscript(List<Message> restored, string originalRequest, string currentRequest)
    {
        contextManager.CancelBackground(true);
        Message system = messages[0];
        messages = new List<Message>();
        messages.Add(system);
        messages.AddRange(restored);
        OriginalRequest = originalRequest;
        CurrentRequest = currentRequest;
        planNudged = false;
        contextManager.ResetAnchor();
        RepairTranscript("[Tool execution was interrupted by a restart. Its outcome is unknown. Inspect files or command state before retrying; do not assume it failed or rerun it blindly.]");
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation = turnCancellation;
        if (cancellation != null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The turn already ended.
            }
        }
        contextManager.CancelBackground(true);
    }

    public int ContextPercent()
    {
        return contextManager.FillPercent(messages, tools);
    }

    public ContextBudget ContextBudget()
    {
        return contextManager.Budget(messages, tools);
    }

    public async Task CompactNowAsync(bool force, bool deterministic)
    {
        ui.StartSpinner("organizing context");
        try
        {
            await contextManager.ForegroundAsync();
            await eventBus.EmitAsync(AgentEventKind.PreCompact, null);
            CancellationToken token = CancellationToken.None;
            if (turnCancellation != null)
            {
                token = turnCancellation.Token;
            }
            CompactionResult result = await contextManager.ManageAsync(messages, tools, provider, CompactState(), force, deterministic, token);
            messages = result.Messages;
            await eventBus.EmitAsync(AgentEventKind.PostCompact, result.Report);
            await eventBus.EmitAsync(AgentEventKind.ContextUpdate, null);
        }
        finally
        {
            ui.StopSpinner();
        }
    }

    public async Task RunTurnAsync(string userInput, IReadOnlyList<Attachment> attachments)
    {
        await contextManager.ForegroundAsync();
        contextManager.CancelBackground(true);
        Outcome = AgentOutcome.Running;
        contextManager.ResetAnchor();
        LastError = null;
        verificationResult = null;
        verification = callerVerification;
        progressFailure = string.Empty;
        sameVerificationFailures = 0;
        // Only a fresh conversation can safely drop every old narrative.
        canRefreshVerification = OriginalRequest.Length == 0 && messages.Count == 1;

        bool canSeeImages = true;
        if (provider.SupportsVision.HasValue)
        {
            canSeeImages = provider.SupportsVision.Value;
        }
        AttachmentRendering rendered = attachmentStore.RenderForModel(attachments, canSeeImages);

        string request = userInput;
        if (request.Length == 0 && attachments.Count > 0)
        {
            if (attachments.Count == 1)
            {
                request = "See the attached file.";
            }
            else
            {
                request = "See the attached files.";
            }
        }
        // Compaction keeps the request text, so the file names go along with it.
        string requestNote = request;
        if (attachments.Count > 0)
        {
            requestNote = request + " [attached: " + string.Join(", ", attachments.Select(attachment => attachment.Name)) + "]";
        }
        if (OriginalRequest.Length == 0)
        {
            OriginalRequest = requestNote;
        }
        CurrentRequest = requestNote;

        string userContent = request;
        if (rendered.Text.Length > 0)
        {
            userContent = userContent + "\n\n" + rendered.Text;
        }
        userContent = userContent + VerificationInstruction();
        Message userMessage = Message.User(userContent);
        userMessage.Images = rendered.Images;
        messages.Add(userMessage);

        CancellationTokenSource cancellation = new CancellationTokenSource();
        turnCancellation = cancellation;
        CancellationToken token = cancellation.Token;

        TurnState turn = new TurnState();
        TurnStats stats = new TurnStats();
        LastTurnStats = stats;
        long startedAt = Environment.TickCount64;
        bool completed = false;

        try
        {
            await RefreshLoadedWindowAsync(token);
            while (turn.Steps < maxSteps)
            {
                turn.Steps++;
                StepOutcome outcome = await RunStepAsync(turn, stats, token);
                if (outcome == StepOutcome.Completed)
                {
                    completed = true;
                    Outcome = AgentOutcome.Completed;
                    return;
                }
            }
            throw new InvalidOperationException("Paused after " + maxSteps + " model steps. Progress is kept. Say \"continue\" to keep going.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            ui.PrintLine(string.Empty);
            ui.Status("· cancelled");
            Outcome = AgentOutcome.Cancelled;
            RepairTranscript("[cancelled by the user before this tool ran]");
        }
        catch (Exception exception)
        {
            Outcome = AgentOutcome.Error;
            LastError = exception.Message;
            RepairTranscript("[Tool did not run because the turn stopped after an error. Inspect the preceding error before continuing.]");
            throw;
        }
        finally
        {
            await contextManager.ForegroundAsync();
            turnCancellation = null;
            cancellation.Dispose();
            stats.DurationMilliseconds = Environment.TickCount64 - startedAt;
            if (completed)
            {
                ui.TurnEnd(TextTruncation.Duration(stats.DurationMilliseconds) + stats.Describe());
            }
        }
    }

    /// <summary>
    /// One model call and the tool calls it asked for.
    /// </summary>
    private async Task<StepOutcome> RunStepAsync(TurnState turn, TurnStats stats, CancellationToken token)
    {
        await eventBus.EmitAsync(AgentEventKind.PreRequest, null);
        if (contextManager.NeedsAttention(messages, tools))
        {
            await CompactNowAsync(false, false);
        }
        await contextManager.ForegroundAsync();
        contextManager.AssertFits(messages, tools);

        ui.StartSpinner("thinking");
        ChatResult result;
        try
        {
            result = await ChatWithRetryAsync(token, turn.ActionOnlyCalls > 0);
            if (turn.ActionOnlyCalls > 0)
            {
                turn.ActionOnlyCalls--;
            }
        }
        finally
        {
            ui.StopSpinner();
        }

        contextManager.Calibrate(result.PromptTokens, messages, tools);
        Message reply = Message.Assistant(result.Content);
        reply.ToolCalls = result.ToolCalls;
        reply.Thinking = result.Thinking;
        messages.Add(reply);
        // Record after adding the reply: the anchor then covers exactly these messages.
        contextManager.RecordUsage(result.PromptTokens, result.CompletionTokens, messages.Count);
        if (stats.ModelCalls == 0)
        {
            await RefreshLoadedWindowAsync(token);
        }
        await eventBus.EmitAsync(AgentEventKind.ContextUpdate, null);
        RecordStats(stats, result);
        if (result.Content.Length > 0)
        {
            ui.PrintLine(string.Empty);
        }

        if (result.ToolCalls.Count == 0)
        {
            return await HandlePlainReplyAsync(turn, result, token);
        }
        turn.Nudges = 0;

        foreach (ToolCall call in result.ToolCalls)
        {
            token.ThrowIfCancellationRequested();
            bool restartLoop = await HandleToolCallAsync(turn, stats, result, call, token);
            if (restartLoop)
            {
                return StepOutcome.Continue;
            }
        }

        // Different reads and small edits can go on forever without tripping a repeat guard.
        // A periodic real check grounds the investigation.
        if (turn.WroteThisTurn && mode != PermissionMode.ReadOnly && turn.ToolCallsThisTurn - turn.LastProgressCheck >= 24)
        {
            turn.LastProgressCheck = turn.ToolCallsThisTurn;
            DiscoverVerification(turn.WroteThisTurn);
            if (verification != null && verificationResult != null)
            {
                // Once acceptance found a real failure, keep checking that behavior.
                if (await RunAcceptanceAsync(turn, token))
                {
                    messages.Add(Message.User("[Acceptance checks passed. Finish your response with the verified result.]"));
                }
                turn.ResetGuards(false);
            }
            else if (await CheckProgressAsync(token))
            {
                turn.ResetGuards(false);
            }
        }
        return StepOutcome.Continue;
    }

    private async Task<StepOutcome> HandlePlainReplyAsync(TurnState turn, ChatResult result, CancellationToken token)
    {
        bool noContent = result.Content.Trim().Length == 0;

        // A cut-off or empty reply is not a finished turn. Nudge the model back (a few times at most).
        if (result.Truncated && turn.Nudges < 3)
        {
            turn.Nudges++;
            ui.Status("· reply hit the output limit — asking the model to continue");
            bool burnedByThinking = noContent && result.Thinking != null && result.Thinking.Trim().Length > 0;
            string nudgeText;
            if (burnedByThinking)
            {
                turn.ReasoningExhaustions++;
                if (turn.ReasoningExhaustions == 1)
                {
                    turn.ActionOnlyCalls = 1;
                }
                else
                {
                    turn.ActionOnlyCalls = Math.Min(8, (int)Math.Pow(2, Math.Min(turn.ReasoningExhaustions, 3)));
                }
                string responses = "responses";
                if (turn.ActionOnlyCalls == 1)
                {
                    responses = "response";
                }
                ui.Status("· reasoning exhausted the reply budget — " + turn.ActionOnlyCalls + " " + responses + " with thinking off, then restore the selected effort");
                nudgeText = "[Your reasoning used the entire output limit (" + provider.MaxOutputTokens + " tokens) and produced no answer. Do not re-derive everything — reply now with your next tool call or a brief answer.]";
            }
            else if (noContent)
            {
                nudgeText = "[" + TruncatedCallHint() + "]";
            }
            else
            {
                nudgeText = "[Your reply was cut off by the output length limit of " + provider.MaxOutputTokens + " tokens. Continue where you left off. If a file was too large for one write_file call, split the content into separate files — writing the same path again replaces it completely.]";
            }
            messages.Add(Message.User(nudgeText));
            return StepOutcome.Continue;
        }

        if (noContent && turn.Nudges < 2)
        {
            turn.Nudges++;
            ui.Status("· empty reply — nudging the model");
            messages.Add(Message.User("[Your reply was empty. If the task is finished, summarize what you did. Otherwise make the next tool call now.]"));
            return StepOutcome.Continue;
        }

        // The model wants to stop but its own plan has open steps. Push back once per plan state.
        Plan plan = toolContext.Plan;
        if (plan.Exists && plan.CurrentIndex >= 0 && turn.ToolCallsThisTurn > 0 && !planNudged)
        {
            planNudged = true;
            ui.Status("· plan has unfinished steps — nudging the model to continue");
            messages.Add(Message.User("[Your plan still has unfinished steps: " + plan.PendingSummary() + ". Continue with the next step now — or if a step no longer applies, mark it done with the plan tool and explain why.]"));
            return StepOutcome.Continue;
        }

        if (result.Truncated || noContent)
        {
            throw new InvalidOperationException("The model repeatedly returned an empty or cut-off reply. Progress is kept. Try /effort off, switch models with /models, or say continue.");
        }

        DiscoverVerification(turn.WroteThisTurn);
        if (verification != null)
        {
            bool passed = await RunAcceptanceAsync(turn, token);
            if (!passed)
            {
                turn.ResetGuards(true);
                return StepOutcome.Continue;
            }
        }
        return StepOutcome.Completed;
    }

    /// <summary>
    /// Runs one tool call. Returns true when the outer loop must start a new step right away.
    /// </summary>
    private async Task<bool> HandleToolCallAsync(TurnState turn, TurnStats stats, ChatResult result, ToolCall call, CancellationToken token)
    {
        if (call.ParseError != null)
        {
            JsonObject preview = new JsonObject();
            string raw = string.Empty;
            if (call.RawArguments != null)
            {
                raw = call.RawArguments;
            }
            if (raw.Length > 80)
            {
                raw = raw.Substring(0, 80);
            }
            preview["__raw"] = raw;
            ui.ToolCall(call.Name, preview);
        }
        else
        {
            ui.ToolCall(call.Name, call.Arguments);
        }

        string output;
        string? observedOutput = null;
        if (call.ParseError != null)
        {
            if (result.Truncated)
            {
                output = "Error: " + TruncatedCallHint();
            }
            else
            {
                output = "Error: your tool call arguments could not be parsed (" + call.ParseError + "). Send the arguments as a single JSON object, e.g. {\"path\": \"src/app.js\"}.";
            }
        }
        else if (result.Truncated)
        {
            output = "Error: " + TruncatedCallHint();
        }
        else if (!tools.Any(tool => tool.Name == call.Name))
        {
            // Hard mode check. The schemas sent to the model are only advice.
            output = "Error: the tool \"" + call.Name + "\" is not available in " + PermissionModes.Label(mode) + " mode. Available tools: " + string.Join(", ", tools.Select(tool => tool.Name)) + ".";
        }
        else
        {
            toolContext.ResultCharLimit = contextManager.ToolResultCharLimit();
            if (call.Name == ToolNames.RunCommand)
            {
                int end = messages.Count - 1;
                while (end >= 0 && messages[end].Role == MessageRole.Tool)
                {
                    end--;
                }
                contextManager.PrepareBackground(messages.Take(Math.Max(0, end)).ToList(), tools, provider, CompactState());
            }
            output = await GateAndExecuteAsync(call.Name, call.Arguments, token);
            observedOutput = output;
            turn.ToolCallsThisTurn++;
            stats.ToolCalls++;

            // Tier 0: a rewrite makes every earlier read of that file wrong.
            string? writtenPath = JsonArguments.GetString(call.Arguments, "path");
            bool isWrite = call.Name == ToolNames.WriteFile || call.Name == ToolNames.EditFile;
            if (isWrite && !output.StartsWith("Error", StringComparison.Ordinal) && writtenPath != null)
            {
                turn.WroteThisTurn = true;
                contextManager.EvictStaleReads(messages, writtenPath);
                turn.RepeatedReads.Clear();
                turn.ReadsSinceAction = 0;
            }

            // Keep the plan honest with a short periodic reminder.
            Plan plan = toolContext.Plan;
            if (call.Name == ToolNames.Plan)
            {
                turn.SincePlanUpdate = 0;
                string action = JsonArguments.GetText(call.Arguments, "action");
                if (!output.StartsWith("Error", StringComparison.Ordinal) && (action == "set" || action == "done" || action == "add"))
                {
                    planNudged = false;
                }
            }
            else if (plan.Exists && plan.CurrentIndex >= 0)
            {
                turn.SincePlanUpdate++;
                if (turn.SincePlanUpdate >= 4)
                {
                    turn.SincePlanUpdate = 0;
                    PlanStep current = plan.Steps[plan.CurrentIndex];
                    output = output + "\n[Reminder: the plan still shows step " + (plan.CurrentIndex + 1) + " \"" + current.Text + "\" as current. If you have finished steps, mark each with plan {\"action\": \"done\"} now.]";
                }
            }
        }

        string evidence = output;
        if (observedOutput != null)
        {
            evidence = observedOutput;
        }
        string key = call.Name + "\u0000" + call.Arguments.ToJsonString() + "\u0000" + evidence;
        if (key == turn.RepeatKey)
        {
            turn.Repeats++;
        }
        else
        {
            turn.Repeats = 1;
        }
        turn.RepeatKey = key;
        if (output.StartsWith("Error", StringComparison.Ordinal))
        {
            turn.FailedCalls++;
        }
        else
        {
            turn.FailedCalls = 0;
        }
        if (turn.Repeats == 3 || turn.FailedCalls == 3)
        {
            output = output + "\n[Repeated attempts are not making progress. Inspect the error or relevant file and change your approach before trying again.]";
        }

        int readRepeats = 0;
        string readKey = string.Empty;
        bool isRead = call.Name == ToolNames.ReadFile || call.Name == ToolNames.Search || call.Name == ToolNames.ListFiles;
        if (isRead && !output.StartsWith("Error", StringComparison.Ordinal))
        {
            turn.ReadsSinceAction++;
            if (turn.ReadsSinceAction % 12 == 0 && mode != PermissionMode.ReadOnly)
            {
                output = output + "\n[Investigation checkpoint: record the exact APIs, unresolved error and next small edit with plan checkpoint. Then make and verify that edit before inspecting more modules.]";
            }
            // Rereading an edited file or another line range is real progress, so the output is part of the key.
            readKey = Sha256(call.Name + call.Arguments.ToJsonString() + evidence);
            ReadRecord? previous;
            bool retained = false;
            if (turn.RepeatedReads.TryGetValue(readKey, out previous))
            {
                // Rereading output that we removed is fine. Count only what the model can still see.
                retained = previous.Receipt != null && !previous.Receipt.Evicted && messages.Contains(previous.Receipt);
            }
            if (retained && previous != null)
            {
                readRepeats = previous.Count + 1;
            }
            else
            {
                readRepeats = 1;
                turn.Repeats = 1;
            }
            turn.RepeatedReads[readKey] = new ReadRecord(readRepeats);
            if (readRepeats >= 3)
            {
                output = output + "\n[This unchanged result has already been read repeatedly. Do not restart the same reads after compaction. Use a different small line range or narrow search if needed, then implement the next step.]";
            }
        }

        int outputCap = contextManager.ToolResultCharLimit();
        // read_file already pages on whole lines. Never cut that page in the middle.
        if (output.Length > outputCap && call.Name != ToolNames.ReadFile)
        {
            output = TextTruncation.Middle(output, outputCap) + "\n[Output capped for this context window. Read a smaller line range or narrow the search.]";
        }

        string? planAction = ToolDispatcher.PlanAction(call.Arguments);
        bool planChanged = call.Name == ToolNames.Plan
            && !output.StartsWith("Error", StringComparison.Ordinal)
            && (planAction == "set" || planAction == "done" || planAction == "add");
        if (planChanged)
        {
            ui.PlanUpdated(toolContext.Plan);
        }
        else
        {
            ui.ToolResult(output);
        }

        Message toolMessage = Message.ToolResult(call.Id, call.Name, output);
        messages.Add(toolMessage);
        if (readKey.Length > 0)
        {
            turn.RepeatedReads[readKey].Receipt = toolMessage;
        }
        JsonObject eventPayload = new JsonObject();
        eventPayload["name"] = call.Name;
        eventPayload["args"] = call.Arguments.DeepClone();
        await eventBus.EmitAsync(AgentEventKind.PostTool, eventPayload);
        await eventBus.EmitAsync(AgentEventKind.ContextUpdate, null);
        token.ThrowIfCancellationRequested();

        // A long investigation without an edit must come back to real, runnable evidence.
        bool needsEvidence = readRepeats >= 5
            || turn.Repeats >= 6
            || turn.FailedCalls >= 6
            || (turn.WroteThisTurn && turn.ReadsSinceAction >= 24 && mode != PermissionMode.ReadOnly);
        if (needsEvidence)
        {
            DiscoverVerification(turn.WroteThisTurn);
        }
        if (verification != null && needsEvidence)
        {
            RepairTranscript("[Not executed: the harness switched to executable checks after repeated attempts.]");
            contextManager.ResetAnchor();
            // Known failing project checks are already the evidence. Do not spend acceptance attempts yet.
            if (verificationResult == null && progressFailure.Length > 0 && await CheckProgressAsync(token))
            {
                turn.ResetGuards(true);
                return true;
            }
            if (!await RunAcceptanceAsync(turn, token))
            {
                turn.ResetGuards(true);
                return true;
            }
            // Passing acceptance does not allow dropping the rest of the task.
            messages.Add(Message.User("[Acceptance passed. Review the original request, finish any remaining work, and summarize the verified result.]"));
            turn.ResetGuards(true);
            return true;
        }
        if (readRepeats >= 5)
        {
            throw new InvalidOperationException("Paused after repeatedly reading the same unchanged data without an edit. Progress is kept. Ask for a specific next change, read a different range, or use a larger context window.");
        }
        if (turn.Repeats >= 6 || turn.FailedCalls >= 6)
        {
            throw new InvalidOperationException("Paused after repeated tool attempts made no progress. Progress is kept; inspect the error, change the request or model, then continue.");
        }
        return false;
    }

    private void RecordStats(TurnStats stats, ChatResult result)
    {
        stats.ModelCalls++;
        if (result.GeneratedTokens.HasValue && result.GeneratedTokens.Value > 0)
        {
            stats.GeneratedTokens += result.GeneratedTokens.Value;
            if (result.GeneratedTokensPerSecond.HasValue && result.GeneratedTokensPerSecond.Value > 0)
            {
                stats.GenerationSeconds += result.GeneratedTokens.Value / result.GeneratedTokensPerSecond.Value;
            }
        }
        if (result.PromptTokens.HasValue && result.PromptTokens.Value > 0)
        {
            stats.PromptTokensLast = result.PromptTokens.Value;
            stats.PromptTokensTotal += result.PromptTokens.Value;
        }
        if (result.CacheHitTokens.HasValue)
        {
            stats.CacheHitTokens += result.CacheHitTokens.Value;
        }
        if (result.Thinking != null)
        {
            stats.ThinkingChars += result.Thinking.Length;
        }
    }

    /// <summary>
    /// Coaching for a tool call that was cut off by the output cap.
    /// </summary>
    public string TruncatedCallHint()
    {
        int cap = provider.MaxOutputTokens;
        int part = Math.Max(300, (int)Math.Floor(cap * 0.5));
        return "Your tool call was cut off by the output limit of " + cap + " tokens, so it was NOT executed and nothing was saved. "
            + "Send smaller calls: write the file in parts of at most ~" + part + " tokens — write_file with the first part, "
            + "then edit_file to append each next part (old_text = the last line you wrote, new_text = that line followed by the next part) — "
            + "or split the code across several smaller files.";
    }

    /// <summary>
    /// One model call, retried on short backend failures. The transcript does not change
    /// between attempts, so a retry is always safe.
    /// </summary>
    private async Task<ChatResult> ChatWithRetryAsync(CancellationToken token, bool actionOnly)
    {
        Exception? lastError = null;
        bool recoveredContext = false;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            bool streamed = false;
            try
            {
                ChatRequestOptions options = new ChatRequestOptions();
                options.CancellationToken = token;
                options.ForceThinkingOff = actionOnly;
                options.MaxTokens = Math.Min(provider.MaxOutputTokens, ContextBudget().Reserve);
                options.OnToken = text =>
                {
                    streamed = true;
                    ui.Token(text);
                };
                options.OnThinking = text =>
                {
                    streamed = true;
                    ui.Thinking(text);
                };
                return await provider.ChatAsync(messages, tools, options);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (streamed)
                {
                    ui.ResetResponse();
                }
                if (!recoveredContext && ContextError.IsMatch(exception.Message))
                {
                    recoveredContext = true;
                    ui.Status("· backend context limit — reducing history and retrying");
                    await CompactNowAsync(true, true);
                    contextManager.AssertFits(messages, tools);
                    continue;
                }
                if (attempt == 3 || !TransientError.IsMatch(exception.Message))
                {
                    throw;
                }
                string shortMessage = exception.Message;
                if (shortMessage.Length > 80)
                {
                    shortMessage = shortMessage.Substring(0, 80);
                }
                ui.Warn("· backend error (" + shortMessage + ") — retrying in " + (attempt * 3) + "s");
                await Task.Delay(attempt * 3000, token);
            }
        }
        if (lastError != null)
        {
            throw lastError;
        }
        throw new InvalidOperationException("The model call failed.");
    }

    private async Task RefreshLoadedWindowAsync(CancellationToken token)
    {
        int? actual = await provider.LoadedContextWindowAsync(token);
        if (actual.HasValue && actual.Value > 0 && actual.Value < ContextBudget().Window)
        {
            contextManager.SetWindow(actual.Value, Math.Min(provider.MaxOutputTokens, actual.Value / 4));
            ui.Status("· loaded model context changed to " + actual.Value.ToString("N0") + " tokens; budget adjusted");
            await eventBus.EmitAsync(AgentEventKind.ContextUpdate, null);
        }
    }

    private async Task<string> GateAndExecuteAsync(string name, JsonObject arguments, CancellationToken token)
    {
        string? command = toolDispatcher.CommandOf(name, arguments);
        // Bypass never asks. In read-only mode the command tools do not exist.
        if (command != null && mode != PermissionMode.Bypass)
        {
            string? refusal = await commandGate.CheckAsync(command, toolContext.Workspace, interactive, ui, token);
            if (refusal != null)
            {
                return refusal;
            }
        }
        token.ThrowIfCancellationRequested();
        if (!tools.Any(tool => tool.Name == name))
        {
            return "Error: " + name + " is no longer available in " + PermissionModes.Label(mode) + " mode.";
        }
        return await toolDispatcher.ExecuteAsync(name, arguments, toolContext, token);
    }

    /// <summary>
    /// Makes the transcript valid again: every tool call gets exactly one result, right after it,
    /// in call order. Missing results get the given reason. Orphan results are dropped.
    /// </summary>
    private void RepairTranscript(string reason)
    {
        List<Message> repaired = new List<Message>();
        for (int index = 0; index < messages.Count; index++)
        {
            Message message = messages[index];
            if (message.Role == MessageRole.Tool)
            {
                continue;
            }
            repaired.Add(message);
            if (!message.HasToolCalls)
            {
                continue;
            }
            Dictionary<string, Message> results = new Dictionary<string, Message>();
            while (index + 1 < messages.Count && messages[index + 1].Role == MessageRole.Tool)
            {
                index++;
                Message toolResult = messages[index];
                if (toolResult.ToolCallId != null)
                {
                    results[toolResult.ToolCallId] = toolResult;
                }
            }
            foreach (ToolCall call in message.ToolCalls)
            {
                Message? found;
                if (results.TryGetValue(call.Id, out found))
                {
                    repaired.Add(found);
                }
                else
                {
                    repaired.Add(Message.ToolResult(call.Id, call.Name, reason));
                }
            }
        }
        messages = repaired;
    }

    private CompactState CompactState()
    {
        string failure = string.Empty;
        if (verificationResult != null && !verificationResult.Passed)
        {
            failure = "\nLast acceptance failure (actual command output; resolve before completion):\n" + TextTruncation.Middle(verificationResult.Output, 1800);
        }
        string progress = string.Empty;
        if (progressFailure.Length > 0)
        {
            progress = "\nLast project check failure (may predate subsequent edits):\n" + TextTruncation.Middle(progressFailure, 1800);
        }
        CompactState state = new CompactState(OriginalRequest, CurrentRequest, toolContext.FilesTouched, toolContext.CommandsRun);
        state.PlanLine = toolContext.Plan.CompactLine();
        state.VerificationLine = VerificationInstruction() + failure + progress;
        return state;
    }

    private string VerificationInstruction()
    {
        if (verification == null)
        {
            return string.Empty;
        }
        string owner;
        if (verification.FromProject)
        {
            owner = "Project checks (" + verification.Command + ")";
        }
        else
        {
            owner = "Caller-owned acceptance checks";
        }
        return "\n" + owner + " must pass before completion. The harness runs them automatically and returns failures for repair. Use project files and returned failures to fix the application. Do not weaken or bypass acceptance checks.";
    }

    private void DiscoverVerification(bool wroteThisTurn)
    {
        bool replaceable = verification == null || verification.FromProject;
        if (!replaceable || !wroteThisTurn || mode == PermissionMode.ReadOnly)
        {
            return;
        }
        string? command = projectInspector.ProjectVerificationCommand(toolContext.Workspace);
        if (command == null)
        {
            return;
        }
        // Keep checks required earlier in this turn. Deleting a script must not skip it.
        List<string> commands = new List<string>();
        if (verification != null)
        {
            foreach (string part in verification.Command.Split(" && "))
            {
                if (!commands.Contains(part))
                {
                    commands.Add(part);
                }
            }
        }
        foreach (string part in command.Split(" && "))
        {
            if (!commands.Contains(part))
            {
                commands.Add(part);
            }
        }
        verification = new VerificationSettings(string.Join(" && ", commands), null, true);
    }

    private async Task<bool> RunAcceptanceAsync(TurnState turn, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // A summary right after a passing check does not need to run it again.
        if (verificationResult != null && verificationResult.Passed && turn.LastVerifiedToolCalls == turn.ToolCallsThisTurn)
        {
            return true;
        }
        bool passed = await VerifyAsync(token);
        turn.LastVerifiedToolCalls = turn.ToolCallsThisTurn;
        return passed;
    }

    private async Task<bool> VerifyAsync(CancellationToken token)
    {
        if (mode == PermissionMode.ReadOnly)
        {
            throw new InvalidOperationException("Acceptance commands are unavailable in read-only mode.");
        }
        if (verification == null)
        {
            return true;
        }
        VerificationSettings check = verification;
        int attempts = 1;
        if (verificationResult != null)
        {
            attempts = verificationResult.Attempts + 1;
        }
        if (attempts > check.AttemptLimit)
        {
            throw new InvalidOperationException("Acceptance attempt limit reached before the agent finished. The task is incomplete.");
        }
        ui.Status("· checking acceptance (" + attempts + "/" + check.AttemptLimit + ")");
        JsonObject callPreview = new JsonObject();
        callPreview["command"] = check.Command;
        ui.ToolCall("verification", callPreview);
        string output = await shellRunner.RunCommandAsync(check.Command, toolContext.Workspace, token);
        token.ThrowIfCancellationRequested();
        bool passed = VerificationRules.Passed(output);
        if (passed)
        {
            sameVerificationFailures = 0;
        }
        else if (verificationResult != null && !verificationResult.Passed && VerificationRules.FailureSignature(verificationResult.Output) == VerificationRules.FailureSignature(output))
        {
            sameVerificationFailures++;
        }
        else
        {
            sameVerificationFailures = 1;
        }
        verificationResult = new VerificationResult(attempts, passed, output);
        ui.ToolResult(output);
        await eventBus.EmitAsync(AgentEventKind.PostVerify, verificationResult);
        if (passed)
        {
            ui.Status("· acceptance checks passed");
            return true;
        }
        if (attempts >= check.AttemptLimit)
        {
            throw new InvalidOperationException("Acceptance checks still fail after " + attempts + " attempts. The task is incomplete.\n" + TextTruncation.Middle(output, 1600));
        }
        ui.Status("· acceptance failed — continuing repairs automatically");
        string commandLine = string.Empty;
        if (check.FromProject)
        {
            commandLine = "\nCommand: " + check.Command;
        }
        messages.Add(Message.User("[Acceptance failed; the task is not complete. Repair the first failing behavior. The harness will rerun acceptance automatically. Do not skip tests or report success." + commandLine + "\n" + TextTruncation.Middle(output, contextManager.ToolResultCharLimit()) + "]"));
        if (sameVerificationFailures == 2 && canRefreshVerification)
        {
            ui.Status("· same check failed again — refreshing working context");
            // Repeating the same idea in a bigger transcript is not progress.
            // Keep the task, plan and real failure; drop old model-written narratives.
            contextManager.CancelBackground(true);
            messages = messages.Where(message => !message.CompactNote).ToList();
            contextManager.ResetAnchor();
            await CompactNowAsync(true, true);
        }
        return false;
    }

    private async Task<bool> CheckProgressAsync(CancellationToken token)
    {
        string? command = projectInspector.ProjectVerificationCommand(toolContext.Workspace);
        if (command == null)
        {
            return false;
        }
        ui.Status("· checking implementation progress");
        JsonObject callPreview = new JsonObject();
        callPreview["command"] = command;
        ui.ToolCall("verification", callPreview);
        contextManager.PrepareBackground(messages, tools, provider, CompactState());
        string output = await shellRunner.RunCommandAsync(command, toolContext.Workspace, token);
        token.ThrowIfCancellationRequested();
        bool passed = VerificationRules.Passed(output);
        if (passed)
        {
            progressFailure = string.Empty;
        }
        else
        {
            progressFailure = output;
        }
        ui.ToolResult(output);
        JsonObject payload = new JsonObject();
        payload["command"] = command;
        payload["passed"] = passed;
        payload["output"] = output;
        await eventBus.EmitAsync(AgentEventKind.PostProgressCheck, payload);
        if (passed)
        {
            messages.Add(Message.User("[Project checks passed: " + command + ". Continue the remaining work in the original request.]"));
        }
        else
        {
            messages.Add(Message.User("[Progress checks failed. Fix the first concrete failure before further investigation. Continue the same task.\nCommand: " + command + "\n" + TextTruncation.Middle(output, contextManager.ToolResultCharLimit()) + "]"));
        }
        return true;
    }

    private static string Sha256(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }

    private enum StepOutcome
    {
        Continue,
        Completed
    }

    private sealed class ReadRecord
    {
        public ReadRecord(int count)
        {
            Count = count;
        }

        public int Count { get; }

        public Message? Receipt { get; set; }
    }

    /// <summary>
    /// Counters for one user turn.
    /// </summary>
    private sealed class TurnState
    {
        public TurnState()
        {
            RepeatKey = string.Empty;
            RepeatedReads = new Dictionary<string, ReadRecord>();
            LastVerifiedToolCalls = -1;
        }

        public int Steps { get; set; }

        public int Nudges { get; set; }

        public int ToolCallsThisTurn { get; set; }

        public int LastVerifiedToolCalls { get; set; }

        public int SincePlanUpdate { get; set; }

        public string RepeatKey { get; set; }

        public int Repeats { get; set; }

        public int FailedCalls { get; set; }

        public Dictionary<string, ReadRecord> RepeatedReads { get; }

        public int ReadsSinceAction { get; set; }

        public int ActionOnlyCalls { get; set; }

        public int ReasoningExhaustions { get; set; }

        public bool WroteThisTurn { get; set; }

        public int LastProgressCheck { get; set; }

        /// <summary>
        /// Clears the repeat guards after a real check ran.
        /// </summary>
        public void ResetGuards(bool markProgressCheck)
        {
            RepeatedReads.Clear();
            Repeats = 0;
            FailedCalls = 0;
            ReadsSinceAction = 0;
            if (markProgressCheck)
            {
                LastProgressCheck = ToolCallsThisTurn;
            }
        }
    }
}
