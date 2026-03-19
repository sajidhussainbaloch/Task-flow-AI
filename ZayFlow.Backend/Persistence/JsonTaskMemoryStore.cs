using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Persistence;

public sealed class JsonTaskMemoryStore : ITaskMemoryStore
{
    public Task<TaskMemoryState> GetAsync(CancellationToken cancellationToken = default)
    {
        return JsonFileHelper.ReadOrDefaultAsync(StoragePathProvider.TaskMemoryPath, new TaskMemoryState(), cancellationToken);
    }

    public Task SaveAsync(TaskMemoryState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.UpdatedAtUtc = DateTime.UtcNow;
        return JsonFileHelper.WriteAsync(StoragePathProvider.TaskMemoryPath, state, cancellationToken);
    }
}
