namespace ZayFlow.Backend.Contracts;

public interface ISubscriptionProvider
{
    Task<string> GetTierAsync(CancellationToken cancellationToken = default);
    Task<int> GetDailyTokenLimitAsync(CancellationToken cancellationToken = default);
}
