using ZayFlow.App.Services.Assistant;

namespace ZayFlow.App.Services.CodeGeneration.Contracts;

public enum CodeGenerationStage
{
    Understanding,
    Planning,
    Generation,
    Verification,
    Improvement,
    Finalization
}

public enum CodeGenerationEventStatus
{
    Started,
    Completed,
    IterationStarted,
    IterationCompleted,
    Blocked,
    Failed
}

public sealed class RepositoryContextInfo
{
    public string WorkspaceRoot { get; set; } = string.Empty;
    public List<string> RelevantFiles { get; set; } = [];
    public List<string> CandidateTargets { get; set; } = [];
    public List<string> Conventions { get; set; } = [];
    public string DetectedProjectType { get; set; } = string.Empty;

    public static RepositoryContextInfo FromSnapshot(WorkspaceContextSnapshot snapshot)
    {
        var info = new RepositoryContextInfo
        {
            WorkspaceRoot = snapshot.RootPath,
            RelevantFiles = snapshot.RelativeFiles.Take(20).ToList(),
            CandidateTargets = snapshot.FileContents.Keys.Take(10).ToList(),
            DetectedProjectType = DetectProjectType(snapshot.RelativeFiles)
        };

        if (snapshot.RelativeFiles.Any(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
            info.Conventions.Add("WPF/XAML project structure");
        if (snapshot.RelativeFiles.Any(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            info.Conventions.Add("C# project files present");
        if (snapshot.RelativeFiles.Any(path => path.Contains("ViewModel", StringComparison.OrdinalIgnoreCase)))
            info.Conventions.Add("MVVM naming present");

        return info;
    }

    private static string DetectProjectType(IEnumerable<string> files)
    {
        if (files.Any(path => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
            return "dotnet-solution";
        if (files.Any(path => path.EndsWith("pubspec.yaml", StringComparison.OrdinalIgnoreCase)))
            return "flutter";
        if (files.Any(path => path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)))
            return "node";
        return "workspace";
    }
}

public sealed class CodeGenerationStageEvent
{
    public CodeGenerationStage Stage { get; set; }
    public CodeGenerationEventStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public double Percent { get; set; } = -1;
    public int Iteration { get; set; }
    public int GeneratedFileCount { get; set; }
    public double ConfidenceScore { get; set; }
    public string BlockedReason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class CodeGenerationTelemetry
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int IterationsUsed { get; set; }
    public double FinalConfidenceScore { get; set; }
    public string OutputType { get; set; } = string.Empty;
    public string BlockedReason { get; set; } = string.Empty;
    public Dictionary<string, long> StageDurationsMs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CodeGenerationStageEvent> Events { get; set; } = [];
    public List<string> ValidationNotes { get; set; } = [];

    public long TotalDurationMs => CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
        : 0;
}
