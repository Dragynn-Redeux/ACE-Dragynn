using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database.Models.Shard;
using ACE.Server.Market;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

public interface IPlayerMarketRepository
{
    PlayerMarketListing CreateListingFromWorldObject(
        Player seller,
        WorldObject item,
        int listedPrice,
        MarketCurrencyType currencyType,
        int vendorTier,
        int? wieldReq,
        int? itemTier);

    IEnumerable<PlayerMarketListing> GetListingsForVendorTier(int vendorTier, DateTime nowUtc);

    IEnumerable<PlayerMarketListing> GetListingsForAccount(uint accountId, DateTime nowUtc);

    IEnumerable<PlayerMarketListing> GetExpiredListingsForAccount(uint accountId, DateTime nowUtc);

    void MarkListingReturned(PlayerMarketListing listing, DateTime returnedAtUtc);

    PlayerMarketListing? GetListingByItemGuid(uint itemGuid);

    PlayerMarketListing? GetListingByItemBiotaId(uint itemBiotaId);

    PlayerMarketListing? GetListingById(int listingId);

    bool MarkListingSold(PlayerMarketListing listing, Player buyer);

    void CancelListing(PlayerMarketListing listing);

    void ExpireListings(DateTime nowUtc);

    PlayerMarketPayout CreatePayout(PlayerMarketListing listing, int amount);

    IEnumerable<PlayerMarketPayout> GetPendingPayouts(uint accountId);

    void MarkPayoutClaimed(PlayerMarketPayout payout);

    PlayerMarketTransaction CreateTransaction(PlayerMarketListing listing, PlayerMarketPayout payout, Player buyer);
}

/// <summary>
/// Temporary in-memory implementation of IPlayerMarketRepository.
/// Replace internals with real DB persistence when ready.
/// </summary>
public sealed class PlayerMarketRepository : IPlayerMarketRepository
{
    private readonly Dictionary<int, PlayerMarketListing> _listingsById = new();
    private readonly Dictionary<uint, PlayerMarketListing> _listingsByItemGuid = new();
    private readonly Dictionary<uint, PlayerMarketListing> _listingsByItemBiotaId = new();
    private readonly Dictionary<int, PlayerMarketPayout> _payoutsById = new();
    private readonly Dictionary<int, PlayerMarketTransaction> _transactionsById = new();

    private int _nextListingId = 1;
    private int _nextPayoutId = 1;
    private int _nextTransactionId = 1;

    public PlayerMarketListing CreateListingFromWorldObject(
        Player seller,
        WorldObject item,
        int listedPrice,
        MarketCurrencyType currencyType,
        int vendorTier,
        int? wieldReq,
        int? itemTier)
    {
        var now = DateTime.UtcNow;
        var lifetimeSeconds = PropertyManager
            .GetDouble("market_listing_lifetime_seconds", MarketServiceLocator.Config.ListingLifetime.TotalSeconds)
            .Item;

        if (lifetimeSeconds < 0)
        {
            lifetimeSeconds = 0;
        }

        var listing = new PlayerMarketListing
        {
            Id = _nextListingId++,
            SellerAccountId = seller.Character.AccountId,
            SellerCharacterId = seller.Character.Id,
            SellerName = seller.Name,
            ItemGuid = item.Guid.Full,
             ItemBiotaId = (uint)item.Biota.Id,
            ItemWeenieClassId = item.WeenieClassId,
            OriginalValue = item.Value ?? 0,
            ListedPrice = listedPrice,
            CurrencyType = (int)currencyType,
            MarketVendorTier = vendorTier,
            ItemTier = itemTier,
            WieldReq = wieldReq,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + TimeSpan.FromSeconds(lifetimeSeconds)
        };

        _listingsById[listing.Id] = listing;
        _listingsByItemGuid[listing.ItemGuid] = listing;
        _listingsByItemBiotaId[listing.ItemBiotaId] = listing;
        MarketListingEvents.RaiseListingCreated(listing.Id);

        return listing;
    }

    public IEnumerable<PlayerMarketListing> GetListingsForVendorTier(int vendorTier, DateTime nowUtc)
    {
        var baseQuery = _listingsById.Values.Where(l =>
            !l.IsSold
            && !l.IsCancelled
            && l.ExpiresAtUtc > nowUtc);

        if (vendorTier == 0)
        {
            return baseQuery.Where(l => !l.ItemTier.HasValue || l.MarketVendorTier == 0);
        }

        return baseQuery.Where(l =>
            // Prefer ItemTier-based routing when available.
            // Fall back to MarketVendorTier for older listings where ItemTier is null.
            ((l.ItemTier.HasValue && l.ItemTier.Value == vendorTier)
             || (!l.ItemTier.HasValue && l.MarketVendorTier == vendorTier)));
    }

