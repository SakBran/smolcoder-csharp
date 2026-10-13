namespace Smolcoder.Domain.Text;

/// <summary>
/// Rough token count: about four characters per token.
/// Real usage from the backend corrects it later.
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Tokens one attached image costs in the context gauge.
    /// </summary>
    public const int ImageTokens = 1500;

    public static int Estimate(string? text)
    {
        if (text == null)
        {
            return 0;
        }
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}
