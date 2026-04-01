namespace ZayFlow.App.Services.CodeGeneration.Contracts;

/// <summary>
/// Tracks the full state of a code-generation session as it moves through layers.
/// </summary>
public sealed class CodeGenerationSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public NormalizedBrief? Brief { get; set; }
    public ImplementationPlan? Plan { get; set; }
    public List<GeneratedFile> GeneratedFiles { get; set; } = [];
    public List<VerificationReport> VerificationHistory { get; set; } = [];
    public List<ImprovementAction> ImprovementHistory { get; set; } = [];
    public FinalOutputPackage? FinalOutput { get; set; }
    public RepositoryContextInfo RepositoryContext { get; set; } = new();
    public CodeGenerationTelemetry Telemetry { get; set; } = new();
    public List<CodeGenerationStageEvent> StageEvents { get; set; } = [];
    public int CurrentIteration { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
