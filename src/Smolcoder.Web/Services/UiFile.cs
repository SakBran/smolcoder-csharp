namespace Smolcoder.Web.Services;

/// <summary>
/// An attachment chip shown on a user message.
/// </summary>
public sealed class UiFile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsImage { get; set; }

    public long Size { get; set; }
}
