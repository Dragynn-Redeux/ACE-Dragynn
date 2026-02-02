using System;
using System.Linq;
using System.Text;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Market;

public static class MarketBroker
{
    public const string TemplateName = "Market Broker";

    private static string? TryGetListingItemName(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        if (listing == null)
        {
            return null;
        }

        // Prefer the exact persisted item name, if the biota still exists.
        if (listing.ItemBiotaId > 0)
        {
            try
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                var name = biota?.BiotaPropertiesString?.FirstOrDefault(p => p.Type == (ushort)PropertyString.Name)?.Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch
            {
                // ignore; fall back to weenie
            }
        }

        // Fallback: base weenie name.
        var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
        if (weenie?.PropertiesString != null
            && weenie.PropertiesString.TryGetValue(PropertyString.Name, out var weenieName)
            && !string.IsNullOrWhiteSpace(weenieName))
        {
            return weenieName;
        }

        return null;
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return $"{(int)Math.Floor(remaining.TotalDays)}d {remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)Math.Floor(remaining.TotalHours)}h {remaining.Minutes}m";
        }

        if (remaining.TotalMinutes >= 1)
        {
            return $"{(int)Math.Floor(remaining.TotalMinutes)}m {remaining.Seconds}s";
        }

        return $"{Math.Max(0, remaining.Seconds)}s";
    }

    private static string FormatListingDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{duration.TotalDays:N0} day(s)";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{duration.TotalHours:N0} hour(s)";
        }

        return $"{duration.TotalMinutes:N0} minute(s)";
    }

    private static WorldObject? ResolveBroker(Player player, WorldObject? broker)
    {
        if (broker != null)
        {
            return broker;
        }

        if (player == null)
        {
            return null;
        }

        if (_stateByPlayerGuid.TryGetValue(player.Guid.Full, out var state)
            && state.BrokerGuid.HasValue
            && player.CurrentLandblock != null)
        {
            return player.CurrentLandblock.GetObject(state.BrokerGuid.Value);
        }

        return null;
    }

    private static void SendTell(Player player, WorldObject? broker, string message)
    {
        if (player?.Session == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!message.StartsWith("Market Broker tells you, ", StringComparison.OrdinalIgnoreCase))
        {
            message = $"Market Broker tells you, {message}";
        }

        var speaker = ResolveBroker(player, broker);
        if (speaker != null)
        {
            // Some clients render NPC->player Tell packets as "You think".
            // Emit the same outgoing-tell style line the client shows when you tell someone.
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(message, ChatMessageType.Tell));
            return;
        }

        player.Session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Tell));
    }

    private static void SendTell(Player player, string message) => SendTell(player, null, message);

    public static bool IsMarketBroker(WorldObject npc)
    {
        if (npc == null)
        {
            return false;
        }

        return string.Equals(npc.GetProperty(PropertyString.Template), TemplateName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSellable(WorldObject item, out string reason)
    {
        if (item == null)
        {
            reason = "Item not found.";
            return false;
        }

        if (item.Attuned == AttunedStatus.Attuned)
        {
            reason = "Attuned items cannot be listed.";
            return false;
        }

        var t = item.ItemType;
        var ok = t == ItemType.Weapon
                 || t == ItemType.MeleeWeapon
                 || t == ItemType.MissileWeapon
                 || t == ItemType.Caster
                 || t == ItemType.Armor
                 || t == ItemType.Clothing
                 || t == ItemType.Jewelry
                 || t == ItemType.Food
                 || t == ItemType.Gem
                 || t == ItemType.TinkeringMaterial
                 || t == ItemType.Useless;

        if (!ok)
        {
            reason = "Only weapons, armor, clothing, jewelry, casters, gems, salvage, trophies, and consumables can be listed.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static void SendHelp(Player player, WorldObject? broker = null)
    {
        if (player?.Session == null)
        {
            return;
        }

        var pendingPayouts = MarketServiceLocator.PlayerMarketRepository
            .GetPendingPayouts(player.Character.AccountId)
            .Count();

        var activeListings = MarketServiceLocator.PlayerMarketRepository
            .GetListingsForAccount(player.Character.AccountId, DateTime.UtcNow)
            .Count();

        SendTell(
            player,
            broker,
            $"Market Broker tells you, \"Give me an item to start a listing.\"");

        SendTell(
            player,
            broker,
            $"Market Broker tells you, \"Say 'listings' to review your active listings ({activeListings}), 'cancel <id>' to cancel a listing, 'payouts' to see pending payouts ({pendingPayouts}), or 'claim expired' to reclaim expired listings.\"");
    }

    public static void HandleTalkDirect(Player player, WorldObject broker, string message)
    {
        if (player == null || broker == null)
        {
            return;
        }

        if (!IsMarketBroker(broker) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        // Track the broker we are interacting with so follow-up confirmations can reply from the broker.
        _stateByPlayerGuid.AddOrUpdate(
            player.Guid.Full,
            _ => new MarketBrokerSessionState { BrokerGuid = broker.Guid.Full, PendingItemGuid = null, PendingPrice = null, PendingCancelListingId = null },
            (_, state) =>
            {
                state.BrokerGuid = broker.Guid.Full;
                return state;
            });

        var input = message.Trim();

        if (TryParseCancelInput(input, out var cancelListingId))
        {
            StartCancelListing(player, broker, cancelListingId);
            return;
        }

        if (input.Equals("help", StringComparison.OrdinalIgnoreCase)
            || input.Equals("market", StringComparison.OrdinalIgnoreCase))
        {
            SendHelp(player, broker);
            return;
        }

        if (input.Equals("payouts", StringComparison.OrdinalIgnoreCase))
        {
            var payouts = MarketServiceLocator.PlayerMarketRepository
                .GetPendingPayouts(player.Character.AccountId)
                .ToList();

            if (payouts.Count == 0)
            {
                SendTell(player, broker, "You have no pending payouts.");
                return;
            }

            var sb = new StringBuilder();
            var orderedPayouts = payouts.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id).ToList();

            sb.AppendLine($"You have {orderedPayouts.Count} pending payout(s):");
            var displayId = 1;
            foreach (var p in orderedPayouts)
            {
                var listing = MarketServiceLocator.PlayerMarketRepository
                    .GetListingsForAccount(player.Character.AccountId, DateTime.MaxValue)
                    .FirstOrDefault(l => l.Id == p.ListingId);

                var itemName = listing != null
                    ? (TryGetListingItemName(listing) ?? $"WCID {listing.ItemWeenieClassId}")
                    : "Unknown item";

                var gross = listing?.ListedPrice ?? p.Amount;
                var fee = MarketServiceLocator.CalculateSaleFee(gross);
                sb.AppendLine($"#{displayId} | {p.Amount:N0} py (fee {fee:N0}) | {itemName}");
                displayId++;
            }
            sb.AppendLine();
            sb.AppendLine("Say 'claim payouts' to claim all pending payouts.");
            SendTell(player, broker, sb.ToString().TrimEnd());
            return;
        }

        if (input.Equals("claim payouts", StringComparison.OrdinalIgnoreCase)
            || input.Equals("claim", StringComparison.OrdinalIgnoreCase))
        {
            ClaimPendingPayouts(player, broker);
            return;
        }

        if (input.Equals("claim expired", StringComparison.OrdinalIgnoreCase)
            || input.Equals("claim expired listings", StringComparison.OrdinalIgnoreCase)
            || input.Equals("expired", StringComparison.OrdinalIgnoreCase))
        {
            ClaimExpiredListings(player, broker);
            return;
        }

        if (input.Equals("listings", StringComparison.OrdinalIgnoreCase))
        {
            var listings = MarketServiceLocator.PlayerMarketRepository
                .GetListingsForAccount(player.Character.AccountId, DateTime.UtcNow)
                .ToList();

            if (listings.Count == 0)
            {
                SendTell(player, broker, "You have no active listings.");
                return;
            }

            var now = DateTime.UtcNow;

            // Build output in chunks so we don't exceed tell size limits.
            // Also, order by soonest expiration so the most urgent listings show first.
            var ordered = listings
                .OrderBy(l => l.ExpiresAtUtc)
                .ThenBy(l => l.Id)
                .ToList();

            var header = $"You have {ordered.Count} active listing(s):";

            var sb = new StringBuilder();
            sb.AppendLine(header);

            // Rough limit to avoid overly long messages.
            const int maxTellLength = 850;

            var displayId = 1;
            foreach (var l in ordered)
            {
                var remaining = l.ExpiresAtUtc - now;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                var itemName = TryGetListingItemName(l) ?? $"WCID {l.ItemWeenieClassId}";
                var line = $"#{displayId} | {itemName} | {l.ListedPrice:N0} py | {FormatRemaining(remaining)} left";
                displayId++;

                // If adding another line would exceed the limit, flush and start a new tell.
                if (sb.Length + line.Length + Environment.NewLine.Length > maxTellLength)
                {
                    SendTell(player, broker, sb.ToString().TrimEnd());
                    sb.Clear();
                }

                sb.AppendLine(line);
            }

            if (sb.Length > 0)
            {
                SendTell(player, broker, sb.ToString().TrimEnd());
            }
            return;
        }

        if (TryParsePriceInput(input, out var price))
        {
            if (!TryGetPendingItemGuid(player, out var itemGuid))
            {
                SendTell(player, broker, "No pending item. Give me an item first.");
                return;
            }

            var item = player.FindObject(
                itemGuid,
                Player.SearchLocations.MyInventory | Player.SearchLocations.MyEquippedItems,
                out _,
                out _,
                out _);
            if (item == null)
            {
                ClearPendingItem(player);
                SendTell(player, broker, "I can't find that item anymore. Give it to me again.");
                return;
            }

            if (!IsSellable(item, out var reason))
            {
                ClearPendingItem(player);
                SendTell(player, broker, reason);
                return;
            }

            var duration = MarketServiceLocator.Config.ListingLifetime;
            var expiresAtUtc = DateTime.UtcNow + duration;

            // Store pending listing details and require a Yes/No confirmation.
            _stateByPlayerGuid.AddOrUpdate(
                player.Guid.Full,
                _ => new MarketBrokerSessionState
                {
                    BrokerGuid = broker.Guid.Full,
                    PendingItemGuid = item.Guid.Full,
                    PendingItemWeenieClassId = item.WeenieClassId,
                    PendingPrice = price,
                    PendingExpiresAtUtc = expiresAtUtc
                },
                (_, state) =>
                {
                    state.BrokerGuid = broker.Guid.Full;
                    state.PendingItemGuid = item.Guid.Full;
                    state.PendingItemWeenieClassId = item.WeenieClassId;
                    state.PendingPrice = price;
                    state.PendingExpiresAtUtc = expiresAtUtc;
                    return state;
                });

            var confirmText =
                $"List '{item.Name}' for {price:N0} pyreals?\n\n" +
                $"Duration: {FormatListingDuration(duration)}\n" +
                $"Expires: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC\n\n" +
                $"To cancel later: Tell the Market Broker \"listings\".";

            player.ConfirmationManager.EnqueueSend(
                new Confirmation_Custom(
                    player.Guid,
                    () => FinalizeConfirmedListing(player)
                ),
                confirmText);

            SendTell(player, broker, "Please confirm the listing.");
            return;
        }

        SendHelp(player, broker);
    }

    private static void ClaimPendingPayouts(Player player, WorldObject? broker = null)
    {
        if (player?.Session == null)
        {
            return;
        }

        var payouts = MarketServiceLocator.PlayerMarketRepository
            .GetPendingPayouts(player.Character.AccountId)
            .OrderBy(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .ToList();

        if (payouts.Count == 0)
        {
            SendTell(player, broker, "You have no pending payouts.");
            return;
        }

        var total = payouts.Sum(p => p.Amount);

        // Create coin stacks and ensure they can be added. If any add fails, abort.
        var created = new System.Collections.Generic.List<WorldObject>();
        var remaining = total;
        while (remaining > 0)
        {
            var stack = WorldObjectFactory.CreateNewWorldObject("coinstack");
            var toSet = Math.Min(remaining, stack.MaxStackSize ?? remaining);
            stack.SetStackSize(toSet);
            created.Add(stack);
            remaining -= toSet;
        }

        foreach (var stack in created)
        {
            if (!player.TryCreateInInventoryWithNetworking(stack))
            {
                foreach (var c in created)
                {
                    c.Destroy();
                }
                SendTell(player, broker, "You do not have enough pack space to claim your payouts.");
                return;
            }
        }

        foreach (var payout in payouts)
        {
            MarketServiceLocator.PlayerMarketRepository.MarkPayoutClaimed(payout);
        }

        SendTell(player, broker, $"Claimed {payouts.Count} payout(s) for a total of {total:N0} pyreals.");
    }

    private static void ClaimExpiredListings(Player player, WorldObject? broker = null)
    {
        if (player?.Session == null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        // Ensure expirations are marked before we query.
        MarketServiceLocator.PlayerMarketRepository.ExpireListings(now);

        var expired = MarketServiceLocator.PlayerMarketRepository
            .GetExpiredListingsForAccount(player.Character.AccountId, now)
            .Where(l => !l.IsSold)
            .OrderBy(l => l.ExpiresAtUtc)
            .ThenBy(l => l.Id)
            .ToList();

        if (expired.Count == 0)
        {
            SendTell(player, broker, "You have no expired listings to claim.");
            return;
        }

        var returned = 0;
        foreach (var listing in expired)
        {
            // Ensure listing is cancelled so it doesn't show as active.
            if (!listing.IsCancelled)
            {
                MarketServiceLocator.PlayerMarketRepository.CancelListing(listing);
            }

            if (listing.ItemBiotaId <= 0)
            {
                continue;
            }

            WorldObject item = null;
            try
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                if (biota != null)
                {
                    var entityBiota = Database.Adapter.BiotaConverter.ConvertToEntityBiota(biota);
                    item = WorldObjectFactory.CreateWorldObject(entityBiota);
                }
            }
            catch
            {
                item = null;
            }

            if (item == null)
            {
                continue;
            }

            var fee = MarketServiceLocator.CalculateCancellationFee(listing.ListedPrice);
            if (fee > 0 && (player.CoinValue ?? 0) < fee)
            {
                item.Destroy();
                SendTell(player, broker, $"You need {fee:N0} pyreals to pay the 5% expiration fee for '{item.Name}'.");
                break;
            }

            if (!player.TryCreateInInventoryWithNetworking(item))
            {
                item.Destroy();
                SendTell(player, broker, "You do not have enough pack space to claim your expired listings.");
                break;
            }

            if (fee > 0)
            {
                player.TryConsumeFromInventoryWithNetworking((uint)WeenieClassName.W_COINSTACK_CLASS, fee);
            }

            MarketServiceLocator.PlayerMarketRepository.MarkListingReturned(listing, DateTime.UtcNow);

            returned++;
        }

        if (returned == 0)
        {
            SendTell(player, broker, "Unable to restore any expired listing items.");
            return;
        }

        SendTell(player, broker, $"Claimed {returned} expired listing item(s)." );
    }

    private static bool TryParseCancelInput(string input, out int listingId)
    {
        listingId = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        if (!trimmed.StartsWith("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed.Substring("cancel".Length).Trim();
        if (rest.StartsWith("#", StringComparison.Ordinal))
        {
            rest = rest.Substring(1).Trim();
        }

        return int.TryParse(rest, out listingId) && listingId > 0;
    }

    private static void StartCancelListing(Player player, WorldObject broker, int listingId)
    {
        if (player?.Session == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var activeListings = MarketServiceLocator.PlayerMarketRepository
            .GetListingsForAccount(player.Character.AccountId, now)
            .OrderBy(l => l.ExpiresAtUtc)
            .ThenBy(l => l.Id)
            .ToList();

        // If the user typed an id that matches a DB id, allow that.
        // Otherwise interpret it as the displayed index (1..N).
        var listing = activeListings.FirstOrDefault(l => l.Id == listingId);
        if (listing == null && listingId > 0 && listingId <= activeListings.Count)
        {
            listing = activeListings[listingId - 1];
        }

        if (listing == null)
        {
            SendTell(player, broker, $"Listing #{listingId} not found (or not active). Use 'listings' to see your active listings.");
            return;
        }

        var itemName = TryGetListingItemName(listing) ?? $"WCID {listing.ItemWeenieClassId}";
        var confirmText =
            $"Cancel listing?\n\n" +
            $"Item: {itemName}\n" +
            $"Price: {listing.ListedPrice:N0} pyreals\n\n" +
            $"This will return the item to your inventory.";

        _stateByPlayerGuid.AddOrUpdate(
            player.Guid.Full,
            _ => new MarketBrokerSessionState { BrokerGuid = broker.Guid.Full, PendingCancelListingId = listing.Id },
            (_, state) =>
            {
                state.BrokerGuid = broker.Guid.Full;
                state.PendingCancelListingId = listing.Id;
                return state;
            });

        player.ConfirmationManager.EnqueueSend(
            new Confirmation_Custom(player.Guid, () => FinalizeConfirmedCancel(player)),
            confirmText);

        SendTell(player, broker, "Please confirm the cancellation.");
    }

    private static void FinalizeConfirmedCancel(Player player)
    {
        if (player?.Session == null)
        {
            return;
        }

        if (!_stateByPlayerGuid.TryGetValue(player.Guid.Full, out var state)
            || !state.PendingCancelListingId.HasValue)
        {
            SendTell(player, "No pending cancellation to confirm.");
            return;
        }

        var listingId = state.PendingCancelListingId.Value;
        state.PendingCancelListingId = null;

        var now = DateTime.UtcNow;
        var listing = MarketServiceLocator.PlayerMarketRepository
            .GetListingsForAccount(player.Character.AccountId, now)
            .FirstOrDefault(l => l.Id == listingId);

        if (listing == null)
        {
            SendTell(player, $"Listing #{listingId} not found (or not active). Use 'listings' to see your active listings.");
            return;
        }

        // Recreate the escrowed item and attempt to return it first.
        WorldObject item = null;
        if (listing.ItemBiotaId > 0)
        {
            try
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                if (biota != null)
                {
                    var entityBiota = Database.Adapter.BiotaConverter.ConvertToEntityBiota(biota);
                    item = WorldObjectFactory.CreateWorldObject(entityBiota);
                }
            }
            catch
            {
                // ignore
            }
        }

        if (item == null)
        {
            SendTell(player, "Unable to restore the escrowed item for this listing. Cancellation aborted.");
            return;
        }

        if (!player.TryCreateInInventoryWithNetworking(item))
        {
            // Do not cancel the listing if the player can't receive the item.
            item.Destroy();
            SendTell(player, "You do not have enough pack space to receive the item. Cancellation aborted.");
            return;
        }

        var fee = MarketServiceLocator.CalculateCancellationFee(listing.ListedPrice);
        if (fee > 0)
        {
            // Charge cancellation fee in pyreals.
            if ((player.CoinValue ?? 0) < fee)
            {
                SendTell(player, $"You need {fee:N0} pyreals to pay the 5% cancellation fee. Cancellation aborted.");
                // Roll back item return because cancellation did not complete.
                if (!player.TryRemoveFromInventoryWithNetworking(item.Guid, out _, Player.RemoveFromInventoryAction.GiveItem))
                {
                    // If we can't remove it, at least don't cancel the listing.
                }
                return;
            }

            player.TryConsumeFromInventoryWithNetworking((uint)WeenieClassName.W_COINSTACK_CLASS, fee);
        }

        MarketServiceLocator.PlayerMarketRepository.CancelListing(listing);
        SendTell(player, $"Cancelled listing #{listingId}." + (fee > 0 ? $" Cancellation fee: {fee:N0} pyreals." : ""));
    }

    private static void FinalizeConfirmedListing(Player player)
    {
        if (player?.Session == null)
        {
            return;
        }

        if (!_stateByPlayerGuid.TryGetValue(player.Guid.Full, out var state)
            || !state.PendingItemGuid.HasValue
            || !state.PendingPrice.HasValue)
        {
            SendTell(player, "No pending listing to confirm.");
            return;
        }

        var itemGuid = state.PendingItemGuid.Value;
        var price = state.PendingPrice.Value;

        var item = player.FindObject(
            itemGuid,
            Player.SearchLocations.MyInventory | Player.SearchLocations.MyEquippedItems,
            out _,
            out _,
            out var wasEquipped);
        if (item == null)
        {
            ClearPendingItem(player);
            SendTell(player, "I can't find that item anymore. Give it to me again.");
            return;
        }

        if (!IsSellable(item, out var reason))
        {
            ClearPendingItem(player);
            SendTell(player, reason);
            return;
        }

        // Escrow: remove the whole object from the player now.
        if (wasEquipped)
        {
            if (!player.TryDequipObjectWithNetworking(item.Guid, out _, Player.DequipObjectAction.GiveItem))
            {
                SendTell(player, "Unable to escrow item (failed to dequip).");
                return;
            }
        }
        else
        {
            if (!player.TryRemoveFromInventoryWithNetworking(item.Guid, out _, Player.RemoveFromInventoryAction.GiveItem))
            {
                SendTell(player, "Unable to escrow item (failed to remove from inventory).");
                return;
            }
        }

        // Persist item off-player. For now, keep the same biota record but clear ownership.
        item.ContainerId = (uint?)null;
        item.WielderId = (uint?)null;
        item.Location = null;
        item.SaveBiotaToDatabase();

        int? wieldReq = null;
        int? itemTier = null;

        // Determine tier so the listing can show up on the correct market vendor tier.
        // Weapons/Armor: derive tier from WieldDifficulty.
        // Clothing/Jewelry: derive tier from required level (wield difficulty if using level requirement).
        switch (item.ItemType)
        {
            case ItemType.Weapon:
            case ItemType.MeleeWeapon:
            case ItemType.MissileWeapon:
            case ItemType.Caster:
            case ItemType.Armor:
                if (item.WieldDifficulty.HasValue)
                {
                    wieldReq = item.WieldDifficulty.Value;
                    itemTier = LootGenerationFactory.GetTierFromWieldDifficulty(item.WieldDifficulty.Value);
                }

                break;

            case ItemType.Clothing:
            case ItemType.Jewelry:
                // prefer explicit required level if present
                if (item.WieldRequirements == WieldRequirement.Level && item.WieldDifficulty.HasValue)
                {
                    wieldReq = item.WieldDifficulty.Value;
                    itemTier = LootGenerationFactory.GetTierFromRequiredLevel(item.WieldDifficulty.Value);
                }
                else if (item.WieldDifficulty.HasValue)
                {
                    // fallback: treat wield difficulty as the level requirement if that's all we have
                    wieldReq = item.WieldDifficulty.Value;
                    itemTier = LootGenerationFactory.GetTierFromRequiredLevel(item.WieldDifficulty.Value);
                }

                break;
        }

        // Route the listing to the vendor tier that matches the item.
        // Do not fall back to the seller tier; if we can't classify, the listing fails.
        var isNonTier = item.ItemType == ItemType.Misc
                        || item.ItemType == ItemType.Useless
                        || item.ItemType == ItemType.Food
                        || item.ItemType == ItemType.CraftAlchemyBase
                        || item.ItemType == ItemType.CraftAlchemyIntermediate
                        || item.ItemType == ItemType.CraftCookingBase
                        || item.ItemType == ItemType.CraftFletchingBase
                        || item.ItemType == ItemType.CraftFletchingIntermediate
                        || item.ItemType == ItemType.Gem
                        || item.ItemType == ItemType.TinkeringMaterial;

        int vendorTier;
        if (isNonTier)
        {
            vendorTier = 0;
            itemTier = null;
            wieldReq = null;
        }
        else if (itemTier.HasValue)
        {
            vendorTier = itemTier.Value;
        }
        else
        {
            // Give the item back and abort. We do not want silent re-routing.
            var returned = player.TryCreateInInventoryWithNetworking(item);
            if (!returned)
            {
                item.Destroy();
            }

            ClearPendingItem(player);
            SendTell(player, "Unable to determine market tier for this item. Listing cancelled.");
            return;
        }

        MarketServiceLocator.PlayerMarketRepository.CreateListingFromWorldObject(
            player,
            item,
            price,
            MarketCurrencyType.Pyreal,
            vendorTier,
            wieldReq,
            itemTier,
            $"Listed by {player.Name}");

        ClearPendingItem(player);

        SendTell(player, $"Listed {item.Name} for {price:N0} pyreals.");
    }

    private static bool TryParsePriceInput(string input, out int price)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        if (int.TryParse(trimmed, out price) && price > 0)
        {
            return true;
        }

        if (trimmed.StartsWith("price ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed.Substring("price ".Length).Trim();
            return int.TryParse(rest, out price) && price > 0;
        }

        return false;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, MarketBrokerSessionState> _stateByPlayerGuid =
        new();

    public static bool TryGetPendingItemGuid(Player player, out uint itemGuid)
    {
        itemGuid = 0;
        if (player == null)
        {
            return false;
        }

        if (_stateByPlayerGuid.TryGetValue(player.Guid.Full, out var state)
            && state.PendingItemGuid.HasValue)
        {
            itemGuid = state.PendingItemGuid.Value;
            return true;
        }

        return false;
    }

    public static void ClearPendingItem(Player player)
    {
        if (player == null)
        {
            return;
        }

        _stateByPlayerGuid.TryRemove(player.Guid.Full, out _);
    }

    public static void StartListingFromRefuse(Player player, WorldObject item)
    {
        if (player == null || item == null)
        {
            return;
        }

        if (!IsSellable(item, out var reason))
        {
            SendTell(player, reason);
            return;
        }

        _stateByPlayerGuid[player.Guid.Full] = new MarketBrokerSessionState
        {
            PendingItemGuid = item.Guid.Full,
            PendingItemWeenieClassId = item.WeenieClassId
        };

        SendTell(player, $"Ready to list '{item.Name}'. Tell me the price (number of pyreals).");
    }
}
