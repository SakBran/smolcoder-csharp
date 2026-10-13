using System.Text.RegularExpressions;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// Cleans up a model-written session title.
/// </summary>
public static class SessionTitles
{
    private static readonly Regex ThinkBlock = new Regex(@"<think>[\s\S]*?(</think>|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TitlePrefix = new Regex(@"^(title|session( name)?)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OuterNoise = new Regex("^[\"'`“”*#\\s]+|[\"'`“”*.\\s]+$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Brackets = new Regex("[<>{}]", RegexOptions.Compiled);
    private static readonly Regex TrailingPunctuation = new Regex(@"[\s,;:.-]+$", RegexOptions.Compiled);

    /// <summary>
    /// A clean title, or null when the text is not usable.
    /// </summary>
    public static string? Clean(string? raw)
    {
        string text = string.Empty;
        if (raw != null)
        {
            text = raw;
        }
        text = ThinkBlock.Replace(text, string.Empty);
        string? firstLine = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        if (firstLine == null)
        {
            return null;
        }
        text = TitlePrefix.Replace(firstLine, string.Empty, 1);
        text = OuterNoise.Replace(text, string.Empty);
        text = Whitespace.Replace(text, " ");
        if (text.Length < 3 || Brackets.IsMatch(text))
        {
            return null;
        }
        if (text.Length > 60)
        {
            text = text.Substring(0, 60);
            int cut = text.LastIndexOf(' ');
            if (cut > 20)
            {
                text = text.Substring(0, cut);
            }
            text = TrailingPunctuation.Replace(text, string.Empty);
        }
        if (text.Length == 0)
        {
            return null;
        }
        return text;
    }
}
