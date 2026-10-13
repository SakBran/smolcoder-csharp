using Smolcoder.Application.Abstractions.Persistence;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// The model a session asked for is not among the detected ones.
/// </summary>
public sealed class ModelNotFoundException : InvalidOperationException
{
    public ModelNotFoundException(string wanted)
        : base("Model \"" + wanted + "\" was not found on the selected backend. Use /models to choose an available model.")
    {
        Wanted = wanted;
    }

    public string Wanted { get; }
}

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
        ModelDetectOptions options = new ModelDetectOptions();
        string? remembered = settings.LastModel;
        if (preferences.Model == null && remembered != null)
        {
            // Stop looking once the remembered model answers; a switched-off machine need not be waited for.
            options.Until = model => model.Id == remembered && (url == null || model.BaseUrl == url);
        }
        IReadOnlyList<ModelDescriptor> detected = await catalog.DetectAsync(options, cancellationToken);
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
            string where = chosen.Host == null || chosen.Host == "cloud" ? string.Empty : " on " + chosen.Host;
            string memory = chosen.Backend == Backends.Ollama ? " into memory" : string.Empty;
            progress("loading " + chosen.Id + memory + where);
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
            throw new ModelNotFoundException(wanted);
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
        // With nothing remembered, a model on this computer beats one elsewhere,
        // then Ollama (it loads on demand) beats an LM Studio model that is already in memory.
        List<ModelDescriptor> local = models.Where(model => model.Host == null).ToList();
        List<ModelDescriptor> pool = models.ToList();
        if (local.Count > 0)
        {
            pool = local;
        }
        ModelDescriptor? ollama = pool.FirstOrDefault(model => model.Backend == Backends.Ollama && model.SupportsTools != false);
        if (ollama != null)
        {
            return ollama;
        }
        ModelDescriptor? loaded = pool.FirstOrDefault(model => model.Backend == Backends.LmStudio && model.Loaded == true);
        if (loaded != null)
        {
            return loaded;
        }
        return pool[0];
    }

    public const string NoModelsText = "No usable model found.\n\n"
            + "smolcoder looks for a model server on this computer, on servers you added, and in the cloud. Any one of these works:\n"
            + "  · Ollama: install it (brew install ollama, or https://ollama.com/download), start it, then: ollama pull qwen3\n"
            + "  · LM Studio: download a model, then turn on the server under Developer → Local Server.\n"
            + "  · Another machine or an OpenAI-compatible server (llama.cpp, vLLM…): add it in the web UI (smallcode --web).\n"
            + "  · DeepSeek cloud: export DEEPSEEK_API_KEY=sk-... or paste a key in the web UI.\n\n"
            + "Then run smallcode again.";

    public string NoModelsMessage()
    {
        return NoModelsText;
    }
}
