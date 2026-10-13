using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Browsing;

public sealed class WorkspaceBrowser : IWorkspaceBrowser
{
    private const long MaxViewBytes = 2 * 1024 * 1024;
    private const int MaxFolders = 500;

    private static readonly HashSet<string> HiddenInTree = new HashSet<string>
    {
        "node_modules", ".git", "bin", "obj", ".vs", ".idea", "__pycache__", ".venv", "venv", "dist", ".next"
    };

    private static readonly string[] ProjectMarkers = new string[]
    {
        ".git", "package.json", "pyproject.toml", "Cargo.toml", "go.mod", "AGENTS.md", "pom.xml", "Gemfile", "global.json"
    };

    private static readonly Regex SystemFolders = new Regex(@"^(node_modules|\$recycle\.bin|system volume information|windows|program files( \(x86\))?|programdata|appdata)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IWorkspaceSandbox sandbox;

    public WorkspaceBrowser(IWorkspaceSandbox sandbox)
    {
        this.sandbox = sandbox;
    }

    public IReadOnlyList<FileEntry> ListDirectory(string workspaceRoot, string relativePath)
    {
        string path = relativePath;
        if (path.Length == 0)
        {
            path = ".";
        }
        string directory = sandbox.ResolveInWorkspace(workspaceRoot, path);
        List<FileEntry> entries = new List<FileEntry>();
        if (!Directory.Exists(directory))
        {
            return entries;
        }
        List<FileSystemInfo> children = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
        foreach (FileSystemInfo child in children)
        {
            if (HiddenInTree.Contains(child.Name) || child.LinkTarget != null)
            {
                continue;
            }
            bool isDirectory = child is DirectoryInfo;
            entries.Add(new FileEntry(child.Name, sandbox.RelativePath(workspaceRoot, child.FullName), isDirectory));
        }
        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public FileView ReadForView(string workspaceRoot, string relativePath)
    {
        string absolutePath;
        try
        {
            absolutePath = sandbox.ResolveInWorkspace(workspaceRoot, relativePath);
        }
        catch (SandboxException exception)
        {
            return new FileView(relativePath, null, exception.Message);
        }
        if (!File.Exists(absolutePath))
        {
            return new FileView(relativePath, null, "This file does not exist.");
        }
        FileInfo info = new FileInfo(absolutePath);
        if (info.Length > MaxViewBytes)
        {
            return new FileView(relativePath, null, "This file is larger than 2 MB and is not shown.");
        }
        byte[] bytes = File.ReadAllBytes(absolutePath);
        int probe = Math.Min(bytes.Length, 8192);
        for (int index = 0; index < probe; index++)
        {
            if (bytes[index] == 0)
            {
                return new FileView(relativePath, null, "This looks like a binary file.");
            }
        }
        FileView view = new FileView(relativePath, Encoding.UTF8.GetString(bytes), null);
        view.LastWriteUtc = info.LastWriteTimeUtc;
        return view;
    }

    public DateTime? LastWriteUtc(string workspaceRoot, string relativePath)
    {
        try
        {
            string absolutePath = sandbox.ResolveInWorkspace(workspaceRoot, relativePath);
            if (!File.Exists(absolutePath))
            {
                return null;
            }
            return File.GetLastWriteTimeUtc(absolutePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public FolderListing BrowseFolders(string? path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string target = home;
        if (path != null && path.Trim().Length > 0)
        {
            target = path.Trim();
            if (target == "~")
            {
                target = home;
            }
            else if (target.StartsWith("~/", StringComparison.Ordinal) || target.StartsWith("~\\", StringComparison.Ordinal))
            {
                target = Path.Combine(home, target.Substring(2));
            }
        }
        target = Path.GetFullPath(target);

        FolderListing listing = new FolderListing(target, Display(target, home));
        DirectoryInfo? parent = Directory.GetParent(target);
        if (parent != null)
        {
            listing.Parent = parent.FullName;
        }
        foreach (string root in Roots())
        {
            listing.Roots.Add(root);
        }
        if (!Directory.Exists(target))
        {
            listing.Problem = "This folder does not exist.";
            return listing;
        }
        listing.IsProject = LooksLikeProject(target);
        try
        {
            List<DirectoryInfo> folders = new DirectoryInfo(target).EnumerateDirectories()
                .Where(folder => !folder.Name.StartsWith(".", StringComparison.Ordinal) && !SystemFolders.IsMatch(folder.Name))
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxFolders)
                .ToList();
            foreach (DirectoryInfo folder in folders)
            {
                listing.Folders.Add(new FolderChoice(folder.Name, folder.FullName, LooksLikeProject(folder.FullName)));
            }
        }
        catch (Exception exception)
        {
            listing.Problem = exception.Message;
        }
        return listing;
    }

    private static bool LooksLikeProject(string folder)
    {
        foreach (string marker in ProjectMarkers)
        {
            string candidate = Path.Combine(folder, marker);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> Roots()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => drive.RootDirectory.FullName).ToList();
        }
        return new List<string> { "/" };
    }

    private static string Display(string path, string home)
    {
        if (path == home)
        {
            return "~";
        }
        if (path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return "~" + path.Substring(home.Length);
        }
        return path;
    }
}
