using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Providers;
using ZayFlow.App.Services.Assistant;
using ZayFlow.App.Services.CodeGeneration.Checkpointing;
using ZayFlow.App.Services.CodeGeneration.Contracts;
using ZayFlow.App.Services.CodeGeneration.Intelligence;
using ZayFlow.App.Services.CodeGeneration.Memory;
using ZayFlow.App.Services.CodeGeneration.Notes;
using ZayFlow.App.Services.CodeGeneration.Verification;

namespace ZayFlow.App.Services.CodeGeneration;

/// <summary>
/// Top-level code-generation engine orchestrator.
/// Runs the full pipeline: Understanding â†’ Planning â†’ Generation â†’ Verification â†’ Improvement â†’ Output.
/// Emits structured stage events and telemetry for the assistant UI.
/// </summary>
public sealed class CodeGenerationOrchestrator
{
    private readonly UnderstandingService _understanding;
    private readonly PlanningService _planning;
    private readonly GenerationService _generation;
    private readonly VerificationService _verification;
    private readonly ImprovementService _improvement;
    private readonly OutputAssembler _outputAssembler;
    private readonly CodeGenerationLoopPolicy _loopPolicy;
    private readonly CodeGenerationScenarioValidator _scenarioValidator;
    private readonly ICodeEngineMemoryService _memory;
    private readonly ProjectIntelligenceService _intelligence;
    private readonly IAppPreferencesService _preferencesService;
    private readonly WorkflowCheckpointService _checkpoint;
    private readonly AuditTrailService _auditTrail;
    private readonly WorkflowNotesService _workflowNotes;
    private readonly ILogger<CodeGenerationOrchestrator> _logger;

    public CodeGenerationOrchestrator(
        UnderstandingService understanding,
        PlanningService planning,
        GenerationService generation,
        VerificationService verification,
        ImprovementService improvement,
        OutputAssembler outputAssembler,
        CodeGenerationLoopPolicy loopPolicy,
        CodeGenerationScenarioValidator scenarioValidator,
        ICodeEngineMemoryService memory,
        ProjectIntelligenceService intelligence,
        IAppPreferencesService preferencesService,
        WorkflowCheckpointService checkpoint,
        AuditTrailService auditTrail,
        WorkflowNotesService workflowNotes,
        ILogger<CodeGenerationOrchestrator> logger)
    {
        _understanding = understanding;
        _planning = planning;
        _generation = generation;
        _verification = verification;
        _improvement = improvement;
        _outputAssembler = outputAssembler;
        _loopPolicy = loopPolicy;
        _scenarioValidator = scenarioValidator;
        _memory = memory;
        _intelligence = intelligence;
        _preferencesService = preferencesService;
        _checkpoint = checkpoint;
        _auditTrail = auditTrail;
        _workflowNotes = workflowNotes;
        _logger = logger;
    }

    public async Task<AssistantTurnResult> RunAsync(
        AssistantTurnRequest request,
        WorkspaceContextSnapshot snapshot,
        CancellationToken ct = default)
    {
        var model = SelectModel();

        // â”€â”€ Phase 2: multi-turn continuation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // If the caller attached a previous session, continue from its last state
        // (skip Understanding + Planning, jump straight to Improvement â†’ Verification loop).
        if (request.PreviousCodeSession?.FinalOutput != null &&
            request.PreviousCodeSession.GeneratedFiles.Count > 0)
        {
            _logger.LogInformation("Code engine: continuing previous session {SessionId}", request.PreviousCodeSession.SessionId);
            return await ContinueSessionAsync(request, request.PreviousCodeSession, model, ct).ConfigureAwait(false);
        }

        var session = new CodeGenerationSession
        {
            RepositoryContext = RepositoryContextInfo.FromSnapshot(snapshot),
            StartedAt = DateTime.UtcNow,
            Telemetry = new CodeGenerationTelemetry { StartedAt = DateTime.UtcNow }
        };

        // â”€â”€ Phase 3: audit trail + workflow notes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _auditTrail.RecordDecision(session.SessionId, "Starting code generation pipeline",
            $"Request: {Truncate(request.UserMessage, 120)}");

