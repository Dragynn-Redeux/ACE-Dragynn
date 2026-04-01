using System;

namespace ACE.Server.Market;

public sealed class PlayerMarketConfig
{
    /// <summary>
    /// How long a listing remains active before it expires.
    /// </summary>
    public TimeSpan ListingLifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum number of active listings allowed per account.
    /// Set to 0 or negative to disable the limit.
    /// </summary>
    public int MaxActiveListingsPerAccount { get; init; } = 30;

    /// <summary>
    /// Whether sellers are paid in pyreals or trade notes by default.
    /// </summary>
    public MarketCurrencyType DefaultPayoutCurrency { get; init; } = MarketCurrencyType.TradeNote;

    /// <summary>
    /// If true, expired listings require the player to manually reclaim the item.
    /// If false, expired listings are automatically cancelled and the item is destroyed
    /// (not recommended unless you know what you're doing).
    /// </summary>
    public bool RequireManualReclaimOnExpire { get; init; } = true;

    /// <summary>
    /// If > 0, the server will periodically attempt to expire old listings in bulk.
    /// Otherwise, expiration is only evaluated lazily (on vendor / lister interaction).
    /// </summary>
    public TimeSpan? ExpirationSweepInterval { get; init; } = TimeSpan.FromMinutes(5);

}

/// <summary>
/// Currency type used for pricing and payouts.
/// </summary>
public enum MarketCurrencyType
{
    Pyreal = 0,
    TradeNote = 1
}

public enum MarketPayoutStatus
{
    Pending = 0,
    Claimed = 1,
    Expired = 2,
    Failed = 3
}
