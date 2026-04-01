using ZayFlow.App.Services.CodeGeneration.Intelligence;

namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>
/// The output of the Understanding Layer.
/// Converts a vague user request into a structured, unambiguous internal brief
/// that downstream layers can consume without guessing.
/// </summary>
public sealed class NormalizedBrief
{
    /// <summary>One-sentence goal statement derived from the user's request.</summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>Target artifact type: file, class, method, refactor, test, etc.</summary>
    public string ArtifactType { get; set; } = string.Empty;

    /// <summary>Programming language requested or inferred.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Explicit constraints stated by the user (e.g. "use async", "no third-party libs").</summary>
    public List<string> UserConstraints { get; set; } = [];

    /// <summary>Defaults filled in by the engine when the user didn't specify (e.g. naming convention, error handling style).</summary>
    public List<string> InferredDefaults { get; set; } = [];

    /// <summary>Assumptions the engine made that could be wrong — flagged for verification.</summary>
    public List<string> Assumptions { get; set; } = [];

    /// <summary>Acceptance criteria the output must satisfy.</summary>
    public List<string> AcceptanceCriteria { get; set; } = [];

    /// <summary>Known missing details that may affect architecture or implementation.</summary>
    public List<string> MissingDetails { get; set; } = [];

    /// <summary>Risk level: low, medium, high — based on scope and destructiveness.</summary>
    public string RiskLevel { get; set; } = "low";

    /// <summary>Original user message preserved for traceability.</summary>
    public string OriginalRequest { get; set; } = string.Empty;

    /// <summary>Workspace root path if a project is open.</summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>Relevant file contents or summaries from the workspace snapshot.</summary>
    public Dictionary<string, string> RelevantContext { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Structured repository context carried through the session.</summary>
    public RepositoryContextInfo RepositoryContext { get; set; } = new();

    // ── Phase 2: repo-aware editing ───────────────────────────────────────────

    /// <summary>Phase 2 — If the user's request targets an existing workspace file, its path goes here.</summary>
    public string EditTargetFile { get; set; } = string.Empty;

    /// <summary>Phase 2 — Populated by UnderstandingService when EditTargetFile exists on disk.</summary>
    public ExistingFileContext? ExistingFileContext { get; set; }

    // ── Phase 3: project intelligence ─────────────────────────────────────────

    /// <summary>Phase 3 — Project blueprint produced by ProjectIntelligenceService with concrete file manifests.</summary>
    public ProjectBlueprint? Blueprint { get; set; }
}
