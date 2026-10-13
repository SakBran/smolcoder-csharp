namespace Smolcoder.Application.Context;

/// <summary>
/// Where the context window stands.
/// </summary>
public sealed class ContextBudget
{
    public ContextBudget(int prompt, int window, int reserve, int available, string source)
    {
        Prompt = prompt;
        Window = window;
        Reserve = reserve;
        Available = available;
        Source = source;
    }

    public int Prompt { get; }

    public int Window { get; }

    /// <summary>
    /// Tokens kept free for the model's reply.
    /// </summary>
    public int Reserve { get; }

    public int Available { get; }

    /// <summary>
    /// "measured + estimate" once the backend reported real usage, otherwise "estimate".
    /// </summary>
    public string Source { get; }
}
