using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface IQueuedPlanStore
{
    Task<IReadOnlyList<QueuedPlan>> GetAllAsync(CancellationToken cancellationToken = default);
    Task EnqueueAsync(QueuedPlan queuedPlan, CancellationToken cancellationToken = default);
    Task ReplaceAllAsync(IReadOnlyList<QueuedPlan> queuedPlans, CancellationToken cancellationToken = default);
}
