using Markdig;

namespace Smolcoder.Web.Services;

/// <summary>
/// Turns model replies (markdown) into HTML. Raw HTML in the reply is not allowed,
/// so a reply cannot inject script into the page.
/// </summary>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline pipeline;

    public MarkdownRenderer()
    {
        pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UsePipeTables()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseTaskLists()
            .Build();
    }

    public string ToHtml(string markdown)
    {
        return Markdown.ToHtml(markdown, pipeline);
    }
}
