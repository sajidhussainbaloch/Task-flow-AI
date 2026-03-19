using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Persistence;

public sealed class JsonQueuedPlanStore : IQueuedPlanStore
{
    public async Task<IReadOnlyList<QueuedPlan>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var queued = await JsonFileHelper.ReadOrDefaultAsync(StoragePathProvider.QueuePath, new List<QueuedPlan>(), cancellationToken)
            .ConfigureAwait(false);
        return queued;
    }

    public async Task EnqueueAsync(QueuedPlan queuedPlan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queuedPlan);

        var list = (await GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        list.Add(queuedPlan);

        await JsonFileHelper.WriteAsync(StoragePathProvider.QueuePath, list, cancellationToken).ConfigureAwait(false);
    }

    public Task ReplaceAllAsync(IReadOnlyList<QueuedPlan> queuedPlans, CancellationToken cancellationToken = default)
    {
        var copy = queuedPlans?.ToList() ?? new List<QueuedPlan>();
        return JsonFileHelper.WriteAsync(StoragePathProvider.QueuePath, copy, cancellationToken);
    }
}
