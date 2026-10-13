using System.Text.Json;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;
using Smolcoder.Domain.Planning;

namespace Smolcoder.Infrastructure.Persistence;

/// <summary>
/// Turns a SessionSnapshot into JSON and back.
/// </summary>
public static class MessageJson
{
    public static string SerializeSnapshot(SessionSnapshot snapshot)
    {
        JsonObject root = new JsonObject();
        JsonArray messages = new JsonArray();
        foreach (Message message in snapshot.Messages)
        {
            messages.Add(SerializeMessage(message));
        }
        root["messages"] = messages;

        JsonArray steps = new JsonArray();
        foreach (PlanStep step in snapshot.PlanSteps)
        {
            JsonObject item = new JsonObject();
            item["text"] = step.Text;
            item["done"] = step.Done;
            if (step.Note != null)
            {
                item["note"] = step.Note;
            }
            steps.Add(item);
        }
        root["plan"] = steps;
        root["filesTouched"] = StringArray(snapshot.FilesTouched);
        root["commandsRun"] = StringArray(snapshot.CommandsRun);
        root["originalRequest"] = snapshot.OriginalRequest;
        root["currentRequest"] = snapshot.CurrentRequest;
        root["mode"] = PermissionModes.ShortName(snapshot.Mode);
        if (snapshot.Effort.HasValue)
        {
            root["effort"] = Efforts.Name(snapshot.Effort);
        }
        root["model"] = snapshot.Model;
        root["backend"] = snapshot.Backend;
        root["baseUrl"] = snapshot.BaseUrl;
        return root.ToJsonString();
    }

    public static SessionSnapshot DeserializeSnapshot(string json)
    {
        SessionSnapshot snapshot = new SessionSnapshot();
        JsonObject? root = JsonNode.Parse(json) as JsonObject;
        if (root == null)
        {
            return snapshot;
        }
        JsonArray? messages = root["messages"] as JsonArray;
        if (messages != null)
        {
            foreach (JsonNode? node in messages)
            {
                JsonObject? item = node as JsonObject;
                if (item != null)
                {
                    snapshot.Messages.Add(DeserializeMessage(item));
                }
            }
        }
        JsonArray? steps = root["plan"] as JsonArray;
        if (steps != null)
        {
            foreach (JsonNode? node in steps)
            {
                JsonObject? item = node as JsonObject;
                if (item == null)
                {
                    continue;
                }
                PlanStep step = new PlanStep(Text(item, "text"));
                step.Done = Flag(item, "done");
                string? note = OptionalText(item, "note");
                step.Note = note;
                snapshot.PlanSteps.Add(step);
            }
        }
        snapshot.FilesTouched = Strings(root["filesTouched"] as JsonArray);
        snapshot.CommandsRun = Strings(root["commandsRun"] as JsonArray);
        snapshot.OriginalRequest = Text(root, "originalRequest");
        snapshot.CurrentRequest = Text(root, "currentRequest");
        PermissionMode mode;
        if (PermissionModes.TryParse(OptionalText(root, "mode"), out mode))
        {
            snapshot.Mode = mode;
        }
        Effort? effort;
        if (Efforts.TryParse(OptionalText(root, "effort"), out effort))
        {
            snapshot.Effort = effort;
        }
        snapshot.Model = OptionalText(root, "model");
        snapshot.Backend = OptionalText(root, "backend");
        snapshot.BaseUrl = OptionalText(root, "baseUrl");
        return snapshot;
    }

