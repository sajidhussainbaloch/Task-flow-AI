using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 7: Developer tools — git quick, api test, code format, qr code.
/// </summary>
public class DevToolsHandler
{
    private readonly ILogger<DevToolsHandler> _logger;

    public DevToolsHandler(ILogger<DevToolsHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>git_quick — Quick git operations (status, log, diff, branch).</summary>
    public async Task<ActionResult> ExecuteGitQuickAsync(string resolvedPath, string command, CancellationToken ct)
    {
        if (!Directory.Exists(resolvedPath))
            return new ActionResult { Success = false, Message = $"Directory not found: {resolvedPath}" };

        var gitArgs = command.ToLowerInvariant() switch
        {
            "status" or "" => "status --short",
            "log" => "log --oneline -20",
            "branch" or "branches" => "branch -a",
            "diff" => "diff --stat",
            "remote" or "remotes" => "remote -v",
            "stash" or "stashes" => "stash list",
            "tags" => "tag -l",
            _ => command // pass-through for advanced users
        };

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = gitArgs,
                WorkingDirectory = resolvedPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            var output = await proc!.StandardOutput.ReadToEndAsync(ct);
            var error = await proc.StandardError.ReadToEndAsync(ct);
            proc.WaitForExit(10000);

            if (!string.IsNullOrWhiteSpace(error) && proc.ExitCode != 0)
                return new ActionResult { Success = false, Message = $"Git error:\n{error}" };

            var result = new StringBuilder();
            result.AppendLine($"🔀 git {gitArgs} (in {Path.GetFileName(resolvedPath)})\n");
            result.AppendLine(string.IsNullOrWhiteSpace(output) ? "(no output)" : output.Trim());

            return new ActionResult { Success = true, Message = result.ToString() };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ActionResult { Success = false, Message = "Git is not installed or not in PATH. Install Git from https://git-scm.com" };
        }
    }