        try
        {
            // â”€â”€ Phase 2: enrich understanding prompt with memory context â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var memoryContext = await _memory.GetContextSummaryAsync(request.UserMessage, ct).ConfigureAwait(false);
            var enrichedMessage = string.IsNullOrWhiteSpace(memoryContext)
                ? request.UserMessage
                : $"{request.UserMessage}\n\n{memoryContext}";

            EmitProgress(session, request.CodeProgress, CodeGenerationStage.Understanding, CodeGenerationEventStatus.Started,
                "Understanding request and workspace context...", 8);
            session.Brief = await RunStageAsync(
                session,
                CodeGenerationStage.Understanding,
                () => _understanding.AnalyzeAsync(enrichedMessage, snapshot, model, ct),
                request.CodeProgress,
                brief => $"Understood: {brief.Language} — {Truncate(brief.Goal, 80)} (risk: {brief.RiskLevel}).").ConfigureAwait(false);
            session.Brief.RepositoryContext = session.RepositoryContext;

            // Emit detailed thinking about what we understood
            EmitProgress(session, request.CodeProgress, CodeGenerationStage.Understanding, CodeGenerationEventStatus.Completed,
                $"🎯 Goal: {Truncate(session.Brief.Goal, 100)}\n📝 Language: {session.Brief.Language}\n⚡ Risk: {session.Brief.RiskLevel}" +
                (session.Brief.UserConstraints.Count > 0 ? $"\n📌 Constraints: {string.Join(", ", session.Brief.UserConstraints.Take(3))}" : ""),
                16);

            // â”€â”€ Phase 3: project intelligence â€” produce blueprint before planning â”€â”€â”€
            EmitProgress(session, request.CodeProgress, CodeGenerationStage.Understanding, CodeGenerationEventStatus.Started,
                "Analyzing project structure and framework requirements...", 14);
            session.Brief.Blueprint = await _intelligence.AnalyzeAsync(session.Brief, model, ct).ConfigureAwait(false);
            _logger.LogInformation("Code engine: blueprint produced â€” IsKnown={IsKnown}, Complexity={Complexity}, Files={FileCount}",
                session.Brief.Blueprint.IsKnownFramework, session.Brief.Blueprint.Complexity, session.Brief.Blueprint.EstimatedFileCount);

            EmitProgress(session, request.CodeProgress, CodeGenerationStage.Planning, CodeGenerationEventStatus.Started,
                "Planning implementation steps and verification targets...", 20);
            session.Plan = await RunStageAsync(
                session,
                CodeGenerationStage.Planning,
                () => _planning.PlanAsync(session.Brief, model, ct),
                request.CodeProgress,
                plan =>
                {
                    var stepList = string.Join(", ", plan.Steps.Take(5).Select(s => Path.GetFileName(s.TargetFile)));
                    return $"Planned {plan.Steps.Count} step(s) as {plan.ChangeType}: {stepList}";
                }).ConfigureAwait(false);

            if (session.Plan.ChangeType == "blocked")
            {
                return BuildBlockedResult("The request is ambiguous and requires clarification.", session);
            }

            if (session.Plan.ChangeType == "multi-file" && !_intelligence.RequiresMultiFile(session.Brief))
            {
                _logger.LogInformation("Code engine: overriding multi-file -> single-file (language {Language} doesn't require project structure)",
                    session.Brief.Language);
                session.Plan.ChangeType = "single-file";
                session.Plan.ProjectName = string.Empty;

                var goal = session.Brief.Goal;
                var targetFile = session.Plan.Steps.FirstOrDefault()?.TargetFile ?? $"app.{GetDefaultExtension(session.Brief.Language)}";
                if (targetFile.Contains('/') || targetFile.Contains('\\'))
                    targetFile = Path.GetFileName(targetFile);

                session.Plan.Steps.Clear();
                session.Plan.Steps.Add(new PlanStep
                {
                    Order = 1,
                    Description = goal,
                    TargetFile = targetFile,
                    Operation = "create",
                    Rationale = "Collapsed to single file - language supports it"
                });
            }

            // â”€â”€ Phase 2: populate EditStrategy on plan steps â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            PopulateEditStrategies(session.Brief, session.Plan, snapshot);

            EmitProgress(session, request.CodeProgress, CodeGenerationStage.Generation, CodeGenerationEventStatus.Started,
                $"Generating {session.Plan.Steps.Count} file(s): {string.Join(", ", session.Plan.Steps.Take(4).Select(s => Path.GetFileName(s.TargetFile)))}...", 40, generatedFileCount: session.Plan.Steps.Count);
            session.GeneratedFiles = await RunStageAsync(
                session,
                CodeGenerationStage.Generation,
                () => _generation.GenerateAsync(session.Plan, model, ct),
                request.CodeProgress,
                files => $"Generated {files.Count} file(s).",
                generatedFileCount: session.Plan.Steps.Count).ConfigureAwait(false);

            for (int iteration = 1; ; iteration++)
            {
                session.CurrentIteration = iteration;

                EmitProgress(session, request.CodeProgress, CodeGenerationStage.Verification, CodeGenerationEventStatus.IterationStarted,
                    $"Verification pass {iteration}...", 55, iteration, session.GeneratedFiles.Count);

                var report = await RunStageAsync(
                    session,
                    CodeGenerationStage.Verification,
                    () => _verification.VerifyAsync(session.GeneratedFiles, session.Plan, iteration, model, ct),
                    request.CodeProgress,
                    value => $"Verification found {value.ErrorCount} error(s) and {value.WarningCount} warning(s).",
                    iteration: iteration,
                    generatedFileCount: session.GeneratedFiles.Count).ConfigureAwait(false);

                session.VerificationHistory.Add(report);
                session.Telemetry.IterationsUsed = iteration;
                session.Telemetry.FinalConfidenceScore = report.ConfidenceScore;

                var decision = _loopPolicy.Evaluate(report, session.VerificationHistory, iteration);
                EmitProgress(session, request.CodeProgress, CodeGenerationStage.Verification, CodeGenerationEventStatus.IterationCompleted,
                    decision.Reason, 68, iteration, session.GeneratedFiles.Count, report.ConfidenceScore,
                    blockedReason: decision.StopLoop && !decision.AcceptOutput ? decision.Reason : string.Empty);

                if (decision.AcceptOutput || decision.StopLoop)
                    break;

                EmitProgress(session, request.CodeProgress, CodeGenerationStage.Improvement, CodeGenerationEventStatus.Started,
                    $"Improving failing artifacts after pass {iteration}...", 76, iteration, session.GeneratedFiles.Count, report.ConfidenceScore);

                session.GeneratedFiles = await RunStageAsync(
                    session,
                    CodeGenerationStage.Improvement,
                    () => _improvement.ImproveAsync(session.GeneratedFiles, report, session.Plan, model, ct),
                    request.CodeProgress,
                    files => $"Updated {files.Count} file(s) after verification findings.",
                    iteration: iteration,
                    generatedFileCount: session.GeneratedFiles.Count,
                    confidenceScore: report.ConfidenceScore).ConfigureAwait(false);
            }

            return await FinalizeSessionAsync(session, request.CodeProgress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _auditTrail.RecordError(session.SessionId, "Pipeline", "Cancelled by user");
            _ = _auditTrail.FlushAsync(session.SessionId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code engine failed during processing");
            _auditTrail.RecordError(session.SessionId, "Pipeline", ex.Message);
            _ = _auditTrail.FlushAsync(session.SessionId, CancellationToken.None);
            return BuildBlockedResult($"Code generation failed: {ex.Message}", session);
        }
    }

    /// <summary>
    /// Phase 2 â€” Multi-turn continuation: the user is refining or extending a previous session.
    /// Skips Understanding + Planning; applies the user's follow-up request as an improvement pass
    /// on the prior session's generated files, then re-verifies.
    /// </summary>
    private async Task<AssistantTurnResult> ContinueSessionAsync(
        AssistantTurnRequest request,
        CodeGenerationSession prior,
        string model,
        CancellationToken ct)
    {
        var session = new CodeGenerationSession
        {
            SessionId = Guid.NewGuid().ToString(),
            Brief = prior.Brief,
            Plan = prior.Plan,
            GeneratedFiles = prior.GeneratedFiles,
            VerificationHistory = prior.VerificationHistory,
            RepositoryContext = prior.RepositoryContext,
            StartedAt = DateTime.UtcNow,
            Telemetry = new CodeGenerationTelemetry { StartedAt = DateTime.UtcNow }
        };

        // Inject the follow-up request into the plan brief
        if (session.Brief != null)
        {
            session.Brief.OriginalRequest = request.UserMessage;
            session.Brief.UserConstraints.Insert(0, $"Follow-up: {request.UserMessage}");
        }

        EmitProgress(session, request.CodeProgress, CodeGenerationStage.Improvement, CodeGenerationEventStatus.Started,
            $"Applying follow-up changes to {session.GeneratedFiles.Count} existing file(s)...", 30,
            generatedFileCount: session.GeneratedFiles.Count);

        // Re-run improvement (treating the follow-up request as a correction directive)
        var continuationReport = prior.VerificationHistory.LastOrDefault() ?? new VerificationReport
        {
            Summary = $"Follow-up request: {request.UserMessage}",
            ConfidenceScore = 0.5
        };

        try
        {
            session.GeneratedFiles = await RunStageAsync(
                session,
                CodeGenerationStage.Improvement,
                () => _improvement.ImproveAsync(session.GeneratedFiles, continuationReport, session.Plan!, model, ct),
                request.CodeProgress,
                files => $"Updated {files.Count} file(s) for follow-up.",
                generatedFileCount: session.GeneratedFiles.Count).ConfigureAwait(false);

            for (int iteration = 1; ; iteration++)
            {
                session.CurrentIteration = iteration;

                EmitProgress(session, request.CodeProgress, CodeGenerationStage.Verification, CodeGenerationEventStatus.IterationStarted,
                    $"Re-verification pass {iteration}...", 55, iteration, session.GeneratedFiles.Count);

                var report = await RunStageAsync(
                    session,
                    CodeGenerationStage.Verification,
                    () => _verification.VerifyAsync(session.GeneratedFiles, session.Plan!, iteration, model, ct),
                    request.CodeProgress,
                    v => $"Re-verification found {v.ErrorCount} error(s), {v.WarningCount} warning(s).",
                    iteration: iteration,
                    generatedFileCount: session.GeneratedFiles.Count).ConfigureAwait(false);

                session.VerificationHistory.Add(report);
                session.Telemetry.IterationsUsed = iteration;
                session.Telemetry.FinalConfidenceScore = report.ConfidenceScore;

                var decision = _loopPolicy.Evaluate(report, session.VerificationHistory, iteration);
                EmitProgress(session, request.CodeProgress, CodeGenerationStage.Verification, CodeGenerationEventStatus.IterationCompleted,
                    decision.Reason, 68, iteration, session.GeneratedFiles.Count, report.ConfidenceScore);

                if (decision.AcceptOutput || decision.StopLoop)
                    break;

                session.GeneratedFiles = await RunStageAsync(
                    session,
                    CodeGenerationStage.Improvement,
                    () => _improvement.ImproveAsync(session.GeneratedFiles, report, session.Plan!, model, ct),
                    request.CodeProgress,
                    files => $"Updated {files.Count} file(s) after re-verification.",
                    iteration: iteration,
                    generatedFileCount: session.GeneratedFiles.Count,
                    confidenceScore: report.ConfidenceScore).ConfigureAwait(false);
            }

            return await FinalizeSessionAsync(session, request.CodeProgress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code engine continuation failed");
            return BuildBlockedResult($"Continuation failed: {ex.Message}", session);
        }
    }

    /// <summary>
    /// Shared finalization path: assemble the final output package, record to memory, and return the result.
    /// </summary>
    private async Task<AssistantTurnResult> FinalizeSessionAsync(
        CodeGenerationSession session,
        IProgress<CodeGenerationStageEvent>? progress,
        CancellationToken ct)
    {
        EmitProgress(session, progress, CodeGenerationStage.Finalization, CodeGenerationEventStatus.Started,
            "Finalizing code package and assistant preview...", 92, session.CurrentIteration, session.GeneratedFiles.Count,
            session.VerificationHistory.LastOrDefault()?.ConfidenceScore ?? 0);

        session.CompletedAt = DateTime.UtcNow;
        session.Telemetry.CompletedAt = session.CompletedAt;
        session.Telemetry.OutputType = DetermineIntent(session.Plan!);
        var lastReport = session.VerificationHistory.LastOrDefault();
        session.Telemetry.ValidationNotes.AddRange(_scenarioValidator.ValidateSession(session));

        session.FinalOutput = new FinalOutputPackage
        {
            Success = true,
            Summary = BuildSummary(session),
            Files = session.GeneratedFiles,
            Intent = DetermineIntent(session.Plan!),
            ProjectName = session.Plan!.ProjectName,
            Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
            ConfidenceScore = lastReport?.ConfidenceScore ?? 0.5,
            IterationsUsed = session.CurrentIteration,
            Assumptions = session.Brief?.Assumptions ?? [],
            LastVerification = lastReport,
            Telemetry = session.Telemetry,
            RepositoryContext = session.RepositoryContext
        };

        EmitProgress(session, progress, CodeGenerationStage.Finalization, CodeGenerationEventStatus.Completed,
            $"Code package ready: {session.GeneratedFiles.Count} file(s), confidence {(lastReport?.ConfidenceScore ?? 0.5):P0}.",
            100, session.CurrentIteration, session.GeneratedFiles.Count, lastReport?.ConfidenceScore ?? 0.5);

        // â”€â”€ Phase 2: persist session to memory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        try
        {
            await _memory.RecordSessionAsync(session, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record session to code engine memory");
        }

        var result = _outputAssembler.Assemble(session.FinalOutput, session.Brief?.Blueprint);
        result.RawCodeSession = session; // Phase 2: expose for multi-turn continuation

        // â”€â”€ Phase 3: audit trail flush on completion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        _auditTrail.RecordDecision(session.SessionId, "Pipeline completed",
            $"Files: {session.GeneratedFiles.Count}, Confidence: {(lastReport?.ConfidenceScore ?? 0.5):P0}");
        _ = _auditTrail.FlushAsync(session.SessionId, CancellationToken.None);

        return result;
    }

    /// <summary>
    /// Phase 2 â€” Populate EditStrategy and ExistingContent on plan steps.
    /// When the brief has a detected edit target, updates the matching step to use
    /// OverwriteExisting (or PatchExisting for small files) and copies in existing content.
    /// </summary>
    private static void PopulateEditStrategies(
        NormalizedBrief brief,
        ImplementationPlan plan,
        WorkspaceContextSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(brief.EditTargetFile)) return;

        var targetFileName = Path.GetFileName(brief.EditTargetFile);
        var existingContent = brief.ExistingFileContext?.Content ?? string.Empty;

        foreach (var step in plan.Steps)
        {
            var stepFileName = Path.GetFileName(step.TargetFile);
            bool matches = string.IsNullOrWhiteSpace(step.TargetFile)
                || string.Equals(stepFileName, targetFileName, StringComparison.OrdinalIgnoreCase)
                || step.TargetFile.Contains(targetFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(step.Operation, "edit", StringComparison.OrdinalIgnoreCase);

            if (matches && !string.IsNullOrWhiteSpace(existingContent))
            {
                step.ExistingContent = existingContent;
                // Use PatchExisting for small files (<= 200 lines); OverwriteExisting for larger ones
                var lineCount = brief.ExistingFileContext?.LineCount ?? 0;
                step.EditStrategy = lineCount > 0 && lineCount <= 200
                    ? RepoFileEditStrategy.PatchExisting
                    : RepoFileEditStrategy.OverwriteExisting;

                // Ensure step file name matches the actual existing file
                if (string.IsNullOrWhiteSpace(step.TargetFile))
                    step.TargetFile = targetFileName;

                step.Operation = "edit";
                break; // Only the first matching step gets the existing content
            }
        }
    }

    private async Task<T> RunStageAsync<T>(
        CodeGenerationSession session,
        CodeGenerationStage stage,
        Func<Task<T>> action,
        IProgress<CodeGenerationStageEvent>? progress,
        Func<T, string> completedMessageFactory,
        int iteration = 0,
        int generatedFileCount = 0,
        double confidenceScore = 0)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await action().ConfigureAwait(false);
            stopwatch.Stop();
            session.Telemetry.StageDurationsMs[stage.ToString()] = stopwatch.ElapsedMilliseconds;
            EmitProgress(session, progress, stage, CodeGenerationEventStatus.Completed,
                completedMessageFactory(value), GetCompletedPercent(stage), iteration, generatedFileCount, confidenceScore);
            return value;
        }
        catch
        {
            stopwatch.Stop();
            session.Telemetry.StageDurationsMs[stage.ToString()] = stopwatch.ElapsedMilliseconds;
            throw;
        }
    }

