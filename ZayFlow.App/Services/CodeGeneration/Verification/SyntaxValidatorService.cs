using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.CodeGeneration.Verification;

/// <summary>
/// Phase 2 — Syntax Validator.
/// Performs fast static-analysis style syntax checks on generated code without calling the AI.
/// Detects unclosed braces/brackets/parens, incomplete method bodies, and obvious structural gaps.
/// </summary>
public sealed class SyntaxValidatorService
{
    private readonly ILogger<SyntaxValidatorService> _logger;

    public SyntaxValidatorService(ILogger<SyntaxValidatorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs syntax checks on all generated files and returns per-file findings.
    /// </summary>
    public List<VerificationFinding> Validate(List<GeneratedFile> files)
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
                _logger.LogWarning(ex, "Syntax validation failed for {File}", file.RelativePath);
            }
        }

        return findings;
    }

    private static List<VerificationFinding> CheckFile(GeneratedFile file)
    {
        var findings = new List<VerificationFinding>();
        var content = file.Content ?? string.Empty;

        // ── Brace balance ──────────────────────────────────────────────────────────
        var openBraces = Count(content, '{');
        var closeBraces = Count(content, '}');
        if (openBraces != closeBraces)
        {
            findings.Add(new VerificationFinding
            {
                Category = "syntax",
                Severity = "error",
                FilePath = file.RelativePath,
                Description = $"Unbalanced braces: {openBraces} opening '{{' vs {closeBraces} closing '}}'.",
                SuggestedFix = "Add or remove the missing closing/opening brace."
            });
        }

        // ── Bracket balance ────────────────────────────────────────────────────────
        var openBrackets = Count(content, '[');
        var closeBrackets = Count(content, ']');
        if (openBrackets != closeBrackets)
        {
            findings.Add(new VerificationFinding
            {
                Category = "syntax",
                Severity = "error",
                FilePath = file.RelativePath,
                Description = $"Unbalanced square brackets: {openBrackets} '[' vs {closeBrackets} ']'.",
                SuggestedFix = "Balance all array/index brackets."
            });
        }

        // ── Parenthesis balance ────────────────────────────────────────────────────
        var openParens = Count(content, '(');
        var closeParens = Count(content, ')');
        if (openParens != closeParens)
        {
            findings.Add(new VerificationFinding
            {
                Category = "syntax",
                Severity = "error",
                FilePath = file.RelativePath,
                Description = $"Unbalanced parentheses: {openParens} '(' vs {closeParens} ')'.",
                SuggestedFix = "Balance all method call or expression parentheses."
            });
        }

        // ── Placeholder stubs ──────────────────────────────────────────────────────
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            bool isStub =
                string.Equals(line, "...", StringComparison.Ordinal) ||
                string.Equals(line, "pass", StringComparison.Ordinal) ||
                string.Equals(line, "// TODO", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("# TODO", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("throw new NotImplementedException", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("raise NotImplementedError", StringComparison.OrdinalIgnoreCase);

            if (isStub)
            {
                findings.Add(new VerificationFinding
                {
                    Category = "syntax",
                    Severity = "warning",
                    FilePath = file.RelativePath,
                    Description = $"Line {i + 1}: stub/placeholder detected — '{lines[i].Trim()}'.",
                    SuggestedFix = "Replace with actual implementation."
                });
            }
        }

        // ── Empty file ─────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(content))
        {
            findings.Add(new VerificationFinding
            {
                Category = "syntax",
                Severity = "error",
                FilePath = file.RelativePath,
                Description = "File is empty.",
                SuggestedFix = "Generate proper content for this file."
            });
        }

        return findings;
    }

    private static int Count(string source, char ch)
    {
        // Count occurrences of ch not inside string literals (simplified heuristic)
        var count = 0;
        var inString = false;
        var stringChar = '"';
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (!inString && (c == '"' || c == '\''))
            { inString = true; stringChar = c; continue; }
            if (inString && c == stringChar && (i == 0 || source[i - 1] != '\\'))
            { inString = false; continue; }
            if (!inString && c == ch) count++;
        }
        return count;
    }
}
