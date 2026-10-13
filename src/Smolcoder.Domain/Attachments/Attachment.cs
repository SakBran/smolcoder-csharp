namespace Smolcoder.Domain.Attachments;

/// <summary>
/// A file the user attached to a message. The file itself is stored on disk.
/// </summary>
public sealed class Attachment
{
    public Attachment(string id, string name, AttachmentKind kind, string mimeType, long size, string path)
    {
        Id = id;
        Name = name;
        Kind = kind;
        MimeType = mimeType;
        Size = size;
        Path = path;
    }

    public string Id { get; }

    public string Name { get; }

    public AttachmentKind Kind { get; }

    public string MimeType { get; }

    public long Size { get; }

    public string Path { get; }
}
