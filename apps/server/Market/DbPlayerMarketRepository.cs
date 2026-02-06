using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database.Models.Shard;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using Microsoft.EntityFrameworkCore;

namespace ACE.Server.Market;

/// <summary>
/// Shard-database backed implementation of IPlayerMarketRepository.
/// </summary>
public sealed class DbPlayerMarketRepository : IPlayerMarketRepository
{
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

        using var context = new ShardDbContext();

        var snapshotJson = MarketListingSnapshotSerializer.TryCreateSnapshotJson(item);

        var listing = new PlayerMarketListing
        {
            SellerAccountId = seller.Character.AccountId,
            SellerCharacterId = seller.Character.Id,
            SellerName = seller.Name,
            ItemGuid = item.Guid.Full,
            ItemBiotaId = (uint)item.Biota.Id,
            ItemWeenieClassId = item.WeenieClassId,
            ItemSnapshotJson = snapshotJson,
            OriginalValue = item.Value ?? 0,
            ListedPrice = listedPrice,
            CurrencyType = (int)currencyType,
            MarketVendorTier = vendorTier,
            ItemTier = itemTier,
            WieldReq = wieldReq,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + TimeSpan.FromSeconds(lifetimeSeconds),
            IsCancelled = false,
            IsSold = false
        };

        context.PlayerMarketListings.Add(listing);
        context.SaveChanges();

        MarketListingEvents.RaiseListingCreated(listing.Id);

