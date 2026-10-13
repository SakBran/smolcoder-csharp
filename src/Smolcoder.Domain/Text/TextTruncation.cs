namespace Smolcoder.Domain.Text;

/// <summary>
/// Shortens long text while keeping the useful parts.
/// </summary>
public static class TextTruncation
{
    /// <summary>
    /// Keeps the start and the end, and drops the middle.
    /// </summary>
    public static string Middle(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }
        int head = (int)Math.Floor(maxChars * 0.6);
        int tail = maxChars - head;
        int omitted = text.Length - maxChars;
        return text.Substring(0, head)
            + "\n... [" + omitted + " characters omitted to save context] ...\n"
            + text.Substring(text.Length - tail);
    }

    /// <summary>
    /// Keeps the start and drops the end.
    /// </summary>
    public static string End(string text, int maxChars)
    {
        return End(text, maxChars, null);
    }

    public static string End(string text, int maxChars, string? note)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }
        string suffix;
        if (note == null)
        {
            suffix = "\n... [truncated]";
        }
        else
        {
            suffix = "\n... [truncated: " + note + "]";
        }
        return text.Substring(0, maxChars) + suffix;
    }

    public static string Plural(int count, string word)
    {
        if (count == 1)
        {
            return count + " " + word;
        }
        return count + " " + word + "s";
    }

    /// <summary>
    /// 850ms, 4.2s, or 3m12s.
    /// </summary>
    public static string Duration(long milliseconds)
    {
        if (milliseconds < 1000)
        {
            return milliseconds + "ms";
        }
        if (milliseconds < 60000)
        {
            return (milliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }
        long minutes = milliseconds / 60000;
        long seconds = (milliseconds % 60000) / 1000;
        return minutes + "m" + seconds + "s";
    }
}
