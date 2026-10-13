using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;

namespace Smolcoder.Infrastructure.Providers.OpenAi;

/// <summary>
/// The OpenAI chat-completions message and tool shape, shared by OpenAI-compatible backends.
/// </summary>
public static class OpenAiWireFormat
{
    public static JsonArray Messages(IReadOnlyList<Message> messages)
    {
        JsonArray wire = new JsonArray();
        foreach (Message message in messages)
        {
            wire.Add(Message(message));
        }
        return wire;
    }

    public static JsonObject Message(Message message)
    {
        JsonObject item = new JsonObject();
        if (message.Role == MessageRole.Assistant && message.HasToolCalls)
        {
            item["role"] = "assistant";
            if (message.Content.Length > 0)
            {
                item["content"] = message.Content;
            }
            else
            {
                item["content"] = null;
            }
            JsonArray calls = new JsonArray();
            foreach (ToolCall call in message.ToolCalls)
            {
                JsonObject function = new JsonObject();
                function["name"] = call.Name;
                function["arguments"] = call.Arguments.ToJsonString();
                JsonObject wireCall = new JsonObject();
                wireCall["id"] = call.Id;
                wireCall["type"] = "function";
                wireCall["function"] = function;
                calls.Add(wireCall);
            }
            item["tool_calls"] = calls;
            return item;
        }
        if (message.Role == MessageRole.Tool)
        {
            item["role"] = "tool";
            item["tool_call_id"] = message.ToolCallId;
            item["content"] = message.Content;
            return item;
        }
        if (message.Role == MessageRole.User && message.Images.Count > 0)
        {
            // Content parts: the text, then each image as a data URL.
            JsonArray parts = new JsonArray();
            JsonObject textPart = new JsonObject();
            textPart["type"] = "text";
            textPart["text"] = message.Content;
            parts.Add(textPart);
            foreach (ImageReference image in message.Images)
            {
                string? url = ImageDataUrl(image);
                if (url == null)
                {
                    continue;
                }
                JsonObject imageUrl = new JsonObject();
                imageUrl["url"] = url;
                JsonObject imagePart = new JsonObject();
                imagePart["type"] = "image_url";
                imagePart["image_url"] = imageUrl;
                parts.Add(imagePart);
            }
            item["role"] = "user";
            if (parts.Count > 1)
            {
                item["content"] = parts;
            }
            else
            {
                item["content"] = message.Content;
            }
            return item;
        }
        item["role"] = RoleName(message.Role);
        item["content"] = message.Content;
        return item;
    }

    public static JsonArray Tools(IReadOnlyList<ToolSpec> tools)
    {
        JsonArray wire = new JsonArray();
        foreach (ToolSpec tool in tools)
        {
            JsonObject function = new JsonObject();
            function["name"] = tool.Name;
            function["description"] = tool.Description;
            function["parameters"] = tool.Parameters.DeepClone();
            JsonObject item = new JsonObject();
            item["type"] = "function";
            item["function"] = function;
            wire.Add(item);
        }
        return wire;
    }

    /// <summary>
    /// A data URL for a stored image, or null when the file is gone.
    /// A deleted upload must not fail the whole request.
    /// </summary>
    public static string? ImageDataUrl(ImageReference image)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(image.Path);
            return "data:" + image.MimeType + ";base64," + Convert.ToBase64String(bytes);
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
        if (role == MessageRole.Assistant)
        {
            return "assistant";
        }
        return "tool";
    }
}
