namespace Smolcoder.Application.Abstractions.Interaction;

/// <summary>
/// One row in a picker.
/// </summary>
public sealed class SelectOption
{
    public SelectOption(string label, string? hint, bool current)
    {
        Label = label;
        Hint = hint;
        Current = current;
    }

    public string Label { get; }

    public string? Hint { get; }

    /// <summary>
    /// True for the value in use right now.
    /// </summary>
    public bool Current { get; }
}
