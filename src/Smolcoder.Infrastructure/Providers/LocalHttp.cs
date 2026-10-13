using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// What a probe learned: whether anything answered, and its JSON when it was JSON.
/// </summary>
public sealed class ProbeResult
{
    public ProbeResult(bool reached, JsonNode? data, int? status)
    {
        Reached = reached;
        Data = data;
        Status = status;
    }

    /// <summary>
    /// True when a server answered at all, even with an error status.
    /// </summary>
    public bool Reached { get; }

    public JsonNode? Data { get; }

    public int? Status { get; }
}

/// <summary>
/// Short HTTP calls to model servers: probing, listing, small control requests.
/// Never throws for network trouble; a dead address is a normal answer here.
/// </summary>
public static class LocalHttp
{
    /// <summary>
    /// Named client for model servers on this computer and the local network. It skips the system proxy.
    /// </summary>
    public const string ClientName = "local-llm";

    public static Task<ProbeResult> GetJsonAsync(HttpClient client, string url, TimeSpan timeout, string? apiKey, CancellationToken token)
    {
        return SendAsync(client, HttpMethod.Get, url, null, timeout, apiKey, token);
    }

    public static Task<ProbeResult> PostJsonAsync(HttpClient client, string url, JsonObject body, TimeSpan timeout, CancellationToken token)
    {
        return SendAsync(client, HttpMethod.Post, url, body, timeout, null, token);
    }

    /// <summary>
    /// The data of a successful JSON answer, or null.
    /// </summary>
    public static async Task<JsonNode?> TryGetJsonAsync(HttpClient client, string url, TimeSpan timeout, CancellationToken token)
    {
        ProbeResult result = await GetJsonAsync(client, url, timeout, null, token);
        if (result.Status.HasValue && result.Status.Value >= 200 && result.Status.Value < 300)
        {
            return result.Data;
        }
        return null;
    }

    public static async Task<JsonNode?> TryPostJsonAsync(HttpClient client, string url, JsonObject body, TimeSpan timeout, CancellationToken token)
    {
        ProbeResult result = await PostJsonAsync(client, url, body, timeout, token);
        if (result.Status.HasValue && result.Status.Value >= 200 && result.Status.Value < 300)
        {
            return result.Data;
        }
        return null;
    }

    private static async Task<ProbeResult> SendAsync(HttpClient client, HttpMethod method, string url, JsonObject? body, TimeSpan timeout, string? apiKey, CancellationToken token)
    {
        using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            limit.CancelAfter(timeout);
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(method, url))
                {
                    if (apiKey != null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    }
                    if (body != null)
                    {
                        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    }
                    using (HttpResponseMessage response = await client.SendAsync(request, limit.Token))
                    {
                        int status = (int)response.StatusCode;
                        string text = await response.Content.ReadAsStringAsync(limit.Token);
                        JsonNode? data = null;
                        try
                        {
                            data = JsonNode.Parse(text);
                        }
                        catch (JsonException)
                        {
                            data = null;
                        }
                        return new ProbeResult(true, data, status);
                    }
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                return new ProbeResult(false, null, null);
            }
        }
    }
}
