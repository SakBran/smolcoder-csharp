using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// Builds ToolCall objects from streamed argument text.
/// </summary>
public static class ToolCallParser
{
    private static readonly string CallPrefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
    private static int callCounter;

    public static string NextCallId()
    {
        int next = Interlocked.Increment(ref callCounter);
        return "call_" + CallPrefix + "_" + next;
    }

    /// <summary>
    /// Parses the arguments. When that fails, the call keeps the raw text and a parse error.
    /// </summary>
    public static ToolCall Build(string id, string name, string rawArguments)
    {
        string callId = id;
        if (callId.Length == 0)
        {
            callId = NextCallId();
        }
        ToolCall call = new ToolCall(callId, name, new JsonObject());
        call.RawArguments = rawArguments;
        if (rawArguments.Trim().Length == 0)
        {
            return call;
        }
        try
        {
            JsonNode? parsed = JsonNode.Parse(rawArguments);
            if (parsed is JsonObject parsedObject)
            {
                call.Arguments = parsedObject;
            }
            else
            {
                call.ParseError = "arguments were not a JSON object";
            }
        }
        catch (JsonException exception)
        {
            call.ParseError = "invalid JSON: " + exception.Message;
        }
        return call;
    }

    /// <summary>
    /// Tokens the visible reply and tool-call JSON take when sent again in the next prompt.
    /// </summary>
    public static int EstimateReplayTokens(string content, IReadOnlyList<ToolCall> toolCalls)
    {
        int characters = content.Length;
        foreach (ToolCall call in toolCalls)
        {
            string arguments = call.Arguments.ToJsonString();
            if (call.RawArguments != null)
            {
                arguments = call.RawArguments;
            }
            characters += call.Name.Length + arguments.Length + 12;
        }
        return (int)Math.Ceiling(characters / 4.0);
    }
}
