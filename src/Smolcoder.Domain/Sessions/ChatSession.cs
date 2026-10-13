using Smolcoder.Domain.Modes;

namespace Smolcoder.Domain.Sessions;

/// <summary>
/// One saved conversation in one workspace.
/// </summary>
public sealed class ChatSession
{
    public ChatSession(string id, string workspacePath, DateTime createdAt)
    {
        Id = id;
        WorkspacePath = workspacePath;
        Title = string.Empty;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Mode = PermissionMode.Edit;
    }

    public string Id { get; set; }

    public string WorkspacePath { get; set; }

    public string Title { get; set; }

    /// <summary>
    /// True when the user renamed the session. Automatic titles then stop.
    /// </summary>
    public bool TitleByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public PermissionMode Mode { get; set; }

    public Effort? Effort { get; set; }

    public string? Model { get; set; }

    public string? Backend { get; set; }
}
