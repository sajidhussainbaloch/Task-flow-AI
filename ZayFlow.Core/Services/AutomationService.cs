using Microsoft.Extensions.Logging;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Enums;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Services;

public sealed class AutomationService : IAutomationService
{
    private readonly IPlanner _planner;
    private readonly IRiskAnalyzer _riskAnalyzer;
    private readonly IPreviewService _previewService;
    private readonly IActionExecutor _actionExecutor;
    private readonly IUndoManager _undoManager;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(
        IPlanner planner,
        IRiskAnalyzer riskAnalyzer,
        IPreviewService previewService,
        IActionExecutor actionExecutor,
        IUndoManager undoManager,
        ILogger<AutomationService> logger)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _riskAnalyzer = riskAnalyzer ?? throw new ArgumentNullException(nameof(riskAnalyzer));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _undoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Plan> GeneratePlanAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input cannot be empty.", nameof(input));
        }

        var generatedPlan = await _planner.GeneratePlanAsync(input, cancellationToken).ConfigureAwait(false);
        var calculatedRisk = _riskAnalyzer.Analyze(generatedPlan.Actions);

        var enrichedPlan = new Plan(
            generatedPlan.Id,
            generatedPlan.Description,
            generatedPlan.Actions,
            calculatedRisk,
            generatedPlan.CreatedAt);

        _logger.LogInformation(
            "Plan generated. PlanId: {PlanId}, Actions: {ActionCount}, Risk: {RiskLevel}",
            enrichedPlan.Id,
            enrichedPlan.Actions.Count,
            enrichedPlan.RiskLevel);

        return enrichedPlan;
    }

    public Task<PreviewResult> PreviewAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var preview = _previewService.BuildPreview(plan);

        _logger.LogInformation(
            "Preview generated. PlanId: {PlanId}, Items: {ItemCount}, Risk: {RiskLevel}",
            preview.PlanId,
            preview.Items.Count,
            preview.RiskLevel);

        return Task.FromResult(preview);
    }

    public async Task<ExecutionResult> ExecuteAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _logger.LogInformation(
            "Execution started. PlanId: {PlanId}, ActionCount: {ActionCount}, Risk: {RiskLevel}",
            plan.Id,
            plan.Actions.Count,
            plan.RiskLevel);

        var executionResult = await _actionExecutor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);

        if (executionResult.ExecutedActions.Count > 0)
        {
            await _undoManager
                .StoreBatchAsync(plan, executionResult.ExecutedActions, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Execution completed. PlanId: {PlanId}, Success: {SuccessCount}, Failures: {FailureCount}",
            plan.Id,
            executionResult.SuccessCount,
            executionResult.FailureCount);

        return executionResult;
    }

    public async Task<ExecutionResult> UndoLastAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Undo requested for last execution batch.");

        var undoResult = await _undoManager.UndoLastAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Undo finished. Success: {SuccessCount}, Failures: {FailureCount}",
            undoResult.SuccessCount,
            undoResult.FailureCount);

        return undoResult;
    }
}
