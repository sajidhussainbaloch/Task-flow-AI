using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Persistence;

public sealed class JsonBackendStateStore
{
    public Task<BackendState> GetAsync(CancellationToken cancellationToken = default)
    {
        return JsonFileHelper.ReadOrDefaultAsync(StoragePathProvider.BackendStatePath, new BackendState(), cancellationToken);
    }

    public Task SaveAsync(BackendState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonFileHelper.WriteAsync(StoragePathProvider.BackendStatePath, state, cancellationToken);
    }
}
