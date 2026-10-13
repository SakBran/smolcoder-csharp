namespace Smolcoder.Domain.Messages;

/// <summary>
/// An image attached to a user turn.
/// The bytes stay on disk. They are read only when the request is sent.
/// </summary>
public sealed class ImageReference
{
    public ImageReference(string path, string mimeType, string name)
    {
        Path = path;
        MimeType = mimeType;
        Name = name;
    }

    public string Path { get; }

    public string MimeType { get; }

    public string Name { get; }
}
