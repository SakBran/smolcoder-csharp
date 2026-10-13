namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// Options for one model call.
/// </summary>
public sealed class ChatRequestOptions
{
    public ChatRequestOptions()
    {
        CancellationToken = CancellationToken.None;
    }

    /// <summary>
    /// Called with each piece of the visible reply as it streams in.
    /// </summary>
    public Action<string>? OnToken { get; set; }

    /// <summary>
    /// Called with each piece of reasoning as it streams in.
    /// </summary>
    public Action<string>? OnThinking { get; set; }

    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Turns thinking off for this one call, whatever the session effort is.
    /// </summary>
    public bool ForceThinkingOff { get; set; }

    /// <summary>
    /// Caps the reply for this one call.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Whole-request deadline. Default 900 seconds.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Longest allowed silence between network chunks. Default 180 seconds.
    /// </summary>
    public TimeSpan? IdleTimeout { get; set; }

    /// <summary>
    /// Optional maintenance work. It yields to coding on the same server.
    /// </summary>
    public bool Background { get; set; }
}
