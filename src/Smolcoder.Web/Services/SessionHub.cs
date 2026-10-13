using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Time;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Sessions;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Sessions;

namespace Smolcoder.Web.Services;

/// <summary>
/// All workspaces and sessions of the web UI. Many sessions run side by side.
/// Sessions are saved to the database (debounced) so they can be resumed after a restart.
/// </summary>
public sealed class SessionHub : IDisposable
{
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(1500);

    private readonly object gate = new object();
    private readonly Dictionary<string, LiveSession> live = new Dictionary<string, LiveSession>();
    private readonly Dictionary<string, ChatSession> stored = new Dictionary<string, ChatSession>();
    private readonly List<Workspace> workspaces = new List<Workspace>();
    private readonly Dictionary<string, CancellationTokenSource> pendingSaves = new Dictionary<string, CancellationTokenSource>();
    private readonly IModelSelector modelSelector;
    private readonly IInteractiveSessionFactory sessionFactory;
    private readonly ISettingsStore settingsStore;
    private readonly ISessionRepository sessionRepository;
    private readonly IWorkspaceRepository workspaceRepository;
    private readonly IAttachmentStore attachmentStore;
    private readonly ILineTerminalFactory terminalFactory;
    private readonly IClock clock;
    private readonly HubOptions options;
    private bool saveWarningShown;

    public SessionHub(
        IModelSelector modelSelector,
        IInteractiveSessionFactory sessionFactory,
        ISettingsStore settingsStore,
        ISessionRepository sessionRepository,
        IWorkspaceRepository workspaceRepository,
        IAttachmentStore attachmentStore,
        ILineTerminalFactory terminalFactory,
        IClock clock,
        HubOptions options)
    {
        this.modelSelector = modelSelector;
        this.sessionFactory = sessionFactory;
        this.settingsStore = settingsStore;
        this.sessionRepository = sessionRepository;
        this.workspaceRepository = workspaceRepository;
        this.attachmentStore = attachmentStore;
        this.terminalFactory = terminalFactory;
        this.clock = clock;
        this.options = options;
    }

