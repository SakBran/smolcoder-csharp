using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Smolcoder.Application.Abstractions.Providers;
using Smolcoder.Domain.Messages;
using Smolcoder.Domain.Modes;

namespace Smolcoder.Infrastructure.Providers.OpenAi;

/// <summary>
/// The parts every OpenAI-compatible /chat/completions backend shares: request shape,
/// SSE streaming, deadlines, error text and the one-request-per-server queue.
/// Subclasses decide effort, reasoning replay and fallbacks.
/// </summary>
public abstract class OpenAiCompatibleProvider : IModelProvider
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string clientName;
    private readonly IInferenceScheduler scheduler;
    private readonly string? apiKey;

    protected OpenAiCompatibleProvider(
        IHttpClientFactory httpClientFactory,
        string clientName,
        IInferenceScheduler scheduler,
        string baseUrl,
        string? apiKey,
        string modelId,
        int contextWindow,
        int maxOutputTokens,
        bool? supportsVision)
    {
        this.httpClientFactory = httpClientFactory;
        this.clientName = clientName;
        this.scheduler = scheduler;
        this.apiKey = apiKey;
        BaseUrl = baseUrl;
        ModelId = modelId;
        ContextWindow = contextWindow;
        MaxOutputTokens = maxOutputTokens;
        SupportsVision = supportsVision;
    }

    public abstract bool ReplaysThinking { get; }

    public abstract string Label { get; }

    public string ModelId { get; }

    public int ContextWindow { get; }

    public int MaxOutputTokens { get; }

    public bool? SupportsVision { get; }

    protected string BaseUrl { get; }

    protected Effort? CurrentEffort { get; private set; }

    /// <summary>
    /// The name used in error messages, for example "LM Studio".
    /// </summary>
    protected abstract string BackendName { get; }

    protected virtual string CompletionsUrl
    {
        get
        {
            return BaseUrl + "/chat/completions";
        }
    }

    public virtual void SetEffort(Effort? effort)
    {
        CurrentEffort = effort;
    }

    public abstract string? EffortLabel();

    public virtual Task<int?> LoadedContextWindowAsync(CancellationToken cancellationToken)
    {
        int? unknown = null;
        return Task.FromResult(unknown);
    }

    public Task<ChatResult> ChatAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options)
    {
        return scheduler.RunAsync(BaseUrl, options.Background, options.CancellationToken, token => ChatScheduledAsync(messages, tools, options, token));
    }

    protected abstract Task<ChatResult> ChatScheduledAsync(IReadOnlyList<Message> messages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options, CancellationToken token);

    /// <summary>
    /// Text added after the status code of an error, for example " (check the API key)".
    /// </summary>
    protected virtual string ErrorHint(int status)
    {
        return string.Empty;
    }

    /// <summary>
    /// True when this reply's reasoning will be sent again in the next prompt.
    /// </summary>
    protected virtual bool CountThinkingInReplay(JsonObject body)
    {
        return false;
    }

    /// <summary>
    /// model, messages, tools and max_tokens. Streaming fields are added per attempt.
    /// </summary>
    protected JsonObject BaseBody(JsonArray wireMessages, IReadOnlyList<ToolSpec> tools, ChatRequestOptions options)
    {
        JsonObject body = new JsonObject();
        body["model"] = ModelId;
        body["messages"] = wireMessages;
        if (tools.Count > 0)
        {
            body["tools"] = OpenAiWireFormat.Tools(tools);
        }
        int maxTokens = MaxOutputTokens;
        if (options.MaxTokens.HasValue)
        {
            maxTokens = options.MaxTokens.Value;
        }
        body["max_tokens"] = maxTokens;
        return body;
    }

    /// <summary>
    /// Adds stream: true with usage reporting.
    /// </summary>
    protected static JsonObject Streaming(JsonObject body, bool includeUsage)
    {
        JsonObject copy = (JsonObject)body.DeepClone();
        copy["stream"] = true;
        if (includeUsage)
        {
            JsonObject streamOptions = new JsonObject();
            streamOptions["include_usage"] = true;
            copy["stream_options"] = streamOptions;
        }
        return copy;
    }

    /// <summary>
    /// Tries stream with usage, then plain stream, then a whole reply. Local servers differ in
    /// which of these they accept. Only a 4xx before any data moves to the next shape;
    /// errors naming one of rethrowWords go back to the caller, which owns that parameter.
    /// </summary>
    protected async Task<ChatResult> SendWithShapeFallbackAsync(JsonObject body, ChatRequestOptions options, CancellationToken token, StreamProgress progress, params string[] rethrowWords)
    {
        try
        {
            return await SendAsync(Streaming(body, true), options, token, progress);
        }
        catch (BackendHttpException exception) when (!progress.Streaming && exception.IsClientError && !exception.Mentions(rethrowWords))
        {
            // Some servers refuse stream_options.
        }
        try
        {
            return await SendAsync(Streaming(body, false), options, token, progress);
        }
        catch (BackendHttpException exception) when (!progress.Streaming && exception.IsClientError && !exception.Mentions(rethrowWords))
        {
            // Older servers refuse streaming together with tools.
        }
        JsonObject whole = (JsonObject)body.DeepClone();
        whole["stream"] = false;
        return await SendAsync(whole, options, token, progress);
    }

    protected Task<ChatResult> SendAsync(JsonObject body, ChatRequestOptions options, CancellationToken token, StreamProgress progress)
    {
        return RequestDeadline.RunAsync(token, options.Timeout, options.IdleTimeout, deadline => ReadResponseAsync(body, options, deadline, progress));
    }

    private async Task<ChatResult> ReadResponseAsync(JsonObject body, ChatRequestOptions options, RequestDeadline deadline, StreamProgress progress)
    {
        Stopwatch clock = Stopwatch.StartNew();
        HttpClient client = httpClientFactory.CreateClient(clientName);
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, CompletionsUrl))
        {
            if (apiKey != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw await BackendHttpException.FromResponseAsync(BackendName, response, ErrorHint((int)response.StatusCode), deadline.Token);
                }

                OpenAiStreamReader reader = new OpenAiStreamReader(options, progress, clock);
                bool streamed = JsonRead.Bool(body, "stream") == true;
                if (!streamed)
                {
                    string text = await response.Content.ReadAsStringAsync(deadline.Token);
                    reader.HandleCompletion(JsonRead.ParseStreamChunk(text));
                    return reader.ToResult(CountThinkingInReplay(body));
                }

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
                            if (line.Length > 4000000)
                            {
                                throw new IOException("Backend stream frame exceeded 4 MB");
                            }
                            reader.HandleLine(line);
                        }
                    }
                }

                if (!reader.Finished)
                {
                    throw new IOException(BackendName + " stream ended before completion; the response was discarded");
                }
                return reader.ToResult(CountThinkingInReplay(body));
            }
        }
    }
}
