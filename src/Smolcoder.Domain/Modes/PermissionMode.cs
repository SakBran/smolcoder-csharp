namespace Smolcoder.Domain.Modes;

/// <summary>
/// What the agent may do without asking.
/// </summary>
public enum PermissionMode
{
    /// <summary>Read and search files only.</summary>
    ReadOnly,

    /// <summary>Edit files and run commands inside the workspace. Anything outside asks first.</summary>
    Edit,

    /// <summary>Full access. Nothing asks for approval.</summary>
    Bypass
}
