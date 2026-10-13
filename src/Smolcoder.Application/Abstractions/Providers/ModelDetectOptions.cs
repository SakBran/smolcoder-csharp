namespace Smolcoder.Application.Abstractions.Providers;

/// <summary>
/// How hard model detection looks.
/// </summary>
public sealed class ModelDetectOptions
{
    /// <summary>
    /// Return as soon as a matching model turns up. Startup looks for the remembered model
    /// and need not wait for a saved machine that is switched off.
    /// </summary>
    public Func<ModelDescriptor, bool>? Until { get; set; }

    public bool IncludeSavedServers { get; set; } = true;

    /// <summary>
    /// Also ask each Ollama model for its context size and capabilities. The picker wants this; startup does not.
    /// </summary>
    public bool IncludeDetails { get; set; }
}
