using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Infrastructure.Providers.LmStudio;

/// <summary>
/// LM Studio fixes the context when a model is loaded. A loaded model keeps it and --ctx can only
/// shrink our budget; a model that is not loaded yet is loaded with the --ctx size.
/// </summary>
public sealed class LmStudioBackend : IModelBackend
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IInferenceScheduler scheduler;

    public LmStudioBackend(IHttpClientFactory httpClientFactory, IInferenceScheduler scheduler)
    {
        this.httpClientFactory = httpClientFactory;
        this.scheduler = scheduler;
    }

    public string Name
    {
        get
        {
            return Backends.LmStudio;
        }
    }

    public async Task<ModelDescriptor> ResolveContextWindowAsync(ModelDescriptor model, int? contextOverride, CancellationToken cancellationToken)
    {
        ModelDescriptor resolved = model.Copy();
        if (model.Loaded == false && contextOverride.HasValue)
        {
            int desired = contextOverride.Value;
            if (model.MaxContext.HasValue)
            {
                desired = Math.Min(desired, model.MaxContext.Value);
            }
            JsonObject request = new JsonObject();
            request["model"] = model.Id;
            request["context_length"] = desired;
            request["echo_load_config"] = true;
            HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
            ProbeResult answer = await LocalHttp.PostJsonAsync(client, model.BaseUrl + "/api/v1/models/load", request, TimeSpan.FromSeconds(180), cancellationToken);
            if (!answer.Status.HasValue || answer.Status.Value < 200 || answer.Status.Value >= 300)
            {
                string status = answer.Status.HasValue ? answer.Status.Value.ToString() : "no answer";
                throw new InvalidOperationException("LM Studio could not load " + model.Id + " with " + desired + " context tokens (" + status + "). Load the model in LM Studio or reduce --ctx.");
            }
            int? actual = JsonRead.Int(JsonRead.Object(answer.Data, "load_config"), "context_length");
            string? instance = JsonRead.String(answer.Data, "instance_id");
            if (!actual.HasValue || actual.Value < 1024 || instance == null)
            {
                throw new InvalidOperationException("LM Studio did not confirm the loaded context. Load the model in LM Studio and select it again.");
            }
            resolved.Id = instance;
            resolved.Loaded = true;
            resolved.ContextWindow = Math.Min(desired, actual.Value);
            resolved.Note = null;
            return resolved;
        }
        if (contextOverride.HasValue && contextOverride.Value < model.ContextWindow)
        {
            resolved.ContextWindow = contextOverride.Value;
        }
        return resolved;
    }

    public IModelProvider CreateProvider(ModelDescriptor model)
    {
        return new LmStudioProvider(
            httpClientFactory,
            scheduler,
            model.BaseUrl,
            model.Id,
            model.ContextWindow,
            Backends.LocalOutputBudget(model.ContextWindow),
            model.ReasoningOptions,
            model.ReasoningDefault,
            model.SupportsVision);
    }
}
