namespace ZayFlow.Core.Domain.Models;

public sealed record ExecutionResult
{
    public int SuccessCount { get; }
    public int FailureCount { get; }
    public IReadOnlyList<FailedExecutionItem> FailedItems { get; }
    public IReadOnlyList<ExecutedAction> ExecutedActions { get; }

    public ExecutionResult(
        int successCount,
        int failureCount,
        IReadOnlyList<FailedExecutionItem> failedItems,
        IReadOnlyList<ExecutedAction> executedActions)
    {
        SuccessCount = successCount;
        FailureCount = failureCount;
        FailedItems = failedItems ?? throw new ArgumentNullException(nameof(failedItems));
        ExecutedActions = executedActions ?? throw new ArgumentNullException(nameof(executedActions));
    }

    public static ExecutionResult Empty => new(0, 0, Array.Empty<FailedExecutionItem>(), Array.Empty<ExecutedAction>());
}
