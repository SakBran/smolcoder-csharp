namespace Smolcoder.Application.Abstractions.Interaction;

/// <summary>
/// Everything an interactive session loop needs from its user interface.
/// </summary>
public interface ISessionInteraction : IUserInteraction
{
    /// <summary>
    /// Waits for the next user message.
    /// </summary>
    Task<UserInput> ReadInputAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Shows a picker. Returns the chosen index, or null when cancelled.
    /// </summary>
    Task<int?> SelectAsync(string title, IReadOnlyList<SelectOption> options, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for one line of text. Returns null when cancelled.
    /// </summary>
    Task<string?> PromptAsync(string title, string? placeholder, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the interface that the status changed.
    /// </summary>
    void Refresh();
}
