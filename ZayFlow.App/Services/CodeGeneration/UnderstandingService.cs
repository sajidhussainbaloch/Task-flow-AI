using System.IO;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Understanding Layer â€” converts a raw user request + workspace context
/// into a <see cref="NormalizedBrief"/> with explicit goal, constraints,
/// assumptions, and acceptance criteria.
/// </summary>
public sealed class UnderstandingService
{
    private readonly CloudflareProvider _openRouterProvider;
    private readonly ILogger<UnderstandingService> _logger;

    public UnderstandingService(CloudflareProvider openRouterProvider, ILogger<UnderstandingService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    public async Task<NormalizedBrief> AnalyzeAsync(
        string userMessage,
        WorkspaceContextSnapshot snapshot,
        string model,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(userMessage, snapshot);

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt, userPrompt, model, Array.Empty<AssistantAttachment>(), ct)
            .ConfigureAwait(false);

        var brief = ParseBrief(result, userMessage, snapshot);
        _logger.LogInformation("Understanding layer produced brief: Goal={Goal}, ArtifactType={ArtifactType}, Risk={Risk}",
            brief.Goal, brief.ArtifactType, brief.RiskLevel);

        return brief;
    }

    private static string BuildSystemPrompt()
    {
        return """
You are the Understanding Layer of a code-generation engine. Your job is to analyze a user's coding request and produce a structured brief.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON; no extra text.
2. Extract the user's goal into a single clear sentence.
3. Identify the target artifact type (file, class, method, refactor, test, module, etc.).
4. Identify the programming language (infer from context if not explicit).
5. List explicit user constraints (things they specifically asked for or ruled out).
6. List inferred defaults you're applying (coding conventions, error handling style, etc.).
7. List assumptions you're making that could be wrong â€” these will be verified later.
8. Derive acceptance criteria â€” what must be true for the output to be correct.
9. Assess risk level: low (new file, isolated), medium (edits existing code), high (destructive, multi-file).

## RESPONSE FORMAT (strict JSON â€” you MUST use this exact structure):
{"intent":"understand","message":"<brief summary>","parameters":{"goal":"<one-sentence goal>","artifactType":"<file|class|method|refactor|test|module>","language":"<language>","userConstraints":["..."],"inferredDefaults":["..."],"assumptions":["..."],"acceptanceCriteria":["..."],"riskLevel":"<low|medium|high>"}}
""";
    }

    private static string BuildUserPrompt(string userMessage, WorkspaceContextSnapshot snapshot)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## User Request");
        sb.AppendLine(userMessage);

        if (snapshot.RelativeFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Workspace Files (for context)");
            foreach (var file in snapshot.RelativeFiles.Take(20))
                sb.AppendLine($"- {file}");
        }

        if (snapshot.FileContents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Relevant File Contents");
            foreach (var (path, content) in snapshot.FileContents.Take(3))
            {
                sb.AppendLine($"### {path}");
                sb.AppendLine("```");
                sb.AppendLine(content.Length > 800 ? content[..800] + "\n... (truncated)" : content);
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    private static NormalizedBrief ParseBrief(AssistantTurnResult result, string userMessage, WorkspaceContextSnapshot snapshot)
    {
        var brief = new NormalizedBrief
        {
            OriginalRequest = userMessage,
            WorkspaceRoot = snapshot.RootPath,
            RepositoryContext = RepositoryContextInfo.FromSnapshot(snapshot)
        };

        // Extract fields from the AI response parameters
        if (result.Parameters.TryGetValue("goal", out var goal))
            brief.Goal = goal?.ToString() ?? string.Empty;
        else
            brief.Goal = result.Message;

        if (result.Parameters.TryGetValue("artifactType", out var artifactType))
            brief.ArtifactType = artifactType?.ToString() ?? "file";

        if (result.Parameters.TryGetValue("language", out var language))
            brief.Language = language?.ToString() ?? string.Empty;

        if (result.Parameters.TryGetValue("riskLevel", out var risk))
            brief.RiskLevel = risk?.ToString() ?? "low";

        brief.UserConstraints = ExtractStringList(result.Parameters, "userConstraints");
        brief.InferredDefaults = ExtractStringList(result.Parameters, "inferredDefaults");
        brief.Assumptions = ExtractStringList(result.Parameters, "assumptions");
        brief.AcceptanceCriteria = ExtractStringList(result.Parameters, "acceptanceCriteria");
        brief.MissingDetails = ExtractStringList(result.Parameters, "missingDetails");

        // Carry workspace context forward
        foreach (var (path, content) in snapshot.FileContents)
            brief.RelevantContext[path] = content;

        // â”€â”€ Phase 2: detect edit target â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // If the workspace snapshot contains files and the request names one of them
        // (by extension or by filename match), populate EditTargetFile and read its content.
        brief.EditTargetFile = DetectEditTarget(userMessage, snapshot);
        if (!string.IsNullOrWhiteSpace(brief.EditTargetFile))
        {
            brief.ExistingFileContext = ReadExistingFile(brief.EditTargetFile, snapshot);
            if (!string.IsNullOrWhiteSpace(brief.RiskLevel) &&
                string.Equals(brief.RiskLevel, "low", StringComparison.OrdinalIgnoreCase))
            {
                brief.RiskLevel = "medium"; // editing existing file always elevates risk
            }
        }

        return brief;
    }

    private static List<string> ExtractStringList(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value))
            return [];

        if (value is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in jsonElement.EnumerateArray())
                list.Add(item.GetString() ?? string.Empty);
            return list;
        }

