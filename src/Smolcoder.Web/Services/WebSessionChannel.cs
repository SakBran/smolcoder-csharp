using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Tools;
using Smolcoder.Domain.Attachments;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Web.Services;

/// <summary>
/// The user interface of one web session. The agent writes rows into the transcript;
/// the page reads them and answers questions through this channel.
/// </summary>
public sealed class WebSessionChannel : ISessionInteraction
{
    private const int MaxEvents = 2000;
    private const int MaxMergedChars = 100000;
    private static readonly Regex AnsiCodes = new Regex(@"\x1b\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

    private readonly object gate = new object();
    private readonly List<UiEvent> events = new List<UiEvent>();
    private readonly Dictionary<int, PendingQuestion> pending = new Dictionary<int, PendingQuestion>();
    private readonly Channel<UserInput> inputs = Channel.CreateUnbounded<UserInput>();
    private int lastAskId;
    private int notifyScheduled;
    private bool closed;

    /// <summary>
    /// Raised (at most every 40 ms) when the transcript or status changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when something worth saving happened.
    /// </summary>
    public event Action? SaveRequested;

    public SessionStatus CurrentStatus { get; private set; }

    /// <summary>
    /// What the agent is doing right now ("thinking", "organizing context"), or null.
    /// </summary>
    public string? BusyLabel { get; private set; }

    /// <summary>
    /// Called by the first plain user message when the session has no title yet.
    /// </summary>
    public Action<string>? OnFirstMessage { get; set; }

    public List<UiEvent> EventsSnapshot()
    {
        lock (gate)
        {
            return new List<UiEvent>(events);
        }
    }

    /// <summary>
    /// Puts back saved rows. Unanswered questions are dropped; they cannot be answered after a restart.
    /// </summary>
    public void Restore(IEnumerable<UiEvent> saved)
    {
        lock (gate)
        {
            foreach (UiEvent item in saved)
            {
                bool isQuestion = item.Kind == UiEventKind.Confirm || item.Kind == UiEventKind.Select || item.Kind == UiEventKind.Prompt;
                if (isQuestion && !item.Answered)
                {
                    continue;
                }
                if (item.AskId > lastAskId)
                {
                    lastAskId = item.AskId;
                }
                events.Add(item);
            }
        }
        NotifyChanged();
    }

    public void SetStatus(SessionStatus status)
    {
        CurrentStatus = status;
        NotifyChanged();
    }

    /// <summary>
    /// A message typed in the page. It goes to the waiting session loop.
    /// </summary>
    public bool Send(string text, List<Attachment> attachments)
    {
        string trimmed = text.Trim();
        if (closed || (trimmed.Length == 0 && attachments.Count == 0))
        {
            return false;
        }
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.User;
        row.Text = trimmed;
        foreach (Attachment attachment in attachments)
        {
            UiFile file = new UiFile();
            file.Id = attachment.Id;
            file.Name = attachment.Name;
            file.IsImage = attachment.Kind == AttachmentKind.Image;
            file.Size = attachment.Size;
            row.Files.Add(file);
        }
        Add(row);
        CurrentStatus = SessionStatus.Busy;
        if (OnFirstMessage != null && !trimmed.StartsWith("/", StringComparison.Ordinal) && trimmed.Length > 0)
        {
            OnFirstMessage(trimmed);
        }
        inputs.Writer.TryWrite(new UserInput(trimmed, attachments));
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// Answers every open question with "cancelled" (a command approval with "no").
    /// </summary>
    public void CancelQuestions()
    {
        List<PendingQuestion> open;
        lock (gate)
        {
            open = pending.Values.ToList();
            pending.Clear();
        }
        foreach (PendingQuestion question in open)
        {
            question.Row.Answered = true;
            question.Row.Answer = "cancelled";
            question.Cancel();
        }
        Line("status", "· cancelled");
        NotifyChanged();
    }

    public void Answer(int askId, string? answer, int? index)
    {
        PendingQuestion? question;
        lock (gate)
        {
            if (!pending.TryGetValue(askId, out question))
            {
                return;
            }
            pending.Remove(askId);
        }
        question.Row.Answered = true;
        question.Row.Answer = answer;
        question.Resolve(answer, index);
        CurrentStatus = SessionStatus.Busy;
        NotifyChanged();
        RequestSave();
    }

    /// <summary>
    /// The session ended. Waiting reads stop.
    /// </summary>
    public void Close()
    {
        closed = true;
        inputs.Writer.TryComplete();
        CancelQuestions();
        CurrentStatus = SessionStatus.Closed;
        NotifyChanged();
    }

    public async Task<UserInput> ReadInputAsync(CancellationToken cancellationToken)
    {
        BusyLabel = null;
        CurrentStatus = SessionStatus.Idle;
        NotifyChanged();
        RequestSave();
        try
        {
            return await inputs.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            throw new OperationCanceledException("The session was closed.");
        }
    }

    public Task<int?> SelectAsync(string title, IReadOnlyList<SelectOption> options, CancellationToken cancellationToken)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Select;
        row.Text = title;
        row.Options = options.ToList();
        TaskCompletionSource<int?> completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ask(row, new PendingQuestion(row,
            (answer, index) => completion.TrySetResult(index),
            () => completion.TrySetResult(null)));
        cancellationToken.Register(() => completion.TrySetResult(null));
        return completion.Task;
    }

    public Task<string?> PromptAsync(string title, string? placeholder, CancellationToken cancellationToken)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Prompt;
        row.Text = title;
        row.Placeholder = placeholder;
        TaskCompletionSource<string?> completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ask(row, new PendingQuestion(row,
            (answer, index) =>
            {
                string? value = null;
                if (answer != null)
                {
                    value = answer.Trim();
                    if (value.Length > 500)
                    {
                        value = value.Substring(0, 500);
                    }
                    if (value.Length == 0)
                    {
                        value = null;
                    }
                }
                completion.TrySetResult(value);
            },
            () => completion.TrySetResult(null)));
        cancellationToken.Register(() => completion.TrySetResult(null));
        return completion.Task;
    }

