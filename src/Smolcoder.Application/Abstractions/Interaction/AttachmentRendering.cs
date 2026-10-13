using Smolcoder.Domain.Messages;

namespace Smolcoder.Application.Abstractions.Interaction;

/// <summary>
/// Attachments turned into text and images for the model.
/// </summary>
public sealed class AttachmentRendering
{
    public AttachmentRendering(string text, List<ImageReference> images)
    {
        Text = text;
        Images = images;
    }

    public string Text { get; }

    public List<ImageReference> Images { get; }
}