        // ParseParameters stores arrays/objects as raw JSON text â€” re-parse
        if (value is string rawJson && rawJson.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawJson);
                if (parsed.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var item in parsed.EnumerateArray())
                        list.Add(item.GetString() ?? item.GetRawText());
                    return list;
                }
            }
            catch { /* fall through */ }
        }

        if (value is IEnumerable<object> enumerable)
            return enumerable.Select(x => x.ToString() ?? string.Empty).ToList();

        return [];
    }

    // â”€â”€ Phase 2: edit-target detection helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Scans the workspace's known files and the user message to identify a candidate edit target.
    /// Returns the matching file path, or empty string when none is found.
    /// </summary>
    private static string DetectEditTarget(string userMessage, WorkspaceContextSnapshot snapshot)
    {
        if (snapshot.RelativeFiles.Count == 0) return string.Empty;

        var msgLower = userMessage.ToLowerInvariant();

        // Look for exact filename mentions first
        foreach (var relPath in snapshot.RelativeFiles)
        {
            var fileName = Path.GetFileName(relPath);
            if (msgLower.Contains(fileName.ToLowerInvariant()) && !string.IsNullOrWhiteSpace(fileName))
                return Path.Combine(snapshot.RootPath, relPath).Replace('/', Path.DirectorySeparatorChar);
        }

        // Look for edit-intent keywords that suggest an existing file
        var editKeywords = new[] { "edit ", "modify ", "update ", "change ", "fix ", "add to ", "refactor " };
        bool isEditIntent = editKeywords.Any(kw => msgLower.Contains(kw));
        if (!isEditIntent) return string.Empty;

        // Find the most-recently-relevant file from FileContents (the workspace service pre-selects these)
        var firstContextFile = snapshot.FileContents.Keys.FirstOrDefault();
        if (firstContextFile != null)
        {
            var candidate = Path.IsPathRooted(firstContextFile)
                ? firstContextFile
                : Path.Combine(snapshot.RootPath, firstContextFile);
            if (File.Exists(candidate)) return candidate;
        }

        return string.Empty;
    }

    /// <summary>
    /// Reads the existing file on disk and wraps it in an <see cref="ExistingFileContext"/>.
    /// Silent on I/O failure â€” returns an unreadable entry.
    /// </summary>
    private static ExistingFileContext ReadExistingFile(string absolutePath, WorkspaceContextSnapshot snapshot)
    {
        var ctx = new ExistingFileContext
        {
            FilePath = absolutePath,
            Language = InferLanguage(absolutePath)
        };

        // Return cached content if the workspace snapshot already loaded this file
        var relPath = snapshot.RootPath.Length > 0 && absolutePath.StartsWith(snapshot.RootPath, StringComparison.OrdinalIgnoreCase)
            ? absolutePath.Substring(snapshot.RootPath.Length).TrimStart(Path.DirectorySeparatorChar, '/')
            : absolutePath;

        if (snapshot.FileContents.TryGetValue(relPath, out var cached) ||
            snapshot.FileContents.TryGetValue(absolutePath, out cached))
        {
            ctx.Content = cached;
            ctx.LineCount = cached.Split('\n').Length;
            ctx.IsReadable = true;
            ctx.LastModified = File.Exists(absolutePath) ? File.GetLastWriteTimeUtc(absolutePath) : null;
            return ctx;
        }

        // Fall back to direct read (small files only)
        try
        {
            if (File.Exists(absolutePath))
            {
                var fi = new FileInfo(absolutePath);
                if (fi.Length <= 128 * 1024) // 128 KB limit
                {
                    ctx.Content = File.ReadAllText(absolutePath);
                    ctx.LineCount = ctx.Content.Split('\n').Length;
                    ctx.IsReadable = true;
                    ctx.LastModified = fi.LastWriteTimeUtc;
                }
            }
        }
        catch { /* leave IsReadable = false */ }

        return ctx;
    }

    private static string InferLanguage(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".py" => "python",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".js" => "javascript",
            ".jsx" => "javascript",
            ".java" => "java",
            ".go" => "go",
            ".rs" => "rust",
            ".cpp" or ".cc" or ".cxx" => "cpp",
            ".c" => "c",
            ".h" => "c",
            ".kt" => "kotlin",
            ".swift" => "swift",
            ".rb" => "ruby",
            ".php" => "php",
            ".dart" => "dart",
            ".xml" => "xml",
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".xaml" => "xml",
            ".sql" => "sql",
            ".sh" => "bash",
            ".ps1" => "powershell",
            _ => "text"
        };
    }
}
