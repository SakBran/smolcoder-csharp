namespace Smolcoder.Web.Services;

/// <summary>
/// A session row in the sidebar.
/// </summary>
public sealed class SessionView
{
    public SessionView(string id, string title, SessionStatus status, bool isLive, DateTime updatedAt, string? model)
    {
        Id = id;
        Title = title;
        Status = status;
        IsLive = isLive;
        UpdatedAt = updatedAt;
        Model = model;
    }

    public string Id { get; }

    public string Title { get; }

    public SessionStatus Status { get; }

    public bool IsLive { get; }

    public DateTime UpdatedAt { get; }

    public string? Model { get; }
}
