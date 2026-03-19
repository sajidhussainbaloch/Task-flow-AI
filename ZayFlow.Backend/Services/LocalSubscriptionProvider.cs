using ZayFlow.Backend.Contracts;

namespace ZayFlow.Backend.Services;

public sealed class LocalSubscriptionProvider : ISubscriptionProvider
{
    public Task<string> GetTierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("Free");
    }

    public Task<int> GetDailyTokenLimitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(100);
    }
}
