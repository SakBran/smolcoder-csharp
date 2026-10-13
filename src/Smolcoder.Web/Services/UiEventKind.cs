namespace Smolcoder.Web.Services;

/// <summary>
/// The kinds of rows the chat transcript shows.
/// </summary>
public enum UiEventKind
{
    User,
    Assistant,
    Thinking,
    Tool,
    Result,
    Plan,
    Line,
    Print,
    TurnEnd,
    Confirm,
    Select,
    Prompt
}
