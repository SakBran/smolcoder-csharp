using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Text;

namespace Smolcoder.Application.Context;

/// <summary>
/// Context budget management.
///
/// The fill gauge anchors on the real prompt size the backend reports,
/// and only estimates the new messages since then (about four characters per token).
///
/// Compaction goes from cheap to expensive:
///   Tier 0: when a file is rewritten, earlier reads of it are stubbed out.
///   Tier 1: old reasoning, old write bodies and old tool results are removed.
///   Tier 2: the transcript is rebuilt around a hand-over note. The harness writes the facts.
///           The model writes a short summary with thinking off.
/// </summary>
public sealed class ContextManager : IContextManager
{
    private const int MessageOverheadTokens = 8;
    private const int EvictKeepRecent = 6;
    private const int StaleReadMinChars = 1500;
    private const string EvictStub = "[old output removed to save space — run the tool again if you need it]";
    private const string StaleReadStub = "[this read is out of date — the file was rewritten afterwards. Call read_file again if you need its current content.]";
    private const string HandoverMarker = "Hand-over notes:\n";

    private static readonly Regex WriteReceipt = new Regex(@"^(Created|Overwrote|Edited) ", RegexOptions.Compiled);
    private static readonly Regex HistoryPlaceholder = new Regex(@"^\[\d+ characters already applied to [^\r\n]+\. Read the file for current code\.\]$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    private int window;
    private int reserve;
    private int lastPromptTokens;
    private int lastCompletionTokens;
    private int anchorIndex;
    private bool floorWarned;
    private double calibration = 1;
    private bool replaysThinking = true;
    private BackgroundJob? background;
    private PreparedCompaction? prepared;
    private int preparedAt;

    public ContextManager(int window, int reserve)
    {
        this.window = window;
        this.reserve = reserve;
    }

    public void SetWindow(int newWindow, int? newReserve)
    {
        CancelBackground(true);
        window = newWindow;
        calibration = 1;
        if (newReserve.HasValue)
        {
            reserve = newReserve.Value;
        }
        ResetAnchor();
    }

    /// <summary>
    /// After this call, lastPromptTokens + lastCompletionTokens cover exactly
    /// the first messageCount messages.
    /// </summary>
    public void RecordUsage(int? promptTokens, int? completionTokens, int messageCount)
    {
        if (promptTokens.HasValue && promptTokens.Value > 0)
        {
            lastPromptTokens = promptTokens.Value;
            if (completionTokens.HasValue)
            {
                lastCompletionTokens = completionTokens.Value;
            }
            else
            {
                lastCompletionTokens = 0;
            }
            anchorIndex = messageCount;
        }
    }

    public void ResetAnchor()
    {
        lastPromptTokens = 0;
        lastCompletionTokens = 0;
        anchorIndex = 0;
        floorWarned = false;
    }

    public void SetReplayThinking(bool value)
    {
        replaysThinking = value;
        ResetAnchor();
    }

    public int EstimateMessages(IReadOnlyList<Message> messages)
    {
        int thinkingFrom = Message.LastUserIndex(messages);
        int total = 0;
        for (int index = 0; index < messages.Count; index++)
        {
            Message message = messages[index];
            total += TokenEstimator.Estimate(message.Content) + MessageOverheadTokens;
            total += message.Images.Count * TokenEstimator.ImageTokens;
            if (replaysThinking && message.Thinking != null && index > thinkingFrom)
            {
                total += TokenEstimator.Estimate(message.Thinking);
            }
            foreach (ToolCall call in message.ToolCalls)
            {
                total += TokenEstimator.Estimate(call.Name + call.Arguments.ToJsonString()) + MessageOverheadTokens;
            }
        }
        return total;
    }

    public int EstimateTools(IReadOnlyList<ToolSpec> tools)
    {
        return TokenEstimator.Estimate(TranscriptFingerprint.OfTools(tools));
    }

    public int EstimatePrompt(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools)
    {
        int charBased = CharBasedEstimate(messages, tools);
        if (lastPromptTokens > 0 && anchorIndex <= messages.Count)
        {
            List<Message> newMessages = messages.Skip(anchorIndex).ToList();
            int anchored = lastPromptTokens + lastCompletionTokens + EstimateMessages(newMessages);
            return Math.Max(charBased, anchored);
        }
        return charBased;
    }

    public int UsableWindow()
    {
        int safety = Math.Min(256, (int)Math.Floor(window * 0.05));
        return Math.Max(0, window - reserve - safety);
    }

    /// <summary>
    /// Room for several reads, their calls and the next edit. A character cap.
    /// </summary>
    public int ToolResultCharLimit()
    {
        return Math.Min(10000, Math.Max(600, (int)Math.Floor(UsableWindow() * 0.4)));
    }

    /// <summary>
    /// Learns the tokenizer overhead from real usage. It only goes up, to at most 3.
    /// </summary>
    public void Calibrate(int? promptTokens, IReadOnlyList<Message> input, IReadOnlyList<ToolSpec> tools)
    {
        if (!promptTokens.HasValue || promptTokens.Value <= 0)
        {
            return;
        }
        int estimate = EstimateMessages(input) + EstimateTools(tools);
        if (estimate > 0)
        {
            calibration = Math.Max(calibration, Math.Min(3, (double)promptTokens.Value / estimate));
        }
    }

    public ContextBudget Budget(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools)
    {
        int prompt = EstimatePrompt(messages, tools);
        string source = "estimate";
        if (lastPromptTokens > 0)
        {
            source = "measured + estimate";
        }
        return new ContextBudget(prompt, window, reserve, Math.Max(0, UsableWindow() - prompt), source);
    }

    public void AssertFits(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools)
    {
        int size = EstimatePrompt(messages, tools);
        if (size > UsableWindow())
        {
            throw new InvalidOperationException(
                "Context budget exceeded: about " + size + " input tokens, " + UsableWindow()
                + " available after reserving the reply. Shorten the request or AGENTS.md, use /models for a larger loaded window, or restart with --ctx. The request was not sent.");
        }
    }

    public int FillPercent(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools)
    {
        if (window <= 0)
        {
            return 0;
        }
        return Math.Min(100, (int)Math.Round((double)EstimatePrompt(messages, tools) / window * 100, MidpointRounding.AwayFromZero));
    }

    public bool NeedsAttention(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools)
    {
        int estimate = EstimatePrompt(messages, tools);
        if (estimate <= 0.8 * UsableWindow())
        {
            floorWarned = false;
            return false;
        }
        return !floorWarned || estimate > UsableWindow();
    }

    /// <summary>
    /// Tier 0: a file was just rewritten, so earlier big reads of it are wrong. Stub them out.
    /// </summary>
    public int EvictStaleReads(List<Message> messages, string filePath)
    {
        string target = NormalizePath(filePath);
        int stubbed = 0;
        for (int index = 1; index < messages.Count - 1; index++)
        {
            Message message = messages[index];
            if (message.Role != MessageRole.Tool || message.Evicted || message.Content.Length < StaleReadMinChars)
            {
                continue;
            }
            ToolCall? call = CallFor(messages, index);
            if (call == null || call.Name != "read_file")
            {
                continue;
            }
            string path = Tools.JsonArguments.GetText(call.Arguments, "path");
            if (NormalizePath(path) != target)
            {
                continue;
            }
            message.Content = StaleReadStub;
            message.Evicted = true;
            stubbed++;
        }
        if (stubbed > 0)
        {
            anchorIndex = 0;
            lastPromptTokens = 0;
        }
        return stubbed;
    }

    /// <summary>
    /// Prepares a compaction while a command runs, so it is ready when needed.
    /// It never waits behind coding.
    /// </summary>
    public void PrepareBackground(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, IModelProvider provider, CompactState state)
    {
        if (background != null || prepared != null)
        {
            return;
        }
        if (messages.Count < Math.Max(10, preparedAt + 6))
        {
            return;
        }
        if (EstimatePrompt(messages, tools) < UsableWindow() * 0.6)
        {
            return;
        }

        preparedAt = messages.Count;
        List<Message> snapshot = messages.Select(message => message.Clone()).ToList();
        string source = TranscriptFingerprint.OfMessages(snapshot);
        CompactState frozen = state.Freeze();
        CancellationTokenSource cancellation = new CancellationTokenSource();
        BackgroundJob job = new BackgroundJob(cancellation);
        job.Work = RunBackgroundAsync(job, snapshot, source, tools, provider, frozen);
        background = job;
    }

    private async Task RunBackgroundAsync(BackgroundJob job, List<Message> snapshot, string source, IReadOnlyList<ToolSpec> tools, IModelProvider provider, CompactState frozen)
    {
        CancellationToken token = job.Cancellation.Token;
        try
        {
            // Most checks finish faster than a summary. Give them time before starting one.
            await Task.Delay(750, token);
            List<Message> working = snapshot.Select(message => message.Clone()).ToList();
            List<Message> compacted = await CompactAsync(working, provider, frozen, false, true, token);
            if (!token.IsCancellationRequested && CharBasedEstimate(compacted, tools) < CharBasedEstimate(snapshot, tools))
            {
                prepared = new PreparedCompaction(source, snapshot.Count, compacted);
            }
        }
        catch (Exception)
        {
            // Background work is optional. Failures are ignored.
        }
        finally
        {
            if (background == job)
            {
                background = null;
            }
            job.Cancellation.Dispose();
        }
    }

    public void CancelBackground(bool discard)
    {
        BackgroundJob? job = background;
        if (job != null)
        {
            try
            {
                job.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The job already finished.
            }
        }
        if (discard)
        {
            prepared = null;
            preparedAt = 0;
        }
    }

    public async Task ForegroundAsync()
    {
        BackgroundJob? job = background;
        CancelBackground(false);
        if (job != null && job.Work != null)
        {
            await job.Work;
        }
    }

    /// <summary>
    /// Brings the transcript back under budget. Changes the list and may return a new one.
    /// </summary>
    public async Task<CompactionResult> ManageAsync(List<Message> messages, IReadOnlyList<ToolSpec> tools, IModelProvider provider, CompactState state, bool force, bool deterministic, CancellationToken cancellationToken)
    {
        int before = EstimatePrompt(messages, tools);
        if (!force && before <= 0.8 * UsableWindow())
        {
            return new CompactionResult(messages, new CompactionReport(CompactionAction.None, before, before));
        }

        if (prepared != null)
        {
            PreparedCompaction ready = prepared;
            prepared = null;
            if (ready.Count <= messages.Count && TranscriptFingerprint.OfMessages(messages.Take(ready.Count).ToList()) == ready.Source)
            {
                List<Message> candidate = new List<Message>(ready.Messages);
                candidate.AddRange(messages.Skip(ready.Count));
                int candidateSize = CharBasedEstimate(candidate, tools);
                if (candidateSize < before && candidateSize <= UsableWindow() * 0.8)
                {
                    ResetAnchor();
                    return new CompactionResult(candidate, new CompactionReport(CompactionAction.Compacted, before, candidateSize));
                }
            }
        }

        DropOldReasoning(messages);
        CollapseOldWrites(messages);

        // Tier 1b: remove old tool-result bodies, oldest first.
        int evictBoundary = Math.Max(1, messages.Count - EvictKeepRecent);
        for (int index = 1; index < evictBoundary; index++)
        {
            Message message = messages[index];
            if (message.Role == MessageRole.Tool && !message.Evicted && message.Content.Length > 200)
            {
                message.Content = EvictStub;
                message.Evicted = true;
                anchorIndex = 0;
                lastPromptTokens = 0;
                if (EstimatePrompt(messages, tools) <= 0.6 * UsableWindow())
                {
                    break;
                }
            }
        }
        int after = EstimatePrompt(messages, tools);
        if (!force && after <= 0.8 * UsableWindow())
        {
            return new CompactionResult(messages, new CompactionReport(CompactionAction.Evicted, before, after));
        }

        // Tier 2: rebuild around a hand-over note.
        List<Message> compacted = await CompactAsync(messages, provider, state, deterministic, false, cancellationToken);
        anchorIndex = 0;
        lastPromptTokens = 0;
        after = EstimatePrompt(compacted, tools);

        // When the kept tail alone is too big, drop whole groups. Never leave orphan tool results.
        while (after > UsableWindow() && compacted.Count > 2)
        {
            int end = 3;
            if (compacted[2].Role == MessageRole.Assistant && compacted[2].HasToolCalls)
            {
                while (end < compacted.Count && compacted[end].Role == MessageRole.Tool)
                {
                    end++;
                }
            }
            compacted.RemoveRange(2, end - 2);
            after = EstimatePrompt(compacted, tools);
        }

        if (after > 0.8 * UsableWindow())
        {
            // The window cannot hold what must stay. Stop repeating futile summaries.
            floorWarned = true;
            return new CompactionResult(compacted, new CompactionReport(CompactionAction.Floor, before, after));
        }
        return new CompactionResult(compacted, new CompactionReport(CompactionAction.Compacted, before, after));
    }

    /// <summary>
    /// Older reasoning is cheaper to drop than fresh source code. The newest assistant message keeps its reasoning.
    /// </summary>
    private void DropOldReasoning(List<Message> messages)
    {
        int thinkingFrom = messages.Count;
        for (int index = messages.Count - 1; index >= 1; index--)
        {
            if (messages[index].Role == MessageRole.Assistant)
            {
                thinkingFrom = index;
                break;
            }
        }
        for (int index = 1; index < thinkingFrom; index++)
        {
            if (messages[index].Role == MessageRole.Assistant && messages[index].Thinking != null)
            {
                messages[index].Thinking = null;
                ResetAnchor();
            }
        }
    }

    /// <summary>
    /// Completed writes are already on disk. Their big bodies are replaced with a short history record.
    /// The newest tool-call group stays whole, and failed calls are never removed.
    /// </summary>
    private void CollapseOldWrites(List<Message> messages)
    {
        int latestGroup = messages.Count;
        for (int index = messages.Count - 1; index >= 1; index--)
        {
            if (messages[index].Role == MessageRole.Assistant && messages[index].HasToolCalls)
            {
                latestGroup = index;
                break;
            }
        }

        for (int index = latestGroup - 1; index >= 1; index--)
        {
            Message message = messages[index];
            if (!message.HasToolCalls)
            {
                continue;
            }
            List<Message> results = new List<Message>();
            for (int next = index + 1; next < messages.Count && messages[next].Role == MessageRole.Tool; next++)
            {
                results.Add(messages[next]);
            }

            HashSet<string> removed = new HashSet<string>();
            List<string> receipts = new List<string>();
            foreach (ToolCall call in message.ToolCalls)
            {
                Message? receipt = results.FirstOrDefault(result => result.ToolCallId == call.Id);
                if (receipt == null || !WriteReceipt.IsMatch(receipt.Content))
                {
                    continue;
                }
                string[] keys;
                if (call.Name == "write_file")
                {
                    keys = new string[] { "content" };
                }
                else if (call.Name == "edit_file")
                {
                    keys = new string[] { "old_text", "new_text" };
                }
                else
                {
                    keys = new string[0];
                }
                bool large = false;
                foreach (string key in keys)
                {
                    string? value = Tools.JsonArguments.GetString(call.Arguments, key);
                    if (value != null && (value.Length > 1200 || HistoryPlaceholder.IsMatch(value.Trim())))
                    {
                        large = true;
                    }
                }
                if (!large)
                {
                    continue;
                }
                removed.Add(call.Id);
                receipts.Add(call.Name + ": " + TextTruncation.End(receipt.Content, 600));
            }

            if (removed.Count == 0)
            {
                continue;
            }

            List<ToolCall> remainingCalls = message.ToolCalls.Where(call => !removed.Contains(call.Id)).ToList();
            List<Message> retained = new List<Message>();
            if (remainingCalls.Count > 0)
            {
                Message kept = message.Clone();
                kept.ToolCalls = remainingCalls;
                retained.Add(kept);
                foreach (Message result in results)
                {
                    if (result.ToolCallId == null || !removed.Contains(result.ToolCallId))
                    {
                        retained.Add(result);
                    }
                }
            }
            else if (message.Content.Length > 0)
            {
                retained.Add(Message.Assistant(message.Content));
            }

            Message record = Message.User(
                "[Harness history record — earlier tool executions, not a new request. Applied code omitted; read_file returns current source. Future changes require actual tool calls.]\n"
                + string.Join("\n", receipts));
            record.HistoryNote = true;
            retained.Add(record);

            messages.RemoveRange(index, results.Count + 1);
            messages.InsertRange(index, retained);
            ResetAnchor();
        }
    }

    private async Task<List<Message>> CompactAsync(List<Message> allMessages, IModelProvider provider, CompactState state, bool deterministic, bool isBackground, CancellationToken cancellationToken)
    {
        Message system = allMessages[0];
        // Old notes are removed. Their content is written again below.
        List<Message> messages = new List<Message>();
        messages.Add(system);
        messages.AddRange(allMessages.Skip(1).Where(message => !message.CompactNote));

        List<string> facts = new List<string>();
        if (state.PlanLine != null)
        {
            facts.Add(state.PlanLine);
        }
        if (state.FilesTouched.Count > 0)
        {
            facts.Add("Files created/modified so far: " + string.Join(", ", state.FilesTouched.Skip(Math.Max(0, state.FilesTouched.Count - 30))));
        }
        if (state.CommandsRun.Count > 0)
        {
            IEnumerable<string> recent = state.CommandsRun
                .Skip(Math.Max(0, state.CommandsRun.Count - 6))
                .Select(command => TextTruncation.Middle(Whitespace.Replace(command, " "), 180));
            facts.Add("Recent commands: " + string.Join("; ", recent));
        }

        int narrativeCap = Math.Min(2800, Math.Max(600, (int)Math.Floor(UsableWindow() * 0.3)));
        string narrative = string.Empty;
        double digestBudget = Math.Min(60000, Math.Max(0, (UsableWindow() / calibration - 1500) * 3));
        int digestBudgetChars = (int)Math.Floor(digestBudget);
        if (!deterministic && digestBudgetChars >= 3000)
        {
            try
            {
                string previous = string.Join("\n", allMessages.Where(message => message.CompactNote).Select(message => HandoverPart(message.Content, true)));
                string transcript = DigestRenderer.Render(messages.Skip(1).ToList(), Math.Max(0, digestBudgetChars - Math.Min(previous.Length, 2400)));

                string request =
                    "Write hand-over notes for this session under exactly these headings:\n"
                    + "In progress: what was being worked on when the log ends, and its current state.\n"
                    + "Next: the next concrete step.\n"
                    + "Notes: key decisions, gotchas, exact names/APIs/values the agent must not forget, and any unresolved errors.\n"
                    + "Keep it under 180 words. Preserve exact module exports, function signatures and required argument shapes. Do not repeat the goal, plan or file list: the harness adds those separately. Never treat a failed check as completed work.\n\n";
                if (state.PlanLine != null)
                {
                    request = request + "Current plan:\n" + TextTruncation.End(state.PlanLine, 1600) + "\n\n";
                }
                if (previous.Length > 0)
                {
                    request = request + "Previous hand-over (retain still-relevant decisions):\n" + TextTruncation.End(previous, 2400) + "\n\n";
                }
                request = request + "Session log (oldest first, long outputs shortened):\n" + transcript;

                List<Message> summaryRequest = new List<Message>();
                summaryRequest.Add(Message.System("You write hand-over notes for a coding agent whose conversation is about to be cleared. Be concrete and factual; never invent. Reply with only the notes."));
                summaryRequest.Add(Message.User(request));

                ChatRequestOptions options = new ChatRequestOptions();
                options.ForceThinkingOff = true;
                options.MaxTokens = Math.Min(700, reserve);
                options.CancellationToken = cancellationToken;
                options.Timeout = TimeSpan.FromSeconds(45);
                options.Background = isBackground;

                ChatResult result = await provider.ChatAsync(summaryRequest, new List<ToolSpec>(), options);
                if (!result.Truncated)
                {
                    narrative = TextTruncation.End(result.Content.Trim(), narrativeCap);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (isBackground)
                {
                    throw;
                }
                narrative = string.Empty;
            }
        }
        if (narrative.Length == 0)
        {
            string oldNotes = string.Join("\n", allMessages.Where(message => message.CompactNote).Select(message => HandoverPart(message.Content, false)));
            narrative = TextTruncation.End(oldNotes, narrativeCap);
        }

        int keepFrom = FindTailStart(messages);
        List<Message> tail = new List<Message>();
        if (keepFrom < messages.Count)
        {
            tail.AddRange(messages.Skip(keepFrom));
        }
        foreach (Message message in tail)
        {
            if (message.Role == MessageRole.Assistant)
            {
                message.Thinking = null;
            }
        }

        string requestLines;
        if (state.CurrentRequest.Length > 0 && state.CurrentRequest != state.OriginalRequest)
        {
            requestLines = "Original request: " + state.OriginalRequest + "\nCurrent request (what you are working on NOW): " + state.CurrentRequest + "\n";
        }
        else
        {
            requestLines = "Original request: " + state.OriginalRequest + "\n";
        }

        string note = "[The conversation so far was compacted to save context. Continue the task from these notes — do not start over, and do not redo finished steps.]\n" + requestLines;
        if (state.VerificationLine.Length > 0)
        {
            note = note + state.VerificationLine + "\n";
        }
        if (facts.Count > 0)
        {
            note = note + TextTruncation.End(string.Join("\n", facts), 2400) + "\n";
        }
        if (narrative.Length > 0)
        {
            note = note + "\n[Model-written summary; file contents and tool results take precedence.]\n" + HandoverMarker + narrative;
        }

        Message noteMessage = Message.User(note);
        noteMessage.CompactNote = true;

        List<Message> rebuilt = new List<Message>();
        rebuilt.Add(system);
        rebuilt.Add(noteMessage);
        rebuilt.AddRange(tail);
        return rebuilt;
    }

    /// <summary>
    /// Where the kept tail starts: the latest real user message nearby,
    /// or else the last complete tool-call group.
    /// </summary>
    private static int FindTailStart(List<Message> messages)
    {
        int keepFrom = messages.Count;
        for (int index = messages.Count - 1; index >= Math.Max(1, messages.Count - 8); index--)
        {
            if (messages[index].IsPlainUserTurn)
            {
                keepFrom = index;
                break;
            }
        }
        if (keepFrom == messages.Count)
        {
            for (int index = messages.Count - 1; index >= 1; index--)
            {
                Message message = messages[index];
                if (message.Role == MessageRole.Assistant && message.HasToolCalls)
                {
                    bool allAnswered = true;
                    foreach (ToolCall call in message.ToolCalls)
                    {
                        bool answered = messages.Skip(index + 1).Any(other => other.Role == MessageRole.Tool && other.ToolCallId == call.Id);
                        if (!answered)
                        {
                            allAnswered = false;
                        }
                    }
                    if (allAnswered)
                    {
                        keepFrom = index;
                    }
                    break;
                }
                if (message.Role == MessageRole.Assistant)
                {
                    keepFrom = index;
                    break;
                }
            }
        }
        return keepFrom;
    }

    /// <summary>
    /// The hand-over part of an earlier note. When it has none, returns the whole note
    /// (wholeWhenMissing) or an empty string.
    /// </summary>
    private static string HandoverPart(string content, bool wholeWhenMissing)
    {
        int position = content.IndexOf(HandoverMarker, StringComparison.Ordinal);
        if (position >= 0)
        {
            string rest = content.Substring(position + HandoverMarker.Length);
            int nextMarker = rest.IndexOf(HandoverMarker, StringComparison.Ordinal);
            if (nextMarker >= 0)
            {
                return rest.Substring(0, nextMarker);
            }
            return rest;
        }
        if (wholeWhenMissing)
        {
            return content;
        }
        return string.Empty;
    }

    private int CharBasedEstimate(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools)
    {
        return (int)Math.Ceiling((EstimateMessages(messages) + EstimateTools(tools)) * calibration);
    }

    private static string NormalizePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }
        return normalized;
    }

    /// <summary>
    /// The tool call that a tool-result message answers.
    /// </summary>
    private static ToolCall? CallFor(List<Message> messages, int toolMessageIndex)
    {
        string? id = messages[toolMessageIndex].ToolCallId;
        if (id == null)
        {
            return null;
        }
        for (int index = toolMessageIndex - 1; index >= 0; index--)
        {
            Message message = messages[index];
            if (message.Role != MessageRole.Assistant || !message.HasToolCalls)
            {
                continue;
            }
            foreach (ToolCall call in message.ToolCalls)
            {
                if (call.Id == id)
                {
                    return call;
                }
            }
        }
        return null;
    }

    private sealed class BackgroundJob
    {
        public BackgroundJob(CancellationTokenSource cancellation)
        {
            Cancellation = cancellation;
        }

        public CancellationTokenSource Cancellation { get; }

        public Task? Work { get; set; }
    }

    private sealed class PreparedCompaction
    {
        public PreparedCompaction(string source, int count, List<Message> messages)
        {
            Source = source;
            Count = count;
            Messages = messages;
        }

        public string Source { get; }

        public int Count { get; }

        public List<Message> Messages { get; }
    }
}
