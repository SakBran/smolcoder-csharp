namespace Smolcoder.Web.Services;

/// <summary>
/// The user's answer to a question in the transcript.
/// </summary>
public sealed class AnswerRequest
{
    public AnswerRequest(int askId, string? answer, int? index)
    {
        AskId = askId;
        Answer = answer;
        Index = index;
    }

    public int AskId { get; }

    public string? Answer { get; }

    public int? Index { get; }
}
