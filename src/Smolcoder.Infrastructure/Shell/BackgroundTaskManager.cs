using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Shell;

/// <summary>
/// Background tasks for one session. Each task keeps its last 300 output lines.
/// All tasks are killed when the session ends.
/// </summary>
public sealed class BackgroundTaskManager : IBackgroundTaskManager
{
    private const int RingSize = 300;
    private static readonly Regex LineBreak = new Regex("\r?\n", RegexOptions.Compiled);
    private static readonly Regex LocalUrl = new Regex(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1?\])(?::\d+)?(?:/[^\s""'<>)\]]*)?", RegexOptions.Compiled);

    private readonly object gate = new object();
    private readonly Dictionary<string, BackgroundTask> tasks = new Dictionary<string, BackgroundTask>();
    private readonly IShellLocator shellLocator;
    private readonly string workingDirectory;
    private int counter;

    public BackgroundTaskManager(IShellLocator shellLocator, string workingDirectory)
    {
        this.shellLocator = shellLocator;
        this.workingDirectory = workingDirectory;
    }

    public int RunningCount
    {
        get
        {
            lock (gate)
            {
                return tasks.Values.Count(task => task.Status == "running");
            }
        }
    }

    /// <summary>
    /// Starts the task, then waits a moment so early output or a crash is visible.
    /// </summary>
    public async Task<string> StartWithEarlyOutputAsync(string command, CancellationToken cancellationToken)
    {
        string id = Start(command);
        await Task.Delay(1500, cancellationToken);
        BackgroundTask task;
        string early;
        lock (gate)
        {
            task = tasks[id];
            early = string.Join("\n", task.Lines.Skip(Math.Max(0, task.Lines.Count - 15)));
        }
        string status;
        if (task.Status == "running")
        {
            status = "Task " + id + " is running in the background.";
        }
        else
        {
            status = "Task " + id + " exited almost immediately (exit code " + ExitCodeText(task) + ").";
        }
        string earlyPart = string.Empty;
        if (early.Length > 0)
        {
            earlyPart = "Early output:\n" + early + "\n";
        }
        return status + " Command: " + command + "\n" + earlyPart + "Use task {\"action\": \"logs\", \"task_id\": \"" + id + "\"} to check on it, {\"action\": \"stop\"} to kill it.";
    }

    public string Logs(string taskId, int lines)
    {
        lock (gate)
        {
            BackgroundTask? task;
            if (!tasks.TryGetValue(taskId, out task))
            {
                return UnknownTask(taskId);
            }
            int count = Math.Min(Math.Max(lines, 1), RingSize);
            List<string> tail = task.Lines.Skip(Math.Max(0, task.Lines.Count - count)).ToList();
            string code = string.Empty;
            if (task.ExitCode.HasValue)
            {
                code = " code " + task.ExitCode.Value;
            }
            string header = "Task " + task.Id + " [" + task.Status + code + "] " + task.Command;
            if (tail.Count == 0)
            {
                return header + "\n(no output yet)";
            }
            return header + "\n" + string.Join("\n", tail);
        }
    }

    public string Stop(string taskId)
    {
        BackgroundTask? task;
        lock (gate)
        {
            if (!tasks.TryGetValue(taskId, out task))
            {
                return UnknownTask(taskId);
            }
            if (task.Status != "running")
            {
                return "Task " + taskId + " already " + task.Status + ".";
            }
            task.Status = "stopped";
        }
        if (task.Process != null)
        {
            task.Process.KillTree();
        }
        return "Task " + taskId + " stopped. (" + task.Command + ")";
    }

    public string List()
    {
        lock (gate)
        {
            if (tasks.Count == 0)
            {
                return "No background tasks. Start one with task {\"action\": \"start\", \"command\": \"...\"}.";
            }
            List<string> rows = new List<string>();
            foreach (BackgroundTask task in tasks.Values)
            {
                long age = (long)Math.Round((DateTime.UtcNow - task.StartedAt).TotalSeconds);
                string status = task.Status;
                if (task.Status != "running" && task.ExitCode.HasValue)
                {
                    status = task.Status + "(" + task.ExitCode.Value + ")";
                }
                rows.Add(task.Id + "  " + status + "  " + age + "s  " + task.Command);
            }
            return "id  status  age  command\n" + string.Join("\n", rows);
        }
    }

    /// <summary>
    /// Local URLs printed by running tasks, like what a dev server announces on start.
    /// </summary>
    public IReadOnlyList<string> RecentUrls()
    {
        List<string> urls = new List<string>();
        lock (gate)
        {
            foreach (BackgroundTask task in tasks.Values)
            {
                if (task.Status != "running")
                {
                    continue;
                }
                foreach (string line in task.Lines)
                {
                    foreach (Match match in LocalUrl.Matches(line))
                    {
                        string url = match.Value.Replace("0.0.0.0", "localhost");
                        if (url.EndsWith("/", StringComparison.Ordinal))
                        {
                            url = url.Substring(0, url.Length - 1);
                        }
                        if (!urls.Contains(url))
                        {
                            urls.Add(url);
                        }
                    }
                }
            }
        }
        return urls.Take(8).ToList();
    }

    public void KillAll()
    {
        List<BackgroundTask> running = new List<BackgroundTask>();
        lock (gate)
        {
            foreach (BackgroundTask task in tasks.Values)
            {
                if (task.Status == "running")
                {
                    task.Status = "stopped";
                    running.Add(task);
                }
            }
        }
        foreach (BackgroundTask task in running)
        {
            if (task.Process != null)
            {
                task.Process.KillTree();
            }
        }
    }

    private string Start(string command)
    {
        BackgroundTask task;
        lock (gate)
        {
            counter++;
            task = new BackgroundTask("t" + counter, command);
            tasks[task.Id] = task;
        }

        Action<string> push = chunk =>
        {
            lock (gate)
            {
                string text = task.Partial + chunk;
                string[] parts = LineBreak.Split(text);
                task.Partial = parts[parts.Length - 1];
                for (int index = 0; index < parts.Length - 1; index++)
                {
                    AddLine(task, parts[index]);
                }
            }
        };

        try
        {
            task.Process = ShellProcess.Start(shellLocator.Shell, command, workingDirectory, push);
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                task.Lines.Add("[failed to start: " + exception.Message + "]");
                task.Status = "exited";
                task.ExitCode = -1;
            }
            return task.Id;
        }

        _ = WatchAsync(task);
        return task.Id;
    }

    private async Task WatchAsync(BackgroundTask task)
    {
        if (task.Process == null)
        {
            return;
        }
        try
        {
            await task.Process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Ignore; the status below is still updated.
        }
        lock (gate)
        {
            if (task.Partial.Length > 0)
            {
                AddLine(task, task.Partial);
                task.Partial = string.Empty;
            }
            if (task.Status == "running")
            {
                task.Status = "exited";
                try
                {
                    task.ExitCode = task.Process.ExitCode;
                }
                catch (Exception)
                {
                    task.ExitCode = null;
                }
            }
        }
    }

    private static void AddLine(BackgroundTask task, string line)
    {
        if (line.Length == 0)
        {
            return;
        }
        task.Lines.Add(line);
        if (task.Lines.Count > RingSize)
        {
            task.Lines.RemoveAt(0);
        }
    }

    private static string ExitCodeText(BackgroundTask task)
    {
        if (task.ExitCode.HasValue)
        {
            return task.ExitCode.Value.ToString();
        }
        return "null";
    }

    private string UnknownTask(string taskId)
    {
        string known = string.Join(", ", tasks.Keys);
        if (known.Length == 0)
        {
            known = "none";
        }
        return "Error: no task with id \"" + taskId + "\". Known tasks: " + known + ". Use task {\"action\": \"list\"} to see them.";
    }

    private sealed class BackgroundTask
    {
        public BackgroundTask(string id, string command)
        {
            Id = id;
            Command = command;
            Lines = new List<string>();
            Status = "running";
            StartedAt = DateTime.UtcNow;
            Partial = string.Empty;
        }

        public string Id { get; }

        public string Command { get; }

        public ShellProcess? Process { get; set; }

        public List<string> Lines { get; }

        /// <summary>
        /// "running", "exited" or "stopped".
        /// </summary>
        public string Status { get; set; }

        public int? ExitCode { get; set; }

        public DateTime StartedAt { get; }

        /// <summary>
        /// Output after the last line break, not yet a full line.
        /// </summary>
        public string Partial { get; set; }
    }
}
