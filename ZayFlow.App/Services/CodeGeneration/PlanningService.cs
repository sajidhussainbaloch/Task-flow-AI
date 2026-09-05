using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Intelligence;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Planning Layer — converts a <see cref="NormalizedBrief"/> into a structured
/// <see cref="ImplementationPlan"/> with ordered steps, dependencies, and verification objectives.
/// </summary>
public sealed class PlanningService
{
    private readonly OpenRouterProvider _openRouterProvider;
    private readonly ILogger<PlanningService> _logger;

    public PlanningService(OpenRouterProvider openRouterProvider, ILogger<PlanningService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    public async Task<ImplementationPlan> PlanAsync(
        NormalizedBrief brief,
        string model,
        CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(brief);

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt, userPrompt, model, Array.Empty<AssistantAttachment>(), ct)
            .ConfigureAwait(false);

        var plan = ParsePlan(result, brief);
        _logger.LogInformation("Planning layer produced plan: Scope={Scope}, Steps={StepCount}, ChangeType={ChangeType}",
            plan.Scope, plan.Steps.Count, plan.ChangeType);

        return plan;
    }

    private static string BuildSystemPrompt()
    {
        return """
You are the Planning Layer of a code-generation engine. You receive a structured brief and produce an implementation plan.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON; no extra text.
2. Determine the scope: what is being built and at what scale.
3. Classify the change type: single-file or multi-file.
4. Break the work into ordered steps. Each step targets one file.
5. Identify dependencies between steps.
6. List edge cases the implementation should handle.
7. Define verification objectives.

## CRITICAL DECISION: single-file vs multi-file

### USE single-file (DEFAULT — use this unless multi-file is REQUIRED)
single-file means: 1 step, 1 file, ALL code inside that file.

These are ALL single-file — even with GUI:
- Python anything: tkinter GUI, pygame, flask server, CLI tool, script, bot — Python can do everything in 1 file
- HTML page (inline CSS + JS in one .html file)
- JavaScript/Node.js: server, tool, bot — 1 file
- C/C++ program — 1 file
- Java program — 1 file (one class with main)
- Go program — 1 file
- Rust program — 1 file
- Any "app", "tool", "game", "calculator", "GUI app" in Python/JS/HTML = 1 file

KEY INSIGHT: "GUI" does NOT mean multi-file. A Python tkinter app with 10 buttons, menus, and dialogs is still ONE .py file. A pygame game is ONE .py file. An HTML page with CSS and JavaScript is ONE .html file.

### USE multi-file ONLY when the framework literally cannot run without multiple files:
- Flutter (REQUIRES a real project scaffold, not just pubspec.yaml + lib/main.dart. Plan for pubspec.yaml, lib/main.dart, and enough supporting files for a useful starter scaffold such as README.md, test/, and analysis options when appropriate. The execution layer will hydrate platform folders after creation.)
- React/Next.js (REQUIRES package.json + src/ structure)
- Angular (REQUIRES multiple config + component files)
- C# WPF/WinForms (REQUIRES .csproj + .xaml + .cs)
- Android Studio (REQUIRES gradle + manifest + java/kotlin)
- iOS/Swift project (REQUIRES xcodeproj structure)
- Django project (REQUIRES settings.py + urls.py + views.py)
- The user EXPLICITLY says "create a project with multiple files/modules"

For multi-file plans:
- Include "projectName" in parameters (e.g. "my_flutter_app").
- Each targetFile uses relative paths (e.g. "lib/main.dart", "pubspec.yaml").
- Order: config files first, then models, then UI.
- For Flutter, NEVER return only two files unless the user explicitly asks for the smallest possible minimal example.
- Prefer a useful starter scaffold over a toy scaffold.

## ASK YOURSELF BEFORE CHOOSING multi-file:
"Can this ENTIRE program run from a single file in this language?" If YES → single-file.

## RESPONSE FORMAT (strict JSON — you MUST use this exact structure):
{"intent":"plan","message":"<brief summary>","parameters":{"scope":"<description>","changeType":"<single-file|multi-file|blocked>","projectName":"<folder name, only for multi-file>","steps":[{"order":1,"description":"...","targetFile":"...","operation":"create|edit","dependsOn":[],"rationale":"..."}],"dependencies":["..."],"edgeCases":["..."],"verificationObjectives":["..."]}}
""";
    }

    private static string BuildUserPrompt(NormalizedBrief brief)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Normalized Brief");
        sb.AppendLine($"**Goal:** {brief.Goal}");
        sb.AppendLine($"**Artifact Type:** {brief.ArtifactType}");
        sb.AppendLine($"**Language:** {brief.Language}");
        sb.AppendLine($"**Risk Level:** {brief.RiskLevel}");

