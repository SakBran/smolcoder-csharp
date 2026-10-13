using System.Text.Json.Nodes;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Application.Abstractions.Interaction;

/// <summary>
/// What the agent loop needs from a user interface.
/// The web session and the headless console both implement it.
/// </summary>
public interface IUserInteraction
{
    /// <summary>
    /// Throws away a streamed reply before a retry.
    /// </summary>
    void ResetResponse();

    void Token(string text);

    void Thinking(string text);

    void ToolCall(string name, JsonObject arguments);

    void ToolResult(string result);

    void PrintLine(string text);

    void Status(string text);

    void Warn(string text);

    void Error(string text);

    void StartSpinner(string label);

    void StopSpinner();

    /// <summary>
    /// Asks the user to approve a command. The reason says why it was not run automatically.
    /// </summary>
    Task<CommandApproval> ConfirmCommandAsync(string command, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Called when a turn finishes normally, with a short summary label.
    /// </summary>
    void TurnEnd(string label);

    void PlanUpdated(Plan plan);
}
