using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Attachments;

namespace Smolcoder.Application.Abstractions.Interaction;

/// <summary>
/// One message from the user: text plus any attached files, or a model picked in the UI.
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

    /// <summary>
    /// Set when the user picked a model. It travels through the input queue, so a switch never lands mid-turn.
    /// </summary>
    public ModelDescriptor? ModelChoice { get; private set; }

    public static UserInput ChooseModel(ModelDescriptor model)
    {
        UserInput input = new UserInput(string.Empty, new List<Attachment>());
        input.ModelChoice = model;
        return input;
    }
}