    public static JsonObject SerializeMessage(Message message)
    {
        JsonObject item = new JsonObject();
        item["role"] = message.Role.ToString().ToLowerInvariant();
        item["content"] = message.Content;
        if (message.Images.Count > 0)
        {
            JsonArray images = new JsonArray();
            foreach (ImageReference image in message.Images)
            {
                JsonObject imageItem = new JsonObject();
                imageItem["path"] = image.Path;
                imageItem["mime"] = image.MimeType;
                imageItem["name"] = image.Name;
                images.Add(imageItem);
            }
            item["images"] = images;
        }
        if (message.HasToolCalls)
        {
            JsonArray calls = new JsonArray();
            foreach (ToolCall call in message.ToolCalls)
            {
                JsonObject callItem = new JsonObject();
                callItem["id"] = call.Id;
                callItem["name"] = call.Name;
                callItem["args"] = call.Arguments.DeepClone();
                if (call.RawArguments != null)
                {
                    callItem["rawArgs"] = call.RawArguments;
                }
                if (call.ParseError != null)
                {
                    callItem["parseError"] = call.ParseError;
                }
                calls.Add(callItem);
            }
            item["toolCalls"] = calls;
        }
        if (message.ToolCallId != null)
        {
            item["toolCallId"] = message.ToolCallId;
        }
        if (message.ToolName != null)
        {
            item["toolName"] = message.ToolName;
        }
        if (message.Thinking != null)
        {
            item["thinking"] = message.Thinking;
        }
        if (message.Evicted)
        {
            item["evicted"] = true;
        }
        if (message.CompactNote)
        {
            item["compactNote"] = true;
        }
        if (message.HistoryNote)
        {
            item["historyNote"] = true;
        }
        return item;
    }

    public static Message DeserializeMessage(JsonObject item)
    {
        MessageRole role = MessageRole.User;
        string roleName = Text(item, "role");
        if (roleName == "system")
        {
            role = MessageRole.System;
        }
        else if (roleName == "assistant")
        {
            role = MessageRole.Assistant;
        }
        else if (roleName == "tool")
        {
            role = MessageRole.Tool;
        }
        Message message = new Message(role, Text(item, "content"));
        JsonArray? images = item["images"] as JsonArray;
        if (images != null)
        {
            foreach (JsonNode? node in images)
            {
                JsonObject? image = node as JsonObject;
                if (image != null)
                {
                    message.Images.Add(new ImageReference(Text(image, "path"), Text(image, "mime"), Text(image, "name")));
                }
            }
        }
        JsonArray? calls = item["toolCalls"] as JsonArray;
        if (calls != null)
        {
            foreach (JsonNode? node in calls)
            {
                JsonObject? callItem = node as JsonObject;
                if (callItem == null)
                {
                    continue;
                }
                JsonObject arguments = new JsonObject();
                JsonObject? storedArguments = callItem["args"] as JsonObject;
                if (storedArguments != null)
                {
                    arguments = (JsonObject)storedArguments.DeepClone();
                }
                ToolCall call = new ToolCall(Text(callItem, "id"), Text(callItem, "name"), arguments);
                call.RawArguments = OptionalText(callItem, "rawArgs");
                call.ParseError = OptionalText(callItem, "parseError");
                message.ToolCalls.Add(call);
            }
        }
        message.ToolCallId = OptionalText(item, "toolCallId");
        message.ToolName = OptionalText(item, "toolName");
        message.Thinking = OptionalText(item, "thinking");
        message.Evicted = Flag(item, "evicted");
        message.CompactNote = Flag(item, "compactNote");
        message.HistoryNote = Flag(item, "historyNote");
        return message;
    }

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        JsonArray array = new JsonArray();
        foreach (string value in values)
        {
            array.Add(value);
        }
        return array;
    }

    private static List<string> Strings(JsonArray? array)
    {
        List<string> values = new List<string>();
        if (array == null)
        {
            return values;
        }
        foreach (JsonNode? node in array)
        {
            if (node != null && node.GetValueKind() == JsonValueKind.String)
            {
                values.Add(node.GetValue<string>());
            }
        }
        return values;
    }

    private static string Text(JsonObject item, string key)
    {
        string? value = OptionalText(item, key);
        if (value == null)
        {
            return string.Empty;
        }
        return value;
    }

    private static string? OptionalText(JsonObject item, string key)
    {
        JsonNode? node = item[key];
        if (node == null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }
        return node.GetValue<string>();
    }

    private static bool Flag(JsonObject item, string key)
    {
        JsonNode? node = item[key];
        if (node == null)
        {
            return false;
        }
        return node.GetValueKind() == JsonValueKind.True;
    }
}
