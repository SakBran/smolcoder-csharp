namespace Smolcoder.Application.Agents;

/// <summary>
/// An acceptance command. It comes from the caller or from the project, never from the model.
/// </summary>
public sealed class VerificationSettings
{
    public VerificationSettings(string command, int? maxAttempts, bool fromProject)
    {
        Command = command;
        MaxAttempts = maxAttempts;
        FromProject = fromProject;
    }

    public string Command { get; }

    public int? MaxAttempts { get; }

    /// <summary>
    /// True when the command was found in package.json, not given by the caller.
    /// </summary>
    public bool FromProject { get; }

    public int AttemptLimit
    {
        get
        {
            if (MaxAttempts.HasValue)
            {
                return MaxAttempts.Value;
            }
            return 6;
        }
    }
}