    /// <summary>
    /// Raised when the sidebar (workspaces, sessions, statuses) changed.
    /// </summary>
    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Workspace> savedWorkspaces = await workspaceRepository.ListAsync(cancellationToken);
        IReadOnlyList<ChatSession> savedSessions = await sessionRepository.ListAsync(cancellationToken);
        lock (gate)
        {
            workspaces.Clear();
            workspaces.AddRange(savedWorkspaces);
            stored.Clear();
            foreach (ChatSession session in savedSessions)
            {
                stored[session.Id] = session;
            }
        }
    }

    public LiveSession? Find(string sessionId)
    {
        lock (gate)
        {
            LiveSession? session;
            if (live.TryGetValue(sessionId, out session))
            {
                return session;
            }
            return null;
        }
    }

    /// <summary>
    /// The sidebar: workspaces by most recent activity, each with its sessions newest first.
    /// </summary>
    public List<WorkspaceView> Sidebar()
    {
        lock (gate)
        {
            Dictionary<string, WorkspaceView> views = new Dictionary<string, WorkspaceView>();
            foreach (Workspace workspace in workspaces)
            {
                views[workspace.Path] = new WorkspaceView(workspace.Path, workspace.LastOpened);
            }
            Dictionary<string, ChatSession> all = new Dictionary<string, ChatSession>(stored);
            foreach (LiveSession session in live.Values)
            {
                all[session.Id] = session.Meta;
            }
            foreach (ChatSession session in all.Values)
            {
                WorkspaceView? view;
                if (!views.TryGetValue(session.WorkspacePath, out view))
                {
                    view = new WorkspaceView(session.WorkspacePath, session.UpdatedAt);
                    views[session.WorkspacePath] = view;
                }
                LiveSession? running;
                live.TryGetValue(session.Id, out running);
                SessionStatus status = SessionStatus.Stored;
                if (running != null)
                {
                    status = running.Channel.CurrentStatus;
                }
                view.Sessions.Add(new SessionView(session.Id, DisplayTitle(session), status, running != null, session.UpdatedAt, session.Model));
                if (session.UpdatedAt > view.LastActivity)
                {
                    view.LastActivity = session.UpdatedAt;
                }
            }
            foreach (WorkspaceView view in views.Values)
            {
                view.Sessions.Sort((first, second) => second.UpdatedAt.CompareTo(first.UpdatedAt));
            }
            return views.Values.OrderByDescending(view => view.LastActivity).ToList();
        }
    }

    public async Task<string> OpenSessionAsync(string workspacePath)
    {
        string path = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException("That folder does not exist: " + path);
        }
        await AddWorkspaceAsync(path);
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        ChatSession meta = new ChatSession(id, path, clock.UtcNow);
        LiveSession session = CreateLive(meta);
        RaiseChanged();
        await StartAsync(session, options.Preferences.Copy(), null);
        return id;
    }

    /// <summary>
    /// Resumes a saved session, or retries one that failed to start.
    /// </summary>
    public async Task ResumeAsync(string sessionId)
    {
        LiveSession? existing = Find(sessionId);
        if (existing != null && existing.IsRunning)
        {
            return;
        }
        StoredSession? saved = await sessionRepository.LoadAsync(sessionId, CancellationToken.None);
        ChatSession meta;
        if (saved != null)
        {
            meta = saved.Session;
        }
        else if (existing != null)
        {
            meta = existing.Meta;
        }
        else
        {
            throw new InvalidOperationException("No such session.");
        }

        LiveSession session = CreateLive(meta);
        SessionPreferences preferences = options.Preferences.Copy();
        if (saved != null)
        {
            List<UiEvent> events = new List<UiEvent>();
            foreach (string json in saved.Events)
            {
                UiEvent? item = JsonSerializer.Deserialize<UiEvent>(json);
                if (item != null)
                {
                    events.Add(item);
                }
            }
            session.Channel.Restore(events);
            SessionSnapshot snapshot = saved.Snapshot;
            PermissionMode mode = snapshot.Mode;
            bool bypassDropped = false;
            if (mode == PermissionMode.Bypass)
            {
                // Bypass is never restored. It needs an explicit choice each time.
                mode = PermissionMode.Edit;
                bypassDropped = true;
            }
            preferences.Mode = mode;
            preferences.EffortGiven = true;
            preferences.Effort = snapshot.Effort;
            if (snapshot.Model != null)
            {
                preferences.Model = snapshot.Model;
                preferences.Backend = snapshot.Backend;
                preferences.BaseUrl = snapshot.BaseUrl;
            }
            string note = "· session resumed";
            if (bypassDropped)
            {
                note = note + " in edit mode (bypass is not restored)";
            }
            session.Channel.Status(note + " — earlier command approvals are not remembered");
            if (meta.Title.Length > 0)
            {
                session.TitleAttempts = 99;
            }
        }
        RaiseChanged();
        await StartAsync(session, preferences, saved);
    }

    public async Task CloseAsync(string sessionId)
    {
        LiveSession? session = Find(sessionId);
        if (session == null)
        {
            return;
        }
        await SaveNowAsync(session);
        await session.ShutdownAsync();
        lock (gate)
        {
            live.Remove(sessionId);
            if (session.Meta.Title.Length > 0)
            {
                stored[sessionId] = session.Meta;
            }
        }
        RaiseChanged();
    }

    public async Task DeleteAsync(string sessionId)
    {
        LiveSession? session = Find(sessionId);
        if (session != null)
        {
            await session.ShutdownAsync();
        }
        CancelPendingSave(sessionId);
        lock (gate)
        {
            live.Remove(sessionId);
            stored.Remove(sessionId);
        }
        await sessionRepository.DeleteAsync(sessionId, CancellationToken.None);
        attachmentStore.RemoveSession(sessionId);
        RaiseChanged();
    }

    public async Task RenameAsync(string sessionId, string title)
    {
        string clean = Whitespace.Replace(title, " ").Trim();
        if (clean.Length > 80)
        {
            clean = clean.Substring(0, 80);
        }
        if (clean.Length == 0)
        {
            return;
        }
        ChatSession? meta = null;
        lock (gate)
        {
            LiveSession? running;
            if (live.TryGetValue(sessionId, out running))
            {
                meta = running.Meta;
            }
            else
            {
                stored.TryGetValue(sessionId, out meta);
            }
        }
        if (meta == null)
        {
            return;
        }
        meta.Title = clean;
        meta.TitleByUser = true;
        LiveSession? liveSession = Find(sessionId);
        if (liveSession != null)
        {
            ScheduleSave(liveSession);
        }
        else
        {
            await sessionRepository.RenameAsync(sessionId, clean, CancellationToken.None);
        }
        RaiseChanged();
    }

    public async Task AddWorkspaceAsync(string path)
    {
        string full = Path.GetFullPath(path);
        await workspaceRepository.AddAsync(full, CancellationToken.None);
        IReadOnlyList<Workspace> saved = await workspaceRepository.ListAsync(CancellationToken.None);
        lock (gate)
        {
            workspaces.Clear();
            workspaces.AddRange(saved);
        }
        RaiseChanged();
    }

    public async Task RemoveWorkspaceAsync(string path)
    {
        string full = Path.GetFullPath(path);
        lock (gate)
        {
            if (live.Values.Any(session => session.Meta.WorkspacePath == full))
            {
                throw new InvalidOperationException("Close its open sessions first.");
            }
            List<string> removed = stored.Values.Where(session => session.WorkspacePath == full).Select(session => session.Id).ToList();
            foreach (string id in removed)
            {
                stored.Remove(id);
            }
            workspaces.RemoveAll(workspace => workspace.Path == full);
        }
        await sessionRepository.DeleteByWorkspaceAsync(full, CancellationToken.None);
        await workspaceRepository.RemoveAsync(full, CancellationToken.None);
        RaiseChanged();
    }

    /// <summary>
    /// Saves every live session right away (used when the hub stops).
    /// </summary>
    public async Task SaveAllAsync()
    {
        List<LiveSession> sessions;
        lock (gate)
        {
            sessions = live.Values.ToList();
        }
        foreach (LiveSession session in sessions)
        {
            await SaveNowAsync(session);
        }
    }

    public void Dispose()
    {
        List<LiveSession> sessions;
        lock (gate)
        {
            sessions = live.Values.ToList();
        }
        foreach (LiveSession session in sessions)
        {
            InteractiveSession? running = session.Session;
            if (running != null)
            {
                running.Parts.TaskManager.KillAll();
            }
        }
    }

    private LiveSession CreateLive(ChatSession meta)
    {
        LiveSession session = new LiveSession(meta, terminalFactory);
        session.Changed += RaiseChanged;
        session.Channel.SaveRequested += () => ScheduleSave(session);
        session.Channel.OnFirstMessage = text =>
        {
            if (session.Meta.Title.Length == 0)
            {
                string title = Whitespace.Replace(text, " ").Trim();
                if (title.Length > 60)
                {
                    title = title.Substring(0, 60);
                }
                session.Meta.Title = title;
            }
            session.Meta.UpdatedAt = clock.UtcNow;
            RaiseChanged();
        };
        lock (gate)
        {
            live[meta.Id] = session;
            stored.Remove(meta.Id);
        }
        return session;
    }

    private async Task StartAsync(LiveSession session, SessionPreferences preferences, StoredSession? restore)
    {
        await session.StartAsync(
            modelSelector,
            sessionFactory,
            settingsStore,
            preferences,
            restore,
            AfterTurnAsync,
            ended =>
            {
                ScheduleSave(ended);
                RaiseChanged();
            });
        RaiseChanged();
    }

    /// <summary>
    /// After a turn: save, and ask the model for a better title (twice at most).
    /// </summary>
    private async Task AfterTurnAsync(LiveSession session)
    {
        session.Meta.UpdatedAt = clock.UtcNow;
        ScheduleSave(session);
        if (session.Meta.TitleByUser || session.TitleAttempts >= 2)
        {
            return;
        }
        InteractiveSession? running = session.Session;
        if (running == null)
        {
            return;
        }
        session.TitleAttempts++;
        string? title = await running.SuggestTitleAsync();
        if (title != null && !session.Meta.TitleByUser)
        {
            session.Meta.Title = title;
            session.TitleAttempts = 99;
            ScheduleSave(session);
            RaiseChanged();
        }
    }

    private void ScheduleSave(LiveSession session)
    {
        CancellationTokenSource cancellation = new CancellationTokenSource();
        lock (gate)
        {
            CancellationTokenSource? previous;
            if (pendingSaves.TryGetValue(session.Id, out previous))
            {
                previous.Cancel();
            }
            pendingSaves[session.Id] = cancellation;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDelay, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await SaveNowAsync(session);
        });
    }

    private void CancelPendingSave(string sessionId)
    {
        lock (gate)
        {
            CancellationTokenSource? previous;
            if (pendingSaves.TryGetValue(sessionId, out previous))
            {
                previous.Cancel();
                pendingSaves.Remove(sessionId);
            }
        }
    }

    /// <summary>
    /// Only sessions with a title are saved; an empty session is not worth keeping.
    /// </summary>
    private async Task SaveNowAsync(LiveSession session)
    {
        InteractiveSession? running = session.Session;
        if (running == null || session.Meta.Title.Length == 0)
        {
            return;
        }
        try
        {
            ChatSession meta = session.Meta;
            meta.Mode = running.Agent.Mode;
            meta.Effort = running.Effort;
            List<string> events = session.Channel.EventsSnapshot().Select(item => JsonSerializer.Serialize(item)).ToList();
            StoredSession record = new StoredSession(meta, running.Snapshot(), events);
            await sessionRepository.SaveAsync(record, CancellationToken.None);
        }
        catch (Exception exception)
        {
            if (!saveWarningShown)
            {
                saveWarningShown = true;
                session.Channel.Warn("Session could not be saved: " + exception.Message + ". Keep this session open and check free disk space and permissions.");
            }
        }
    }

    private string DisplayTitle(ChatSession session)
    {
        if (session.Title.Length > 0)
        {
            return session.Title;
        }
        return "New session";
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
