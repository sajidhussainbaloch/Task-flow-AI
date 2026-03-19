using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface ITaskMemoryStore
{
    Task<TaskMemoryState> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TaskMemoryState state, CancellationToken cancellationToken = default);
}
