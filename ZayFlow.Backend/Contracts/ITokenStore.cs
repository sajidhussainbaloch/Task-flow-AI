using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Contracts;

public interface ITokenStore
{
    Task<TokenState> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TokenState state, CancellationToken cancellationToken = default);
}
