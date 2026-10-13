using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Sessions;
using Smolcoder.Domain.Attachments;
using Smolcoder.Web.Services;

namespace Smolcoder.Web.Hosting;

/// <summary>
/// Small HTTP endpoints next to the Blazor UI: health check, folder hand-over and uploads.
/// </summary>
public static class ApiEndpoints
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    public static void Map(IEndpointRouteBuilder endpoints, HubOptions options)
    {
        endpoints.MapGet("/api/ping", () =>
        {
            JsonObject result = new JsonObject();
            result["ok"] = true;
            result["pid"] = Environment.ProcessId;
            result["version"] = options.Version;
            return Results.Text(result.ToJsonString(), "application/json");
        });

        endpoints.MapPost("/api/workspaces/add", async (HttpContext context, SessionHub hub) =>
        {
            JsonObject? body = await JsonNode.ParseAsync(context.Request.Body) as JsonObject;
            if (body == null)
            {
                return Error("Bad request.", 400);
            }
            string? path = null;
            JsonNode? pathNode = body["path"];
            if (pathNode != null)
            {
                path = pathNode.GetValue<string>();
            }
            bool start = false;
            JsonNode? startNode = body["start"];
            if (startNode != null)
            {
                start = startNode.GetValue<bool>();
            }
            if (path == null || !Directory.Exists(path))
            {
                return Error("That folder does not exist.", 400);
            }
            JsonObject result = new JsonObject();
            result["path"] = path;
            if (start)
            {
                string id = await hub.OpenSessionAsync(path);
                result["id"] = id;
            }
            else
            {
                await hub.AddWorkspaceAsync(path);
            }
            return Results.Text(result.ToJsonString(), "application/json");
        });

        endpoints.MapPost("/api/upload", async (HttpContext context, SessionHub hub, IAttachmentStore store) =>
        {
            string sessionId = context.Request.Query["sid"].ToString();
            string name = context.Request.Query["name"].ToString();
            LiveSession? session = hub.Find(sessionId);
            if (session == null)
            {
                return Error("No such session.", 404);
            }
            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > MaxUploadBytes)
            {
                return Error("\"" + name + "\" is larger than the 20 MB upload limit.", 413);
            }
            byte[] content;
            using (MemoryStream buffer = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(buffer);
                if (buffer.Length > MaxUploadBytes)
                {
                    return Error("\"" + name + "\" is larger than the 20 MB upload limit.", 413);
                }
                content = buffer.ToArray();
            }
            string mimeType = "application/octet-stream";
            if (context.Request.ContentType != null)
            {
                mimeType = context.Request.ContentType;
            }
            try
            {
                Attachment attachment = await store.SaveAsync(sessionId, name, mimeType, content, context.RequestAborted);
                JsonObject result = new JsonObject();
                result["id"] = attachment.Id;
                result["name"] = attachment.Name;
                if (attachment.Kind == AttachmentKind.Image)
                {
                    result["kind"] = "image";
                }
                else
                {
                    result["kind"] = "text";
                }
                result["size"] = attachment.Size;
                SessionState? state = session.State();
                if (attachment.Kind == AttachmentKind.Image && state != null && state.SupportsVision == false)
                {
                    result["warning"] = state.Model + " cannot see images — switch to a vision-capable model with /models";
                }
                return Results.Text(result.ToJsonString(), "application/json");
            }
            catch (AttachmentRejectedException exception)
            {
                return Error(exception.Message, exception.StatusCode);
            }
            catch (Exception exception)
            {
                return Error(exception.Message, 500);
            }
        });

        endpoints.MapGet("/api/upload", (HttpContext context, IAttachmentStore store) =>
        {
            string sessionId = context.Request.Query["sid"].ToString();
            string id = context.Request.Query["id"].ToString();
            Attachment? attachment = store.Find(sessionId, id);
            if (attachment == null)
            {
                return Results.NotFound();
            }
            return Results.File(attachment.Path, attachment.MimeType);
        });
    }

    private static IResult Error(string message, int status)
    {
        JsonObject result = new JsonObject();
        result["error"] = message;
        return Results.Text(result.ToJsonString(), "application/json", null, status);
    }
}
