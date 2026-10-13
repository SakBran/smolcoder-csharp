namespace Smolcoder.Infrastructure.Providers;

/// <summary>
/// A model server answered with an error status before any data streamed.
/// The message keeps the "&lt;Backend&gt; returned &lt;status&gt;: &lt;body&gt;" shape the agent's retry rules match on.
/// </summary>
public sealed class BackendHttpException : HttpRequestException
{
    public BackendHttpException(string backendName, int status, string body, string hint)
        : base(backendName + " returned " + status + hint + ": " + body)
    {
        Status = status;
        Body = body;
    }

    public int Status { get; }

    public string Body { get; }

    public bool IsClientError
    {
        get
        {
            return Status >= 400 && Status < 500;
        }
    }

    /// <summary>
    /// True when the error text names any of the words, ignoring case.
    /// Servers say which parameter they rejected only in the body.
    /// </summary>
    public bool Mentions(params string[] words)
    {
        foreach (string word in words)
        {
            if (Body.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Reads the error body (first 300 characters) and builds the exception.
    /// </summary>
    public static async Task<BackendHttpException> FromResponseAsync(string backendName, HttpResponseMessage response, string hint, CancellationToken token)
    {
        string text = string.Empty;
        try
        {
            text = await response.Content.ReadAsStringAsync(token);
        }
        catch (Exception)
        {
            text = string.Empty;
        }
        if (text.Length > 300)
        {
            text = text.Substring(0, 300);
        }
        return new BackendHttpException(backendName, (int)response.StatusCode, text, hint);
    }
}
