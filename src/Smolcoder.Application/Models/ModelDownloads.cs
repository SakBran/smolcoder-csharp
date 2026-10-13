using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Models;

/// <summary>
/// One Ollama download, as the UI shows it.
/// </summary>
public sealed class DownloadState
{
    public DownloadState(string id, string model, string baseUrl)
    {
        Id = id;
        Model = model;
        BaseUrl = baseUrl;
        Status = "starting";
        Cancellation = new CancellationTokenSource();
    }

    public string Id { get; }

    public string Model { get; }

    public string BaseUrl { get; }

    public string Status { get; set; }

    /// <summary>
    /// 0 to 100 once the size is known.
    /// </summary>
    public double? Percent { get; set; }

    public long? Completed { get; set; }

    public long? Total { get; set; }

    public string? Error { get; set; }

    public bool Done { get; set; }

    public bool Succeeded
    {
        get
        {
            return Done && Error == null;
        }
    }

    internal CancellationTokenSource Cancellation { get; }
}

/// <summary>
/// Running and finished downloads. Lives as long as the hub, so progress survives a page reload.
/// Progress events are throttled to about four a second; a download pushes hundreds.
/// </summary>
public sealed class ModelDownloads
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(250);

    private readonly object gate = new object();
    private readonly List<DownloadState> downloads = new List<DownloadState>();
    private readonly IModelDownloader downloader;
    private DateTime lastRaised = DateTime.MinValue;
    private int counter;

    public ModelDownloads(IModelDownloader downloader)
    {
        this.downloader = downloader;
    }

    /// <summary>
    /// Raised when progress moved or a download started or ended.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised once for every download that finished successfully.
    /// </summary>
    public event Action<DownloadState>? Completed;

    public List<DownloadState> Snapshot()
    {
        lock (gate)
        {
            return downloads.ToList();
        }
    }

    public DownloadState? Find(string id)
    {
        lock (gate)
        {
            return downloads.FirstOrDefault(download => download.Id == id);
        }
    }

    /// <summary>
    /// Starts a download, or returns the one already running for the same model and server.
    /// </summary>
    public DownloadState Start(string model, string baseUrl)
    {
        string name = model.Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Type a model name such as qwen3:8b.");
        }
        DownloadState state;
        lock (gate)
        {
            DownloadState? running = downloads.FirstOrDefault(download => !download.Done && download.Model == name && download.BaseUrl == baseUrl);
            if (running != null)
            {
                return running;
            }
            downloads.RemoveAll(download => download.Done && download.Model == name && download.BaseUrl == baseUrl);
            counter++;
            state = new DownloadState("dl" + counter, name, baseUrl);
            downloads.Add(state);
        }
        Raise(true);
        _ = Task.Run(() => RunAsync(state));
        return state;
    }

    public void Cancel(string id)
    {
        DownloadState? state = Find(id);
        if (state != null && !state.Done)
        {
            state.Cancellation.Cancel();
        }
    }

    /// <summary>
    /// Removes a finished download from the list.
    /// </summary>
    public void Dismiss(string id)
    {
        lock (gate)
        {
            downloads.RemoveAll(download => download.Id == id && download.Done);
        }
        Raise(true);
    }

    private async Task RunAsync(DownloadState state)
    {
        Progress<PullProgress> progress = new Progress<PullProgress>(report =>
        {
            state.Status = report.Status;
            if (report.Total.HasValue && report.Total.Value > 0)
            {
                state.Total = report.Total;
                state.Completed = report.Completed;
                state.Percent = Math.Min(100, 100.0 * (report.Completed ?? 0) / report.Total.Value);
            }
            Raise(false);
        });
        try
        {
            await downloader.PullAsync(state.BaseUrl, state.Model, progress, state.Cancellation.Token);
            state.Status = "done";
            state.Percent = 100;
        }
        catch (OperationCanceledException)
        {
            state.Error = "cancelled";
        }
        catch (Exception exception)
        {
            state.Error = exception.Message;
        }
        state.Done = true;
        Raise(true);
        if (state.Succeeded)
        {
            Action<DownloadState>? handler = Completed;
            if (handler != null)
            {
                handler(state);
            }
        }
    }

    private void Raise(bool always)
    {
        lock (gate)
        {
            DateTime now = DateTime.UtcNow;
            if (!always && now - lastRaised < Throttle)
            {
                return;
            }
            lastRaised = now;
        }
        Action? handler = Changed;
        if (handler != null)
        {
            handler();
        }
    }
}
