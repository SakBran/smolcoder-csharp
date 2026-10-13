namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Thrown when a path is outside the workspace or cannot be checked.
/// </summary>
public sealed class SandboxException : Exception
{
    public SandboxException(string message)
        : base(message)
    {
    }
}
