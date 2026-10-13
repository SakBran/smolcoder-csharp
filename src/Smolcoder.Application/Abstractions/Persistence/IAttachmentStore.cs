using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Domain.Attachments;

namespace Smolcoder.Application.Abstractions.Persistence;

/// <summary>
/// Stores uploaded files and prepares them for the model.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>
    /// Saves an upload. Throws AttachmentRejectedException when the file is not allowed.
    /// </summary>
    Task<Attachment> SaveAsync(string sessionId, string fileName, string mimeType, byte[] content, CancellationToken cancellationToken);

    Attachment? Find(string sessionId, string attachmentId);

    void Remove(string sessionId, string attachmentId);

    void RemoveSession(string sessionId);

    AttachmentRendering RenderForModel(IReadOnlyList<Attachment> attachments, bool modelCanSeeImages);
}