    /// <summary>api_test — Simple HTTP request tester.</summary>
    public async Task<ActionResult> ExecuteApiTestAsync(string url, string method, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new ActionResult { Success = false, Message = "Please provide a URL to test." };

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var httpMethod = method.ToUpperInvariant() switch
            {
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => HttpMethod.Patch,
                "HEAD" => HttpMethod.Head,
                _ => HttpMethod.Get
            };

            var request = new HttpRequestMessage(httpMethod, url);
            if (!string.IsNullOrWhiteSpace(body) && httpMethod != HttpMethod.Get)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await client.SendAsync(request, ct);
            sw.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            if (responseBody.Length > 3000) responseBody = responseBody[..3000] + "\n... (truncated)";

            var sb = new StringBuilder();
            sb.AppendLine($"🌐 {httpMethod.Method} {url}");
            sb.AppendLine($"📊 Status: {(int)response.StatusCode} {response.StatusCode}");
            sb.AppendLine($"⏱️ Time: {sw.ElapsedMilliseconds}ms");
            sb.AppendLine($"📦 Size: {response.Content.Headers.ContentLength ?? responseBody.Length} bytes");
            sb.AppendLine($"📝 Content-Type: {response.Content.Headers.ContentType}");
            sb.AppendLine($"\n📄 Response:\n{responseBody}");

            return new ActionResult { Success = true, Message = sb.ToString() };
        }
        catch (Exception ex)
        {
            return new ActionResult { Success = false, Message = $"Request failed: {ex.Message}" };
        }
    }

    /// <summary>code_format — Basic code formatting and analysis.</summary>
    public async Task<ActionResult> ExecuteCodeFormatAsync(string resolvedPath, string action, CancellationToken ct)
    {
        if (!File.Exists(resolvedPath))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedPath}" };

        var content = await File.ReadAllTextAsync(resolvedPath, ct);
        var ext = Path.GetExtension(resolvedPath).ToLowerInvariant();
        var sb = new StringBuilder();

        switch (action.ToLowerInvariant())
        {
            case "analyze" or "stats" or "":
                var lines = content.Split('\n');
                var codeLines = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                var blankLines = lines.Length - codeLines;
                var commentLines = lines.Count(l =>
                    l.TrimStart().StartsWith("//") || l.TrimStart().StartsWith("#") ||
                    l.TrimStart().StartsWith("/*") || l.TrimStart().StartsWith("*"));

                sb.AppendLine($"📝 Code Analysis: {Path.GetFileName(resolvedPath)}\n");
                sb.AppendLine($"  Language:     {ExtToLanguage(ext)}");
                sb.AppendLine($"  Total lines:  {lines.Length}");
                sb.AppendLine($"  Code lines:   {codeLines}");
                sb.AppendLine($"  Comments:     {commentLines}");
                sb.AppendLine($"  Blank lines:  {blankLines}");
                sb.AppendLine($"  Characters:   {content.Length}");
                sb.AppendLine($"  Avg line len: {(lines.Length > 0 ? content.Length / lines.Length : 0)}");

                // Detect potential issues
                var longLines = lines.Count(l => l.Length > 120);
                var trailingWhitespace = lines.Count(l => l.EndsWith(" ") || l.EndsWith("\t"));
                var todos = lines.Count(l => l.Contains("TODO", StringComparison.OrdinalIgnoreCase) || l.Contains("FIXME", StringComparison.OrdinalIgnoreCase));

                if (longLines > 0 || trailingWhitespace > 0 || todos > 0)
                {
                    sb.AppendLine("\n⚠️ Issues:");
                    if (longLines > 0) sb.AppendLine($"  {longLines} lines > 120 chars");
                    if (trailingWhitespace > 0) sb.AppendLine($"  {trailingWhitespace} lines with trailing whitespace");
                    if (todos > 0) sb.AppendLine($"  {todos} TODO/FIXME markers");
                }
                break;

            case "trim":
                var trimmed = string.Join("\n", content.Split('\n').Select(l => l.TrimEnd()));
                await File.WriteAllTextAsync(resolvedPath, trimmed, ct);
                sb.AppendLine($"✅ Trimmed trailing whitespace from {Path.GetFileName(resolvedPath)}");
                break;

            default:
                return new ActionResult { Success = false, Message = "Use action: 'analyze' or 'trim'." };
        }

        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>qr_code — Generate a scannable QR code image.</summary>
    public Task<ActionResult> ExecuteQrCodeAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new ActionResult { Success = false, Message = "Please provide text or URL to encode." });

        try
        {
            // Save QR code PNG to temp folder
            var fileName = $"QR_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var savePath = Path.Combine(Path.GetTempPath(), "ZayFlow", "QRCodes", fileName);

            QrCodeGenerator.GenerateAndSave(text, savePath, 12);

            var dataLen = Encoding.UTF8.GetByteCount(text);
            var sb = new StringBuilder();
            sb.AppendLine($"📱 **QR Code Generated!**\n");
            sb.AppendLine($"![QR Code]({savePath})\n");
            sb.AppendLine($"**Encoded:** {(text.Length > 80 ? text[..80] + "..." : text)}");
            sb.AppendLine($"**Size:** {dataLen} bytes");
            sb.AppendLine($"**Saved to:** {savePath}");

            return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ActionResult { Success = false, Message = $"Failed to generate QR code: {ex.Message}" });
        }
    }

    private static string ExtToLanguage(string ext) => ext switch
    {
        ".cs" => "C#",
        ".py" => "Python",
        ".js" => "JavaScript",
        ".ts" => "TypeScript",
        ".java" => "Java",
        ".cpp" or ".cc" => "C++",
        ".c" => "C",
        ".go" => "Go",
        ".rs" => "Rust",
        ".rb" => "Ruby",
        ".php" => "PHP",
        ".swift" => "Swift",
        ".kt" => "Kotlin",
        ".html" or ".htm" => "HTML",
        ".css" => "CSS",
        ".sql" => "SQL",
        ".sh" => "Shell",
        ".ps1" => "PowerShell",
        _ => ext.TrimStart('.')
    };
}
