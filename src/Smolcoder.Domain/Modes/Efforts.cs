namespace Smolcoder.Domain.Modes;

/// <summary>
/// Names and parsing for reasoning effort.
/// </summary>
public static class Efforts
{
    public static string Name(Effort? effort)
    {
        if (!effort.HasValue)
        {
            return "default";
        }
        return effort.Value.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Parses off, low, medium, high or default. "default" gives a null effort.
    /// </summary>
    public static bool TryParse(string? text, out Effort? effort)
    {
        effort = null;
        if (text == null)
        {
            return false;
        }
        string value = text.Trim().ToLowerInvariant();
        if (value == "default")
        {
            effort = null;
            return true;
        }
        if (value == "off")
        {
            effort = Effort.Off;
            return true;
        }
        if (value == "low")
        {
            effort = Effort.Low;
            return true;
        }
        if (value == "medium")
        {
            effort = Effort.Medium;
            return true;
        }
        if (value == "high")
        {
            effort = Effort.High;
            return true;
        }
        return false;
    }
}
