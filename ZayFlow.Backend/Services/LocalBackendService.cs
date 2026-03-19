using Microsoft.Extensions.Logging;
using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.DTOs;
using ZayFlow.Backend.Mapping;
using ZayFlow.Backend.Models;
using ZayFlow.Backend.Persistence;
using ZayFlow.Core.Abstractions;
using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Backend.Services;

public sealed class LocalBackendService : ILocalBackendService
{
    private readonly IAutomationService _automationService;
    private readonly PlannerStrategyProvider _plannerStrategy;
    private readonly LocalTokenService _tokenService;
    private readonly IExecutionAuditStore _auditStore;
    private readonly IQueuedPlanStore _queueStore;
    private readonly JsonBackendStateStore _backendStateStore;
    private readonly PlannerActionLogger _plannerLogger;
    private readonly ITaskMemoryManager _taskMemory;
    private readonly ISuggestionService _suggestionService;
    private readonly ILogger<LocalBackendService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalBackendService(
        IAutomationService automationService,
        PlannerStrategyProvider plannerStrategy,
        LocalTokenService tokenService,
        IExecutionAuditStore auditStore,
        IQueuedPlanStore queueStore,
        JsonBackendStateStore backendStateStore,
        PlannerActionLogger plannerLogger,
        ITaskMemoryManager taskMemory,
        ISuggestionService suggestionService,
        ILogger<LocalBackendService> logger)
    {
        _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        _plannerStrategy = plannerStrategy ?? throw new ArgumentNullException(nameof(plannerStrategy));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _queueStore = queueStore ?? throw new ArgumentNullException(nameof(queueStore));
        _backendStateStore = backendStateStore ?? throw new ArgumentNullException(nameof(backendStateStore));
        _plannerLogger = plannerLogger ?? throw new ArgumentNullException(nameof(plannerLogger));
        _taskMemory = taskMemory ?? throw new ArgumentNullException(nameof(taskMemory));
        _suggestionService = suggestionService ?? throw new ArgumentNullException(nameof(suggestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generate a plan using the active planner strategy (RuleBasedPlanner or LLMPlanner).
    /// Returns PlanDTO directly without domain model conversion.
    /// </summary>
    public async Task<PlanDTO> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            throw new ArgumentException("User input cannot be empty.", nameof(userInput));
        }

        try
        {
            var planner = _plannerStrategy.GetActivePlanner();
            var plannerType = planner.GetType().Name;

            _logger.LogInformation("Generating plan using {PlannerType}. Input: {Input}", plannerType, userInput);

            var plan = await planner.GeneratePlanAsync(userInput, cancellationToken).ConfigureAwait(false);

            _plannerLogger.LogPlanGenerated(plannerType, userInput, plan);
            return plan;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Plan generation cancelled for input: {Input}", userInput);
            throw;
        }
        catch (Exception ex)
        {
            _plannerLogger.LogPlannerException("GeneratePlan", ex, userInput);
            throw;
        }
    }

    public async Task<PreviewDTO> PreviewPlanAsync(PlanDTO plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var domainPlan = BackendMapper.ToDomain(plan);
        var preview = await _automationService.PreviewAsync(domainPlan, cancellationToken).ConfigureAwait(false);
        return BackendMapper.ToDto(preview);
    }

    public async Task<ExecutionResultDTO> ExecutePlanAsync(PlanDTO plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tokenStatus = await _tokenService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            var isOnline = await GetOnlineStatusAsync(cancellationToken).ConfigureAwait(false);

            _plannerLogger.LogExecutionStarted(plan, isOnline, tokenStatus.Remaining);

            if (!isOnline)
            {
                var queued = new QueuedPlan { Plan = plan, QueuedAtUtc = DateTime.UtcNow };
                await _queueStore.EnqueueAsync(queued, cancellationToken).ConfigureAwait(false);

                var queuedCount = (await _queueStore.GetAllAsync(cancellationToken).ConfigureAwait(false)).Count;
                _plannerLogger.LogPlanQueued(plan.Id, queuedCount);

                var queuedResult = new ExecutionResultDTO(
                    0,
                    0,
                    Array.Empty<FailedExecutionItemDTO>(),
                    true,
                    "Offline mode: plan queued for later execution.");

                await LogAuditAsync("ExecuteQueued", plan, queuedResult, cancellationToken).ConfigureAwait(false);
                return queuedResult;
            }

            var tokenAllowed = await _tokenService.TryDeductAsync(1, cancellationToken).ConfigureAwait(false);
            if (!tokenAllowed)
            {
                _plannerLogger.LogTokenLimitReached(tokenStatus.Used, tokenStatus.DailyLimit);
                return new ExecutionResultDTO(
                    0,
                    1,
                    Array.Empty<FailedExecutionItemDTO>(),
                    false,
                    "Daily token limit reached.");
            }

            var domainPlan = BackendMapper.ToDomain(plan);
            var execution = await _automationService.ExecuteAsync(domainPlan, cancellationToken).ConfigureAwait(false);
            var dto = BackendMapper.ToDto(execution, false, "Executed locally");

            await _taskMemory.RememberExecutedCommandAsync(plan.SourceCommand, null, cancellationToken).ConfigureAwait(false);

            var newTokenStatus = await _tokenService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            _plannerLogger.LogTokenDeducted(1, newTokenStatus.Remaining, plan.Id);
            _plannerLogger.LogExecutionCompleted(plan, dto);

            await LogAuditAsync("Execute", plan, dto, cancellationToken).ConfigureAwait(false);
            return dto;
        }
        catch (Exception ex)
        {
            _plannerLogger.LogExecutionFailed(plan, ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ExecutionResultDTO> UndoLastAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Starting undo operation");
            var undo = await _automationService.UndoLastAsync(cancellationToken).ConfigureAwait(false);
            var dto = BackendMapper.ToDto(undo, false, "Undo completed locally");

            _plannerLogger.LogUndoOperation(Guid.NewGuid(), dto);
            await LogAuditAsync("Undo", null, dto, cancellationToken).ConfigureAwait(false);
            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Undo operation failed");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetOnlineStatusAsync(bool isOnline, CancellationToken cancellationToken = default)
    {
        var state = await _backendStateStore.GetAsync(cancellationToken).ConfigureAwait(false);
        state.IsOnline = isOnline;
        await _backendStateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);

        if (isOnline)
        {
            await ProcessQueuedActionsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> GetOnlineStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await _backendStateStore.GetAsync(cancellationToken).ConfigureAwait(false);
        return state.IsOnline;
    }

    public Task<TokenStatusDTO> GetTokenStatusAsync(CancellationToken cancellationToken = default)
    {
        return _tokenService.GetStatusAsync(cancellationToken);
    }

    public async Task<int> GetQueuedCountAsync(CancellationToken cancellationToken = default)
    {
        var queue = await _queueStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return queue.Count;
    }

    /// <summary>
    /// Change the active planner strategy. Used for enabling/disabling LLM planner.
    /// </summary>
    public void SetPlannerStrategy(bool useLLMPlanner)
    {
        _plannerStrategy.SetUseLLMPlanner(useLLMPlanner);
    }

    /// <summary>
    /// Get current planner strategy information for debugging/UI display.
    /// </summary>
    public PlannerStrategyInfo GetPlannerStrategyInfo()
    {
        return _plannerStrategy.GetStrategyInfo();
    }

    public async Task<ExecutionResultDTO> ProcessQueuedActionsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await _backendStateStore.GetAsync(cancellationToken).ConfigureAwait(false);
            if (!state.IsOnline)
            {
                return new ExecutionResultDTO(0, 0, Array.Empty<FailedExecutionItemDTO>(), true, "Still offline");
            }

            var queue = (await _queueStore.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (queue.Count == 0)
            {
                return new ExecutionResultDTO(0, 0, Array.Empty<FailedExecutionItemDTO>(), false, "Queue empty");
            }

            _plannerLogger.LogQueuedPlanProcessing(queue.Count, DateTime.UtcNow);

            var remainingQueue = new List<QueuedPlan>();
            var successCount = 0;
            var failures = new List<FailedExecutionItemDTO>();

            foreach (var queued in queue)
            {
                var tokenAllowed = await _tokenService.TryDeductAsync(1, cancellationToken).ConfigureAwait(false);
                if (!tokenAllowed)
                {
                    remainingQueue.Add(queued);
                    var tokenStatus = await _tokenService.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                    _plannerLogger.LogTokenLimitReached(tokenStatus.Used, tokenStatus.DailyLimit);
                    continue;
                }

                try
                {
                    var result = await _automationService.ExecuteAsync(BackendMapper.ToDomain(queued.Plan), cancellationToken)
                        .ConfigureAwait(false);

                    successCount += result.SuccessCount;
                    failures.AddRange(result.FailedItems.Select(item => new FailedExecutionItemDTO(
                        item.ActionType.ToString(),
                        item.SourcePath,
                        item.DestinationPath,
                        item.Error)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Queued plan execution failed. PlanId: {PlanId}", queued.Plan.Id);
                    remainingQueue.Add(queued);
                    failures.Add(new FailedExecutionItemDTO("Queue", queued.Plan.Description, string.Empty, ex.Message));
                }
            }

            await _queueStore.ReplaceAllAsync(remainingQueue, cancellationToken).ConfigureAwait(false);

            var queuedResult = new ExecutionResultDTO(
                successCount,
                failures.Count,
                failures,
                false,
                remainingQueue.Count == 0 ? "Queued actions processed" : "Queued actions partially processed");

            _plannerLogger.LogQueuedPlanProcessingCompleted(queuedResult, remainingQueue.Count);
            await LogAuditAsync("ProcessQueue", null, queuedResult, cancellationToken).ConfigureAwait(false);
            return queuedResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task LogAuditAsync(string operation, PlanDTO? plan, ExecutionResultDTO result, CancellationToken cancellationToken)
    {
        var entry = new ExecutionAuditEntry
        {
            OccurredAtUtc = DateTime.UtcNow,
            Operation = operation,
            Plan = plan,
            Result = result
        };

        return _auditStore.AppendAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<SuggestionDTO>> GetSuggestionsAsync(string? userInput = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _suggestionService.GetSuggestionsAsync(userInput, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve suggestions for input: {Input}", userInput ?? "(none)");
            return Array.Empty<SuggestionDTO>();
        }
    }
}
