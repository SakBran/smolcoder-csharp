namespace Smolcoder.Web.Services;

public enum ModelDialog
{
    Download,
    Servers,
    DeepSeekKey
}

/// <summary>
/// Which model dialog is open in this browser tab. Any component can open one; the page shows it.
/// </summary>
public sealed class ModelDialogs
{
    public event Action? Changed;

    public ModelDialog? Open { get; private set; }

    /// <summary>
    /// The Ollama server a download dialog should target first, when opened from a server row.
    /// </summary>
    public string? DownloadTarget { get; private set; }

    public void Show(ModelDialog dialog)
    {
        Show(dialog, null);
    }

    public void Show(ModelDialog dialog, string? downloadTarget)
    {
        Open = dialog;
        DownloadTarget = downloadTarget;
        Raise();
    }

    public void Close()
    {
        Open = null;
        DownloadTarget = null;
        Raise();
    }

    private void Raise()
    {
        Action? handler = Changed;
        if (handler != null)
        {
            handler();
        }
    }
}
