using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Smolcoder.Application.Abstractions.Interaction;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Domain.Attachments;
using Smolcoder.Domain.Messages;
using Smolcoder.Infrastructure.Persistence;
using Smolcoder.Infrastructure.Persistence.Entities;

namespace Smolcoder.Infrastructure.Attachments;

/// <summary>
/// Files the user attaches in the web UI. The bytes live under ~/.smolcoder/uploads/&lt;session&gt;/,
/// never inside the workspace, so an attachment never changes the user's project.
/// </summary>
public sealed class AttachmentStore : IAttachmentStore
{
    public const long MaxUploadBytes = 20 * 1024 * 1024;
    public const int MaxTextBytes = 48 * 1024;
    private const string Fence = "```";

    private static readonly Dictionary<string, string> ImageMimeTypes = new Dictionary<string, string>
    {
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["webp"] = "image/webp"
    };

    private static readonly Regex ControlCharacters = new Regex(@"[\x00-\x1f\x7f]", RegexOptions.Compiled);
    private static readonly Regex Extension = new Regex(@"\.([a-z0-9]{1,8})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SafeSessionId = new Regex("^[a-z0-9_-]{1,64}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SafeAttachmentId = new Regex("^[a-f0-9]{16}$", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, Attachment> index = new ConcurrentDictionary<string, Attachment>();
    private readonly IDbContextFactory<SmolcoderDbContext> contextFactory;

    public AttachmentStore(IDbContextFactory<SmolcoderDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<Attachment> SaveAsync(string sessionId, string fileName, string mimeType, byte[] content, CancellationToken cancellationToken)
    {
        if (!SafeSessionId.IsMatch(sessionId))
        {
            throw new AttachmentRejectedException("Bad session id.", 400);
        }
        string name = SafeName(fileName);
        if (content.Length > MaxUploadBytes)
        {
            throw new AttachmentRejectedException("\"" + name + "\" is larger than the 20 MB upload limit.", 413);
        }
        if (content.Length == 0)
        {
            throw new AttachmentRejectedException("\"" + name + "\" is empty.", 415);
        }

        string extension = ExtensionOf(name, mimeType);
        AttachmentKind kind;
        string storedMime;
        string? imageMime;
        if (ImageMimeTypes.TryGetValue(extension, out imageMime))
        {
            kind = AttachmentKind.Image;
            storedMime = imageMime;
        }
        else
        {
            if (!LooksLikeText(content))
            {
                throw new AttachmentRejectedException("\"" + name + "\" is not a supported attachment. Images (png, jpg, gif, webp) and text files can be attached.", 415);
            }
            if (content.Length > MaxTextBytes)
            {
                long kilobytes = (long)Math.Round(content.Length / 1024.0);
                throw new AttachmentRejectedException("\"" + name + "\" is too large to attach as text (" + kilobytes + " KB; the limit is " + (MaxTextBytes / 1024) + " KB). Put it in the workspace and ask the agent to read it.", 415);
            }
            kind = AttachmentKind.Text;
            storedMime = "text/plain; charset=utf-8";
        }

        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        string directory = Path.Combine(AppPaths.UploadsDirectory, sessionId);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, id + "." + extension);
        await File.WriteAllBytesAsync(path, content, cancellationToken);

        Attachment attachment = new Attachment(id, name, kind, storedMime, content.Length, path);
        index[Key(sessionId, id)] = attachment;

        using (SmolcoderDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            UploadEntity row = new UploadEntity();
            row.SessionId = sessionId;
            row.Id = id;
            row.Name = name;
            row.Kind = KindName(kind);
            row.MimeType = storedMime;
            row.Size = content.Length;
            row.Path = path;
            row.CreatedAt = DateTime.UtcNow;
            context.Uploads.Add(row);
            await context.SaveChangesAsync(cancellationToken);
        }
        return attachment;
    }

    public Attachment? Find(string sessionId, string attachmentId)
    {
        if (!SafeSessionId.IsMatch(sessionId) || !SafeAttachmentId.IsMatch(attachmentId))
        {
            return null;
        }
        Attachment? attachment;
        if (index.TryGetValue(Key(sessionId, attachmentId), out attachment))
        {
            if (File.Exists(attachment.Path))
            {
                return attachment;
            }
            return null;
        }
        // After a restart the memory index is empty. The database still knows the upload.
        try
        {
            using (SmolcoderDbContext context = contextFactory.CreateDbContext())
            {
                UploadEntity? row = context.Uploads.AsNoTracking().FirstOrDefault(upload => upload.SessionId == sessionId && upload.Id == attachmentId);
                if (row == null || !File.Exists(row.Path))
                {
                    return null;
                }
                AttachmentKind kind = AttachmentKind.Text;
                if (row.Kind == "image")
                {
                    kind = AttachmentKind.Image;
                }
                Attachment restored = new Attachment(row.Id, row.Name, kind, row.MimeType, row.Size, row.Path);
                index[Key(sessionId, attachmentId)] = restored;
                return restored;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Remove(string sessionId, string attachmentId)
    {
        Attachment? attachment = Find(sessionId, attachmentId);
        Attachment? removed;
        index.TryRemove(Key(sessionId, attachmentId), out removed);
        if (attachment != null)
        {
            try
            {
                File.Delete(attachment.Path);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
        try
        {
            using (SmolcoderDbContext context = contextFactory.CreateDbContext())
            {
                context.Uploads.Where(upload => upload.SessionId == sessionId && upload.Id == attachmentId).ExecuteDelete();
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    public void RemoveSession(string sessionId)
    {
        if (!SafeSessionId.IsMatch(sessionId))
        {
            return;
        }
        foreach (string key in index.Keys.Where(key => key.StartsWith(sessionId + "/", StringComparison.Ordinal)).ToList())
        {
            Attachment? removed;
            index.TryRemove(key, out removed);
        }
        string directory = Path.Combine(AppPaths.UploadsDirectory, sessionId);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    /// <summary>
    /// Text files are put inline. Images become vision input when the model can see,
    /// otherwise a note the model can pass on to the user.
    /// </summary>
    public AttachmentRendering RenderForModel(IReadOnlyList<Attachment> attachments, bool modelCanSeeImages)
    {
        List<string> parts = new List<string>();
        List<ImageReference> images = new List<ImageReference>();
        foreach (Attachment attachment in attachments)
        {
            if (attachment.Kind == AttachmentKind.Image)
            {
                if (modelCanSeeImages)
                {
                    images.Add(new ImageReference(attachment.Path, attachment.MimeType, attachment.Name));
                    parts.Add("[Attached image: " + attachment.Name + "]");
                }
                else
                {
                    parts.Add("[Attached image: " + attachment.Name + " — this model cannot view images. If the image matters, tell the user to switch to a vision-capable model with /models.]");
                }
                continue;
            }
            string body;
            try
            {
                body = File.ReadAllText(attachment.Path, Encoding.UTF8);
            }
            catch (Exception)
            {
                body = "(the file is no longer available)";
            }
            if (body.Length > MaxTextBytes)
            {
                body = body.Substring(0, MaxTextBytes) + "\n… [truncated: " + attachment.Size + " bytes in total]";
            }
            string fence = Fence;
            if (body.Contains(Fence))
            {
                fence = Fence + "`";
            }
            parts.Add("[Attached file: " + attachment.Name + " (" + attachment.Size + " bytes)]\n" + fence + "\n" + body + "\n" + fence);
        }
        return new AttachmentRendering(string.Join("\n\n", parts), images);
    }

    /// <summary>
    /// Removes any path and control characters from a file name.
    /// </summary>
    public static string SafeName(string? name)
    {
        string text = string.Empty;
        if (name != null)
        {
            text = name;
        }
        string[] pieces = text.Split('\\', '/');
        string last = pieces[pieces.Length - 1];
        string cleaned = ControlCharacters.Replace(last, string.Empty).Trim();
        if (cleaned.Length > 80)
        {
            cleaned = cleaned.Substring(0, 80);
        }
        if (cleaned.Length == 0)
        {
            return "file";
        }
        return cleaned;
    }

    public static string MimeTypeForExtension(string extension)
    {
        string? mime;
        if (ImageMimeTypes.TryGetValue(extension.ToLowerInvariant(), out mime))
        {
            return mime;
        }
        return "text/plain; charset=utf-8";
    }

    private static string ExtensionOf(string name, string mimeType)
    {
        Match match = Extension.Match(name);
        if (match.Success)
        {
            return match.Groups[1].Value.ToLowerInvariant();
        }
        string type = mimeType.ToLowerInvariant().Split(';')[0].Trim();
        foreach (KeyValuePair<string, string> pair in ImageMimeTypes)
        {
            if (pair.Value == type)
            {
                return pair.Key;
            }
        }
        if (type.StartsWith("text/", StringComparison.Ordinal))
        {
            return "txt";
        }
        return "bin";
    }

    private static bool LooksLikeText(byte[] content)
    {
        int count = Math.Min(content.Length, 8192);
        for (int position = 0; position < count; position++)
        {
            if (content[position] == 0)
            {
                return false;
            }
        }
        return true;
    }

    private static string KindName(AttachmentKind kind)
    {
        if (kind == AttachmentKind.Image)
        {
            return "image";
        }
        return "text";
    }

    private static string Key(string sessionId, string attachmentId)
    {
        return sessionId + "/" + attachmentId;
    }
}
