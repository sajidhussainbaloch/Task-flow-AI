using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>
/// The final output of the entire code-generation engine.
/// Contains only production-ready artifacts that have passed verification.
/// Converts cleanly into the existing <see cref="AssistantTurnResult"/> for the UI.
/// </summary>
public sealed class FinalOutputPackage
{
    /// <summary>Whether the engine produced a usable result.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable summary of what was generated.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The generated code artifacts, keyed by relative file path.</summary>
    public List<GeneratedFile> Files { get; set; } = [];

    /// <summary>The intent to execute (e.g. create_file, edit_file, create_project).</summary>
    public string Intent { get; set; } = "create_file";

    /// <summary>Project folder name for multi-file output.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Intent parameters compatible with IntentExecutionService.</summary>
    public Dictionary<string, object> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>If the engine could not produce a result, the reason why.</summary>
    public string BlockedReason { get; set; } = string.Empty;

    /// <summary>Final confidence score after all iterations.</summary>
    public double ConfidenceScore { get; set; }

    /// <summary>How many verify→improve iterations were needed.</summary>
    public int IterationsUsed { get; set; }

    /// <summary>Assumptions that were carried through the entire pipeline.</summary>
    public List<string> Assumptions { get; set; } = [];

    /// <summary>Last verification report for transparency.</summary>
    public VerificationReport? LastVerification { get; set; }

    /// <summary>Structured telemetry captured during the engine run.</summary>
    public CodeGenerationTelemetry Telemetry { get; set; } = new();

    /// <summary>Repository context the engine used while planning and generating.</summary>
    public RepositoryContextInfo RepositoryContext { get; set; } = new();
}

/// <summary>
/// A single generated file in the output package.
/// </summary>
public sealed class GeneratedFile
{
    /// <summary>Relative file path from workspace root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>The complete file content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Programming language of the file.</summary>
    public string Language { get; set; } = "text";

    /// <summary>Operation: create or edit.</summary>
    public string Operation { get; set; } = "create";

    /// <summary>Which plan step produced this file.</summary>
    public int PlanStepOrder { get; set; }
}
