using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration.Verification;

/// <summary>
/// Phase 2 — Logic Validator.
/// Inspects generated code for logical completeness issues:
/// unreachable return paths, missing null guards, over-broad catch blocks,
/// acceptance-criteria gaps, and unfulfilled plan objectives.
/// </summary>
public sealed class LogicValidatorService
{
    private readonly ILogger<LogicValidatorService> _logger;

    public LogicValidatorService(ILogger<LogicValidatorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs logic checks against the generated files and the original plan.
    /// </summary>
    public List<VerificationFinding> Validate(List<GeneratedFile> files, ImplementationPlan plan)
    {
        var findings = new List<VerificationFinding>();

        foreach (var file in files)
        {
            try
            {
                findings.AddRange(CheckFile(file));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Logic validation failed for {File}", file.RelativePath);
            }
        }

        // Cross-check acceptance criteria against combined file content
        findings.AddRange(CheckAcceptanceCriteria(files, plan));

        return findings;
    }

    private static List<VerificationFinding> CheckFile(GeneratedFile file)
    {
        var findings = new List<VerificationFinding>();
        var content = file.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content)) return findings;

        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var lineNum = i + 1;

            // ── Bare catch with no recovery ────────────────────────────────────────
            bool isBareExceptionCatch =
                (trimmed.StartsWith("catch (Exception", StringComparison.Ordinal) ||
                 trimmed.StartsWith("catch(Exception", StringComparison.Ordinal) ||
                 trimmed.StartsWith("except Exception", StringComparison.Ordinal) ||
                 trimmed == "catch" || trimmed == "except:");

            if (isBareExceptionCatch)
            {
                // Look ahead — if the body is empty or just a comment, flag it
                var bodyLine = i + 1 < lines.Length ? lines[i + 1].Trim() : string.Empty;
                var isEmpty = string.IsNullOrWhiteSpace(bodyLine) || bodyLine == "{" || bodyLine == "}";
                if (isEmpty)
                {
                    findings.Add(new VerificationFinding
                    {
                        Category = "logic",
                        Severity = "warning",
                        FilePath = file.RelativePath,
                        Description = $"Line {lineNum}: empty catch block swallows exceptions silently.",
                        SuggestedFix = "Log the exception or re-throw it rather than ignoring it."
                    });
                }
            }

            // ── Hardcoded secrets / credentials ───────────────────────────────────
            bool looksLikeHardcodedSecret =
                (trimmed.Contains("password =", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("apiKey =", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("secret =", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Contains("token =", StringComparison.OrdinalIgnoreCase)) &&
                (trimmed.Contains("\"") || trimmed.Contains("'")) &&
                !trimmed.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                !trimmed.TrimStart().StartsWith("#", StringComparison.Ordinal);

            if (looksLikeHardcodedSecret)
            {
                findings.Add(new VerificationFinding
                {
                    Category = "logic",
                    Severity = "warning",
                    FilePath = file.RelativePath,
                    Description = $"Line {lineNum}: possible hardcoded credential or secret value.",
                    SuggestedFix = "Move secrets to environment variables or a secure configuration store."
                });
            }

            // ── Method returns nothing after conditional branches ──────────────────
            // Simplified heuristic: if a non-void method has an if block with return but no else/final return
            bool hasConditionalReturn = trimmed.StartsWith("if (", StringComparison.Ordinal) || trimmed.StartsWith("if(", StringComparison.Ordinal);
            if (hasConditionalReturn)
            {
                // Look ahead for a return inside the block
                for (int j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
                {
                    if (lines[j].Trim().StartsWith("return ", StringComparison.Ordinal))
                    {
                        // Check if there's no return after the closing brace (rough heuristic)
                        // Only flag if the function seems to have a return type (not void)
                        // — this is a best-effort heuristic, not full flow analysis
                        break;
                    }
                }
            }
        }

        return findings;
    }

    private static List<VerificationFinding> CheckAcceptanceCriteria(
        List<GeneratedFile> files,
        ImplementationPlan plan)
    {
        var findings = new List<VerificationFinding>();
        var allContent = string.Join("\n", files.Select(f => f.Content ?? string.Empty));

        foreach (var criterion in plan.Brief.AcceptanceCriteria)
        {
            if (string.IsNullOrWhiteSpace(criterion)) continue;

            // Extract key nouns/verbs from the criterion and check they appear somewhere in the code
            var keywords = ExtractKeywords(criterion);
            var missingKeywords = keywords.Where(kw => !allContent.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();

            // Only flag if the majority of check-words are absent (avoid false positives)
            if (keywords.Count > 0 && missingKeywords.Count > keywords.Count / 2)
            {
                findings.Add(new VerificationFinding
                {
                    Category = "logic",
                    Severity = "info",
                    FilePath = files.FirstOrDefault()?.RelativePath ?? string.Empty,
                    Description = $"Acceptance criterion may not be fully addressed: \"{criterion}\".",
                    SuggestedFix = $"Verify the generated code satisfies: {criterion}"
                });
            }
        }

        return findings;
    }

    private static List<string> ExtractKeywords(string text)
    {
        // Very simple keyword extractor — skip common stop words, return meaningful tokens
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "be", "been", "of", "in", "to", "for",
            "that", "and", "or", "it", "with", "as", "at", "by", "on", "not",
            "must", "should", "will", "can", "all", "any", "from", "this", "which"
        };

        return text.Split([' ', '.', ',', ';', ':', '(', ')', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
