using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Smolcoder.Application.Abstractions.Tools;

namespace Smolcoder.Infrastructure.Checks;

/// <summary>
/// Checks a file right after it is written. A stray brace becomes a one-step fix
/// instead of a blank page. Only trusted parsers are used: node --check for JavaScript,
/// the JSON parser, and Python's compiler. Nothing runs the code.
/// </summary>
public sealed class SyntaxChecker : ISyntaxChecker
{
    private const long MaxCheckBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);
    private static readonly HashSet<string> CheckedExtensions = new HashSet<string> { ".js", ".mjs", ".cjs", ".json", ".html", ".htm", ".py" };
    private static readonly Regex LineBreak = new Regex("\r?\n", RegexOptions.Compiled);
    private static readonly Regex TrailingLineNumber = new Regex(@":(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex ErrorLine = new Regex(@"^\w*Error: ", RegexOptions.Compiled);
    private static readonly Regex ModuleHint = new Regex("import|export|module|await", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineScript = new Regex(@"<script\b([^>]*)>([\s\S]*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourceAttribute = new Regex(@"\bsrc\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TypeAttribute = new Regex(@"\btype\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PythonLine = new Regex(@"line (\d+)", RegexOptions.Compiled);
    private static readonly Regex PythonErrorPrefix = new Regex(@"^.*?Error: ?", RegexOptions.Compiled);

    private readonly object gate = new object();
    private bool probedNode;
    private string? nodeExecutable;
    private bool probedPython;
    private string? pythonExecutable;

    public string? Check(string absolutePath, string relativePath)
    {
        try
        {
            string extension = Path.GetExtension(absolutePath).ToLowerInvariant();
            if (!CheckedExtensions.Contains(extension))
            {
                return null;
            }
            if (new FileInfo(absolutePath).Length > MaxCheckBytes)
            {
                return null;
            }
            if (extension == ".py")
            {
                return CheckPython(absolutePath);
            }
            string source = File.ReadAllText(absolutePath, Encoding.UTF8);
            if (extension == ".json")
            {
                try
                {
                    using (JsonDocument.Parse(source))
                    {
                    }
                    return null;
                }
                catch (JsonException exception)
                {
                    return relativePath + " is not valid JSON: " + exception.Message;
                }
            }
            if (extension == ".html" || extension == ".htm")
            {
                string? message = CheckHtml(source);
                if (message == null)
                {
                    return null;
                }
                return relativePath + ": " + message;
            }
            JavaScriptError? error = CheckJavaScript(source, extension == ".mjs");
            if (error == null)
            {
                return null;
            }
            string at = string.Empty;
            if (error.Line.HasValue)
            {
                at = " at line " + error.Line.Value;
            }
            return relativePath + " has a JavaScript syntax error" + at + ": " + error.Message;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? CheckHtml(string source)
    {
        foreach (Match match in InlineScript.Matches(source))
        {
            string attributes = match.Groups[1].Value;
            if (SourceAttribute.IsMatch(attributes))
            {
                continue;
            }
            string? type = null;
            Match typeMatch = TypeAttribute.Match(attributes);
            if (typeMatch.Success)
            {
                type = typeMatch.Groups[1].Value.ToLowerInvariant();
            }
            if (type != null && type != "module" && type != "text/javascript" && type != "application/javascript")
            {
                continue;
            }
            string body = match.Groups[2].Value;
            if (body.Trim().Length == 0)
            {
                continue;
            }
            JavaScriptError? error = CheckJavaScript(body, type == "module");
            if (error != null)
            {
                int bodyStart = match.Groups[2].Index;
                int startLine = source.Substring(0, bodyStart).Split('\n').Length;
                string at = string.Empty;
                if (error.Line.HasValue)
                {
                    at = " at line " + (startLine + error.Line.Value - 1);
                }
                return "an inline <script> has a JavaScript syntax error" + at + ": " + error.Message;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks as a classic script first (like a browser script tag), then as a module
    /// when the error is about import or export.
    /// </summary>
    private JavaScriptError? CheckJavaScript(string source, bool forceModule)
    {
        string? node = FindNode();
        if (node == null)
        {
            return null;
        }
        string directory = Path.Combine(Path.GetTempPath(), "smolcoder-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (forceModule)
            {
                return TryJavaScript(node, directory, source, ".mjs");
            }
            JavaScriptError? classic = TryJavaScript(node, directory, source, ".cjs");
            if (classic == null)
            {
                return null;
            }
            if (ModuleHint.IsMatch(classic.Message))
            {
                return TryJavaScript(node, directory, source, ".mjs");
            }
            return classic;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // Ignore.
            }
        }
    }

    private static JavaScriptError? TryJavaScript(string node, string directory, string source, string extension)
    {
        string temporaryFile = Path.Combine(directory, "snippet" + extension);
        File.WriteAllText(temporaryFile, source, new UTF8Encoding(false));
        ExternalToolResult? result = ExternalTool.Run(node, new string[] { "--check", temporaryFile }, CheckTimeout);
        if (result == null || result.ExitCode == 0)
        {
            return null;
        }
        JavaScriptError? parsed = ParseNodeError(result.StandardError, temporaryFile);
        if (parsed == null)
        {
            return new JavaScriptError(null, "syntax error");
        }
        return parsed;
    }

    private static JavaScriptError? ParseNodeError(string standardError, string temporaryFile)
    {
        string[] lines = LineBreak.Split(standardError);
        int? lineNumber = null;
        string? location = lines.FirstOrDefault(line => line.Contains(temporaryFile));
        if (location != null)
        {
            Match match = TrailingLineNumber.Match(location.Trim());
            if (match.Success)
            {
                lineNumber = int.Parse(match.Groups[1].Value);
            }
        }
        string? errorLine = lines.FirstOrDefault(line => ErrorLine.IsMatch(line));
        if (errorLine == null)
        {
            return null;
        }
        string message = errorLine;
        if (message.StartsWith("SyntaxError: ", StringComparison.Ordinal))
        {
            message = message.Substring("SyntaxError: ".Length);
        }
        return new JavaScriptError(lineNumber, message);
    }

    private string? CheckPython(string absolutePath)
    {
        string? python = FindPython();
        if (python == null)
        {
            return null;
        }
        ExternalToolResult? result = ExternalTool.Run(
            python,
            new string[] { "-I", "-c", "import sys; compile(open(sys.argv[1], 'rb').read(), sys.argv[1], 'exec')", absolutePath },
            CheckTimeout);
        if (result == null || result.ExitCode == 0)
        {
            return null;
        }
        string text = result.StandardError;
        if (text.Trim().Length == 0)
        {
            text = result.StandardOutput;
        }
        string[] lines = LineBreak.Split(text.Trim());
        string? lineNumber = null;
        foreach (string line in lines)
        {
            Match match = PythonLine.Match(line);
            if (match.Success)
            {
                lineNumber = match.Groups[1].Value;
                break;
            }
        }
        string last = "syntax error";
        if (lines.Length > 0 && lines[lines.Length - 1].Length > 0)
        {
            last = lines[lines.Length - 1];
        }
        string at = string.Empty;
        if (lineNumber != null)
        {
            at = " at line " + lineNumber;
        }
        return "Python syntax error" + at + ": " + PythonErrorPrefix.Replace(last, string.Empty, 1);
    }

    private string? FindNode()
    {
        lock (gate)
        {
            if (!probedNode)
            {
                probedNode = true;
                ExternalToolResult? result = ExternalTool.Run("node", new string[] { "-e", "process.stdout.write('1')" }, TimeSpan.FromSeconds(5));
                if (result != null && result.ExitCode == 0 && result.StandardOutput.Trim() == "1")
                {
                    nodeExecutable = "node";
                }
            }
            return nodeExecutable;
        }
    }

    private string? FindPython()
    {
        lock (gate)
        {
            if (!probedPython)
            {
                probedPython = true;
                string[] candidates;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    candidates = new string[] { "python", "py" };
                }
                else
                {
                    candidates = new string[] { "python3", "python" };
                }
                foreach (string candidate in candidates)
                {
                    ExternalToolResult? result = ExternalTool.Run(candidate, new string[] { "-I", "-c", "print(1)" }, TimeSpan.FromSeconds(5));
                    if (result != null && result.ExitCode == 0 && result.StandardOutput.Trim() == "1")
                    {
                        pythonExecutable = candidate;
                        break;
                    }
                }
            }
            return pythonExecutable;
        }
    }

    private sealed class JavaScriptError
    {
        public JavaScriptError(int? line, string message)
        {
            Line = line;
            Message = message;
        }

        public int? Line { get; }

        public string Message { get; }
    }
}
