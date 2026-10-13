using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Text;

namespace Smolcoder.Application.Context;

/// <summary>
/// Renders the transcript for the hand-over summarizer.
/// Recent messages get more room. Tool results are shortened. Reasoning is dropped.
/// </summary>
public static class DigestRenderer
{
    public static string Render(IReadOnlyList<Message> messages, int budgetChars)
    {
        int count = messages.Count;
        List<string> lines = new List<string>();
        for (int index = 0; index < count; index++)
        {
            Message message = messages[index];
            bool recent = index >= count - 12;
            int cap;
            if (message.Role == MessageRole.Tool)
            {
                if (recent)
                {
                    cap = 700;
                }
                else
                {
                    cap = 200;
                }
            }
            else
            {
                if (recent)
                {
                    cap = 1500;
                }
                else
                {
                    cap = 400;
                }
            }

            List<string> calls = new List<string>();
            foreach (ToolCall call in message.ToolCalls)
            {
                JsonObject arguments = (JsonObject)call.Arguments.DeepClone();
                ShortenArgument(arguments, "content", -1);
                ShortenArgument(arguments, "new_text", 120);
                ShortenArgument(arguments, "old_text", 80);
                calls.Add(call.Name + "(" + TextTruncation.End(arguments.ToJsonString(), 200) + ")");
            }

            StringBuilder line = new StringBuilder();
            line.Append(message.Role.ToString().ToUpperInvariant());
            line.Append(": ");
            line.Append(TextTruncation.End(message.Content, cap));
            if (calls.Count > 0)
            {
                line.Append(" [called: ").Append(string.Join(", ", calls)).Append(']');
            }
            lines.Add(line.ToString());
        }

        string output = string.Join("\n", lines);
        if (output.Length > budgetChars)
        {
            output = "[earlier log omitted]\n" + output.Substring(output.Length - budgetChars);
        }
        return output;
    }

    /// <summary>
    /// A cap of -1 replaces the text with its length, like "&lt;1200 chars&gt;".
    /// </summary>
    private static void ShortenArgument(JsonObject arguments, string key, int cap)
    {
        JsonNode? node;
        if (!arguments.TryGetPropertyValue(key, out node))
        {
            return;
        }
        if (node == null || node.GetValueKind() != JsonValueKind.String)
        {
            return;
        }
        string text = node.GetValue<string>();
        if (cap < 0)
        {
            arguments[key] = "<" + text.Length + " chars>";
        }
        else
        {
            arguments[key] = TextTruncation.End(text, cap);
        }
    }
}
