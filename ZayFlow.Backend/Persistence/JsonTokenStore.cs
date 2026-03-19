using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Persistence;

public sealed class JsonTokenStore : ITokenStore
{
    public Task<TokenState> GetAsync(CancellationToken cancellationToken = default)
    {
        var defaultState = new TokenState();
        return JsonFileHelper.ReadOrDefaultAsync(StoragePathProvider.TokensPath, defaultState, cancellationToken);
    }

    public Task SaveAsync(TokenState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonFileHelper.WriteAsync(StoragePathProvider.TokensPath, state, cancellationToken);
    }
}