    private void EmitProgress(
        CodeGenerationSession session,
        IProgress<CodeGenerationStageEvent>? progress,
        CodeGenerationStage stage,
        CodeGenerationEventStatus status,
        string message,
        double percent,
        int iteration = 0,
        int generatedFileCount = 0,
        double confidenceScore = 0,
        string blockedReason = "")
    {
        var evt = new CodeGenerationStageEvent
        {
            Stage = stage,
            Status = status,
            Message = message,
            Percent = percent,
            Iteration = iteration,
            GeneratedFileCount = generatedFileCount,
            ConfidenceScore = confidenceScore,
            BlockedReason = blockedReason,
            Timestamp = DateTime.UtcNow
        };

        session.StageEvents.Add(evt);
        session.Telemetry.Events.Add(evt);
        progress?.Report(evt);
    }

    private static double GetCompletedPercent(CodeGenerationStage stage) => stage switch
    {
        CodeGenerationStage.Understanding => 16,
        CodeGenerationStage.Planning => 32,
        CodeGenerationStage.Generation => 52,
        CodeGenerationStage.Verification => 68,
        CodeGenerationStage.Improvement => 84,
        CodeGenerationStage.Finalization => 100,
        _ => -1
    };

    private string SelectModel()
    {
        var preferences = _preferencesService.Get();
        return string.IsNullOrWhiteSpace(preferences.PreferredCodingModel)
            ? CloudflareProvider.DefaultCodeModel
            : preferences.PreferredCodingModel;
    }

