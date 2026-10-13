using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smolcoder.Application.Tools;

/// <summary>
/// Small helpers to read tool arguments the way the model sent them.
/// </summary>
public static class JsonArguments
{
    /// <summary>
    /// A string argument, or null when it is missing or not a string.
    /// </summary>
    public static string? GetString(JsonObject arguments, string key)
    {
        JsonNode? node;
        if (!arguments.TryGetPropertyValue(key, out node))
        {
            return null;
        }
        if (node == null)
        {
            return null;
        }
        if (node.GetValueKind() == JsonValueKind.String)
        {
            return node.GetValue<string>();
        }
        return null;
    }

    /// <summary>
    /// Any argument turned into text, like JavaScript's String(). Missing or null gives "".
    /// </summary>
    public static string GetText(JsonObject arguments, string key)
    {
        JsonNode? node;
        if (!arguments.TryGetPropertyValue(key, out node))
        {
            return string.Empty;
        }
        if (node == null)
        {
            return string.Empty;
        }
        if (node.GetValueKind() == JsonValueKind.String)
        {
            return node.GetValue<string>();
        }
        return node.ToJsonString();
    }

    public static bool Has(JsonObject arguments, string key)
    {
        JsonNode? node;
        if (!arguments.TryGetPropertyValue(key, out node))
        {
            return false;
        }
        return node != null;
    }

    /// <summary>
    /// A number argument (numbers or numeric strings), or null.
    /// </summary>
    public static double? GetNumber(JsonObject arguments, string key)
    {
        JsonNode? node;
        if (!arguments.TryGetPropertyValue(key, out node))
        {
            return null;
        }
        if (node == null)
        {
            return null;
        }
        JsonValueKind kind = node.GetValueKind();
        if (kind == JsonValueKind.Number)
        {
            return node.GetValue<double>();
        }
        if (kind == JsonValueKind.String)
        {
            double parsed;
            if (double.TryParse(node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
        }
        return null;
    }
}
