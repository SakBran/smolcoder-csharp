namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Thrown when an upload is empty, too large or not a supported type.
/// </summary>
public sealed class AttachmentRejectedException : Exception
{
    public AttachmentRejectedException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The HTTP status the web layer should answer with.
    /// </summary>
    public int StatusCode { get; }
}
