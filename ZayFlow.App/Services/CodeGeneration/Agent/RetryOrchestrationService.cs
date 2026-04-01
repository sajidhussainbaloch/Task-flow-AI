using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.CodeGeneration.Checkpointing;
using ZayFlow.App.Services.CodeGeneration.Notes;

namespace ZayFlow.App.Services.CodeGeneration.Agent;

/// <summary>
/// Phase 3 — Retry &amp; escalation logic for plan step execution.
/// Failed steps retry up to 3 times with different strategies.
/// After retries are exhausted, escalates to the user with options.
/// </summary>
public sealed class RetryOrchestrationService
{
    public const int MaxRetries = 3;

    private readonly FailureGuidanceService _guidanceService;
    private readonly AuditTrailService _auditTrail;
    private readonly WorkflowNotesService _notes;
    private readonly ILogger<RetryOrchestrationService> _logger;

    public RetryOrchestrationService(
        FailureGuidanceService guidanceService,
        AuditTrailService auditTrail,
        WorkflowNotesService notes,
        ILogger<RetryOrchestrationService> logger)
    {
        _guidanceService = guidanceService;
        _auditTrail = auditTrail;
        _notes = notes;
        _logger = logger;
    }

    /// <summary>
    /// Executes a step action with retry logic. Returns the outcome.
    /// The <paramref name="executeStep"/> delegate performs the actual work for a given attempt number.
    /// </summary>
    public async Task<StepExecutionResult> ExecuteWithRetryAsync(
        string sessionId,
        PlanStepItem step,
        Func<int, CancellationToken, Task<StepExecutionResult>> executeStep,
        IProgress<string>? notesProgress = null,
        CancellationToken ct = default)
    {
        StepExecutionResult? lastResult = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            _auditTrail.Record(sessionId, AuditEventType.StepStarted,
                $"Step {step.Id} attempt {attempt}/{MaxRetries}: {step.Description}");

            try
            {
                lastResult = await executeStep(attempt, ct).ConfigureAwait(false);

                if (lastResult.Success)
                {
                    _auditTrail.Record(sessionId, AuditEventType.StepCompleted,
                        $"Step {step.Id} succeeded on attempt {attempt}", lastResult.Summary);
                    step.Status = PlanStepStatus.Completed;
                    step.ResultSummary = lastResult.Summary;
                    step.CompletedAt = DateTime.UtcNow;
                    return lastResult;
                }

                // Step failed — analyze and decide retry
                var guidance = _guidanceService.Analyze(
                    lastResult.ErrorMessage,
                    step.Action,
                    MaxRetries - attempt);

                _auditTrail.RecordError(sessionId, $"Step {step.Id} attempt {attempt}", lastResult.ErrorMessage);

                if (!guidance.ShouldRetry)
                {
                    _logger.LogWarning("Step {StepId} failed and cannot be retried: {Error}", step.Id, lastResult.ErrorMessage);
                    var failNote = _notes.EmitFailure(step.Description, lastResult.ErrorMessage, attempt, MaxRetries, guidance);
                    notesProgress?.Report(failNote);
                    break;
                }

                _auditTrail.RecordRetry(sessionId, attempt, MaxRetries, guidance.Suggestion);
                _logger.LogInformation("Step {StepId} failed, retrying ({Attempt}/{Max}): {Reason}",
                    step.Id, attempt, MaxRetries, guidance.Suggestion);

                if (guidance.RetryDelayMs > 0)
                    await Task.Delay(guidance.RetryDelayMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step {StepId} threw exception on attempt {Attempt}", step.Id, attempt);
                _auditTrail.RecordError(sessionId, $"Step {step.Id} attempt {attempt}", ex.Message);
                lastResult = StepExecutionResult.Failure(ex.Message);

                var guidance = _guidanceService.Analyze(ex.Message, step.Action, MaxRetries - attempt);
                if (!guidance.ShouldRetry || attempt == MaxRetries)
                {
                    var failNote = _notes.EmitFailure(step.Description, ex.Message, attempt, MaxRetries, guidance);
                    notesProgress?.Report(failNote);
                    break;
                }

                _auditTrail.RecordRetry(sessionId, attempt, MaxRetries, guidance.Suggestion);
                if (guidance.RetryDelayMs > 0)
                    await Task.Delay(guidance.RetryDelayMs, ct).ConfigureAwait(false);
            }
        }

        // All retries exhausted — escalate
        step.Status = PlanStepStatus.Failed;
        step.ResultSummary = lastResult?.ErrorMessage ?? "Unknown failure";

        return BuildEscalation(step, lastResult);
    }

    /// <summary>
    /// Builds an escalation result that asks the user for a decision.
    /// </summary>
    private static StepExecutionResult BuildEscalation(PlanStepItem step, StepExecutionResult? lastResult)
    {
        return new StepExecutionResult
        {
            Success = false,
            ErrorMessage = lastResult?.ErrorMessage ?? "Step failed after maximum retries.",
            Summary = lastResult?.Summary ?? string.Empty,
            RequiresEscalation = true,
            EscalationMessage = $"Step {step.Id} \"{step.Description}\" failed after {MaxRetries} attempts.\n\n" +
                                $"**Last error:** {lastResult?.ErrorMessage ?? "Unknown"}\n\n" +
                                "**Options:**\n" +
                                "• Reply **retry** to try again\n" +
                                "• Reply **skip** to skip this step and continue\n" +
                                "• Reply **abort** to cancel the remaining plan"
        };
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public sealed class StepExecutionResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public List<string> FilesCreated { get; set; } = [];
    public bool RequiresEscalation { get; set; }
    public string EscalationMessage { get; set; } = string.Empty;

    public static StepExecutionResult Ok(string summary, IReadOnlyList<string>? files = null)
    {
        return new StepExecutionResult
        {
            Success = true,
            Summary = summary,
            FilesCreated = files?.ToList() ?? []
        };
    }

    public static StepExecutionResult Failure(string error)
    {
        return new StepExecutionResult
        {
            Success = false,
            ErrorMessage = error
        };
    }
}
