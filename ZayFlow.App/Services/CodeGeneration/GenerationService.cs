using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Intelligence;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Generation Layer — produces complete code implementations from the plan.
/// Outputs <see cref="GeneratedFile"/> artifacts with full, runnable code — no placeholders.
/// </summary>
public sealed class GenerationService
{
    private readonly OpenRouterProvider _openRouterProvider;
    private readonly ILogger<GenerationService> _logger;

    public GenerationService(OpenRouterProvider openRouterProvider, ILogger<GenerationService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    public async Task<List<GeneratedFile>> GenerateAsync(
        ImplementationPlan plan,
        string model,
        CancellationToken ct = default)
    {
        var files = new List<GeneratedFile>();

        foreach (var step in plan.Steps.OrderBy(s => s.Order))
        {
            var systemPrompt = BuildSystemPrompt(plan.Brief.Language);
            var userPrompt = BuildUserPrompt(plan, step, files);

            AssistantTurnResult result;
            try
            {
                result = await _openRouterProvider.SendStructuredTurnAsync(
                    systemPrompt, userPrompt, model, Array.Empty<AssistantAttachment>(), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generation failed for step {Step}", step.Order);
                throw new InvalidOperationException(
                    $"Code generation failed at step {step.Order}: {ex.Message}", ex);
            }

            var file = ParseGeneratedFile(result, step, plan.Brief.Language);
            files.Add(file);

            _logger.LogInformation("Generation layer produced file: {File} ({Language}, {Lines} lines)",
                file.RelativePath, file.Language, file.Content.Split('\n').Length);
        }

        return files;
    }

    private static string BuildSystemPrompt(string language)
    {
        return @"You are the Generation Layer of a code-generation engine. You produce complete, production-ready " + language + @" code from an implementation plan step.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON; no extra text.
2. Generate COMPLETE, WORKING code. Every function body must be fully implemented.
3. NEVER use placeholders like '# ...', '// rest of code', '// TODO', '...' or 'pass' as a stub.
4. Include all necessary imports, error handling, and entry points.
5. If the code would be too long, write a simpler but FULLY WORKING version.
6. CRITICAL: All string quotes must match, all brackets must close, all indentation must be correct.
7. Mentally trace through your code and verify it compiles/runs before returning.
8. For Python: every line inside a function/class body MUST be indented with 4 spaces.

## USER EXPERIENCE RULES (CRITICAL)
9. Programs MUST be INTERACTIVE by default. Use input() prompts, menus, and loops — NOT sys.argv or command-line arguments.
10. The user will double-click the file to run it. It must NOT exit immediately. Use a main loop with a menu.
11. For apps/tools/utilities: show a welcome message, present options, accept input, show results, and ask if the user wants to continue or exit.
12. Example pattern for interactive apps:
    - Print welcome/title
    - while True loop with a menu of options
    - Get user choice via input()
    - Process and display result
    - Ask to continue or type 'quit' to exit
13. NEVER use sys.argv unless the user explicitly asks for a CLI/command-line tool.
14. Add input('Press Enter to exit...') at the very end so the window stays open.

## RESPONSE FORMAT (strict JSON — you MUST use this exact structure):
{""intent"":""create_file"",""message"":""<brief summary of what was generated>"",""parameters"":{""fileName"":""<filename.ext>"",""language"":""" + language + @""",""content"":""<complete source code with \n for newlines>"",""rationale"":""<brief explanation of implementation choices>""}}";
    }

    private static string BuildUserPrompt(ImplementationPlan plan, PlanStep step, List<GeneratedFile> previousFiles)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Plan Step {step.Order}: {step.Description}");
        sb.AppendLine($"**Target File:** {step.TargetFile}");
        sb.AppendLine($"**Operation:** {step.Operation}");
        sb.AppendLine($"**Language:** {plan.Brief.Language}");

        if (step.Rationale.Length > 0)
            sb.AppendLine($"**Rationale:** {step.Rationale}");

        if (plan.Brief.UserConstraints.Count > 0)
        {
            sb.AppendLine("**User Constraints:**");
            foreach (var c in plan.Brief.UserConstraints) sb.AppendLine($"- {c}");
        }

        if (plan.EdgeCases.Count > 0)
        {
            sb.AppendLine("**Edge Cases to Handle:**");
            foreach (var e in plan.EdgeCases) sb.AppendLine($"- {e}");
        }

        // For multi-file projects, show the full project structure upfront
        if (plan.ChangeType == "multi-file" && plan.Steps.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("## Full Project Structure (all files being generated)");
            if (!string.IsNullOrWhiteSpace(plan.ProjectName))
                sb.AppendLine($"**Project Folder:** {plan.ProjectName}/");
            foreach (var s in plan.Steps.OrderBy(s => s.Order))
                sb.AppendLine($"- {s.TargetFile} ({s.Description})");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: Write imports and references to match these exact file paths. Ensure this file integrates correctly with the rest of the project.");
        }

        // ── Phase 3: inject blueprint content contract for this specific file ─────
        if (plan.Brief?.Blueprint != null && plan.Brief.Blueprint.Complexity != ProjectComplexity.SingleFile)
        {
            var normalizedTarget = step.TargetFile?.Replace('\\', '/').TrimStart('/') ?? "";
            var blueprintFile = plan.Brief.Blueprint.FileManifest.FirstOrDefault(f =>
                f.RelativePath.Replace('\\', '/').TrimStart('/').Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase));

            if (blueprintFile != null && blueprintFile.ContentMarkers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"## Content Contract for {step.TargetFile}");
                sb.AppendLine("This file MUST contain the following markers/patterns:");
                foreach (var marker in blueprintFile.ContentMarkers)
                    sb.AppendLine($"- `{marker}`");
                sb.AppendLine("IMPORTANT: Ensure all markers above appear in the generated code.");
            }

            if (plan.Brief.Blueprint.Framework != null)
            {
                sb.AppendLine();
                sb.AppendLine($"**Framework:** {plan.Brief.Blueprint.Framework.DisplayName}");
                sb.AppendLine($"**Primary Language:** {plan.Brief.Blueprint.Framework.PrimaryLanguage}");
            }
        }

