using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Providers;

/// <summary>
/// One inference per server at a time.
/// Coding stops optional background work (summaries, titles) so the server never loads twice.
/// </summary>
public sealed class InferenceScheduler : IInferenceScheduler
{
    private readonly object gate = new object();
    private readonly Dictionary<string, ServerQueue> servers = new Dictionary<string, ServerQueue>();

    public async Task<T> RunAsync<T>(string serverKey, bool background, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> work)
    {
        ScheduledJob job = new ScheduledJob(background, cancellationToken);
        ServerQueue queue;
        lock (gate)
        {
            ServerQueue? found;
            if (servers.TryGetValue(serverKey, out found))
            {
                queue = found;
            }
            else
            {
                queue = new ServerQueue();
                servers[serverKey] = queue;
            }
            if (background && (queue.Active != null || queue.Waiting.Count > 0))
            {
                job.Dispose();
                throw new InvalidOperationException("Background inference deferred: server is busy");
            }
            if (queue.Active == null)
            {
                queue.Active = job;
                job.Started.TrySetResult(true);
            }
            else
            {
                queue.Waiting.Add(job);
                if (!background && queue.Active.Background)
                {
                    queue.Active.Cancellation.Cancel();
                }
            }
        }

        try
        {
            await job.Started.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            bool wasWaiting;
            lock (gate)
            {
                wasWaiting = queue.Waiting.Remove(job);
            }
            if (!wasWaiting)
            {
                Release(serverKey, queue, job);
            }
            job.Dispose();
            throw;
        }

        try
        {
            job.Cancellation.Token.ThrowIfCancellationRequested();
            return await work(job.Cancellation.Token);
        }
        finally
        {
            Release(serverKey, queue, job);
            job.Dispose();
        }
    }

    private void Release(string serverKey, ServerQueue queue, ScheduledJob job)
    {
        lock (gate)
        {
            if (queue.Active != job)
            {
                return;
            }
            queue.Active = null;
            if (queue.Waiting.Count > 0)
            {
                ScheduledJob next = queue.Waiting[0];
                queue.Waiting.RemoveAt(0);
                queue.Active = next;
                next.Started.TrySetResult(true);
            }
            else
            {
                servers.Remove(serverKey);
            }
        }
    }

    private sealed class ServerQueue
    {
        public ServerQueue()
        {
            Waiting = new List<ScheduledJob>();
        }

        public ScheduledJob? Active { get; set; }

        public List<ScheduledJob> Waiting { get; }
    }

    private sealed class ScheduledJob : IDisposable
    {
        public ScheduledJob(bool background, CancellationToken callerToken)
        {
            Background = background;
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public bool Background { get; }

        public CancellationTokenSource Cancellation { get; }

        public TaskCompletionSource<bool> Started { get; }

        public void Dispose()
        {
            Cancellation.Dispose();
        }
    }
}