    public Task<CommandApproval> ConfirmCommandAsync(string command, string reason, CancellationToken cancellationToken)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Confirm;
        row.Text = command;
        row.Reason = reason;
        TaskCompletionSource<CommandApproval> completion = new TaskCompletionSource<CommandApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ask(row, new PendingQuestion(row,
            (answer, index) =>
            {
                if (answer == "always")
                {
                    completion.TrySetResult(CommandApproval.Always);
                }
                else if (answer == "yes")
                {
                    completion.TrySetResult(CommandApproval.Yes);
                }
                else
                {
                    completion.TrySetResult(CommandApproval.No);
                }
            },
            () => completion.TrySetResult(CommandApproval.No)));
        cancellationToken.Register(() => completion.TrySetResult(CommandApproval.No));
        return completion.Task;
    }

    public void Refresh()
    {
        NotifyChanged();
    }

    public void ResetResponse()
    {
        lock (gate)
        {
            if (events.Count > 0)
            {
                UiEvent last = events[events.Count - 1];
                if (last.Kind == UiEventKind.Assistant || last.Kind == UiEventKind.Thinking)
                {
                    events.RemoveAt(events.Count - 1);
                }
            }
        }
        NotifyChanged();
    }

    public void Token(string text)
    {
        AppendStream(UiEventKind.Assistant, text);
    }

    public void Thinking(string text)
    {
        AppendStream(UiEventKind.Thinking, text);
    }

    public void ToolCall(string name, JsonObject arguments)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Tool;
        row.Name = name;
        row.Text = ToolCallSummary.Summarize(name, arguments);
        Add(row);
    }

    public void ToolResult(string result)
    {
        string[] lines = result.Split('\n');
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Result;
        string first = lines[0];
        if (first.Length > 160)
        {
            first = first.Substring(0, 160);
        }
        row.Text = first;
        row.IsError = lines[0].StartsWith("Error", StringComparison.Ordinal);
        row.Body = result;
        Add(row);
    }

    public void PrintLine(string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Print;
        row.Text = AnsiCodes.Replace(text, string.Empty);
        Add(row);
    }

    public void Status(string text)
    {
        Line("status", text);
    }

    public void Warn(string text)
    {
        Line("warn", text);
    }

    public void Error(string text)
    {
        Line("error", text.Trim());
    }

    public void StartSpinner(string label)
    {
        BusyLabel = label;
        NotifyChanged();
    }

    public void StopSpinner()
    {
        BusyLabel = null;
        NotifyChanged();
    }

    public void TurnEnd(string label)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.TurnEnd;
        row.Text = label;
        Add(row);
        RequestSave();
    }

    public void PlanUpdated(Plan plan)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Plan;
        foreach (PlanStep step in plan.Steps)
        {
            UiPlanStep item = new UiPlanStep();
            item.Text = step.Text;
            item.Done = step.Done;
            row.PlanSteps.Add(item);
        }
        row.PlanCurrent = plan.CurrentIndex;
        Add(row);
    }

    private void Line(string kind, string text)
    {
        UiEvent row = new UiEvent();
        row.Kind = UiEventKind.Line;
        row.Name = kind;
        row.Text = AnsiCodes.Replace(text, string.Empty);
        Add(row);
    }

    private void Ask(UiEvent row, PendingQuestion question)
    {
        lock (gate)
        {
            lastAskId++;
            row.AskId = lastAskId;
            pending[row.AskId] = question;
        }
        Add(row);
        CurrentStatus = SessionStatus.Waiting;
        NotifyChanged();
    }

    /// <summary>
    /// Streamed text joins the previous row of the same kind.
    /// </summary>
    private void AppendStream(UiEventKind kind, string text)
    {
        lock (gate)
        {
            if (events.Count > 0)
            {
                UiEvent last = events[events.Count - 1];
                if (last.Kind == kind && last.Text.Length + text.Length < MaxMergedChars)
                {
                    last.Text = last.Text + text;
                    NotifyChanged();
                    return;
                }
            }
            UiEvent row = new UiEvent();
            row.Kind = kind;
            row.Text = text;
            AddLocked(row);
        }
        NotifyChanged();
    }

    private void Add(UiEvent row)
    {
        lock (gate)
        {
            AddLocked(row);
        }
        NotifyChanged();
    }

    private void AddLocked(UiEvent row)
    {
        events.Add(row);
        if (events.Count > MaxEvents)
        {
            events.RemoveRange(0, events.Count - MaxEvents);
        }
    }

    private void RequestSave()
    {
        Action? handler = SaveRequested;
        if (handler != null)
        {
            handler();
        }
    }

    /// <summary>
    /// Many tokens per second would re-render the page too often. Group updates into 40 ms steps.
    /// </summary>
    private void NotifyChanged()
    {
        if (Interlocked.Exchange(ref notifyScheduled, 1) == 1)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            await Task.Delay(40);
            Interlocked.Exchange(ref notifyScheduled, 0);
            Action? handler = Changed;
            if (handler != null)
            {
                handler();
            }
        });
    }

    private sealed class PendingQuestion
    {
        private readonly Action<string?, int?> resolve;
        private readonly Action cancel;

        public PendingQuestion(UiEvent row, Action<string?, int?> resolve, Action cancel)
        {
            Row = row;
            this.resolve = resolve;
            this.cancel = cancel;
        }

        public UiEvent Row { get; }

        public void Resolve(string? answer, int? index)
        {
            resolve(answer, index);
        }

        public void Cancel()
        {
            cancel();
        }
    }
}
