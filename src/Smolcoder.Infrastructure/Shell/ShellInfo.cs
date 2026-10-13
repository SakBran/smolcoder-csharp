namespace Smolcoder.Infrastructure.Shell;

/// <summary>
/// The shell program and how to pass it a command.
/// </summary>
public sealed class ShellInfo
{
    public ShellInfo(string executable, IReadOnlyList<string> argumentsBeforeCommand, string label, bool isBash)
    {
        Executable = executable;
        ArgumentsBeforeCommand = argumentsBeforeCommand;
        Label = label;
        IsBash = isBash;
    }

    public string Executable { get; }

    public IReadOnlyList<string> ArgumentsBeforeCommand { get; }

    /// <summary>
    /// Goes into the system prompt so the model knows which dialect to write.
    /// </summary>
    public string Label { get; }

    public bool IsBash { get; }
}
