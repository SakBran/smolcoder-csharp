namespace Smolcoder.Infrastructure.Persistence.Entities;

public sealed class UploadEntity
{
    public string SessionId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// "image" or "text".
    /// </summary>
    public string Kind { get; set; } = "text";

    public string MimeType { get; set; } = string.Empty;

    public long Size { get; set; }

    public string Path { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