        // ── Phase 3: inject blueprint context for precise planning ────────────────
        if (brief.Blueprint != null && brief.Blueprint.Complexity != ProjectComplexity.SingleFile)
        {
            sb.AppendLine();
            sb.AppendLine("## PROJECT BLUEPRINT (from Project Intelligence — follow this exactly)");
            sb.AppendLine($"**Framework:** {(brief.Blueprint.IsKnownFramework ? brief.Blueprint.Framework?.DisplayName ?? "known" : "unknown")}");
            sb.AppendLine($"**Complexity:** {brief.Blueprint.Complexity}");
            sb.AppendLine($"**Change Type:** multi-file");
            sb.AppendLine();

            sb.AppendLine("### REQUIRED Files (you MUST create a step for each):");
            foreach (var file in brief.Blueprint.FileManifest.Where(f => f.Required))
            {
                sb.AppendLine($"- **{file.RelativePath}** — {file.Description}");
                if (file.ContentMarkers.Count > 0)
                    sb.AppendLine($"  Must contain: {string.Join(", ", file.ContentMarkers)}");
            }

            var recommended = brief.Blueprint.FileManifest.Where(f => !f.Required).ToList();
            if (recommended.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### RECOMMENDED Files (create these for a useful starter scaffold):");
                foreach (var file in recommended)
                {
                    sb.AppendLine($"- **{file.RelativePath}** — {file.Description}");
                }
            }

            if (brief.Blueprint.AcceptanceCriteria.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Acceptance Criteria:");
                foreach (var ac in brief.Blueprint.AcceptanceCriteria)
                    sb.AppendLine($"- {ac}");
            }

            if (!string.IsNullOrWhiteSpace(brief.Blueprint.BootstrapCommand))
                sb.AppendLine($"\n**Bootstrap command:** `{brief.Blueprint.BootstrapCommand}`");
            if (!string.IsNullOrWhiteSpace(brief.Blueprint.RunCommand))
                sb.AppendLine($"**Run command:** `{brief.Blueprint.RunCommand}`");

            sb.AppendLine();
            sb.AppendLine("CRITICAL: Create one plan step per file listed above. Use the EXACT relative paths shown. Do NOT skip required files.");
        }

        if (brief.UserConstraints.Count > 0)
        {
            sb.AppendLine("**User Constraints:**");
            foreach (var c in brief.UserConstraints) sb.AppendLine($"- {c}");
        }

        if (brief.InferredDefaults.Count > 0)
        {
            sb.AppendLine("**Inferred Defaults:**");
            foreach (var d in brief.InferredDefaults) sb.AppendLine($"- {d}");
        }

        if (brief.Assumptions.Count > 0)
        {
            sb.AppendLine("**Assumptions:**");
            foreach (var a in brief.Assumptions) sb.AppendLine($"- {a}");
        }

        if (brief.AcceptanceCriteria.Count > 0)
        {
            sb.AppendLine("**Acceptance Criteria:**");
            foreach (var ac in brief.AcceptanceCriteria) sb.AppendLine($"- {ac}");
        }

        if (brief.RelevantContext.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Relevant Code Context");
            foreach (var (path, content) in brief.RelevantContext)
            {
                sb.AppendLine($"### {path}");
                sb.AppendLine("```");
                sb.AppendLine(content.Length > 1500 ? content[..1500] + "\n... (truncated)" : content);
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    private static ImplementationPlan ParsePlan(AssistantTurnResult result, NormalizedBrief brief)
    {
        var plan = new ImplementationPlan { Brief = brief };

        if (result.Parameters.TryGetValue("scope", out var scope))
            plan.Scope = scope?.ToString() ?? string.Empty;
        else
            plan.Scope = brief.Goal;

        if (result.Parameters.TryGetValue("changeType", out var changeType))
            plan.ChangeType = changeType?.ToString() ?? "single-file";

        if (result.Parameters.TryGetValue("projectName", out var projName))
            plan.ProjectName = projName?.ToString() ?? string.Empty;

        plan.Dependencies = ExtractStringList(result.Parameters, "dependencies");
        plan.EdgeCases = ExtractStringList(result.Parameters, "edgeCases");
        plan.VerificationObjectives = ExtractStringList(result.Parameters, "verificationObjectives");

        var stepsElement = ResolveJsonElement(result.Parameters, "steps");
        if (stepsElement.HasValue && stepsElement.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            int order = 1;
            foreach (var stepEl in stepsElement.Value.EnumerateArray())
            {
                var step = new PlanStep
                {
                    Order = stepEl.TryGetProperty("order", out var o) ? o.GetInt32() : order,
                    Description = stepEl.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                    TargetFile = stepEl.TryGetProperty("targetFile", out var tf) ? tf.GetString() ?? string.Empty : string.Empty,
                    Operation = stepEl.TryGetProperty("operation", out var op) ? op.GetString() ?? "create" : "create",
                    Rationale = stepEl.TryGetProperty("rationale", out var r) ? r.GetString() ?? string.Empty : string.Empty
                };

                if (stepEl.TryGetProperty("dependsOn", out var deps) && deps.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var dep in deps.EnumerateArray())
                        step.DependsOn.Add(dep.GetString() ?? string.Empty);
                }

                plan.Steps.Add(step);
                order++;
            }
        }

        // Fallback: if AI didn't return steps, create a single step from the brief
        if (plan.Steps.Count == 0)
        {
            plan.Steps.Add(new PlanStep
            {
                Order = 1,
                Description = brief.Goal,
                TargetFile = string.Empty,
                Operation = "create",
                Rationale = "Single-step fallback from brief"
            });
        }

        return plan;
    }

    /// <summary>
    /// Resolves a parameter value to a JsonElement, handling both direct JsonElement
    /// and raw JSON text strings (from ParseParameters).
    /// </summary>
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

        // ParseParameters stores arrays/objects as raw JSON text — re-parse
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
}
