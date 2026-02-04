using System;

namespace ACE.Server.Market;

public sealed class MarketBrokerSessionState
{
    public uint? BrokerGuid { get; set; }
    public uint? PendingItemGuid { get; set; }
    public uint? PendingItemWeenieClassId { get; set; }
    public int? PendingPrice { get; set; }
    public DateTime? PendingExpiresAtUtc { get; set; }

    public int? PendingCancelListingId { get; set; }

    public int? PendingCancelRequestedIndex { get; set; }
}
