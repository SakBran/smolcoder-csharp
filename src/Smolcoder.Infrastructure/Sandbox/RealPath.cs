namespace Smolcoder.Infrastructure.Sandbox;

/// <summary>
/// Resolves symbolic links in a path, like realpath(3).
/// </summary>
public static class RealPath
{
    private const int MaxLinkDepth = 40;

    /// <summary>
    /// True when something exists at the path, a broken symbolic link included.
    /// </summary>
    public static bool EntryExists(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            return true;
        }
        try
        {
            FileInfo info = new FileInfo(path);
            return info.LinkTarget != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The deepest ancestor of the path that exists (the path itself when it does).
    /// </summary>
    public static string DeepestExisting(string absolutePath)
    {
        string existing = absolutePath;
        while (true)
        {
            if (EntryExists(existing))
            {
                return existing;
            }
            string? parent = Path.GetDirectoryName(existing);
            if (parent == null || parent == existing)
            {
                return existing;
            }
            existing = parent;
        }
    }

    /// <summary>
    /// Resolves every symbolic link in an existing path. Throws when a link cannot be read.
    /// </summary>
    public static string Resolve(string path)
    {
        return Resolve(path, 0);
    }

    private static string Resolve(string path, int depth)
    {
        if (depth > MaxLinkDepth)
        {
            throw new IOException("Too many levels of symbolic links: " + path);
        }
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (root == null || root.Length == 0)
        {
            return full;
        }
        string current = root;
        string remainder = full.Substring(root.Length);
        string[] parts = remainder.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string next = Path.Combine(current, part);
            string? target = LinkTargetOf(next);
            if (target == null)
            {
                current = next;
                continue;
            }
            string targetPath = target;
            if (!Path.IsPathRooted(targetPath))
            {
                targetPath = Path.Combine(current, targetPath);
            }
            current = Resolve(targetPath, depth + 1);
        }
        return current;
    }

    private static string? LinkTargetOf(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path).LinkTarget;
        }
        FileInfo file = new FileInfo(path);
        return file.LinkTarget;
    }

    /// <summary>
    /// The path with links resolved as far as it exists. The part not written yet is kept as given.
    /// Falls back to the path itself.
    /// </summary>
    public static string ResolveExistingPart(string absolutePath)
    {
        try
        {
            string existing = DeepestExisting(absolutePath);
            string resolved = Resolve(existing);
            string rest = Path.GetRelativePath(existing, absolutePath);
            if (rest == ".")
            {
                return resolved;
            }
            return Path.GetFullPath(Path.Combine(resolved, rest));
        }
        catch (Exception)
        {
            return absolutePath;
        }
    }
}
