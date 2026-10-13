namespace Smolcoder.Domain.Modes;

/// <summary>
/// Names and parsing for permission modes.
/// </summary>
public static class PermissionModes
{
    public static readonly IReadOnlyList<PermissionMode> CycleOrder = new List<PermissionMode>
    {
        PermissionMode.ReadOnly,
        PermissionMode.Edit,
        PermissionMode.Bypass
    };

    public static string Label(PermissionMode mode)
    {
        if (mode == PermissionMode.ReadOnly)
        {
            return "read-only";
        }
        if (mode == PermissionMode.Edit)
        {
            return "edit";
        }
        return "bypass permissions";
    }

    public static string ShortName(PermissionMode mode)
    {
        if (mode == PermissionMode.ReadOnly)
        {
            return "ro";
        }
        if (mode == PermissionMode.Edit)
        {
            return "edit";
        }
        return "bypass";
    }

    /// <summary>
    /// Parses ro, edit or bypass, including the old names. Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, out PermissionMode mode)
    {
        mode = PermissionMode.Edit;
        if (text == null)
        {
            return false;
        }
        string value = text.Trim().ToLowerInvariant();
        if (value == "ro" || value == "read-only" || value == "readonly")
        {
            mode = PermissionMode.ReadOnly;
            return true;
        }
        if (value == "edit" || value == "e" || value == "write" || value == "w")
        {
            mode = PermissionMode.Edit;
            return true;
        }
        if (value == "bypass" || value == "bypass-permissions" || value == "b" || value == "yolo" || value == "y")
        {
            mode = PermissionMode.Bypass;
            return true;
        }
        return false;
    }

    public static PermissionMode Next(PermissionMode mode)
    {
        int index = 0;
        for (int position = 0; position < CycleOrder.Count; position++)
        {
            if (CycleOrder[position] == mode)
            {
                index = position;
            }
        }
        return CycleOrder[(index + 1) % CycleOrder.Count];
    }
}
