using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Verification;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Verification Layer — inspects generated code for correctness issues.
/// Phase 2: delegates to three category validators (Syntax, Dependency, Logic)
/// before calling the AI for deep semantic verification.
/// Produces a <see cref="VerificationReport"/> that drives the Improvement Layer.
/// </summary>
public sealed class VerificationService
{
    private readonly OpenRouterProvider _openRouterProvider;
    private readonly SyntaxValidatorService _syntaxValidator;
    private readonly DependencyValidatorService _dependencyValidator;
    private readonly LogicValidatorService _logicValidator;
    private readonly FrameworkValidatorService _frameworkValidator;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        OpenRouterProvider openRouterProvider,
        SyntaxValidatorService syntaxValidator,
        DependencyValidatorService dependencyValidator,
        LogicValidatorService logicValidator,
        FrameworkValidatorService frameworkValidator,
        ILogger<VerificationService> logger)
    {
        _openRouterProvider = openRouterProvider;
        _syntaxValidator = syntaxValidator;
        _dependencyValidator = dependencyValidator;
        _logicValidator = logicValidator;
        _frameworkValidator = frameworkValidator;
        _logger = logger;
    }

    public async Task<VerificationReport> VerifyAsync(
        List<GeneratedFile> files,
        ImplementationPlan plan,
        int iteration,
        string model,
        CancellationToken ct = default)
    {
        // ── Phase 2: run static category validators first (fast, no AI cost) ──────
        var syntaxFindings = _syntaxValidator.Validate(files);
        var depFindings = _dependencyValidator.Validate(files, plan);
        var logicFindings = _logicValidator.Validate(files, plan);

        // ── Phase 3: run framework-aware structural validator ─────────────────────────
        var frameworkFindings = _frameworkValidator.Validate(files, plan.Brief?.Blueprint);

        // ── AI deep verification ──────────────────────────────────────────────────
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(files, plan);

        var result = await _openRouterProvider.SendStructuredTurnAsync(
            systemPrompt, userPrompt, model, Array.Empty<AssistantAttachment>(), ct)
            .ConfigureAwait(false);

        var report = ParseReport(result, iteration);

        // ── Merge static findings into the AI report ──────────────────────────────        report.Findings.InsertRange(0, frameworkFindings);        report.Findings.InsertRange(0, syntaxFindings);
        report.Findings.InsertRange(0, depFindings);
        report.Findings.InsertRange(0, logicFindings);

        // Recalculate counts after merge
        report.ErrorCount = report.Findings.Count(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        report.WarningCount = report.Findings.Count(f => string.Equals(f.Severity, "warning", StringComparison.OrdinalIgnoreCase));

        // Update passed flag to account for static-analysis errors
        if (report.ErrorCount > 0) report.Passed = false;

        // ── Phase 2: build CategorySummary ────────────────────────────────────────
        foreach (var finding in report.Findings)
        {
            var cat = finding.Category ?? "other";
            report.CategorySummary[cat] = report.CategorySummary.GetValueOrDefault(cat) + 1;
        }

        // Recalculate gates based on merged findings
        report.GatesPassed.Clear();
        report.GatesFailed.Clear();

        bool syntaxOk = !report.Findings.Any(f =>
            string.Equals(f.Category, "syntax", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        (syntaxOk ? report.GatesPassed : report.GatesFailed).Add("syntax");

        bool dependencyOk = !report.Findings.Any(f =>
            string.Equals(f.Category, "dependency", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        (dependencyOk ? report.GatesPassed : report.GatesFailed).Add("dependency");

        bool logicOk = !report.Findings.Any(f =>
            string.Equals(f.Category, "logic", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        (logicOk ? report.GatesPassed : report.GatesFailed).Add("logic");

        bool completenessOk = !report.Findings.Any(f =>
            string.Equals(f.Category, "completeness", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        (completenessOk ? report.GatesPassed : report.GatesFailed).Add("completeness");

        _logger.LogInformation(
            "Verification layer iteration {Iteration}: Passed={Passed}, Confidence={Confidence:F2}, " +
            "Total={FindingCount} (syntax: {SyntaxCount}, dep: {DepCount}, logic: {LogicCount}, framework: {FrameworkCount})",
            iteration, report.Passed, report.ConfidenceScore, report.Findings.Count,
            syntaxFindings.Count, depFindings.Count, logicFindings.Count, frameworkFindings.Count);

        return report;
    }

    private static string BuildSystemPrompt()
    {
        return """
You are the Verification Layer of a code-generation engine. You inspect generated code and report issues.

## RULES
1. ALWAYS respond with a JSON object. No markdown outside JSON; no extra text.
2. Check for: syntax errors, missing imports, undefined variables, unmatched brackets/quotes, incomplete function bodies, placeholder text (TODO, ..., pass as stub), missing error handling, incorrect indentation.
3. Check for: logical correctness — trace through the code mentally and find bugs.
4. Check for: completeness — does the code fulfill every acceptance criterion?
5. Check for: consistency — do files reference each other correctly? Are interfaces satisfied?
6. Check for: naming/style issues — but only flag as warnings, not errors.
7. Assign a confidence score: 0.0 = broken, 0.5 = significant issues, 0.8 = minor issues, 1.0 = production-ready.
8. Set passed=true only if confidence >= 0.85 and there are zero error-severity findings.

## RESPONSE FORMAT (strict JSON — you MUST use this exact structure):
{"intent":"verify","message":"<overall assessment>","parameters":{"passed":true,"confidenceScore":0.95,"summary":"<overall assessment>","findings":[{"category":"syntax|completeness|consistency|logic|dependency|naming|style","severity":"error|warning|info","filePath":"<file>","description":"<issue>","suggestedFix":"<fix>"}]}}
""";
    }

    private static string BuildUserPrompt(List<GeneratedFile> files, ImplementationPlan plan)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Implementation Plan Summary");
        sb.AppendLine($"**Scope:** {plan.Scope}");
        sb.AppendLine($"**Language:** {plan.Brief.Language}");

        if (plan.Brief.AcceptanceCriteria.Count > 0)
        {
            sb.AppendLine("**Acceptance Criteria:**");
            foreach (var ac in plan.Brief.AcceptanceCriteria) sb.AppendLine($"- {ac}");
        }

        if (plan.VerificationObjectives.Count > 0)
        {
            sb.AppendLine("**Verification Objectives:**");
            foreach (var vo in plan.VerificationObjectives) sb.AppendLine($"- {vo}");
        }

        sb.AppendLine();
        sb.AppendLine("## Generated Code to Verify");
        foreach (var file in files)
        {
            sb.AppendLine($"### {file.RelativePath} ({file.Language})");
            sb.AppendLine("```");
            sb.AppendLine(file.Content.Length > 3000 ? file.Content[..3000] + "\n... (truncated)" : file.Content);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static VerificationReport ParseReport(AssistantTurnResult result, int iteration)
    {
        var report = new VerificationReport { Iteration = iteration };

        if (result.Parameters.TryGetValue("passed", out var passed))
        {
            report.Passed = passed is bool b ? b
                : passed is System.Text.Json.JsonElement je ? je.GetBoolean()
                : bool.TryParse(passed?.ToString(), out var bParsed) && bParsed;
        }

        if (result.Parameters.TryGetValue("confidenceScore", out var conf))
        {
            report.ConfidenceScore = conf is double d ? d
                : conf is System.Text.Json.JsonElement je2 ? je2.GetDouble()
                : double.TryParse(conf?.ToString(), out var dParsed) ? dParsed
                : 0.0;
        }

        if (result.Parameters.TryGetValue("summary", out var summary))
            report.Summary = summary?.ToString() ?? string.Empty;
        else
            report.Summary = result.Message;

        var findingsElement = ResolveJsonElement(result.Parameters, "findings");
        if (findingsElement.HasValue && findingsElement.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var fEl in findingsElement.Value.EnumerateArray())
            {
                report.Findings.Add(new VerificationFinding
                {
                    Category = fEl.TryGetProperty("category", out var cat) ? cat.GetString() ?? string.Empty : string.Empty,
                    Severity = fEl.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "warning" : "warning",
                    FilePath = fEl.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? string.Empty : string.Empty,
                    Description = fEl.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                    SuggestedFix = fEl.TryGetProperty("suggestedFix", out var fix) ? fix.GetString() ?? string.Empty : string.Empty
                });
            }
        }

        report.ErrorCount = report.Findings.Count(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        report.WarningCount = report.Findings.Count(f => string.Equals(f.Severity, "warning", StringComparison.OrdinalIgnoreCase));

        if (report.ErrorCount == 0)
            report.GatesPassed.Add("syntax");
        else
            report.GatesFailed.Add("syntax");

        if (report.Findings.All(f => !string.Equals(f.Category, "completeness", StringComparison.OrdinalIgnoreCase)
                                  || !string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            report.GatesPassed.Add("completeness");
        else
            report.GatesFailed.Add("completeness");

        if (report.Findings.All(f => !string.Equals(f.Category, "consistency", StringComparison.OrdinalIgnoreCase)
                                  || !string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            report.GatesPassed.Add("consistency");
        else
            report.GatesFailed.Add("consistency");

        return report;
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
}
