namespace ZayFlow.Backend.DTOs;

public sealed record ExecutionResultDTO(
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<FailedExecutionItemDTO> FailedItems,
    bool WasQueued,
    string Message);
