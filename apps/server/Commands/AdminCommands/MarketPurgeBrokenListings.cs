using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

public static class MarketPurgeBrokenListings
{
    [CommandHandler(
        "market-purge-broken",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        "Scans all market listings and cancels any active listings whose escrow biota record is missing.",
        "(dryrun|purge)"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        var doPurge = parameters.Length > 0 && parameters[0].Equals("purge", StringComparison.OrdinalIgnoreCase);

        using var context = new ACE.Database.Models.Shard.ShardDbContext();
        var now = DateTime.UtcNow;

        var active = context.PlayerMarketListings
            .Where(l => !l.IsSold && !l.IsCancelled && l.ReturnedAtUtc == null && l.ExpiresAtUtc > now)
            .ToList();

        var broken = new List<ACE.Database.Models.Shard.PlayerMarketListing>();
        foreach (var listing in active)
        {
            if (listing.ItemBiotaId <= 0)
            {
                continue;
            }

            try
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                if (biota == null)
                {
                    broken.Add(listing);
                }
            }
            catch
            {
                broken.Add(listing);
            }
        }

        if (broken.Count == 0)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "No broken active market listings found.", ChatMessageType.WorldBroadcast);
            return;
        }

        var summary = $"Broken active listings found: {broken.Count:N0} / {active.Count:N0}.";
        if (!doPurge)
        {
            summary += " Use `@market-purge-broken purge` to cancel them.";
            CommandHandlerHelper.WriteOutputInfo(session, summary, ChatMessageType.WorldBroadcast);
            return;
        }

        foreach (var listing in broken)
        {
            // Cancel only; items cannot be restored when the biota is missing.
            var entity = context.PlayerMarketListings.SingleOrDefault(l => l.Id == listing.Id);
            if (entity != null)
            {
                entity.IsCancelled = true;
            }
        }

        context.SaveChanges();

        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"Cancelled {broken.Count:N0} broken active listing(s). Items could not be restored because escrow biota records were missing.",
            ChatMessageType.WorldBroadcast);
    }
}
