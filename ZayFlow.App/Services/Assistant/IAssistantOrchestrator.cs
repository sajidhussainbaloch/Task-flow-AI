using ZayFlow.App.Services.CodeGeneration.Agent;

namespace ZayFlow.App.Services.Assistant;

public interface IAssistantOrchestrator
{
    Task<AssistantTurnResult> ProcessTurnAsync(AssistantTurnRequest request, CancellationToken ct = default);

    /// <summary>Phase 3 — Generates an execution plan for the request (Plan Mode).</summary>
    Task<AssistantTurnResult> GeneratePlanForRequestAsync(string userMessage, string workspaceRoot, IReadOnlyList<string> workspaceFiles, CancellationToken ct = default);

    /// <summary>Phase 3 — Returns the pending plan awaiting user approval, if any.</summary>
    ExecutionPlan? GetPendingPlan();

    /// <summary>Phase 3 — Clears the pending plan.</summary>
    void ClearPendingPlan();
}

public interface IWorkspaceContextService
{
    WorkspaceContextSnapshot Capture(string workspaceRoot, string userMessage, string lastCreatedFilePath);
}

public interface ICodeContextBuilder
{
    string BuildPromptContext(AssistantTurnRequest request, WorkspaceContextSnapshot snapshot);
}

public interface ICodeEditPlanner
{
    void EnrichCodeArtifacts(AssistantTurnResult result, AssistantTurnRequest request, WorkspaceContextSnapshot snapshot);
}

public interface ICodeDiffService
{
    string BuildUnifiedDiff(string originalContent, string updatedContent, string filePath);
}

public interface ICodeApplyService
{
    Task<string> ApplyCreateOrEditAsync(string workspaceRoot, Dictionary<string, object> parameters, CancellationToken ct = default);
}

public interface IClipboardAttachmentService
{
    AssistantAttachment? TryCreateImageAttachmentFromClipboard();
}

public interface IImagePreprocessService
{
    AssistantAttachment Prepare(AssistantAttachment attachment);
}

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(string imagePath, CancellationToken ct = default);
}

public interface IVisionAnalysisService
{
    VisionAnalysisResult Analyze(string text);
}
