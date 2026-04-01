namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>
/// The output of the Planning Layer.
/// A structured implementation plan that describes what to build, in what order,
/// and with what dependencies — so the generator works from a plan, not a guess.
/// </summary>
public sealed class ImplementationPlan
{
    /// <summary>High-level scope description.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Whether this is a single-file, multi-file, or blocked change.</summary>
    public string ChangeType { get; set; } = "single-file";

    /// <summary>Project folder name for multi-file projects (e.g. "my_flutter_app").</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Ordered list of implementation steps forming a task graph.</summary>
    public List<PlanStep> Steps { get; set; } = [];

    /// <summary>External or internal dependencies the plan relies on.</summary>
    public List<string> Dependencies { get; set; } = [];

    /// <summary>Edge cases the plan explicitly accounts for.</summary>
    public List<string> EdgeCases { get; set; } = [];

    /// <summary>What the verification layer should check after generation.</summary>
    public List<string> VerificationObjectives { get; set; } = [];

    /// <summary>Reference back to the brief that produced this plan.</summary>
    public NormalizedBrief Brief { get; set; } = new();
}

/// <summary>
/// A single step in the implementation plan.
/// </summary>
public sealed class PlanStep
{
    /// <summary>Sequential step number.</summary>
    public int Order { get; set; }

    /// <summary>Short description of what this step does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Target file path (relative) this step produces or modifies.</summary>
    public string TargetFile { get; set; } = string.Empty;

    /// <summary>Type of operation: create, edit, delete, rename.</summary>
    public string Operation { get; set; } = "create";

    /// <summary>Interfaces, classes, or components this step depends on from earlier steps.</summary>
    public List<string> DependsOn { get; set; } = [];

    /// <summary>Rationale for why this step exists in the plan.</summary>
    public string Rationale { get; set; } = string.Empty;

    // ── Phase 2: repo-aware editing ───────────────────────────────────────────

    /// <summary>Phase 2 — How the code engine should write this step's output to disk.</summary>
    public RepoFileEditStrategy EditStrategy { get; set; } = RepoFileEditStrategy.CreateNew;

    /// <summary>Phase 2 — Existing file content when EditStrategy is OverwriteExisting or PatchExisting.</summary>
    public string ExistingContent { get; set; } = string.Empty;

    /// <summary>Phase 2 — Surgical patch hunks when EditStrategy is PatchExisting.</summary>
    public List<CodePatchHunk> PatchHunks { get; set; } = [];
}
