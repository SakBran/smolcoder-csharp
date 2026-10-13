namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// Backend names stored on models and sessions.
/// </summary>
public static class Backends
{
    public const string Ollama = "ollama";
    public const string LmStudio = "lmstudio";

    /// <summary>
    /// Any other OpenAI-compatible server (llama.cpp, vLLM, Jan…) the user added by URL.
    /// </summary>
    public const string OpenAi = "openai";
    public const string DeepSeek = "deepseek";

    public static string DisplayName(string backend)
    {
        if (backend == Ollama)
        {
            return "Ollama";
        }
        if (backend == LmStudio)
        {
            return "LM Studio";
        }
        if (backend == DeepSeek)
        {
            return "DeepSeek";
        }
        return "Server";
    }

    /// <summary>
    /// Reply room for models running on a server we own: a quarter of the window, at most 8192.
    /// </summary>
    public static int LocalOutputBudget(int contextWindow)
    {
        return Math.Max(128, Math.Min(8192, contextWindow / 4));
    }
}
