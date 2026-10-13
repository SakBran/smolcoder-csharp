using System.Text.Json;
using System.Text.Json.Nodes;
using Smolcoder.Infrastructure;

namespace Smolcoder.Web.Hosting;

/// <summary>
/// ~/.smolcoder/hub.json: the port, key and process of the running hub,
/// so a second "smol --web" can hand its folder over instead of starting another.
/// </summary>
public sealed class HubRecord
{
    public HubRecord(int port, string token, int processId)
    {
        Port = port;
        Token = token;
        ProcessId = processId;
    }

    public int Port { get; }

    public string Token { get; }

    public int ProcessId { get; }

    public static HubRecord? Read()
    {
        try
        {
            if (!File.Exists(AppPaths.HubRecordPath))
            {
                return null;
            }
            JsonObject? root = JsonNode.Parse(File.ReadAllText(AppPaths.HubRecordPath)) as JsonObject;
            if (root == null)
            {
                return null;
            }
            JsonNode? port = root["port"];
            JsonNode? token = root["token"];
            JsonNode? pid = root["pid"];
            if (port == null || token == null || pid == null)
            {
                return null;
            }
            return new HubRecord(port.GetValue<int>(), token.GetValue<string>(), pid.GetValue<int>());
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Write()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        JsonObject root = new JsonObject();
        root["port"] = Port;
        root["token"] = Token;
        root["pid"] = ProcessId;
        root["startedAt"] = DateTime.UtcNow.ToString("o");
        string temporary = AppPaths.HubRecordPath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, AppPaths.HubRecordPath, true);
    }

    /// <summary>
    /// Removes the record, but only when it belongs to this process.
    /// </summary>
    public static void RemoveIfOwned()
    {
        HubRecord? current = Read();
        if (current != null && current.ProcessId == Environment.ProcessId)
        {
            try
            {
                File.Delete(AppPaths.HubRecordPath);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
