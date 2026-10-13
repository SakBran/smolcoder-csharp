using System.Text.Json.Nodes;

namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// The file tools the model can call. Each method returns the text the model sees.
/// Problems are returned as text that starts with "Error:".
/// </summary>
public interface IFileSystemTools
{
    string ReadFile(string workspaceRoot, JsonObject arguments, int? maxChars);

    string WriteFile(string workspaceRoot, JsonObject arguments);

    string EditFile(string workspaceRoot, JsonObject arguments);

    string ListFiles(string workspaceRoot, JsonObject arguments);

    Task<string> SearchAsync(string workspaceRoot, JsonObject arguments, CancellationToken cancellationToken);
}
