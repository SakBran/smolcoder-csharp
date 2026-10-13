namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// Runs at most one model request per server at a time.
/// Coding requests go first. Background work is refused or stopped when coding needs the server.
/// </summary>
public interface IInferenceScheduler
{
    Task<T> RunAsync<T>(string serverKey, bool background, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> work);
}
