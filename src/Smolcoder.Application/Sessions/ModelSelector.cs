using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Sessions;

public sealed class ModelSelector : IModelSelector
{
    private readonly IModelCatalog catalog;

    public ModelSelector(IModelCatalog catalog)
    {
        this.catalog = catalog;
    }

    public async Task<ModelDescriptor?> PrepareModelAsync(SessionPreferences preferences, AppSettings settings, Action<string>? progress, CancellationToken cancellationToken)
    {
        if (progress != null)
        {
            progress("looking for models");
        }
        string? url = settings.LastModelUrl;
        if (preferences.Model != null)
        {
            url = preferences.BaseUrl;
        }
        IReadOnlyList<ModelDescriptor> detected = await catalog.DetectAsync(cancellationToken);
        List<ModelDescriptor> models = detected
            .Where(model => preferences.Backend == null || model.Backend == preferences.Backend)
            .ToList();
        if (models.Count == 0)
        {
            return null;
        }
        ModelDescriptor chosen = AutoPick(models, preferences.Model, settings.LastModel, url);
        if (progress != null)
        {
            progress("loading " + chosen.Id);
        }
        return await catalog.ResolveContextWindowAsync(chosen, preferences.ContextWindow, cancellationToken);
    }

    /// <summary>
    /// The url only breaks ties between servers that have the same model id.
    /// </summary>
    public ModelDescriptor AutoPick(IReadOnlyList<ModelDescriptor> models, string? wanted, string? remembered, string? url)
    {
        if (wanted != null)
        {
            string id = catalog.ResolveAlias(wanted);
            ModelDescriptor? hit = models.FirstOrDefault(model => model.Id == id && model.BaseUrl == url);
            if (hit == null)
            {
                hit = models.FirstOrDefault(model => model.Id == id);
            }
            if (hit == null)
            {
                hit = models.FirstOrDefault(model => model.Id.ToLowerInvariant().Contains(id.ToLowerInvariant()));
            }
            if (hit != null)
            {
                return hit;
            }
            throw new InvalidOperationException("Model \"" + wanted + "\" was not found on the selected backend. Use /models to choose an available model.");
        }

        ModelDescriptor? rememberedHit = models.FirstOrDefault(model => model.Id == remembered && model.BaseUrl == url);
        if (rememberedHit == null)
        {
            rememberedHit = models.FirstOrDefault(model => model.Id == remembered);
        }
        if (rememberedHit != null)
        {
            return rememberedHit;
        }
        // With nothing remembered, a model on this computer beats one elsewhere.
        List<ModelDescriptor> local = models.Where(model => model.Host == null).ToList();
        List<ModelDescriptor> pool = models.ToList();
        if (local.Count > 0)
        {
            pool = local;
        }
        return pool[0];
    }

    public string NoModelsMessage()
    {
        return "No usable model found.\n\n"
            + "smolcoder connects to DeepSeek's cloud API.\n"
            + "  · Set DEEPSEEK_API_KEY in your environment, for example: export DEEPSEEK_API_KEY=sk-...\n"
            + "  · DEEPSEEK_BASE_URL points it at a proxy instead of api.deepseek.com.\n\n"
            + "Then run smol again.";
    }
}
