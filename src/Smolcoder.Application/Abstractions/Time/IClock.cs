namespace Smolcoder.Application.Abstractions.Time;

/// <summary>
/// The current time. An interface so tests can control it.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
