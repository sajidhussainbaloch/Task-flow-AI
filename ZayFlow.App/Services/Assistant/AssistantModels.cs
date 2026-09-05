using ZayFlow.App.Services.AI.Contracts;
using ZayFlow.App.Services.CodeGeneration.Contracts;

namespace ZayFlow.App.Services.Assistant;

public enum AssistantTurnMode
{
    Chat,
    Code,
    Vision,
    DesktopAction,
    CodeReview,
    Refactor,
    Research,
    Document
}

public enum AssistantArtifactKind
{
    CodePreview,
    Diff,
    Ocr,
    ImageAttachment,
    FileList,
    RunNotes
}

public enum AssistantToolStatus
{
    Planned,
    Running,
    Completed,
    Failed
}

public sealed class AssistantAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Kind { get; set; } = "image";
    public string Title { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/png";
    public string PreviewText { get; set; } = string.Empty;
    public string OcrText { get; set; } = string.Empty;
    public bool IsCloudReady { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class AssistantArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public AssistantArtifactKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SecondaryContent { get; set; } = string.Empty;
    public string Language { get; set; } = "text";
    public string FilePath { get; set; } = string.Empty;
    public bool IsPreviewOnly { get; set; } = true;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ToolInvocation
{
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public AssistantToolStatus Status { get; set; } = AssistantToolStatus.Planned;
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class AssistantTurnRequest
{
    public string UserMessage { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string LastCreatedFilePath { get; set; } = string.Empty;
    public string LastExecutionSummary { get; set; } = string.Empty;
    public bool PrivacyMode { get; set; } = true;
    public bool BypassPrivacyConfirmation { get; set; }
    public bool UseLocalOcrFirst { get; set; } = true;
    public IProgress<CodeGenerationStageEvent>? CodeProgress { get; set; }
    public IReadOnlyList<ChatMessage> RecentMessages { get; set; } = Array.Empty<ChatMessage>();
    public IReadOnlyList<AssistantAttachment> Attachments { get; set; } = Array.Empty<AssistantAttachment>();

    /// <summary>Phase 2 — Attach a prior completed session to enable multi-turn continuation.</summary>
    public CodeGenerationSession? PreviousCodeSession { get; set; }
}

public sealed class AssistantTurnResult
{
    public AssistantTurnMode Mode { get; set; } = AssistantTurnMode.Chat;
    public string Message { get; set; } = string.Empty;
    public string Intent { get; set; } = "chat";
    public Dictionary<string, object> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RequiresConfirmation { get; set; }
    public string ConfirmationMessage { get; set; } = string.Empty;
    public bool IsSensitiveContentRequest { get; set; }
    public int TokenCost { get; set; } = 1;
    public string ToolTraceSummary { get; set; } = string.Empty;
    public string SuggestedWorkspaceRoot { get; set; } = string.Empty;
    public List<AssistantArtifact> Artifacts { get; set; } = new();
    public List<AssistantAttachment> Attachments { get; set; } = new();
    public List<ToolInvocation> ToolInvocations { get; set; } = new();
    public CodeSessionInfo? CodeSession { get; set; }

    /// <summary>Phase 2 — The full completed session, available for multi-turn continuation.</summary>
    public CodeGenerationSession? RawCodeSession { get; set; }
}

public sealed class CodeSessionInfo
{
    public string CurrentStage { get; set; } = string.Empty;
    public int IterationsUsed { get; set; }
    public int GeneratedFileCount { get; set; }
    public double ConfidenceScore { get; set; }
    public bool IsBlocked { get; set; }
    public string BlockedReason { get; set; } = string.Empty;
    public string OutputType { get; set; } = string.Empty;
    public string VerificationSummary { get; set; } = string.Empty;

    public string SummaryText => IsBlocked
        ? $"Blocked • {BlockedReason}"
        : $"{CurrentStage} • Iteration {IterationsUsed} • {GeneratedFileCount} file(s) • Confidence {ConfidenceScore:P0}";
}

public sealed class AssistantTurnEvent
{
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public sealed class WorkspaceContextSnapshot
{
    public string RootPath { get; set; } = string.Empty;
    public List<string> RelativeFiles { get; set; } = new();
    public Dictionary<string, string> FileContents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OcrResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = new();
    public bool IsCodeLike { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed class VisionAnalysisResult
{
    public bool IsCodeLike { get; set; }
    public string Classification { get; set; } = "image";
    public string Summary { get; set; } = string.Empty;
}