    private static string DetermineIntent(ImplementationPlan plan)
    {
        if (plan.ChangeType == "multi-file")
            return "create_project";
        if (plan.Steps.Any(s => s.Operation == "edit"))
            return "edit_file";
        return "create_file";
    }

    private static string BuildSummary(CodeGenerationSession session)
    {
        var fileCount = session.GeneratedFiles.Count;
        var language = session.Brief?.Language ?? "code";
        var goal = session.Brief?.Goal ?? "code generation";
        return $"Generated {fileCount} {language} file(s) for: {goal}";
    }

    private static AssistantTurnResult BuildBlockedResult(string reason, CodeGenerationSession session)
    {
        session.CompletedAt = DateTime.UtcNow;
        session.Telemetry.CompletedAt = session.CompletedAt;
        session.Telemetry.BlockedReason = reason;

        return new AssistantTurnResult
        {
            Mode = AssistantTurnMode.Code,
            Message = reason,
            Intent = "chat",
            ToolTraceSummary = $"Code engine blocked: {reason}",
            CodeSession = new CodeSessionInfo
            {
                CurrentStage = "Blocked",
                IterationsUsed = session.CurrentIteration,
                GeneratedFileCount = session.GeneratedFiles.Count,
                ConfidenceScore = session.VerificationHistory.LastOrDefault()?.ConfidenceScore ?? 0,
                IsBlocked = true,
                BlockedReason = reason,
                OutputType = "chat",
                VerificationSummary = session.VerificationHistory.LastOrDefault()?.Summary ?? string.Empty
            }
        };
    }

    private static string GetDefaultExtension(string language) => language.ToLowerInvariant() switch
    {
        "python" => "py",
        "javascript" => "js",
        "typescript" => "ts",
        "html" => "html",
        "csharp" or "c#" => "cs",
        "java" => "java",
        "go" => "go",
        "rust" => "rs",
        "c" => "c",
        "c++" or "cpp" => "cpp",
        "ruby" => "rb",
        "php" => "php",
        _ => "txt"
    };

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
