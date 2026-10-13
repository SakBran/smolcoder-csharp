using System.Text;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Infrastructure.Providers.Ollama;

/// <summary>
/// POST /api/pull with streamed progress. Ollama reports each layer separately, so sizes are
/// summed per layer digest to give one bar that does not jump back when the next layer starts.
/// </summary>
public sealed class OllamaPuller : IModelDownloader
{
    private readonly IHttpClientFactory httpClientFactory;

    public OllamaPuller(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public async Task PullAsync(string ollamaBaseUrl, string model, IProgress<PullProgress> progress, CancellationToken cancellationToken)
    {
        JsonObject body = new JsonObject();
        body["model"] = model;
        body["stream"] = true;
        HttpClient client = httpClientFactory.CreateClient(LocalHttp.ClientName);
        // No total limit: a big model on a slow line takes as long as it takes. Silence still fails.
        await RequestDeadline.RunAsync(cancellationToken, Timeout.InfiniteTimeSpan, TimeSpan.FromSeconds(120), async deadline =>
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ollamaBaseUrl + "/api/pull"))
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                }
                catch (HttpRequestException exception)
                {
                    throw new InvalidOperationException("Ollama is not answering at " + ollamaBaseUrl + " (" + exception.Message + "). Start Ollama and try again.");
                }
                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        BackendHttpException error = await BackendHttpException.FromResponseAsync("Ollama", response, string.Empty, deadline.Token);
                        throw new InvalidOperationException(Explain(model, ErrorText(error.Body) ?? error.Message));
                    }
                    Dictionary<string, long[]> layers = new Dictionary<string, long[]>();
                    using (Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token))
                    {
                        using (StreamReader lines = new StreamReader(stream, Encoding.UTF8))
                        {
                            while (true)
                            {
                                string? line = await lines.ReadLineAsync(deadline.Token);
                                if (line == null)
                                {
                                    break;
                                }
                                deadline.Activity();
                                if (line.Trim().Length == 0)
                                {
                                    continue;
                                }
                                JsonNode? chunk;
                                try
                                {
                                    chunk = JsonNode.Parse(line);
                                }
                                catch (System.Text.Json.JsonException)
                                {
                                    continue;
                                }
                                string? failure = JsonRead.String(chunk, "error");
                                if (failure != null)
                                {
                                    throw new InvalidOperationException(Explain(model, failure));
                                }
                                string status = JsonRead.String(chunk, "status") ?? string.Empty;
                                string? digest = JsonRead.String(chunk, "digest");
                                long? total = JsonRead.Long(chunk, "total");
                                if (digest != null && total.HasValue)
                                {
                                    layers[digest] = new long[] { JsonRead.Long(chunk, "completed") ?? 0, total.Value };
                                }
                                long completedSum = 0;
                                long totalSum = 0;
                                foreach (long[] layer in layers.Values)
                                {
                                    completedSum += layer[0];
                                    totalSum += layer[1];
                                }
                                progress.Report(new PullProgress(status, totalSum > 0 ? completedSum : null, totalSum > 0 ? totalSum : null));
                                if (status == "success")
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    throw new InvalidOperationException("The download of " + model + " stopped before it finished. Try again.");
                }
            }
        });
    }

    private static string? ErrorText(string body)
    {
        try
        {
            return JsonRead.String(JsonNode.Parse(body), "error");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Explain(string model, string message)
    {
        if (message.Contains("file does not exist", StringComparison.OrdinalIgnoreCase) || message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "There is no model called \"" + model + "\" on ollama.com. Check the name at ollama.com/search.";
        }
        return "Ollama could not download " + model + ": " + message;
    }
}
