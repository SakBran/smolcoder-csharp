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
