using Smolcoder.Domain.Attachments;

namespace Smolcoder.Application.Abstractions.Interaction;

/// <summary>
/// One message from the user: text plus any attached files.
/// </summary>
public sealed class UserInput
{
    public UserInput(string text, List<Attachment> attachments)
    {
        Text = text;
        Attachments = attachments;
    }

    public string Text { get; }

    public List<Attachment> Attachments { get; }
}
