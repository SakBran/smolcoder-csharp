using System.Text.Json.Nodes;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Infrastructure.Providers.Ollama;

/// <summary>
/// Ollama's native /api/chat message shape.
/// Reasoning from turns before the current user message is not sent back (qwen3-style templates drop it anyway);
/// only the current turn's reasoning travels, which is what a tool loop needs.
/// </summary>
public static class OllamaWireFormat
{
    public static JsonArray Messages(IReadOnlyList<Message> messages)
    {
        int keepThinkingAfter = Message.LastUserIndex(messages);
        JsonArray wire = new JsonArray();
        for (int index = 0; index < messages.Count; index++)
        {
            Message message = messages[index];
            JsonObject item = new JsonObject();
            if (message.Role == MessageRole.Assistant && message.HasToolCalls)
            {
                item["role"] = "assistant";
                item["content"] = message.Content;
                if (message.Thinking != null && message.Thinking.Length > 0 && index > keepThinkingAfter)
                {
                    item["thinking"] = message.Thinking;
                }
                JsonArray calls = new JsonArray();
                foreach (ToolCall call in message.ToolCalls)
                {
                    JsonObject function = new JsonObject();
                    function["name"] = call.Name;
                    function["arguments"] = call.Arguments.DeepClone();
                    JsonObject wireCall = new JsonObject();
                    wireCall["function"] = function;
                    calls.Add(wireCall);
                }
                item["tool_calls"] = calls;
            }
            else if (message.Role == MessageRole.Tool)
            {
                item["role"] = "tool";
                item["content"] = message.Content;
                item["tool_name"] = message.ToolName;
            }
            else
            {
                item["role"] = RoleName(message.Role);
                item["content"] = message.Content;
                if (message.Role == MessageRole.User && message.Images.Count > 0)
                {
                    JsonArray images = new JsonArray();
                    foreach (ImageReference image in message.Images)
                    {
                        string? data = ImageBase64(image);
                        if (data != null)
                        {
                            images.Add(data);
                        }
                    }
                    if (images.Count > 0)
                    {
                        item["images"] = images;
                    }
                }
            }
            wire.Add(item);
        }
        return wire;
    }

    /// <summary>
    /// Raw base64 of a stored image, or null when the file is gone.
    /// </summary>
    public static string? ImageBase64(ImageReference image)
    {
        try
        {
            return Convert.ToBase64String(File.ReadAllBytes(image.Path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string RoleName(MessageRole role)
    {
        if (role == MessageRole.System)
        {
            return "system";
        }
        if (role == MessageRole.User)
        {
            return "user";
        }
        return "assistant";
    }
}
