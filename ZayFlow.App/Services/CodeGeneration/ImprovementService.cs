using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Intelligence;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Improvement Layer â€” takes verification findings and applies targeted fixes
/// to generated code without regenerating everything from scratch.
/// </summary>
public sealed class ImprovementService
{
    private readonly CloudflareProvider _openRouterProvider;
    private readonly ILogger<ImprovementService> _logger;

    public ImprovementService(CloudflareProvider openRouterProvider, ILogger<ImprovementService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _logger = logger;
    }

    public async Task<List<GeneratedFile>> ImproveAsync(
        List<GeneratedFile> files,
        VerificationReport report,
        ImplementationPlan plan,
        string model,
        CancellationToken ct = default)
    {
        // Only fix files that have error-severity findings
        var errorFindings = report.Findings
            .Where(f => f.Severity == "error" && !f.Resolved)
            .ToList();

        if (errorFindings.Count == 0)
        {
            _logger.LogInformation("Improvement layer: no error findings to fix");
            return files;
        }

        // â”€â”€ Phase 3: detect missing files from completeness errors â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var missingFileFindings = errorFindings
            .Where(f => f.Category == "completeness" &&
                        f.Description.Contains("missing", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(f.FilePath))
            .ToList();

        // Group remaining fix-in-place findings by file
        var fixFindings = errorFindings.Except(missingFileFindings).ToList();
        var findingsByFile = fixFindings
            .GroupBy(f => f.FilePath)
            .ToDictionary(g => g.Key, g => g.ToList());

        var improved = new List<GeneratedFile>();

        foreach (var file in files)
        {
            if (findingsByFile.TryGetValue(file.RelativePath, out var fileFindings))
            {
                var fixedFile = await FixFileAsync(file, fileFindings, plan, model, ct).ConfigureAwait(false);
                improved.Add(fixedFile);
                _logger.LogInformation("Improvement layer fixed {Count} issues in {File}",
                    fileFindings.Count, file.RelativePath);
            }
            else
            {
                // No issues â€” keep as-is
                improved.Add(file);
            }
        }

        // â”€â”€ Phase 3: generate missing files identified by FrameworkValidator â”€â”€â”€â”€â”€â”€
        if (missingFileFindings.Count > 0 && plan.Brief?.Blueprint != null)
        {
            var existingPaths = improved.Select(f => f.RelativePath.Replace('\\', '/').TrimStart('/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var missing in missingFileFindings)
            {
                var missingPath = missing.FilePath.Replace('\\', '/').TrimStart('/');
                if (existingPaths.Contains(missingPath)) continue;

                var generated = await GenerateMissingFileAsync(missingPath, plan, improved, model, ct).ConfigureAwait(false);
                if (generated != null)
                {
                    improved.Add(generated);
                    existingPaths.Add(missingPath);
                    _logger.LogInformation("Improvement layer generated missing file: {File}", missingPath);
                }
            }
        }

        return improved;
    }

    private async Task<GeneratedFile> FixFileAsync(
        GeneratedFile file,
        List<VerificationFinding> findings,
        ImplementationPlan plan,
        string model,
        CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(plan.Brief.Language);
        var userPrompt = BuildUserPrompt(file, findings);

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt, userPrompt, model, Array.Empty<AssistantAttachment>(), ct)
            .ConfigureAwait(false);

        // Get the corrected content
        string correctedContent;
        if (result.Parameters.TryGetValue("content", out var content))
            correctedContent = content?.ToString() ?? file.Content;
        else
            correctedContent = file.Content; // fallback: keep original if AI didn't return content

        return new GeneratedFile
        {
            RelativePath = file.RelativePath,
            Content = correctedContent,
            Language = file.Language,
            Operation = file.Operation,
            PlanStepOrder = file.PlanStepOrder
        };
    }

    private static string BuildSystemPrompt(string language)
    {
        return @"You are the Improvement Layer of a code-generation engine. You fix specific issues in " + language + @" code.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON; no extra text.
2. Fix ONLY the reported issues. Do not refactor unrelated code.
3. Preserve the overall structure, variable names, and logic unless a finding specifically targets them.
4. The output MUST be the complete corrected file â€” not just the changed lines.
5. Ensure the fix doesn't introduce new issues (e.g. fixing an import shouldn't break another).
6. CRITICAL: All string quotes must match, all brackets must close, all indentation must be correct.

## RESPONSE FORMAT (strict JSON â€” you MUST use this exact structure):
{""intent"":""create_file"",""message"":""<brief summary of fixes>"",""parameters"":{""fileName"":""<same filename>"",""content"":""<complete corrected source code with \n for newlines>"",""fixesSummary"":""<brief summary of what was fixed>""}}";
    }

    private static string BuildUserPrompt(GeneratedFile file, List<VerificationFinding> findings)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## File to Fix: {file.RelativePath}");
        sb.AppendLine();
        sb.AppendLine("### Current Code");
        sb.AppendLine("```");
        sb.AppendLine(file.Content.Length > 3000 ? file.Content[..3000] + "\n... (truncated)" : file.Content);
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine("### Issues to Fix");
        foreach (var finding in findings)
        {
            sb.AppendLine($"- **[{finding.Category}] {finding.Description}**");
            if (!string.IsNullOrWhiteSpace(finding.SuggestedFix))
                sb.AppendLine($"  Suggested fix: {finding.SuggestedFix}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Phase 3 â€” Generate a missing file identified by the framework validator.
    /// Uses the blueprint context and existing files for reference.
    /// </summary>
    private async Task<GeneratedFile?> GenerateMissingFileAsync(
        string relativePath,
        ImplementationPlan plan,
        List<GeneratedFile> existingFiles,
        string model,
        CancellationToken ct)
    {
        var language = plan.Brief?.Language ?? "text";
        var blueprintFile = plan.Brief?.Blueprint?.FileManifest
            .FirstOrDefault(f => f.RelativePath.Replace('\\', '/').TrimStart('/').Equals(relativePath, StringComparison.OrdinalIgnoreCase));

        var systemPrompt = BuildSystemPrompt(language);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Generate Missing File: {relativePath}");
        sb.AppendLine($"**Language:** {blueprintFile?.Language ?? language}");

        if (blueprintFile != null)
        {
            sb.AppendLine($"**Description:** {blueprintFile.Description}");
            if (blueprintFile.ContentMarkers.Count > 0)
            {
                sb.AppendLine("**Must contain:**");
                foreach (var marker in blueprintFile.ContentMarkers)
                    sb.AppendLine($"- `{marker}`");
            }
        }

        if (plan.Brief?.Blueprint?.Framework != null)
            sb.AppendLine($"**Framework:** {plan.Brief.Blueprint.Framework.DisplayName}");

        // Include existing project files for reference
        if (existingFiles.Count > 0)
        {
            sb.AppendLine("\n## Existing Project Files (for reference)");
            foreach (var file in existingFiles.Take(3))
            {
                sb.AppendLine($"### {file.RelativePath}");
                sb.AppendLine("```");
                sb.AppendLine(file.Content.Length > 800 ? file.Content[..800] + "\n...(truncated)" : file.Content);
                sb.AppendLine("```");
            }
        }

        sb.AppendLine($"\nIMPORTANT: Generate the COMPLETE file for '{relativePath}'. The file must integrate with the existing project files shown above.");

        try
        {
            var result = await _openRouterProvider.SendStructuredTurnAsync(
                systemPrompt, sb.ToString(), model, Array.Empty<AssistantAttachment>(), ct)
                .ConfigureAwait(false);

            string content;
            if (result.Parameters.TryGetValue("content", out var c))
                content = c?.ToString() ?? string.Empty;
            else
                content = result.Message;

            if (string.IsNullOrWhiteSpace(content))
                return null;

            return new GeneratedFile
            {
                RelativePath = relativePath,
                Content = content,
                Language = blueprintFile?.Language ?? language,
                Operation = "create",
                PlanStepOrder = existingFiles.Count + 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate missing file {Path}", relativePath);
            return null;
        }
    }
}
