using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Tools;
using Smolcoder.Application.Tools;
using Smolcoder.Domain.Text;

namespace Smolcoder.Infrastructure.FileSystem;

/// <summary>
/// The file tools: read_file, write_file, edit_file, list_files and search.
/// Rules for small models: flat string arguments, helpful error messages
/// (an error is a prompt, so it is written like one), and hard output caps.
/// </summary>
public sealed class FileSystemTools : IFileSystemTools
{
    private const int ReadLineLimit = 250;

    /// <summary>
    /// Must stay under the 10000-character tool result cap, so a read page is never cut in the middle.
    /// </summary>
    private const int ReadCharLimit = 9000;
    private const long ReadFileSizeLimit = 8L * 1024 * 1024;
    private const int ListEntryLimit = 200;
    private const int SearchMatchLimit = 50;
    private const long SearchFileSizeLimit = 512 * 1024;
    private const int SearchFileLimit = 5000;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> IgnoredDirectories = new HashSet<string>
    {
        "node_modules", ".git", "dist", "out", "build", ".next", ".nuxt", ".cache",
        "coverage", "__pycache__", ".venv", "venv", ".idea", ".vscode", "target",
        ".svelte-kit", ".turbo", "vendor", "bin", "obj"
    };

    private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".bmp", ".pdf", ".zip",
        ".gz", ".tar", ".7z", ".rar", ".exe", ".dll", ".so", ".dylib", ".bin",
        ".woff", ".woff2", ".ttf", ".otf", ".eot", ".mp3", ".mp4", ".mov", ".avi",
        ".wasm", ".db", ".sqlite", ".jar", ".class", ".pyc"
    };

    private static readonly Regex LineBreak = new Regex("\r?\n", RegexOptions.Compiled);
    private static readonly Regex HistoryPlaceholder = new Regex(@"^\[\d+ characters already applied to [^\r\n]+\. Read the file for current code\.\]$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HasIdentifierCharacter = new Regex(@"[a-zA-Z_$]", RegexOptions.Compiled);

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly IWorkspaceSandbox sandbox;

    public FileSystemTools(IWorkspaceSandbox sandbox)
    {
        this.sandbox = sandbox;
    }

    public string ReadFile(string workspaceRoot, JsonObject arguments, int? maxChars)
    {
        int requestedChars = ReadCharLimit;
        if (maxChars.HasValue)
        {
            requestedChars = maxChars.Value;
        }
        int charLimit = Math.Max(128, Math.Min(ReadCharLimit, requestedChars));
        string displayPath = JsonArguments.GetText(arguments, "path");
        string absolutePath = sandbox.ResolveInWorkspace(workspaceRoot, JsonArguments.GetString(arguments, "path"));

        if (!File.Exists(absolutePath) && !Directory.Exists(absolutePath))
        {
            string? directory = Path.GetDirectoryName(absolutePath);
            string hint = string.Empty;
            if (directory != null && Directory.Exists(directory))
            {
                List<string> near = Directory.EnumerateFileSystemEntries(directory)
                    .Select(entry => Path.GetFileName(entry))
                    .Take(15)
                    .ToList();
                if (near.Count > 0)
                {
                    hint = " Files that do exist in " + sandbox.RelativePath(workspaceRoot, directory) + ": " + string.Join(", ", near);
                }
            }
            return "Error: file \"" + displayPath + "\" does not exist." + hint;
        }
        if (Directory.Exists(absolutePath))
        {
            return "Error: \"" + displayPath + "\" is a folder, not a file. Use list_files with {\"path\": \"" + displayPath + "\"} to see what is inside it.";
        }

        long size = new FileInfo(absolutePath).Length;
        if (size > ReadFileSizeLimit)
        {
            return "Error: \"" + displayPath + "\" exceeds the 8 MB text-file limit. Use a command to extract a small relevant section into a workspace file, then read that file.";
        }
        if (IsProbablyBinary(absolutePath))
        {
            return "Error: \"" + displayPath + "\" looks like a binary file (" + size + " bytes) and cannot be read as text.";
        }

        string content = File.ReadAllText(absolutePath, Encoding.UTF8);
        string[] lines = LineBreak.Split(content);
        int total = lines.Length;

        int offset = 1;
        double? requestedOffset = JsonArguments.GetNumber(arguments, "offset");
        if (requestedOffset.HasValue && requestedOffset.Value >= 1)
        {
            offset = (int)Math.Floor(requestedOffset.Value);
        }
        if (offset > total)
        {
            string lineWord = "lines";
            if (total == 1)
            {
                lineWord = "line";
            }
            return "The file \"" + displayPath + "\" has only " + total + " " + lineWord + "; you have already read all of it.";
        }

        int limit = ReadLineLimit;
        double? requestedLimit = JsonArguments.GetNumber(arguments, "limit");
        if (requestedLimit.HasValue && requestedLimit.Value != 0)
        {
            limit = (int)Math.Floor(requestedLimit.Value);
        }
        limit = Math.Min(Math.Max(1, limit), 1000);

        List<string> slice = lines.Skip(offset - 1).Take(limit).ToList();
        string body = string.Join("\n", slice);
        int end = offset - 1 + slice.Count;
        bool charCut = false;
        if (body.Length > charLimit)
        {
            List<string> kept = body.Substring(0, charLimit).Split('\n').ToList();
            if (kept.Count > 1)
            {
                // Cut on a line boundary, so the trailer never claims a partly shown line.
                kept.RemoveAt(kept.Count - 1);
                body = string.Join("\n", kept);
                end = offset - 1 + kept.Count;
                charCut = true;
            }
            else
            {
                // One very long line: there is no line boundary to continue from.
                body = body.Substring(0, charLimit);
                return body + "\n\n[line " + offset + " of " + total + " is very long; showing its first " + charLimit + " characters only.]";
            }
        }
        if (offset == 1 && end >= total && !charCut)
        {
            return body;
        }
        return body + "\n\n[showing lines " + offset + "-" + end + " of " + total + ". Call read_file with {\"path\": \"" + displayPath + "\", \"offset\": " + (end + 1) + "} to continue.]";
    }

    public string WriteFile(string workspaceRoot, JsonObject arguments)
    {
        string displayPath = JsonArguments.GetText(arguments, "path");
        string absolutePath = sandbox.ResolveInWorkspace(workspaceRoot, JsonArguments.GetString(arguments, "path"));
        string? content = JsonArguments.GetString(arguments, "content");
        if (content == null)
        {
            return "Error: content is required and must be a string. Example: {\"path\": \"notes.txt\", \"content\": \"hello\"}";
        }
        if (HistoryPlaceholder.IsMatch(content.Trim()))
        {
            return "Error: this is a history placeholder, not source code. No file was changed. Read the current file and provide the actual complete contents.";
        }
        if (Directory.Exists(absolutePath))
        {
            return "Error: \"" + displayPath + "\" is an existing folder; cannot write a file there.";
        }

        bool existed = File.Exists(absolutePath);
        int previousLines = 0;
        if (existed)
        {
            previousLines = File.ReadAllText(absolutePath, Encoding.UTF8).Split('\n').Length;
        }
        string? directory = Path.GetDirectoryName(absolutePath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(absolutePath, content, Utf8NoBom);
        int newLines = content.Split('\n').Length;
        if (existed)
        {
            return "Overwrote " + displayPath + " (was " + previousLines + " lines, now " + newLines + " lines).";
        }
        return "Created " + displayPath + " (" + newLines + " lines).";
    }

    /// <summary>
    /// Exact match first, then a match that ignores whitespace at line ends,
    /// then an error that points to the closest real code.
    /// </summary>
    public string EditFile(string workspaceRoot, JsonObject arguments)
    {
        string displayPath = JsonArguments.GetText(arguments, "path");
        string absolutePath = sandbox.ResolveInWorkspace(workspaceRoot, JsonArguments.GetString(arguments, "path"));
        if (!File.Exists(absolutePath))
        {
            return "Error: file \"" + displayPath + "\" does not exist. Use write_file to create a new file.";
        }
        string? oldText = JsonArguments.GetString(arguments, "old_text");
        if (oldText == null || oldText.Length == 0)
        {
            return "Error: old_text is required — copy the exact text from the file that you want to replace. To create a new file use write_file instead.";
        }
        string newText = string.Empty;
        if (JsonArguments.Has(arguments, "new_text"))
        {
            string? given = JsonArguments.GetString(arguments, "new_text");
            if (given == null)
            {
                return "Error: new_text must be a string (use an empty string to delete the old text).";
            }
            newText = given;
        }
        if (HistoryPlaceholder.IsMatch(newText.Trim()))
        {
            return "Error: this is a history placeholder, not source code. No file was changed. Read the current file and provide the actual replacement text.";
        }

        string rawContent = File.ReadAllText(absolutePath, Encoding.UTF8);
        // Match on LF text, then write back with the file's own line endings.
        bool crlf = rawContent.Contains("\r\n");
        string content = rawContent;
        if (crlf)
        {
            content = rawContent.Replace("\r\n", "\n");
        }
        string oldNormalized = oldText.Replace("\r\n", "\n");
        string newNormalized = newText.Replace("\r\n", "\n");

        // Tier 1: exact match.
        int occurrences = CountOccurrences(content, oldNormalized);
        if (occurrences == 1)
        {
            int position = content.IndexOf(oldNormalized, StringComparison.Ordinal);
            string replaced = content.Substring(0, position) + newNormalized + content.Substring(position + oldNormalized.Length);
            File.WriteAllText(absolutePath, Serialize(replaced, crlf), Utf8NoBom);
            return "Edited " + displayPath + ": replaced 1 occurrence.";
        }
        if (occurrences > 1)
        {
            return "Error: old_text appears " + occurrences + " times in " + displayPath + ". Include a few more surrounding lines in old_text so it matches exactly one place.";
        }

        // Tier 2: match each line with its outer whitespace ignored.
        List<string> fileLines = content.Split('\n').ToList();
        List<string> oldLines = oldNormalized.Split('\n').ToList();
        List<int> matches = FindTrimmedMatches(fileLines, oldLines);
        if (matches.Count == 1)
        {
            int start = matches[0];
            List<string> rebuilt = new List<string>();
            rebuilt.AddRange(fileLines.Take(start));
            rebuilt.AddRange(newNormalized.Split('\n'));
            rebuilt.AddRange(fileLines.Skip(start + oldLines.Count));
            File.WriteAllText(absolutePath, Serialize(string.Join("\n", rebuilt), crlf), Utf8NoBom);
            return "Edited " + displayPath + ": replaced 1 occurrence (whitespace differences in old_text were ignored).";
        }
        if (matches.Count > 1)
        {
            return "Error: old_text matches " + matches.Count + " places in " + displayPath + " (ignoring whitespace). Include more surrounding lines to make it unique.";
        }

        // Tier 3: point to the closest real code.
        Snippet? snippet = ClosestSnippet(fileLines, oldText);
        if (snippet != null)
        {
            JsonObject readHint = new JsonObject();
            readHint["path"] = displayPath;
            readHint["offset"] = snippet.Offset;
            readHint["limit"] = snippet.Limit;
            return "Error: old_text was not found in " + displayPath + ". Closest source starts at line " + snippet.Offset + ". Read this range: " + readHint.ToJsonString()
                + ". Use a small exact replacement; this snippet is only a location hint, not the whole block you tried to replace.\n---\n" + TextTruncation.End(snippet.Text, 1500) + "\n---";
        }
        return "Error: old_text was not found in " + displayPath + ". Call read_file on it first and copy the exact text you want to change.";
    }

    public string ListFiles(string workspaceRoot, JsonObject arguments)
    {
        string startRelative = ".";
        string? requested = JsonArguments.GetString(arguments, "path");
        if (requested != null && requested.Trim().Length > 0)
        {
            startRelative = requested;
        }
        string start = sandbox.ResolveInWorkspace(workspaceRoot, startRelative);
        if (!Directory.Exists(start))
        {
            if (File.Exists(start))
            {
                return "Error: \"" + startRelative + "\" is a file, not a folder. Use read_file to read it.";
            }
            return "Error: folder \"" + startRelative + "\" does not exist.";
        }

        List<string> entries = new List<string>();
        bool truncated = WalkForList(workspaceRoot, start, 0, entries);
        if (entries.Count == 0)
        {
            return "The folder \"" + startRelative + "\" is empty.";
        }
        string output = string.Join("\n", entries);
        if (truncated)
        {
            output = output + "\n\n[listing capped at " + ListEntryLimit + " entries. Call list_files with {\"path\": \"<subfolder>\"} to explore deeper.]";
        }
        return output;
    }

    /// <summary>
    /// Returns true when the listing hit the entry cap.
    /// </summary>
    private bool WalkForList(string workspaceRoot, string directory, int depth, List<string> entries)
    {
        if (depth > 6)
        {
            return false;
        }
        List<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
        }
        catch (Exception)
        {
            return false;
        }
        children.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.InvariantCulture));
        foreach (FileSystemInfo child in children)
        {
            bool isDirectory = child is DirectoryInfo;
            if (child.Name.StartsWith(".", StringComparison.Ordinal) && isDirectory)
            {
                continue;
            }
            if (IgnoredDirectories.Contains(child.Name))
            {
                continue;
            }
            if (child.LinkTarget != null)
            {
                // Never follow links out of the workspace.
                continue;
            }
            string relative = sandbox.RelativePath(workspaceRoot, child.FullName);
            if (isDirectory)
            {
                entries.Add(relative + "/");
                if (entries.Count >= ListEntryLimit)
                {
                    return true;
                }
                if (WalkForList(workspaceRoot, child.FullName, depth + 1, entries))
                {
                    return true;
                }
            }
            else
            {
                entries.Add(relative);
                if (entries.Count >= ListEntryLimit)
                {
                    return true;
                }
            }
        }
        return false;
    }

    public async Task<string> SearchAsync(string workspaceRoot, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return "Error: search cancelled";
        }
        using (CancellationTokenSource timeout = new CancellationTokenSource(SearchTimeout))
        {
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                try
                {
                    return await Task.Run(() => Search(workspaceRoot, arguments, linked.Token), linked.Token);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return "Error: search cancelled";
                    }
                    return "Error: search timed out. Use a simpler pattern or search a smaller folder.";
                }
                catch (RegexMatchTimeoutException)
                {
                    return "Error: search timed out. Use a simpler pattern or search a smaller folder.";
                }
                catch (SandboxException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return "Error: search failed: " + exception.Message;
                }
            }
        }
    }

    private string Search(string workspaceRoot, JsonObject arguments, CancellationToken cancellationToken)
    {
        string? pattern = JsonArguments.GetString(arguments, "pattern");
        if (pattern == null || pattern.Length == 0)
        {
            return "Error: pattern is required. Example: {\"pattern\": \"function main\"}";
        }
        string startRelative = ".";
        string? requested = JsonArguments.GetString(arguments, "path");
        if (requested != null && requested.Trim().Length > 0)
        {
            startRelative = requested;
        }
        string start = sandbox.ResolveInWorkspace(workspaceRoot, startRelative);
        if (!File.Exists(start) && !Directory.Exists(start))
        {
            return "Error: folder \"" + startRelative + "\" does not exist.";
        }
        bool startIsFile = File.Exists(start);

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            regex = new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }

        SearchState state = new SearchState(regex, cancellationToken);
        if (startIsFile)
        {
            state.OnlyFile = Path.GetFullPath(start);
            string? parent = Path.GetDirectoryName(start);
            if (parent != null)
            {
                WalkForSearch(workspaceRoot, parent, 8, state);
            }
        }
        else
        {
            WalkForSearch(workspaceRoot, start, 0, state);
        }

        if (state.Matches.Count == 0)
        {
            return "No matches for \"" + pattern + "\" in " + startRelative + ". (Searched " + state.FilesScanned + " files. Tip: try a shorter or simpler pattern.)";
        }
        string output = string.Join("\n", state.Matches);
        if (state.Matches.Count >= SearchMatchLimit)
        {
            output = output + "\n\n[stopped at " + SearchMatchLimit + " matches — narrow the pattern or search a subfolder with {\"path\": \"...\"}]";
        }
        return output;
    }

    private void WalkForSearch(string workspaceRoot, string directory, int depth, SearchState state)
    {
        if (state.Done || depth > 8)
        {
            return;
        }
        state.CancellationToken.ThrowIfCancellationRequested();
        IEnumerable<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
        }
        catch (Exception)
        {
            return;
        }
        foreach (FileSystemInfo child in children)
        {
            if (state.Done)
            {
                return;
            }
            bool isDirectory = child is DirectoryInfo;
            if (child.Name.StartsWith(".", StringComparison.Ordinal) && isDirectory)
            {
                continue;
            }
            if (IgnoredDirectories.Contains(child.Name))
            {
                continue;
            }
            if (child.LinkTarget != null)
            {
                // A file link could leak its target (for example a key file) into the model's context.
                continue;
            }
            if (state.OnlyFile != null && Path.GetFullPath(child.FullName) != state.OnlyFile)
            {
                continue;
            }
            if (isDirectory)
            {
                WalkForSearch(workspaceRoot, child.FullName, depth + 1, state);
                continue;
            }
            state.FilesScanned++;
            if (state.FilesScanned > SearchFileLimit)
            {
                state.Done = true;
                return;
            }
            FileInfo file = (FileInfo)child;
            long length;
            try
            {
                length = file.Length;
            }
            catch (Exception)
            {
                continue;
            }
            if (length > SearchFileSizeLimit || IsProbablyBinary(file.FullName))
            {
                continue;
            }
            string text;
            try
            {
                text = File.ReadAllText(file.FullName, Encoding.UTF8);
            }
            catch (Exception)
            {
                continue;
            }
            string[] lines = LineBreak.Split(text);
            for (int index = 0; index < lines.Length; index++)
            {
                if (state.Regex.IsMatch(lines[index]))
                {
                    string line = lines[index].Trim();
                    if (line.Length > 200)
                    {
                        line = line.Substring(0, 200);
                    }
                    state.Matches.Add(sandbox.RelativePath(workspaceRoot, file.FullName) + ":" + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + line);
                    if (state.Matches.Count >= SearchMatchLimit)
                    {
                        state.Done = true;
                        break;
                    }
                }
            }
        }
    }

    private static bool IsProbablyBinary(string path)
    {
        if (BinaryExtensions.Contains(Path.GetExtension(path)))
        {
            return true;
        }
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] buffer = new byte[1024];
                int read = stream.Read(buffer, 0, buffer.Length);
                for (int index = 0; index < read; index++)
                {
                    if (buffer[index] == 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception)
        {
            return true;
        }
        return false;
    }

    private static int CountOccurrences(string content, string value)
    {
        int count = 0;
        int position = 0;
        while (true)
        {
            int found = content.IndexOf(value, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return count;
            }
            count++;
            position = found + value.Length;
        }
    }

    private static string Serialize(string text, bool crlf)
    {
        if (crlf)
        {
            return text.Replace("\n", "\r\n");
        }
        return text;
    }

    private static List<int> FindTrimmedMatches(List<string> fileLines, List<string> oldLines)
    {
        List<string> targets = oldLines.Select(line => line.Trim()).ToList();
        List<int> matches = new List<int>();
        for (int start = 0; start + targets.Count <= fileLines.Count; start++)
        {
            bool same = true;
            for (int offset = 0; offset < targets.Count; offset++)
            {
                if (fileLines[start + offset].Trim() != targets[offset])
                {
                    same = false;
                    break;
                }
            }
            if (same)
            {
                matches.Add(start);
            }
        }
        return matches;
    }

    private static Snippet? ClosestSnippet(List<string> fileLines, string oldText)
    {
        List<string> candidates = oldText.Split('\n').Select(line => line.Trim()).ToList();
        // A bare closing brace is not a useful anchor.
        string? firstLine = candidates.FirstOrDefault(line => HasIdentifierCharacter.IsMatch(line) && line.Length > 6);
        if (firstLine == null)
        {
            return null;
        }
        string target = NormalizeForScore(firstLine);
        int bestIndex = -1;
        int bestScore = 0;
        for (int index = 0; index < fileLines.Count; index++)
        {
            string line = NormalizeForScore(fileLines[index]);
            if (line.Length == 0)
            {
                continue;
            }
            int score = 0;
            if (line == target)
            {
                score = 1000;
            }
            else if (line.Contains(target) || target.Contains(line))
            {
                score = 500;
            }
            else
            {
                foreach (string word in target.Split(' ').Where(word => word.Length > 2))
                {
                    if (line.Contains(word))
                    {
                        score += word.Length;
                    }
                }
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }
        if (bestIndex < 0 || bestScore < 6)
        {
            return null;
        }
        int start = Math.Max(0, bestIndex - 2);
        int end = Math.Min(fileLines.Count, start + Math.Min(20, Math.Max(8, candidates.Count + 4)));
        string text = string.Join("\n", fileLines.Skip(start).Take(end - start));
        return new Snippet(text, start + 1, end - start);
    }

    private static string NormalizeForScore(string text)
    {
        return Whitespace.Replace(text, " ").Trim().ToLowerInvariant();
    }

    private sealed class Snippet
    {
        public Snippet(string text, int offset, int limit)
        {
            Text = text;
            Offset = offset;
            Limit = limit;
        }

        public string Text { get; }

        public int Offset { get; }

        public int Limit { get; }
    }

    private sealed class SearchState
    {
        public SearchState(Regex regex, CancellationToken cancellationToken)
        {
            Regex = regex;
            CancellationToken = cancellationToken;
            Matches = new List<string>();
        }

        public Regex Regex { get; }

        public CancellationToken CancellationToken { get; }

        public List<string> Matches { get; }

        public int FilesScanned { get; set; }

        public bool Done { get; set; }

        public string? OnlyFile { get; set; }
    }
}
