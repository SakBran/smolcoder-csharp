using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// Tolerant readers for server JSON. A missing or wrongly typed field reads as null.
/// </summary>
public static class JsonRead
{
    public static string? String(JsonNode? source, string key)
    {
        JsonNode? node = Child(source, key);
        if (node == null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }
        return node.GetValue<string>();
    }

    public static int? Int(JsonNode? source, string key)
    {
        long? value = Long(source, key);
        if (!value.HasValue)
        {
            return null;
        }
        if (value.Value > int.MaxValue)
        {
            return int.MaxValue;
        }
        return (int)value.Value;
    }

    public static long? Long(JsonNode? source, string key)
    {
        JsonNode? node = Child(source, key);
        if (node == null || node.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }
        return (long)node.GetValue<double>();
    }

    public static bool? Bool(JsonNode? source, string key)
    {
        JsonNode? node = Child(source, key);
        if (node == null)
        {
            return null;
        }
        JsonValueKind kind = node.GetValueKind();
        if (kind == JsonValueKind.True)
        {
            return true;
        }
        if (kind == JsonValueKind.False)
        {
            return false;
        }
        return null;
    }

    public static JsonObject? Object(JsonNode? source, string key)
    {
        return Child(source, key) as JsonObject;
    }

    public static JsonArray? Array(JsonNode? source, string key)
    {
        return Child(source, key) as JsonArray;
    }

    /// <summary>
    /// Parses one streamed frame. Malformed JSON and error frames become IOExceptions,
    /// whose text the agent's retry rules recognise.
    /// </summary>
    public static JsonObject ParseStreamChunk(string payload)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            throw new IOException("Backend stream contained malformed JSON; the response was discarded");
        }
        JsonObject? chunk = node as JsonObject;
        if (chunk == null)
        {
            throw new IOException("Backend stream contained malformed JSON; the response was discarded");
        }
        JsonNode? error = chunk["error"];
        if (error != null)
        {
            string message = error.ToJsonString();
            if (error.GetValueKind() == JsonValueKind.String)
            {
                message = error.GetValue<string>();
            }
            else if (error is JsonObject errorObject)
            {
                string? inner = String(errorObject, "message");
                if (inner != null)
                {
                    message = inner;
                }
            }
            throw new IOException("Backend stream error: " + message);
        }
        return chunk;
    }

    private static JsonNode? Child(JsonNode? source, string key)
    {
        JsonObject? container = source as JsonObject;
        if (container == null)
        {
            return null;
        }
        return container[key];
    }
}
