namespace Smolcoder.Web.Services;

public sealed class RenameRequest
{
    public RenameRequest(string sessionId, string title)
    {
        SessionId = sessionId;
        Title = title;
    }

    public string SessionId { get; }

    public string Title { get; }
}
