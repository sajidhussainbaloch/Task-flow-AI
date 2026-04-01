namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>
/// The output of the Verification Layer.
/// A structured report of what passed and what failed after inspecting generated code.
/// Drives the Improvement Layer's targeted fixes.
/// </summary>
public sealed class VerificationReport
{
    /// <summary>Whether all checks passed.</summary>
    public bool Passed { get; set; }

    /// <summary>Overall confidence score from 0.0 (no confidence) to 1.0 (fully confident).</summary>
    public double ConfidenceScore { get; set; }

    /// <summary>Individual verification findings — both passes and failures.</summary>
    public List<VerificationFinding> Findings { get; set; } = [];

    /// <summary>Which iteration of the verify→improve loop produced this report.</summary>
    public int Iteration { get; set; }

    /// <summary>Summary of the overall verification outcome.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Quality gates explicitly passed by this verification run.</summary>
    public List<string> GatesPassed { get; set; } = [];

    /// <summary>Quality gates that failed and triggered improvement or blocking.</summary>
    public List<string> GatesFailed { get; set; } = [];

    /// <summary>Number of error-severity findings in this report.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Number of warning-severity findings in this report.</summary>
    public int WarningCount { get; set; }

    /// <summary>Phase 2 — Per-category finding counts keyed by category name (syntax, dependency, logic, completeness, etc.).</summary>
    public Dictionary<string, int> CategorySummary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A single finding from the verification pass.
/// </summary>
public sealed class VerificationFinding
{
    /// <summary>Category: syntax, completeness, consistency, logic, dependency, naming, style.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Severity: error, warning, info.</summary>
    public string Severity { get; set; } = "warning";

    /// <summary>Which file this finding applies to.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Description of the issue or the pass confirmation.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Suggested fix action if this is a failure.</summary>
    public string SuggestedFix { get; set; } = string.Empty;

    /// <summary>Whether this finding was resolved in a later iteration.</summary>
    public bool Resolved { get; set; }
}
