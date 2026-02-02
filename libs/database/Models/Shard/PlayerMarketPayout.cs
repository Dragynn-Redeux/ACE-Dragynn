using System;

namespace ACE.Database.Models.Shard;

#nullable enable

public class PlayerMarketPayout
{
    public int Id { get; set; }

    public int ListingId { get; set; }

    public uint SellerAccountId { get; set; }
    public uint? SellerCharacterId { get; set; }

    public int Amount { get; set; }

    public int CurrencyType { get; set; } // MarketCurrencyType
    public int Status { get; set; }       // MarketPayoutStatus

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }

    public ulong RowVersion { get; set; }
}

#nullable disable
