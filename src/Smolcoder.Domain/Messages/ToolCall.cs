using System.Text.Json.Nodes;

namespace Smolcoder.Domain.Messages;

/// <summary>
/// One tool call requested by the model.
/// When the arguments could not be parsed, Arguments is empty and ParseError explains why.
/// </summary>
public sealed class ToolCall
{
    public ToolCall(string id, string name, JsonObject arguments)
    {
        Id = id;
        Name = name;
        Arguments = arguments;
    }

    public string Id { get; set; }

    public string Name { get; set; }

    public JsonObject Arguments { get; set; }

    /// <summary>
    /// The raw argument text as the model sent it.
    /// </summary>
    public string? RawArguments { get; set; }

    public string? ParseError { get; set; }

    public ToolCall Clone()
    {
        JsonObject copiedArguments = (JsonObject)Arguments.DeepClone();
        ToolCall copy = new ToolCall(Id, Name, copiedArguments);
        copy.RawArguments = RawArguments;
        copy.ParseError = ParseError;
        return copy;
    }
}
