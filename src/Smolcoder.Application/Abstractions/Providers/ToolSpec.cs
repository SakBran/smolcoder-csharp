using System.Text.Json.Nodes;

namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// A tool definition sent to the model.
/// </summary>
public sealed class ToolSpec
{
    public ToolSpec(string name, string description, JsonObject parameters)
    {
        Name = name;
        Description = description;
        Parameters = parameters;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>
    /// JSON schema of the arguments: { "type": "object", "properties": ..., "required": [...] }.
    /// </summary>
    public JsonObject Parameters { get; }
}
