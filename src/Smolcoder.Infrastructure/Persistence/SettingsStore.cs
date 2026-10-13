using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Domain.Modes;
using Smolcoder.Infrastructure.Persistence.Entities;

namespace Smolcoder.Infrastructure.Persistence;

/// <summary>
/// Settings as key/value rows in SQLite.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private const string LastModelKey = "lastModel";
    private const string LastModelUrlKey = "lastModelUrl";
    private const string LastModeKey = "lastMode";
    private const string EffortKey = "effort";
    private const string ServersKey = "servers";

    private readonly IDbContextFactory<SmolcoderDbContext> contextFactory;

    public SettingsStore(IDbContextFactory<SmolcoderDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public AppSettings Load()
    {
        AppSettings settings = new AppSettings();
        try
        {
            using (SmolcoderDbContext context = contextFactory.CreateDbContext())
            {
                Dictionary<string, string> values = context.Settings.AsNoTracking().ToDictionary(setting => setting.Key, setting => setting.Value);
                settings.LastModel = Value(values, LastModelKey);
                settings.LastModelUrl = Value(values, LastModelUrlKey);
                PermissionMode mode;
                if (PermissionModes.TryParse(Value(values, LastModeKey), out mode))
                {
                    // Bypass is never inherited from an earlier run. It needs an explicit flag each time.
                    if (mode == PermissionMode.Bypass)
                    {
                        mode = PermissionMode.Edit;
                    }
                    settings.LastMode = mode;
                }
                Effort? effort;
                if (Efforts.TryParse(Value(values, EffortKey), out effort))
                {
                    settings.Effort = effort;
                }
            }
        }
        catch (Exception)
        {
            // A missing or broken database gives default settings.
        }
        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            using (SmolcoderDbContext context = contextFactory.CreateDbContext())
            {
                Put(context, LastModelKey, settings.LastModel);
                Put(context, LastModelUrlKey, settings.LastModelUrl);
                string? mode = null;
                if (settings.LastMode.HasValue)
                {
                    mode = PermissionModes.ShortName(settings.LastMode.Value);
                }
                Put(context, LastModeKey, mode);
                Put(context, EffortKey, Efforts.Name(settings.Effort));
                context.SaveChanges();
            }
        }
        catch (Exception)
        {
            // Not fatal.
        }
    }

    public IReadOnlyList<SavedServer> LoadServers()
    {
        List<SavedServer> servers = new List<SavedServer>();
        try
        {
            using (SmolcoderDbContext context = contextFactory.CreateDbContext())
            {
                SettingEntity? row = context.Settings.AsNoTracking().FirstOrDefault(setting => setting.Key == ServersKey);
                if (row == null)
                {
                    return servers;
                }
                JsonArray? items = JsonNode.Parse(row.Value) as JsonArray;
                if (items == null)
                {
                    return servers;
                }
                foreach (JsonNode? item in items)
                {
                    JsonObject? entry = item as JsonObject;
                    string? address = entry?["address"]?.GetValue<string>();
                    if (entry == null || address == null || address.Length == 0)
                    {
                        continue;
                    }
                    ServerKind kind = ServerKind.Auto;
                    if (entry["kind"]?.GetValue<string>() == "openai")
                    {
                        kind = ServerKind.OpenAiCompatible;
                    }
                    SavedServer server = new SavedServer(address, kind);
                    server.Name = entry["name"]?.GetValue<string>();
                    servers.Add(server);
                }
            }
        }
        catch (Exception)
        {
            // A broken row gives no saved servers.
        }
        return servers;
    }

    public void SaveServers(IReadOnlyList<SavedServer> servers)
    {
        JsonArray items = new JsonArray();
        foreach (SavedServer server in servers)
        {
            JsonObject entry = new JsonObject();
            entry["address"] = server.Address;
            if (server.Name != null)
            {
                entry["name"] = server.Name;
            }
            if (server.Kind == ServerKind.OpenAiCompatible)
            {
                entry["kind"] = "openai";
            }
            items.Add(entry);
        }
        try
        {
            using (SmolcoderDbContext context = contextFactory.CreateDbContext())
            {
                string? value = null;
                if (servers.Count > 0)
                {
                    value = items.ToJsonString();
                }
                Put(context, ServersKey, value);
                context.SaveChanges();
            }
        }
        catch (Exception)
        {
            // Not fatal.
        }
    }

    private static string? Value(Dictionary<string, string> values, string key)
    {
        string? value;
        if (values.TryGetValue(key, out value))
        {
            return value;
        }
        return null;
    }

    private static void Put(SmolcoderDbContext context, string key, string? value)
    {
        SettingEntity? existing = context.Settings.Find(key);
        if (value == null)
        {
            if (existing != null)
            {
                context.Settings.Remove(existing);
            }
            return;
        }
        if (existing == null)
        {
            SettingEntity created = new SettingEntity();
            created.Key = key;
            created.Value = value;
            context.Settings.Add(created);
        }
        else
        {
            existing.Value = value;
        }
    }
}
