namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// A file's text for the viewer, or the reason it cannot be shown.
/// </summary>
public sealed class FileView
{
    public FileView(string relativePath, string? content, string? problem)
    {
        RelativePath = relativePath;
        Content = content;
        Problem = problem;
    }

    public string RelativePath { get; }

    public string? Content { get; }

    public string? Problem { get; }

    /// <summary>
    /// When the file was last written, so the viewer can reload after an edit.
    /// </summary>
    public DateTime? LastWriteUtc { get; set; }
}