    public IEnumerable<PlayerMarketListing> GetListingsForAccount(uint accountId, DateTime nowUtc)
    {
        return _listingsById.Values.Where(l =>
            l.SellerAccountId == accountId &&
            !l.IsCancelled &&
            !l.IsSold &&
            l.ExpiresAtUtc > nowUtc);
    }

    public IEnumerable<PlayerMarketListing> GetExpiredListingsForAccount(uint accountId, DateTime nowUtc)
    {
        return _listingsById.Values.Where(l =>
            l.SellerAccountId == accountId
            && !l.IsSold
            && l.ExpiresAtUtc <= nowUtc
            && !l.ReturnedAtUtc.HasValue);
    }

    public PlayerMarketListing? GetListingByItemGuid(uint itemGuid)
    {
        _listingsByItemGuid.TryGetValue(itemGuid, out var listing);
        return listing;
    }

    public PlayerMarketListing? GetListingByItemBiotaId(uint itemBiotaId)
    {
        _listingsByItemBiotaId.TryGetValue(itemBiotaId, out var listing);
        return listing;
    }

    public PlayerMarketListing? GetListingById(int listingId)
    {
        _listingsById.TryGetValue(listingId, out var listing);
        return listing;
    }

    public bool MarkListingSold(PlayerMarketListing listing, Player buyer)
    {
        if (listing.IsSold || listing.IsCancelled)
        {
            return false;
        }

        listing.IsSold = true;
        listing.SoldAtUtc = DateTime.UtcNow;
        listing.BuyerAccountId = buyer.Character.AccountId;
        listing.BuyerCharacterId = buyer.Character.Id;
        listing.BuyerName = buyer.Name;

        return true;
    }

    public void CancelListing(PlayerMarketListing listing)
    {
        listing.IsCancelled = true;
    }

    public void ExpireListings(DateTime nowUtc)
    {
        foreach (var listing in _listingsById.Values)
        {
            if (!listing.IsSold && !listing.IsCancelled && listing.ExpiresAtUtc <= nowUtc)
            {
                listing.IsCancelled = true;
            }
        }
    }

    public PlayerMarketPayout CreatePayout(PlayerMarketListing listing, int amount)
    {
        var payout = new PlayerMarketPayout
        {
            Id = _nextPayoutId++,
            ListingId = listing.Id,
            SellerAccountId = listing.SellerAccountId,
            SellerCharacterId = listing.SellerCharacterId,
            Amount = amount,
            CurrencyType = listing.CurrencyType,
            Status = (int)MarketPayoutStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _payoutsById[payout.Id] = payout;
        return payout;
    }

    public IEnumerable<PlayerMarketPayout> GetPendingPayouts(uint accountId)
    {
        return _payoutsById.Values.Where(p =>
            p.SellerAccountId == accountId &&
            p.Status == (int)MarketPayoutStatus.Pending);
    }

    public void MarkPayoutClaimed(PlayerMarketPayout payout)
    {
        if (_payoutsById.TryGetValue(payout.Id, out var existing))
        {
            existing.Status = (int)MarketPayoutStatus.Claimed;
            existing.ClaimedAtUtc = DateTime.UtcNow;
        }
    }

    public PlayerMarketTransaction CreateTransaction(PlayerMarketListing listing, PlayerMarketPayout payout, Player buyer)
    {
        var tx = new PlayerMarketTransaction
        {
            Id = _nextTransactionId++,
            ListingId = listing.Id,
            PayoutId = payout.Id,
            SellerAccountId = listing.SellerAccountId,
            SellerCharacterId = listing.SellerCharacterId,
            SellerName = listing.SellerName,
            BuyerAccountId = buyer.Character.AccountId,
            BuyerCharacterId = buyer.Character.Id,
            BuyerName = buyer.Name,
            ItemWeenieClassId = listing.ItemWeenieClassId,
            ItemGuid = listing.ItemGuid,
            ItemBiotaId = listing.ItemBiotaId,
            ItemName = null,
            Quantity = 1,
            Price = listing.ListedPrice,
            FeeAmount = 0,
            SellerNetAmount = listing.ListedPrice,
            CurrencyType = listing.CurrencyType,
            MarketVendorTier = listing.MarketVendorTier,
            CreatedAtUtc = DateTime.UtcNow
        };

        _transactionsById[tx.Id] = tx;
        return tx;
    }

    public void MarkListingReturned(PlayerMarketListing listing, DateTime returnedAtUtc)
    {
        if (listing == null)
        {
            return;
        }

        listing.ReturnedAtUtc = returnedAtUtc;
        listing.IsCancelled = true;
    }
}
