namespace Smolcoder.Cli.Headless;

/// <summary>
/// Terminal colors, used only when the output is a terminal and NO_COLOR is not set.
/// </summary>
public static class AnsiColors
{
    private static readonly bool Enabled = !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("NO_COLOR") == null;

    public static string Dim(string text)
    {
        return Wrap("2", text);
    }

    public static string Gray(string text)
    {
        return Wrap("90", text);
    }

    public static string Red(string text)
    {
        return Wrap("31", text);
    }

    public static string Green(string text)
    {
        return Wrap("32", text);
    }

    public static string Yellow(string text)
    {
        return Wrap("33", text);
    }

    public static string Cyan(string text)
    {
        return Wrap("36", text);
    }

    public static string Bold(string text)
    {
        return Wrap("1", text);
    }

    private static string Wrap(string code, string text)
    {
        if (!Enabled)
        {
            return text;
        }
        return "\u001b[" + code + "m" + text + "\u001b[0m";
    }
}
