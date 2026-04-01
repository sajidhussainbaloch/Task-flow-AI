using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Templates;

namespace ZayFlow.App.Services.CodeGeneration.Intelligence;

/// <summary>
/// Project Intelligence Service â€” analyzes any request to produce a <see cref="ProjectBlueprint"/>.
/// For known frameworks â†’ uses <see cref="FrameworkTemplateCatalog"/> for concrete file manifests.
/// For unknown projects â†’ falls back to AI-driven analysis.
/// Replaces the hardcoded RequiresMultiFile() logic in the orchestrator.
/// </summary>
public sealed class ProjectIntelligenceService
{
    private readonly FrameworkTemplateCatalog _catalog;
    private readonly CloudflareProvider _openRouterProvider;
    private readonly ILogger<ProjectIntelligenceService> _logger;

    public ProjectIntelligenceService(
        FrameworkTemplateCatalog catalog,
        CloudflareProvider openRouterProvider,
        ILogger<ProjectIntelligenceService> logger)
    {
        _catalog = catalog;
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    /// <summary>
    /// Analyze a normalized brief and produce a project blueprint.
    /// Returns a blueprint with concrete file manifests for known frameworks,
    /// or an AI-inferred blueprint for unknown project types.
    /// </summary>
    public async Task<ProjectBlueprint> AnalyzeAsync(
        NormalizedBrief brief,
        string model,
        CancellationToken ct = default)
    {
        var language = brief.Language?.ToLowerInvariant() ?? string.Empty;
        var goal = brief.Goal?.ToLowerInvariant() ?? string.Empty;

        // â”€â”€ Step 1: Try known framework detection via catalog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var template = _catalog.TryDetect(language, goal);

        if (template != null)
        {
            _logger.LogInformation("ProjectIntelligence: detected known framework {FrameworkId} from language={Language}, goal excerpt='{GoalExcerpt}'",
                template.FrameworkId, language, goal.Length > 80 ? goal[..80] : goal);

            return ProjectBlueprint.FromTemplate(template, InferProjectName(brief));
        }

        // â”€â”€ Step 2: Check if this is clearly a single-file request â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (IsSingleFileRequest(language, goal))
        {
            var ext = GetExtensionForLanguage(language);
            var fileName = InferFileName(brief, ext);
            _logger.LogInformation("ProjectIntelligence: classified as single-file ({Language}): {FileName}", language, fileName);
            return ProjectBlueprint.SingleFile(language, fileName);
        }

        // â”€â”€ Step 3: AI-driven analysis for unknown multi-file projects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _logger.LogInformation("ProjectIntelligence: no known framework match, falling back to AI analysis");
        return await AnalyzeWithAiAsync(brief, model, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Quick check: does this request require multi-file output?
    /// Uses the catalog and heuristics â€” no AI call needed.
    /// </summary>
    public bool RequiresMultiFile(NormalizedBrief brief)
    {
        var language = brief.Language?.ToLowerInvariant() ?? string.Empty;
        var goal = brief.Goal?.ToLowerInvariant() ?? string.Empty;

        // Known framework â†’ always multi-file
        if (_catalog.TryDetect(language, goal) != null)
            return true;

        // Explicit multi-file keywords
        if (goal.Contains("project") || goal.Contains("multiple file") || goal.Contains("module"))
            return true;

        return false;
    }

    private async Task<ProjectBlueprint> AnalyzeWithAiAsync(
        NormalizedBrief brief,
        string model,
        CancellationToken ct)
    {
        var systemPrompt = BuildAiSystemPrompt();
        var userPrompt = BuildAiUserPrompt(brief);

        try
        {
            var result = await _openRouterProvider.SendStructuredTurnAsync(
                systemPrompt, userPrompt, model, Array.Empty<AssistantAttachment>(), ct)
                .ConfigureAwait(false);

            return ParseAiBlueprint(result, brief);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI blueprint analysis failed, falling back to single-file");
            var ext = GetExtensionForLanguage(brief.Language ?? "text");
            return ProjectBlueprint.SingleFile(brief.Language ?? "text", $"app.{ext}");
        }
    }

    private static string BuildAiSystemPrompt()
    {
        return """
You are the Project Intelligence Layer of a code-generation engine. You analyze a coding request and determine the project structure.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON; no extra text.
2. Determine if this is a single-file or multi-file project.
3. For multi-file: list all required files with their relative paths and descriptions.
4. Estimate project complexity: single-file, multi-file, or full-project.
5. List expected folders, dependencies, and acceptance criteria.

## RESPONSE FORMAT (strict JSON):
{"intent":"blueprint","message":"<summary>","parameters":{"complexity":"single-file|multi-file|full-project","projectType":"<type>","language":"<language>","files":[{"relativePath":"<path>","description":"<desc>","required":true,"language":"<lang>"}],"expectedFolders":["<folder>"],"dependencies":["<dep>"],"acceptanceCriteria":["<criteria>"],"bootstrapCommand":"<cmd>","runCommand":"<cmd>","testCommand":"<cmd>"}}
""";
    }

    private static string BuildAiUserPrompt(NormalizedBrief brief)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Request Analysis");
        sb.AppendLine($"**Goal:** {brief.Goal}");
        sb.AppendLine($"**Language:** {brief.Language}");
        sb.AppendLine($"**Artifact Type:** {brief.ArtifactType}");

        if (brief.UserConstraints.Count > 0)
        {
            sb.AppendLine("**Constraints:**");
            foreach (var c in brief.UserConstraints) sb.AppendLine($"- {c}");
        }

        return sb.ToString();
    }

    private static ProjectBlueprint ParseAiBlueprint(AssistantTurnResult result, NormalizedBrief brief)
    {
        var blueprint = new ProjectBlueprint
        {
            IsKnownFramework = false,
            Language = brief.Language ?? "text",
            ProjectType = brief.ArtifactType ?? "application",
        };

        if (result.Parameters.TryGetValue("complexity", out var complexity))
        {
            blueprint.Complexity = (complexity?.ToString()?.ToLowerInvariant()) switch
            {
                "multi-file" => ProjectComplexity.MultiFile,
                "full-project" => ProjectComplexity.FullProject,
                _ => ProjectComplexity.SingleFile,
            };
        }

        if (result.Parameters.TryGetValue("projectType", out var projType))
            blueprint.ProjectType = projType?.ToString() ?? blueprint.ProjectType;

        if (result.Parameters.TryGetValue("language", out var lang))
            blueprint.Language = lang?.ToString() ?? blueprint.Language;

        // Parse files
        var filesElement = ResolveJsonElement(result.Parameters, "files");
        if (filesElement.HasValue && filesElement.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var fEl in filesElement.Value.EnumerateArray())
            {
                blueprint.FileManifest.Add(new BlueprintFile
                {
                    RelativePath = fEl.TryGetProperty("relativePath", out var rp) ? rp.GetString() ?? "" : "",
                    Description = fEl.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    Required = fEl.TryGetProperty("required", out var req) && req.GetBoolean(),
                    Language = fEl.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "",
                });
            }
        }

        // Parse simple string lists
        blueprint.ExpectedFolders = ExtractStringList(result.Parameters, "expectedFolders");
        blueprint.Dependencies = ExtractStringList(result.Parameters, "dependencies");
        blueprint.AcceptanceCriteria = ExtractStringList(result.Parameters, "acceptanceCriteria");

        if (result.Parameters.TryGetValue("bootstrapCommand", out var bc))
            blueprint.BootstrapCommand = bc?.ToString() ?? "";
        if (result.Parameters.TryGetValue("runCommand", out var rc))
            blueprint.RunCommand = rc?.ToString() ?? "";
        if (result.Parameters.TryGetValue("testCommand", out var tc))
            blueprint.TestCommand = tc?.ToString() ?? "";

        blueprint.EstimatedFileCount = blueprint.FileManifest.Count > 0
            ? blueprint.FileManifest.Count
            : 1;

        return blueprint;
    }

    private static bool IsSingleFileRequest(string language, string goal)
    {
        // Languages that almost always produce single-file output
        var singleFileLangs = new[] { "python", "c", "cpp", "c++", "java", "go", "rust", "ruby", "php", "perl", "lua", "r", "matlab", "bash", "powershell" };

        // Unless the goal explicitly mentions project/module/multi-file
        if (goal.Contains("project") || goal.Contains("multiple file") || goal.Contains("module") || goal.Contains("scaffold"))
            return false;

        foreach (var lang in singleFileLangs)
        {
            if (language.Contains(lang))
                return true;
        }

        // HTML / CSS / JS single page
        if ((language.Contains("html") || language.Contains("css")) && !goal.Contains("react") && !goal.Contains("vue") && !goal.Contains("angular"))
            return true;

        return false;
    }

    private static string InferProjectName(NormalizedBrief brief)
    {
        var goal = brief.Goal ?? "";
        // Try to extract a project name from phrases like "create a todo app" â†’ "todo_app"
        var words = goal.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameWords = new List<string>();

        bool capture = false;
        foreach (var word in words)
        {
            var lower = word.ToLowerInvariant().Trim(',', '.', '!', '?');
            if (lower is "create" or "build" or "make" or "generate" or "develop")
            {
                capture = true;
                continue;
            }
            if (capture && lower is not "a" and not "an" and not "the" and not "new" and not "simple" and not "basic")
            {
                nameWords.Add(lower);
                if (nameWords.Count >= 3) break;
            }
        }

        if (nameWords.Count > 0)
            return string.Join("_", nameWords).Replace(" ", "_");

        return "my_project";
    }

    private static string InferFileName(NormalizedBrief brief, string ext)
    {
        var goal = brief.Goal?.ToLowerInvariant() ?? "";
        if (goal.Contains("calculator")) return $"calculator.{ext}";
        if (goal.Contains("game")) return $"game.{ext}";
        if (goal.Contains("server")) return $"server.{ext}";
        if (goal.Contains("bot")) return $"bot.{ext}";
        if (goal.Contains("scraper")) return $"scraper.{ext}";
        if (goal.Contains("test")) return $"test.{ext}";
        return $"app.{ext}";
    }

    private static string GetExtensionForLanguage(string language) => language.ToLowerInvariant() switch
    {
        "python" => "py",
        "javascript" => "js",
        "typescript" => "ts",
        "html" => "html",
        "csharp" or "c#" => "cs",
        "java" => "java",
        "go" => "go",
        "rust" => "rs",
        "c" => "c",
        "c++" or "cpp" => "cpp",
        "ruby" => "rb",
        "php" => "php",
        "swift" => "swift",
        "kotlin" => "kt",
        "dart" => "dart",
        _ => "txt"
    };

    private static List<string> ExtractStringList(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value))
            return [];

        if (value is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return je.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        return [];
    }

    private static System.Text.Json.JsonElement? ResolveJsonElement(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value))
            return null;

        if (value is System.Text.Json.JsonElement je)
            return je;

        if (value is string rawJson && rawJson.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawJson); }
            catch { return null; }
        }

        return null;
    }
}
