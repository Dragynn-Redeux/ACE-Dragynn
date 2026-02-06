using System;
using System.Linq;
using ACE.Server.WorldObjects;

namespace ACE.Server.Market;

internal static class MarketEscrowGuard
{
    public static bool ShouldPreserveBiotaOnDestroy(WorldObject obj)
    {
        if (obj?.Biota == null)
        {
            return false;
        }

        // Only relevant for DB-backed objects.
        if (!obj.BiotaOriginatedFromOrHasBeenSavedToDatabase())
        {
            return false;
        }

        try
        {
            var biotaId = obj.Biota.Id;
            using var context = new ACE.Database.Models.Shard.ShardDbContext();

            // If any active listing references this biota, preserve it.
            // Active = not sold, not cancelled, not returned, and not expired.
            var nowUtc = DateTime.UtcNow;
            return context.PlayerMarketListings.Any(l =>
                l.ItemBiotaId == biotaId
                && !l.IsSold
                && !l.IsCancelled
                && l.ReturnedAtUtc == null
                && l.ExpiresAtUtc > nowUtc);
        }
        catch
        {
            // If the DB check fails for any reason, fall back to existing behavior.
            return false;
        }
    }
}
