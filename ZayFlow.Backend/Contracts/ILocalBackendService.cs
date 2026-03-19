using ZayFlow.Backend.DTOs;
using ZayFlow.Backend.Services;

namespace ZayFlow.Backend.Contracts;

public interface ILocalBackendService
{
    Task<PlanDTO> GeneratePlanAsync(string userInput, CancellationToken cancellationToken = default);
    Task<PreviewDTO> PreviewPlanAsync(PlanDTO plan, CancellationToken cancellationToken = default);
    Task<ExecutionResultDTO> ExecutePlanAsync(PlanDTO plan, CancellationToken cancellationToken = default);
    Task<ExecutionResultDTO> UndoLastAsync(CancellationToken cancellationToken = default);

    Task SetOnlineStatusAsync(bool isOnline, CancellationToken cancellationToken = default);
    Task<bool> GetOnlineStatusAsync(CancellationToken cancellationToken = default);
    Task<TokenStatusDTO> GetTokenStatusAsync(CancellationToken cancellationToken = default);
    Task<int> GetQueuedCountAsync(CancellationToken cancellationToken = default);
    Task<ExecutionResultDTO> ProcessQueuedActionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SuggestionDTO>> GetSuggestionsAsync(string? userInput = null, CancellationToken cancellationToken = default);

    // New Step 6 methods for planner strategy management
    void SetPlannerStrategy(bool useLLMPlanner);
    PlannerStrategyInfo GetPlannerStrategyInfo();
}
