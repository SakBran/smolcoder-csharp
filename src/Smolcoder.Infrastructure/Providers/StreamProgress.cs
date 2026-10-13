namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// Shared between attempts of one request.
/// </summary>
public sealed class StreamProgress
{
    /// <summary>
    /// True once a chunk arrived. After that, the request must not be sent again:
    /// tokens may already be on screen.
    /// </summary>
    public bool Streaming { get; set; }
}
