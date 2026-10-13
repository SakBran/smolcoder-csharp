using System.Text.RegularExpressions;

namespace Smolcoder.Application.Verification;

/// <summary>
/// Rules for reading the result of a check command.
/// </summary>
public static class VerificationRules
{
    private static readonly Regex AnsiCodes = new Regex(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);
    private static readonly Regex Numbers = new Regex(@"\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ExitCodeZero = new Regex(@"\[exit code 0 in [^\]]+\]\s*$", RegexOptions.Compiled);

    /// <summary>
    /// The same failure with different numbers (times, line counts) gives the same signature.
    /// </summary>
    public static string FailureSignature(string output)
    {
        string withoutColors = AnsiCodes.Replace(output, string.Empty);
        string withoutNumbers = Numbers.Replace(withoutColors, "#");
        return Whitespace.Replace(withoutNumbers, " ").Trim();
    }

    /// <summary>
    /// True when the command ran and exited with code 0.
    /// </summary>
    public static bool Passed(string output)
    {
        if (output.StartsWith("Error", StringComparison.Ordinal))
        {
            return false;
        }
        return ExitCodeZero.IsMatch(output);
    }
}
