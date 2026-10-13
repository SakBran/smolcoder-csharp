using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Sandbox;

/// <summary>
/// Keeps file tools inside the workspace, symbolic links included.
///
/// Commands cannot be contained the same way. Edit mode scans the command text
/// and asks only when something reaches outside. This is a best-effort text check,
/// not an operating-system sandbox.
/// </summary>
public sealed class WorkspaceSandbox : IWorkspaceSandbox
{
    private static readonly Regex HomeTokens = new Regex(@"^(~|\$HOME|\$\{HOME\}|%USERPROFILE%|%HOMEPATH%)([\\/]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TempTokens = new Regex(@"^(\$TMPDIR|\$\{TMPDIR\}|\$TEMP|\$TMP|%TEMP%|%TMP%|\$\{TEMP\}|\$\{TMP\})([\\/]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GlobalInstall = new Regex(@"\b(npm|pnpm|yarn|bun)\b[^;&|]*\s(-g|--global|global)(\s|$)", RegexOptions.Compiled);
    private static readonly Regex MsysRoots = new Regex(@"^/(tmp|usr|etc|bin|home|mnt|dev|proc|var|opt|root)(/|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingRedirect = new Regex(@"^[<>]+|^[\d]?[<>]+&?", RegexOptions.Compiled);
    private static readonly Regex Quotes = new Regex("^[\"'`]+|[\"'`,;)]+$", RegexOptions.Compiled);
    private static readonly Regex UrlScheme = new Regex(@"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AssignmentName = new Regex(@"^[-\w.]+$", RegexOptions.Compiled);
    private static readonly Regex MsysDrive = new Regex(@"^/([a-zA-Z])(/.*)$", RegexOptions.Compiled);
    private static readonly Regex WindowsDrive = new Regex(@"^[a-zA-Z]:[\\/]", RegexOptions.Compiled);
    private static readonly Regex ClimbSegment = new Regex(@"(^|[\\/])\.\.([\\/]|$)", RegexOptions.Compiled);

    private static bool IsWindows
    {
        get
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }
    }

    public string ResolveInWorkspace(string workspaceRoot, string? userPath)
    {
        if (userPath == null || userPath.Trim().Length == 0)
        {
            throw new SandboxException("path is required (relative to the workspace, e.g. \"src/app.js\").");
        }
        string absolutePath = Path.GetFullPath(userPath, Path.GetFullPath(workspaceRoot));

        string existing;
        try
        {
            existing = RealPath.DeepestExisting(absolutePath);
        }
        catch (Exception)
        {
            throw new SandboxException("cannot inspect path \"" + userPath + "\".");
        }

        string realExisting;
        string realRoot;
        try
        {
            realExisting = RealPath.Resolve(existing);
            realRoot = RealPath.Resolve(workspaceRoot);
        }
        catch (Exception)
        {
            throw new SandboxException("cannot resolve path \"" + userPath + "\".");
        }

        string relative = Path.GetRelativePath(NormalizeForCompare(realRoot), NormalizeForCompare(realExisting));
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new SandboxException("\"" + userPath + "\" is outside the workspace. Only files inside " + workspaceRoot + " can be accessed. Use a relative path like \"src/app.js\".");
        }
        return absolutePath;
    }

    public string RelativePath(string workspaceRoot, string absolutePath)
    {
        string relative = Path.GetRelativePath(workspaceRoot, absolutePath);
        if (relative.Length == 0)
        {
            return ".";
        }
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public string? CommandEscapesWorkspace(string command, string workspaceRoot)
    {
        if (GlobalInstall.IsMatch(command))
        {
            return "installs a package globally, outside the workspace";
        }
        foreach (string token in PathCandidates(command))
        {
            if (HomeTokens.IsMatch(token))
            {
                return "uses the home directory (" + token + ")";
            }
            if (TempTokens.IsMatch(token))
            {
                return "uses the system temp directory (" + token + ")";
            }
            string candidate = FromMsys(token);
            if (IsAbsoluteLike(candidate))
            {
                // Under Git Bash on Windows, /tmp or /usr/bin is an MSYS path, never inside the workspace.
                bool posixOnWindows = IsWindows && candidate.StartsWith("/", StringComparison.Ordinal);
                if (posixOnWindows && !MsysRoots.IsMatch(candidate))
                {
                    // A command switch like "/pid", not a path.
                    continue;
                }
                if (posixOnWindows || !InsideWorkspace(workspaceRoot, candidate))
                {
                    return "reaches outside the workspace (" + token + ")";
                }
                continue;
            }
            if (ClimbSegment.IsMatch(candidate))
            {
                string resolved = Path.GetFullPath(candidate, Path.GetFullPath(workspaceRoot));
                if (!InsideWorkspace(workspaceRoot, resolved))
                {
                    return "climbs above the workspace (" + token + ")";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Words that may be paths: quotes removed, the value side of NAME=value,
    /// and redirect targets written without a space.
    /// </summary>
    private static List<string> PathCandidates(string command)
    {
        List<string> candidates = new List<string>();
        foreach (string raw in Whitespace.Split(command))
        {
            if (raw.Length == 0)
            {
                continue;
            }
            string token = LeadingRedirect.Replace(raw, string.Empty, 1);
            token = Quotes.Replace(token, string.Empty);
            if (token.Length == 0)
            {
                continue;
            }
            if (UrlScheme.IsMatch(token))
            {
                continue;
            }
            int equals = token.IndexOf('=');
            if (equals > 0 && AssignmentName.IsMatch(token.Substring(0, equals)))
            {
                token = token.Substring(equals + 1);
            }
            if (token.Length > 0)
            {
                candidates.Add(token);
            }
        }
        return candidates;
    }

    /// <summary>
    /// Git Bash writes C:\x as /c/x. Map it back on Windows.
    /// </summary>
    private static string FromMsys(string token)
    {
        Match match = MsysDrive.Match(token);
        if (match.Success && IsWindows)
        {
            return match.Groups[1].Value.ToUpperInvariant() + ":" + match.Groups[2].Value.Replace('/', '\\');
        }
        return token;
    }

    private static bool IsAbsoluteLike(string token)
    {
        if (WindowsDrive.IsMatch(token))
        {
            return true;
        }
        if (token.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return true;
        }
        return token.StartsWith("/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Both sides are resolved the same way, so a workspace behind a symbolic link still works.
    /// </summary>
    private static bool InsideWorkspace(string workspaceRoot, string absolutePath)
    {
        string root = RealPath.ResolveExistingPart(Path.GetFullPath(workspaceRoot));
        string target = RealPath.ResolveExistingPart(Path.GetFullPath(absolutePath));
        string relative = Path.GetRelativePath(NormalizeForCompare(root), NormalizeForCompare(target));
        if (relative == ".")
        {
            return true;
        }
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static string NormalizeForCompare(string path)
    {
        if (IsWindows)
        {
            return path.ToLowerInvariant();
        }
        return path;
    }
}
