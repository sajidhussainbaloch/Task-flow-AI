using ZayFlow.Backend.Contracts;
using ZayFlow.Backend.DTOs;
using ZayFlow.Backend.Models;

namespace ZayFlow.Backend.Services;

public sealed class LocalTokenService
{
    private readonly ITokenStore _tokenStore;
    private readonly ISubscriptionProvider _subscriptionProvider;

    public LocalTokenService(ITokenStore tokenStore, ISubscriptionProvider subscriptionProvider)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _subscriptionProvider = subscriptionProvider ?? throw new ArgumentNullException(nameof(subscriptionProvider));
    }

    public async Task<TokenStatusDTO> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetNormalizedStateAsync(cancellationToken).ConfigureAwait(false);
        var remaining = Math.Max(0, state.DailyLimit - state.Used);

        return new TokenStatusDTO(state.Used, remaining, state.DailyLimit, state.WindowDate, state.Tier);
    }

    public async Task<bool> TryDeductAsync(int amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return true;
        }

        var state = await GetNormalizedStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Used + amount > state.DailyLimit)
        {
            return false;
        }

        state.Used += amount;
        await _tokenStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<TokenState> GetNormalizedStateAsync(CancellationToken cancellationToken)
    {
        var state = await _tokenStore.GetAsync(cancellationToken).ConfigureAwait(false);

        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
        if (state.WindowDate != nowDate)
        {
            state.WindowDate = nowDate;
            state.Used = 0;
        }

        state.Tier = await _subscriptionProvider.GetTierAsync(cancellationToken).ConfigureAwait(false);
        state.DailyLimit = await _subscriptionProvider.GetDailyTokenLimitAsync(cancellationToken).ConfigureAwait(false);

        await _tokenStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }
}
