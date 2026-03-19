using ZayFlow.Core.Domain.Models;

namespace ZayFlow.Core.Abstractions;

public interface IUndoManager
{
    Task StoreBatchAsync(Plan plan, IReadOnlyList<ExecutedAction> executedActions, CancellationToken cancellationToken = default);
    Task<ExecutionResult> UndoLastAsync(CancellationToken cancellationToken = default);
}
