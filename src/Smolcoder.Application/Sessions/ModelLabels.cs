using System.Globalization;
using Smolcoder.Application.Abstractions.Providers;

namespace Smolcoder.Application.Sessions;

/// <summary>
/// How a model is described in pickers and status lines.
/// </summary>
public static class ModelLabels
{
    /// <summary>
    /// For example "ollama", "lm studio @ gpu-box" or "deepseek · cloud".
    /// </summary>
    public static string BackendLabel(ModelDescriptor model)
    {
        string name = Backends.DisplayName(model.Backend).ToLowerInvariant();
        if (model.Host == null)
        {
            return name;
        }
        if (model.Host == "cloud")
        {
            return name + " · cloud";
        }
        return name + " @ " + model.Host;
    }

    /// <summary>
    /// The second line of a picker row: backend, size, quantization, context, host, warnings.
    /// </summary>
    public static string PickerHint(ModelDescriptor model)
    {
        List<string> parts = new List<string>();
        parts.Add(BackendLabel(model));
        if (model.SizeBytes.HasValue && model.SizeBytes.Value > 0)
        {
            parts.Add(Size(model.SizeBytes.Value));
        }
        string details = ((model.ParameterSize ?? string.Empty) + " " + (model.Quantization ?? string.Empty)).Trim();
        if (details.Length > 0)
        {
            parts.Add(details);
        }
        int? context = model.ContextWindow > 0 ? model.ContextWindow : model.MaxContext;
        if (model.Backend == Backends.Ollama && model.MaxContext.HasValue)
        {
            context = model.MaxContext;
        }
        if (context.HasValue && context.Value > 0)
        {
            parts.Add("ctx " + Number(context.Value));
        }
        if (model.Loaded == false)
        {
            parts.Add("not loaded");
        }
        if (model.SupportsTools == false)
        {
            parts.Add("no tools");
        }
        return string.Join(" · ", parts);
    }

    public static string Size(long bytes)
    {
        double gigabytes = bytes / 1_000_000_000.0;
        if (gigabytes >= 1)
        {
            return gigabytes.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }
        return (bytes / 1_000_000.0).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }

    public static string Number(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