        return listing;
    }

    public IEnumerable<PlayerMarketListing> GetListingsForVendorTier(int vendorTier, DateTime nowUtc)
    {
        using var context = new ShardDbContext();

        var baseQuery = context.PlayerMarketListings
            .AsNoTracking()
            .Where(l => !l.IsSold && !l.IsCancelled && l.ExpiresAtUtc > nowUtc);

        // Tier 0 market vendors show "non-tier" listings.
        // Those are currently stored with null ItemTier (and MarketVendorTier==0).
        // Additionally, treat any listing explicitly marked vendor tier 0 as visible on tier 0 vendors.
        if (vendorTier == 0)
        {
            return baseQuery
                .Where(l => !l.ItemTier.HasValue || l.MarketVendorTier == 0)
                .ToList();
        }

        return baseQuery
            .Where(l =>
                // Prefer ItemTier-based routing when available.
                // Fall back to MarketVendorTier for older listings where ItemTier is null.
                ((l.ItemTier.HasValue && l.ItemTier.Value == vendorTier)
                 || (!l.ItemTier.HasValue && l.MarketVendorTier == vendorTier)))
            .ToList();
    }

    public IEnumerable<PlayerMarketListing> GetExpiredListingsForAccount(uint accountId, DateTime nowUtc)
    {
        using var context = new ShardDbContext();

        return context.PlayerMarketListings
            .AsNoTracking()
            .Where(l =>
                l.SellerAccountId == accountId
                && !l.IsSold
                && l.ExpiresAtUtc <= nowUtc
                && l.ReturnedAtUtc == null)
            .ToList();
    }

    public IEnumerable<PlayerMarketListing> GetListingsForAccount(uint accountId, DateTime nowUtc)
    {
        using var context = new ShardDbContext();

        return context.PlayerMarketListings
            .AsNoTracking()
            .Where(l =>
                l.SellerAccountId == accountId
                && !l.IsCancelled
                && !l.IsSold
                && l.ExpiresAtUtc > nowUtc)
            .ToList();
    }

    public PlayerMarketListing? GetListingByItemGuid(uint itemGuid)
    {
        using var context = new ShardDbContext();

        return context.PlayerMarketListings
            .AsNoTracking()
            .FirstOrDefault(l => l.ItemGuid == itemGuid);
    }

    public PlayerMarketListing? GetListingByItemBiotaId(uint itemBiotaId)
    {
        using var context = new ShardDbContext();

        return context.PlayerMarketListings
            .AsNoTracking()
            .FirstOrDefault(l => l.ItemBiotaId == itemBiotaId);
    }

    public PlayerMarketListing? GetListingById(int listingId)
    {
        using var context = new ShardDbContext();

        return context.PlayerMarketListings
            .AsNoTracking()
            .FirstOrDefault(l => l.Id == listingId);
    }

    public bool MarkListingSold(PlayerMarketListing listing, Player buyer)
    {
        using var context = new ShardDbContext();

        var now = DateTime.UtcNow;

        var updated = context.PlayerMarketListings
            .Where(l => l.Id == listing.Id && !l.IsSold && !l.IsCancelled)
            .ExecuteUpdate(setters => setters
                .SetProperty(l => l.IsSold, true)
                .SetProperty(l => l.SoldAtUtc, now)
                .SetProperty(l => l.BuyerAccountId, buyer.Character.AccountId)
                .SetProperty(l => l.BuyerCharacterId, buyer.Character.Id)
                .SetProperty(l => l.BuyerName, buyer.Name));

        return updated > 0;
    }

    public void CancelListing(PlayerMarketListing listing)
    {
        using var context = new ShardDbContext();

        var entity = context.PlayerMarketListings.SingleOrDefault(l => l.Id == listing.Id);
        if (entity == null)
        {
            return;
        }

        entity.IsCancelled = true;
        context.SaveChanges();
    }

    public void ExpireListings(DateTime nowUtc)
    {
        using var context = new ShardDbContext();

        // Use set-based update so EF doesn't attempt to materialize PlayerMarketListing
        // (which can include columns not yet present in DB, e.g. ReturnedAtUtc).
        context.PlayerMarketListings
            .Where(l => !l.IsSold && !l.IsCancelled && l.ExpiresAtUtc <= nowUtc)
            .ExecuteUpdate(setters => setters.SetProperty(l => l.IsCancelled, true));
    }

    public PlayerMarketPayout CreatePayout(PlayerMarketListing listing, int amount)
    {
        using var context = new ShardDbContext();

        var payout = new PlayerMarketPayout
        {
            ListingId = listing.Id,
            SellerAccountId = listing.SellerAccountId,
            SellerCharacterId = listing.SellerCharacterId,
            Amount = amount,
            CurrencyType = listing.CurrencyType,
            Status = (int)MarketPayoutStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.PlayerMarketPayouts.Add(payout);
        context.SaveChanges();

        return payout;
    }

    public IEnumerable<PlayerMarketPayout> GetPendingPayouts(uint accountId)
    {
        using var context = new ShardDbContext();

        return context.PlayerMarketPayouts
            .AsNoTracking()
            .Where(p => p.SellerAccountId == accountId && p.Status == (int)MarketPayoutStatus.Pending)
            .ToList();
    }

    public void MarkPayoutClaimed(PlayerMarketPayout payout)
    {
        using var context = new ShardDbContext();

        var entity = context.PlayerMarketPayouts.SingleOrDefault(p => p.Id == payout.Id);
        if (entity == null)
        {
            return;
        }

        entity.Status = (int)MarketPayoutStatus.Claimed;
        entity.ClaimedAtUtc = DateTime.UtcNow;

        context.SaveChanges();
    }

    public PlayerMarketTransaction CreateTransaction(PlayerMarketListing listing, PlayerMarketPayout payout, Player buyer)
    {
        using var context = new ShardDbContext();

        var tx = new PlayerMarketTransaction
        {
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

        context.PlayerMarketTransactions.Add(tx);
        context.SaveChanges();

        return tx;
    }

    public void MarkListingReturned(PlayerMarketListing listing, DateTime returnedAtUtc)
    {
        using var context = new ShardDbContext();

        context.PlayerMarketListings
            .Where(l => l.Id == listing.Id)
            .ExecuteUpdate(setters => setters
                .SetProperty(l => l.ReturnedAtUtc, returnedAtUtc)
                .SetProperty(l => l.IsCancelled, true));
    }
}
