namespace Smolcoder.Application.Models;

/// <summary>
/// A model worth suggesting to someone with no model yet.
/// </summary>
public sealed class RecommendedModel
{
    public RecommendedModel(string name, string title, double downloadGigabytes, int minimumMemoryGigabytes, string why)
    {
        Name = name;
        Title = title;
        DownloadGigabytes = downloadGigabytes;
        MinimumMemoryGigabytes = minimumMemoryGigabytes;
        Why = why;
    }

    /// <summary>
    /// The Ollama name, for example "qwen3:8b".
    /// </summary>
    public string Name { get; }

    public string Title { get; }

    public double DownloadGigabytes { get; }

    /// <summary>
    /// Memory the computer should have to run it alongside everything else.
    /// </summary>
    public int MinimumMemoryGigabytes { get; }

    public string Why { get; }
}

/// <summary>
/// Tool-capable Ollama models, smallest first. Sizes are from ollama.com (October 2026);
/// any other name can be typed in the download box.
/// </summary>
public static class RecommendedModels
{
    public static readonly IReadOnlyList<RecommendedModel> All = new List<RecommendedModel>
    {
        new RecommendedModel("qwen3:4b", "Qwen3 4B", 2.5, 8, "small and quick; fine for simple edits"),
        new RecommendedModel("qwen3:8b", "Qwen3 8B", 5.2, 16, "good all-rounder for a 16 GB Mac"),
        new RecommendedModel("qwen3:14b", "Qwen3 14B", 9.3, 24, "noticeably better at multi-step work"),
        new RecommendedModel("gpt-oss:20b", "gpt-oss 20B", 14, 24, "OpenAI's open model; strong at tool use"),
        new RecommendedModel("qwen3.8:27b", "Qwen3.8 27B", 18, 32, "newest Qwen; best for long agentic tasks"),
        new RecommendedModel("qwen3-coder:30b", "Qwen3 Coder 30B", 19, 32, "made for code; fast for its size")
    };

    /// <summary>
    /// The largest model that leaves room for the system and the context cache on this computer.
    /// </summary>
    public static RecommendedModel BestFor(long totalMemoryBytes)
    {
        RecommendedModel best = All[0];
        foreach (RecommendedModel model in All)
        {
            if (Fits(model, totalMemoryBytes) && model.MinimumMemoryGigabytes > best.MinimumMemoryGigabytes)
            {
                best = model;
            }
        }
        return best;
    }

    public static bool Fits(RecommendedModel model, long totalMemoryBytes)
    {
        double gigabytes = totalMemoryBytes / (1024.0 * 1024.0 * 1024.0);
        // Round up: a "16 GB" machine reports a little under 16.
        return Math.Ceiling(gigabytes - 0.5) >= model.MinimumMemoryGigabytes;
    }
}
