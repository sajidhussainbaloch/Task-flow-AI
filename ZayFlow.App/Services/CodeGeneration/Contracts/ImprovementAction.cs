namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>
/// The output of the Improvement Layer.
/// Targeted fix actions derived from verification findings.
/// Each action patches a specific problem without regenerating the entire output.
/// </summary>
public sealed class ImprovementAction
{
    /// <summary>Which verification finding this action addresses.</summary>
    public string FindingDescription { get; set; } = string.Empty;

    /// <summary>Type of fix: patch, replace, insert, restructure.</summary>
    public string ActionType { get; set; } = "patch";

    /// <summary>Which file this action targets.</summary>
    public string TargetFile { get; set; } = string.Empty;

    /// <summary>Description of what the fix does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The corrected code or content to apply.</summary>
    public string CorrectedContent { get; set; } = string.Empty;

    /// <summary>Whether this action was successfully applied.</summary>
    public bool Applied { get; set; }
}
