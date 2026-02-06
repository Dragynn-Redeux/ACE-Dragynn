using System;

#nullable enable

namespace ACE.Database.Models.Shard;

public class PlayerMarketListing
{
    public int Id { get; set; }

    public uint SellerAccountId { get; set; }
    public uint? SellerCharacterId { get; set; }
    public string? SellerName { get; set; }

    public uint ItemGuid { get; set; }
    public uint ItemBiotaId { get; set; }
    public uint ItemWeenieClassId { get; set; }

    public string? ItemSnapshotJson { get; set; }

    public int OriginalValue { get; set; }
    public int ListedPrice { get; set; }

    public int CurrencyType { get; set; }         // map MarketCurrencyType enum
    public int MarketVendorTier { get; set; }
    public int? ItemTier { get; set; }
    public int? WieldReq { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public bool IsCancelled { get; set; }
    public bool IsSold { get; set; }

    public uint? BuyerAccountId { get; set; }
    public uint? BuyerCharacterId { get; set; }
    public string? BuyerName { get; set; }

    public DateTime? SoldAtUtc { get; set; }

    public DateTime? ReturnedAtUtc { get; set; }

    public ulong RowVersion { get; set; }
}

#nullable disable
