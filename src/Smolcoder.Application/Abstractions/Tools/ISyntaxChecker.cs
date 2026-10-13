namespace Smolcoder.Application.Abstractions.Tools;

/// <summary>
/// Checks a file just written for syntax errors.
/// </summary>
public interface ISyntaxChecker
{
    /// <summary>
    /// A short warning about the first syntax error, or null when the file looks fine
    /// or no checker is available.
    /// </summary>
    string? Check(string absolutePath, string relativePath);
}
