namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// Fails visibly when a server accepts a request but stops sending data.
/// Two limits: total time, and the longest silence between chunks.
/// </summary>
public sealed class RequestDeadline : IDisposable
{
    public const string IdleMessage = "Backend timed out waiting for data. Check the model server and available memory.";
    public const string TotalMessage = "Backend request timed out. The model did not finish within the request deadline.";

    private readonly CancellationTokenSource linked;
    private readonly CancellationToken callerToken;
    private readonly TimeSpan idleTimeout;
    private readonly Timer idleTimer;
    private readonly Timer totalTimer;
    private string? reason;

    public RequestDeadline(CancellationToken callerToken, TimeSpan? totalTimeout, TimeSpan? idleTimeout)
    {
        this.callerToken = callerToken;
        linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        if (idleTimeout.HasValue)
        {
            this.idleTimeout = idleTimeout.Value;
        }
        else
        {
            this.idleTimeout = TimeSpan.FromSeconds(180);
        }
        TimeSpan total = TimeSpan.FromSeconds(900);
        if (totalTimeout.HasValue)
        {
            total = totalTimeout.Value;
        }
        idleTimer = new Timer(_ => Expire(IdleMessage), null, this.idleTimeout, Timeout.InfiniteTimeSpan);
        totalTimer = new Timer(_ => Expire(TotalMessage), null, total, Timeout.InfiniteTimeSpan);
    }

    public CancellationToken Token
    {
        get
        {
            return linked.Token;
        }
    }

    /// <summary>
    /// Call on every chunk of data. It restarts the silence timer.
    /// </summary>
    public void Activity()
    {
        try
        {
            idleTimer.Change(idleTimeout, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // The request already ended.
        }
    }

    /// <summary>
    /// Turns a cancellation into the right error: the caller's cancel stays a cancel,
    /// a deadline becomes a TimeoutException with a clear message.
    /// </summary>
    public Exception Translate(Exception exception)
    {
        if (callerToken.IsCancellationRequested)
        {
            return new OperationCanceledException(callerToken);
        }
        if (reason != null)
        {
            return new TimeoutException(reason);
        }
        return exception;
    }

    public void Dispose()
    {
        idleTimer.Dispose();
        totalTimer.Dispose();
        linked.Dispose();
    }

    private void Expire(string message)
    {
        if (reason == null)
        {
            reason = message;
        }
        try
        {
            linked.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request already ended.
        }
    }
}
