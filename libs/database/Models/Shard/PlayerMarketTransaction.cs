using System;

#nullable enable

namespace ACE.Database.Models.Shard;

public class PlayerMarketTransaction
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public int? PayoutId { get; set; }

    public uint SellerAccountId { get; set; }
    public uint? SellerCharacterId { get; set; }
    public string? SellerName { get; set; }

    public uint BuyerAccountId { get; set; }
    public uint? BuyerCharacterId { get; set; }
    public string? BuyerName { get; set; }

    public uint ItemWeenieClassId { get; set; }
    public uint ItemGuid { get; set; }
    public uint ItemBiotaId { get; set; }
    public string? ItemName { get; set; }

    public int Quantity { get; set; }

    public int Price { get; set; }
    public int FeeAmount { get; set; }
    public int SellerNetAmount { get; set; }
    public int CurrencyType { get; set; } // MarketCurrencyType
    public int MarketVendorTier { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }
}

#nullable disable
