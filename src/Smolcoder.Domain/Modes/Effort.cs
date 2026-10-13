namespace Smolcoder.Domain.Modes;

/// <summary>
/// Reasoning effort. Off disables thinking where the backend supports it.
/// A null effort (used elsewhere as Effort?) means "leave it to the backend".
/// </summary>
public enum Effort
{
    Off,
    Low,
    Medium,
    High
}
