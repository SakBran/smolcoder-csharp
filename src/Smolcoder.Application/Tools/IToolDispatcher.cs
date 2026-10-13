using System.Text.Json.Nodes;

namespace Smolcoder.Application.Tools;

public interface IToolDispatcher
{
    /// <summary>
    /// Runs one tool and returns the text the model sees. It never throws, except for cancellation.
    /// </summary>
    Task<string> ExecuteAsync(string name, JsonObject arguments, ToolContext context, CancellationToken cancellationToken);

    /// <summary>
    /// The shell command a call would run, or null when it runs none.
    /// </summary>
    string? CommandOf(string name, JsonObject arguments);
}
