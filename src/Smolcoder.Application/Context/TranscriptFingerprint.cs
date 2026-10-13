using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Application.Context;

/// <summary>
/// Turns messages and tools into JSON text, for size estimates and equality checks.
/// </summary>
public static class TranscriptFingerprint
{
    public static string OfMessages(IReadOnlyList<Message> messages)
    {
        JsonArray array = new JsonArray();
        foreach (Message message in messages)
        {
            JsonObject item = new JsonObject();
            item["role"] = message.Role.ToString();
            item["content"] = message.Content;
            if (message.HasToolCalls)
            {
                JsonArray calls = new JsonArray();
                foreach (ToolCall call in message.ToolCalls)
                {
                    JsonObject callItem = new JsonObject();
                    callItem["id"] = call.Id;
                    callItem["name"] = call.Name;
                    callItem["args"] = call.Arguments.DeepClone();
                    calls.Add(callItem);
                }
                item["toolCalls"] = calls;
            }
            if (message.ToolCallId != null)
            {
                item["toolCallId"] = message.ToolCallId;
            }
            if (message.Thinking != null)
            {
                item["thinking"] = message.Thinking;
            }
            if (message.Images.Count > 0)
            {
                JsonArray images = new JsonArray();
                foreach (ImageReference image in message.Images)
                {
                    images.Add(image.Path);
                }
                item["images"] = images;
            }
            item["evicted"] = message.Evicted;
            item["compactNote"] = message.CompactNote;
            item["historyNote"] = message.HistoryNote;
            array.Add(item);
        }
        return array.ToJsonString();
    }

    public static string OfTools(IReadOnlyList<ToolSpec> tools)
    {
        JsonArray array = new JsonArray();
        foreach (ToolSpec tool in tools)
        {
            JsonObject item = new JsonObject();
            item["name"] = tool.Name;
            item["description"] = tool.Description;
            item["parameters"] = tool.Parameters.DeepClone();
            array.Add(item);
        }
        return array.ToJsonString();
    }
}