        // Include previously generated files for dependency awareness
        if (previousFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Previously Generated Files (for reference)");
            foreach (var prev in previousFiles)
            {
                sb.AppendLine($"### {prev.RelativePath}");
                sb.AppendLine("```");
                sb.AppendLine(prev.Content.Length > 800 ? prev.Content[..800] + "\n... (truncated)" : prev.Content);
                sb.AppendLine("```");
            }
        }

        // Include relevant workspace context
        if (plan.Brief.RelevantContext.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Existing Codebase Context");
            foreach (var (path, content) in plan.Brief.RelevantContext.Take(3))
            {
                sb.AppendLine($"### {path}");
                sb.AppendLine("```");
                sb.AppendLine(content.Length > 500 ? content[..500] + "\n... (truncated)" : content);
                sb.AppendLine("```");
            }
        }

        // ── Phase 2: repo-aware edit context ──────────────────────────────────────
        if (step.EditStrategy != RepoFileEditStrategy.CreateNew && !string.IsNullOrWhiteSpace(step.ExistingContent))
        {
            sb.AppendLine();
            sb.AppendLine($"## Existing File Content (EditStrategy: {step.EditStrategy})");
            sb.AppendLine($"**IMPORTANT:** This file already exists. {(step.EditStrategy == RepoFileEditStrategy.PatchExisting ? "Generate ONLY the changed/new sections. Do not repeat unchanged code." : "Generate the complete updated file incorporating the requested changes.")}");
            sb.AppendLine("```");
            sb.AppendLine(step.ExistingContent.Length > 3000 ? step.ExistingContent[..3000] + "\n... (truncated)" : step.ExistingContent);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static GeneratedFile ParseGeneratedFile(AssistantTurnResult result, PlanStep step, string fallbackLanguage)
    {
        var file = new GeneratedFile
        {
            PlanStepOrder = step.Order,
            Operation = step.Operation
        };

        // Try to get the file name from the response
        if (result.Parameters.TryGetValue("fileName", out var fileName))
            file.RelativePath = fileName?.ToString() ?? step.TargetFile;
        else
            file.RelativePath = step.TargetFile;

        if (string.IsNullOrWhiteSpace(file.RelativePath))
            file.RelativePath = $"generated_step{step.Order}.{GetExtension(fallbackLanguage)}";

        // Get the code content
        if (result.Parameters.TryGetValue("content", out var content))
            file.Content = content?.ToString() ?? string.Empty;
        else
            file.Content = result.Message;

        // Get the language
        if (result.Parameters.TryGetValue("language", out var lang))
            file.Language = lang?.ToString() ?? fallbackLanguage;
        else
            file.Language = fallbackLanguage;

        return file;
    }

    private static string GetExtension(string language) => language.ToLowerInvariant() switch
    {
        "python" => "py",
        "csharp" or "c#" => "cs",
        "javascript" => "js",
        "typescript" => "ts",
        "html" => "html",
        "css" => "css",
        "java" => "java",
        "rust" => "rs",
        "go" => "go",
        "ruby" => "rb",
        "php" => "php",
        "swift" => "swift",
        "kotlin" => "kt",
        _ => "txt"
    };
}
