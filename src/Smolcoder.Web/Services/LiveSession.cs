using System.Text;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Sessions;
using Smolcoder.Domain.Attachments;
using Smolcoder.Domain.Sessions;

namespace Smolcoder.Web.Services;

/// <summary>
/// One open session in the web hub: its channel, its agent loop and its terminals.
/// </summary>
public sealed class LiveSession
{
    private readonly object gate = new object();
    private readonly Dictionary<string, TerminalView> terminals = new Dictionary<string, TerminalView>();
    private readonly ILineTerminalFactory terminalFactory;
    private int terminalCounter;

    public LiveSession(ChatSession meta, ILineTerminalFactory terminalFactory)
    {
        Meta = meta;
        this.terminalFactory = terminalFactory;
        Channel = new WebSessionChannel();
        Channel.Changed += RaiseChanged;
    }

    /// <summary>
    /// Raised when anything shown for this session changed.
    /// </summary>
    public event Action? Changed;

    public ChatSession Meta { get; }

    public string Id
    {
        get
        {
            return Meta.Id;
        }
    }

    public WebSessionChannel Channel { get; }

    public InteractiveSession? Session { get; private set; }

    public string? StartError { get; private set; }

    /// <summary>
    /// True when the session could not start because no usable model was found.
    /// The page shows the setup steps instead of the transcript.
    /// </summary>
    public bool NeedsSetup { get; private set; }

    /// <summary>
    /// The model a restored session asked for, when it is no longer available.
    /// </summary>
    public string? MissingModel { get; private set; }

    /// <summary>
    /// Model title attempts so far. At most two are made.
    /// </summary>
    public int TitleAttempts { get; set; }

    public bool IsRunning
    {
        get
        {
            return Session != null && Channel.CurrentStatus != SessionStatus.Closed;
        }
    }

    public SessionState? State()
    {
        InteractiveSession? session = Session;
        if (session == null)
        {
            return null;
        }
        return session.State();
    }

    /// <summary>
    /// Picks the model, builds the session and starts its input loop in the background.
    /// </summary>
    public async Task<bool> StartAsync(
        IModelSelector modelSelector,
        IInteractiveSessionFactory sessionFactory,
        ISettingsStore settingsStore,
        SessionPreferences preferences,
        StoredSession? restore,
        Func<LiveSession, Task> onTurnDone,
        Action<LiveSession> onExit)
    {
        Channel.SetStatus(SessionStatus.Starting);
        Channel.StartSpinner("looking for models");
        AppSettings settings = settingsStore.Load();
        ModelDescriptor? chosen;
        try
        {
            chosen = await modelSelector.PrepareModelAsync(preferences, settings, label => Channel.StartSpinner(label), CancellationToken.None);
        }
        catch (ModelNotFoundException exception)
        {
            MissingModel = exception.Wanted;
            NeedsSetup = true;
            Fail(exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            return false;
        }
        finally
        {
            Channel.StopSpinner();
        }
        if (chosen == null)
        {
            NeedsSetup = true;
            Fail("No usable model found yet.");
            return false;
        }

        InteractiveSession session = sessionFactory.Create(Channel, Meta.WorkspacePath, chosen, preferences, settings);
        if (restore != null)
        {
            session.Restore(restore.Snapshot);
        }
        session.OnTurnDone = () => onTurnDone(this);
        session.OnExit = () =>
        {
            Channel.Close();
            CloseTerminals();
            onExit(this);
        };
        Session = session;
        Meta.Model = chosen.Id;
        Meta.Backend = chosen.Backend;
        StartError = null;
        NeedsSetup = false;
        MissingModel = null;

        Channel.Status("● " + ModelLabels.BackendLabel(chosen) + " · " + chosen.Id + " · ctx " + chosen.ContextWindow.ToString("N0") + " · " + Domain.Modes.PermissionModes.Label(session.Agent.Mode) + " mode");
        session.Announce();
        _ = Task.Run(async () =>
        {
            try
            {
                await session.RunAsync();
            }
            catch (Exception exception)
            {
                Channel.Error("The session stopped: " + exception.Message);
            }
        });
        RaiseChanged();
        return true;
    }

    public bool Send(string text, List<Attachment> attachments)
    {
        if (Session == null)
        {
            return false;
        }
        return Channel.Send(text, attachments);
    }

    /// <summary>
    /// Switches a running session to another model after the current turn.
    /// </summary>
    public bool ChooseModel(ModelDescriptor model)
    {
        if (Session == null)
        {
            return false;
        }
        return Channel.RequestModel(model);
    }

    public void CycleMode()
    {
        InteractiveSession? session = Session;
        if (session != null)
        {
            session.CycleMode();
        }
    }

    /// <summary>
    /// The stop button: open questions are cancelled, then the running turn.
    /// </summary>
    public void Cancel()
    {
        Channel.CancelQuestions();
        InteractiveSession? session = Session;
        if (session != null)
        {
            session.Cancel();
        }
    }

    public async Task ShutdownAsync()
    {
        InteractiveSession? session = Session;
        if (session != null)
        {
            await session.ShutdownAsync();
        }
        else
        {
            Channel.Close();
            CloseTerminals();
        }
    }

    public IReadOnlyList<TerminalView> Terminals()
    {
        lock (gate)
        {
            return terminals.Values.ToList();
        }
    }

    public TerminalView OpenTerminal()
    {
        TerminalView view;
        lock (gate)
        {
            terminalCounter++;
            string id = "term" + terminalCounter;
            view = new TerminalView(id);
            terminals[id] = view;
        }
        ILineTerminal terminal = terminalFactory.Create(
            view.Id,
            Meta.WorkspacePath,
            output =>
            {
                view.Append(output);
                RaiseChanged();
            },
            (code, folder) =>
            {
                view.WorkingDirectory = folder;
                view.LastExitCode = code;
                RaiseChanged();
            });
        view.Attach(terminal);
        RaiseChanged();
        return view;
    }

    public void CloseTerminal(string terminalId)
    {
        TerminalView? view;
        lock (gate)
        {
            if (!terminals.TryGetValue(terminalId, out view))
            {
                return;
            }
            terminals.Remove(terminalId);
        }
        view.Dispose();
        RaiseChanged();
    }

    private void CloseTerminals()
    {
        List<TerminalView> open;
        lock (gate)
        {
            open = terminals.Values.ToList();
            terminals.Clear();
        }
        foreach (TerminalView view in open)
        {
            view.Dispose();
        }
    }

    private void Fail(string message)
    {
        StartError = message;
        Channel.Error(message);
        if (!NeedsSetup)
        {
            Channel.Warn("Fix the problem above, then click this session in the sidebar to retry.");
        }
        Channel.SetStatus(SessionStatus.Error);
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Action? handler = Changed;
        if (handler != null)
        {
            handler();
        }
    }
}
