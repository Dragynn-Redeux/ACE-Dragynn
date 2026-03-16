using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Market;

public static class MarketBroker
{
    private static readonly Serilog.ILogger MarketPayoutLog = Serilog.Log.ForContext(typeof(MarketBroker)).ForContext("Subsystem", "Market");
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext(typeof(MarketBroker));

    public const string TemplateName = "Market Broker";

    private static bool ShouldDisplayNameWithMaterial(WorldObject item)
    {
        if (item == null)
        {
            return false;
        }

        return item.WeenieType == WeenieType.Salvage
               || item.ItemType == ItemType.Armor
               || item.ItemType == ItemType.Clothing
               || item.ItemType == ItemType.Jewelry
               || item.ItemType == ItemType.MeleeWeapon
               || item.ItemType == ItemType.MissileWeapon
               || item.ItemType == ItemType.Caster
               || item.ItemType == ItemType.Weapon;
    }

    private static string? TryGetListingItemName(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        if (listing == null)
        {
            return null;
        }

        WorldObject TryBuildItemForDisplayFromBiota(uint biotaId)
        {
            var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(biotaId, true);
            if (biota == null)
            {
                return null;
            }

            var entityBiota = Database.Adapter.BiotaConverter.ConvertToEntityBiota(biota);
            return WorldObjectFactory.CreateWorldObject(entityBiota);
        }

        // Prefer the exact persisted item name, if the biota still exists.
        if (listing.ItemBiotaId > 0)
        {
            try
            {
                var item = TryBuildItemForDisplayFromBiota(listing.ItemBiotaId);
                if (item != null)
                {
                    var name = ShouldDisplayNameWithMaterial(item) ? item.NameWithMaterial : item.Name;
                    item.Destroy();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // ignore; fall back to weenie
            }
        }

        // Fallback: use persisted snapshot if present (contains material props for salvage).
        if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
        {
            try
            {
                var item = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                if (item != null)
                {
                    var name = ShouldDisplayNameWithMaterial(item) ? item.NameWithMaterial : item.Name;
                    item.Destroy();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
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

        if (item.AllowedWielder != null)
        {
            reason = "Character-bound items cannot be listed.";
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
                 || t == ItemType.Useless
                 || t == ItemType.Misc
                 || t == ItemType.Writable
                 || t == ItemType.SpellComponents
                 || t == ItemType.ManaStone;

        if (!ok)
        {
            reason = "Only weapons, armor, clothing, jewelry, casters, gems, scrolls, components, mana stones, salvage, trophies, and consumables can be listed.";
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

        var expiredListings = MarketServiceLocator.PlayerMarketRepository
            .GetExpiredListingsForAccount(player.Character.AccountId, DateTime.UtcNow)
            .Where(l => !l.IsSold)
            .Count();

        SendTell(
            player,
            broker,
            $"Market Broker tells you, \"We charge a {PropertyManager.GetDouble("market_listing_payout_fee").Item * 100:N0}% fee upon payout for our services.\"");

        SendTell(
            player,
            broker,
            $"Market Broker tells you, \"We charge a {PropertyManager.GetDouble("market_listing_cancellation_fee").Item * 100:N0}% fee to cancel a listed item.");

        SendTell(
            player,
            broker,
            $"Market Broker tells you, \"Give me an item to start a listing. Then tell me the pyreal price you'd like to list it for.\"");

        SendTell(
            player,
            broker,
            $"Market Broker tells you, \"Commands you can send me:\"");

        player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"- 'listings' to review your active listings ({activeListings}).", ChatMessageType.Tell));

        player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"- 'cancel <id>' to cancel a listing.", ChatMessageType.Tell));

        player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"- 'payouts' to see pending payouts ({pendingPayouts}).", ChatMessageType.Tell));

        player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"- 'claim expired' to reclaim expired listings ({expiredListings}).", ChatMessageType.Tell));
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
            _ => new MarketBrokerSessionState { BrokerGuid = broker.Guid.Full, PendingItemGuid = null, PendingPrice = null, PendingCancelListingId = null, PendingCancelRequestedIndex = null },
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
                    .GetListingById(p.ListingId);

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
            var maxPrice = PropertyManager.GetLong("market_listing_max_price", 1_000_000_000).Item;
            if (maxPrice > 0 && price > maxPrice)
            {
                SendTell(player, broker, $"Maximum listing price is {maxPrice:N0} pyreals.");
                return;
            }

            if (!TryGetPendingItemGuid(player, out var itemGuid))
            {
                SendTell(player, broker, "No pending item. Give me an item first.");
                return;
            }

            var item = player.FindObject(
                itemGuid,
                Player.SearchLocations.MyInventory,
                out _,
                out _,
                out _);
            if (item == null)
            {
                // If the player equipped the pending item, it won't be found in inventory.
                // Give a clearer message than the generic missing-item response.
                var equipped = player.FindObject(
                    itemGuid,
                    Player.SearchLocations.MyEquippedItems,
                    out _,
                    out _,
                    out var wasEquipped);

                ClearPendingItem(player);

                if (equipped != null && wasEquipped)
                {
                    SendTell(player, broker, "You must first unequip that item before listing it.");
                }
                else
                {
                    SendTell(player, broker, "I can't find that item anymore. Give it to me again.");
                }
                return;
            }

            if (!IsSellable(item, out var reason))
            {
                ClearPendingItem(player);
                SendTell(player, broker, reason);
                return;
            }

            var listingLengthSeconds = PropertyManager
                .GetDouble("market_listing_lifetime_seconds", MarketServiceLocator.Config.ListingLifetime.TotalSeconds).Item;

            var duration = TimeSpan.FromSeconds(listingLengthSeconds);
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
                $"Expires: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC\n" +
                $"Fee: You will be charged a {PropertyManager.GetDouble("market_listing_payout_fee").Item * 100:N0}% fee ({MarketServiceLocator.CalculateSaleFee(price):N0} pyreals) upon payout.\n\n" +
                $"To cancel your listing: Tell the Market Broker \"listings\", and then tell them \"cancel <id>\" to cancel a listing. You will be charged {PropertyManager.GetDouble("market_listing_cancellation_fee").Item * 100:N0}% of the listing price to cancel.";

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

        var notesBefore = GetTradeNotesInInventory(player);

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

        if (total <= 0)
        {
            SendTell(player, broker, "You have no pending payouts.");
            return;
        }

        var tradeNoteTotal = (total / 100) * 100;
        var pyrealRemainder = total % 100;

        var breakdown = GetTradeNoteBreakdown(tradeNoteTotal);

        // Pre-validate capacity for the full payout (no partial claims).
        var itemsToReceive = new ItemsToReceive(player);
        foreach (var (wcid, amount) in breakdown)
        {
            itemsToReceive.Add(wcid, amount);
        }
        if (pyrealRemainder > 0)
        {
            itemsToReceive.Add((uint)WeenieClassName.W_COINSTACK_CLASS, pyrealRemainder);
        }
        if (itemsToReceive.PlayerExceedsLimits)
        {
            if (itemsToReceive.PlayerExceedsAvailableBurden)
            {
                SendTell(player, broker, "You are too encumbered to claim your payouts.");
            }
            else if (itemsToReceive.PlayerOutOfInventorySlots)
            {
                SendTell(player, broker, "You do not have enough pack space to claim your payouts.");
            }
            else if (itemsToReceive.PlayerOutOfContainerSlots)
            {
                SendTell(player, broker, "You do not have enough container slots to claim your payouts.");
            }

            return;
        }

        // Create trade note stacks across denominations and ensure they can be added. If any add fails, abort.
        var created = new System.Collections.Generic.List<WorldObject>();
        foreach (var (wcid, amount) in breakdown)
        {
            if (amount <= 0)
            {
                continue;
            }

            var proto = WorldObjectFactory.CreateNewWorldObject(wcid);
            var maxStack = proto.MaxStackSize ?? 1;
            proto.Destroy();
            var remaining = amount;
            while (remaining > 0)
            {
                var stack = WorldObjectFactory.CreateNewWorldObject(wcid);
                var toSet = Math.Min(remaining, maxStack);
                stack.SetStackSize(toSet);
                created.Add(stack);
                remaining -= toSet;
            }
        }

        if (pyrealRemainder > 0)
        {
            var pyreals = WorldObjectFactory.CreateNewWorldObject((uint)WeenieClassName.W_COINSTACK_CLASS);
            pyreals.SetStackSize(pyrealRemainder);
            created.Add(pyreals);
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

        var notesAfter = GetTradeNotesInInventory(player);

        try
        {
            MarketPayoutLog.Information(
                "[MARKET PAYOUT] ClaimedPayouts={PayoutCount} Total={Total} Player='{PlayerName} ({PlayerAccountId})' \nNotesBefore={NotesBefore} \n NotesAfter={NotesAfter}",
                payouts.Count,
                total,
                player.Name,
                player.Character?.AccountId,
                notesBefore,
                notesAfter);
        }
        catch
        {
            // Do not allow logging failures to impact payout claims.
        }

        foreach (var payout in payouts)
        {
            MarketServiceLocator.PlayerMarketRepository.MarkPayoutClaimed(payout);
        }

        SendTell(player, broker, $"Claimed {payouts.Count} payout(s) for a total of {total:N0} pyreals.");
    }

    private static System.Collections.Generic.List<(uint wcid, int amount)> GetTradeNoteBreakdown(int totalPyreals)
    {
        // Prefer larger denominations to minimize created stacks.
        // Values are in pyreals per trade note.
        var denom = new (uint wcid, int value)[]
        {
            ((uint)WeenieClassName.W_TRADENOTE100000_CLASS, 100_000),
            ((uint)WeenieClassName.W_TRADENOTE50000_CLASS, 50_000),
            ((uint)WeenieClassName.W_TRADENOTE10000_CLASS, 10_000),
            ((uint)WeenieClassName.W_TRADENOTE5000_CLASS, 5_000),
            ((uint)WeenieClassName.W_TRADENOTE1000_CLASS, 1_000),
            ((uint)WeenieClassName.W_TRADENOTE500_CLASS, 500),
            ((uint)WeenieClassName.W_TRADENOTE100_CLASS, 100),
        };

        var remaining = Math.Max(0, totalPyreals);
        var result = new System.Collections.Generic.List<(uint wcid, int amount)>(denom.Length);

        foreach (var (wcid, value) in denom)
        {
            if (remaining <= 0)
            {
                break;
            }

            var count = remaining / value;
            if (count > 0)
            {
                result.Add((wcid, count));
                remaining -= count * value;
            }
        }

        return result;
    }

    private static Dictionary<string, int> GetTradeNotesInInventory(Player player)
    {
        var result = new Dictionary<string, int>
        {
            ["M"] = 0,
            ["D"] = 0,
            ["C"] = 0,
            ["L"] = 0,
            ["X"] = 0,
            ["V"] = 0,
            ["I"] = 0,
        };

        foreach (var item in player.GetAllPossessions())
        {
            var key = item.WeenieClassId switch
            {
                (uint)WeenieClassName.W_TRADENOTE100000_CLASS => "M",
                (uint)WeenieClassName.W_TRADENOTE50000_CLASS => "D",
                (uint)WeenieClassName.W_TRADENOTE10000_CLASS => "C",
                (uint)WeenieClassName.W_TRADENOTE5000_CLASS => "L",
                (uint)WeenieClassName.W_TRADENOTE1000_CLASS => "X",
                (uint)WeenieClassName.W_TRADENOTE500_CLASS => "V",
                (uint)WeenieClassName.W_TRADENOTE100_CLASS => "I",
                _ => null
            };

            if (key != null)
            {
                result[key] += item.StackSize ?? 1;
            }
        }

        return result;
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
                else if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
                {
                    item = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
                {
                    item = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                }
                else
                {
                    item = null;
                }
            }

            if (item == null)
            {
                continue;
            }

            var beforeCount = GetItemCountInInventory(player, item.WeenieClassId);

            if (!player.TryCreateInInventoryWithNetworking(item))
            {
                item.Destroy();
                SendTell(player, broker, "You do not have enough pack space to claim your expired listings.");
                break;
            }

            var afterCount = GetItemCountInInventory(player, item.WeenieClassId);

            try
            {
                MarketPayoutLog.Information(
                    "[MARKET EXPIRED CLAIM] ListingId={ListingId} ItemWCID={ItemWeenieClassId} ItemName='{ItemName}' Player='{PlayerName} ({PlayerAccountId})' InventoryBefore={InventoryBefore} InventoryAfter={InventoryAfter}",
                    listing.Id,
                    listing.ItemWeenieClassId,
                    item.Name,
                    player.Name,
                    player.Character?.AccountId,
                    beforeCount,
                    afterCount);
            }
            catch
            {
                // Do not allow logging failures to impact expired claims.
            }

            MarketServiceLocator.PlayerMarketRepository.MarkListingReturned(listing, DateTime.UtcNow);

            returned++;
        }

        if (returned == 0)
        {
            SendTell(player, broker, "Unable to restore any expired listing items.");
            return;
        }

        try
        {
            MarketPayoutLog.Information(
                "[MARKET EXPIRED CLAIM] ReturnedListings={Returned} Requested={Requested} Player='{PlayerName} ({PlayerAccountId})'",
                returned,
                expired.Count,
                player.Name,
                player.Character?.AccountId);
        }
        catch
        {
            // Do not allow logging failures to impact expired claims.
        }

        SendTell(player, broker, $"Claimed {returned} expired listing item(s)." );
    }

    private static int GetItemCountInInventory(Player player, uint weenieClassId)
    {
        var count = 0;
        foreach (var item in player.GetAllPossessions())
        {
            if (item.WeenieClassId == weenieClassId)
            {
                count += item.StackSize ?? 1;
            }
        }

        return count;
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
        int? requestedIndex = null;
        var listing = activeListings.FirstOrDefault(l => l.Id == listingId);
        if (listing == null && listingId > 0 && listingId <= activeListings.Count)
        {
            listing = activeListings[listingId - 1];
            requestedIndex = listingId;
        }

        if (listing == null)
        {
            SendTell(player, broker, $"Listing #{listingId} not found (or not active). Use 'listings' to see your active listings.");
            return;
        }

        var itemName = TryGetListingItemName(listing) ?? $"WCID {listing.ItemWeenieClassId}";

        var cancellationFeePercent = PropertyManager.GetDouble("market_listing_cancellation_fee").Item * 100;
        var cancellationFee = MarketServiceLocator.CalculateCancellationFee(listing.ListedPrice);
        var confirmText =
            $"Cancel listing?\n\n" +
            $"Item: {itemName}\n" +
            $"Price: {listing.ListedPrice:N0} pyreals\n" +
            $"Fee: {cancellationFee:N0} pyreals ({cancellationFeePercent:N0}%)\n\n" +
            $"This will return the item to your inventory.";

        _stateByPlayerGuid.AddOrUpdate(
            player.Guid.Full,
            _ => new MarketBrokerSessionState { BrokerGuid = broker.Guid.Full, PendingCancelListingId = listing.Id, PendingCancelRequestedIndex = requestedIndex },
            (_, state) =>
            {
                state.BrokerGuid = broker.Guid.Full;
                state.PendingCancelListingId = listing.Id;
                state.PendingCancelRequestedIndex = requestedIndex;
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
        var requestedIndex = state.PendingCancelRequestedIndex;
        state.PendingCancelListingId = null;
        state.PendingCancelRequestedIndex = null;

        var now = DateTime.UtcNow;
        var listing = MarketServiceLocator.PlayerMarketRepository
            .GetListingsForAccount(player.Character.AccountId, now)
            .FirstOrDefault(l => l.Id == listingId);

        if (listing == null)
        {
            SendTell(player, $"Listing #{listingId} not found (or not active). Use 'listings' to see your active listings.");
            return;
        }
        var cancelChargeAmount = MarketServiceLocator.CalculateCancellationFee(listing.ListedPrice);
        if (cancelChargeAmount > 0)
        {
            // Charge cancellation fee before returning the escrowed item.
            // Use the inventory consume routine as the source of truth for available currency.
            if (!player.TryConsumeFromInventoryWithNetworking((uint)WeenieClassName.W_COINSTACK_CLASS, cancelChargeAmount))
            {
                SendTell(player, $"You need {cancelChargeAmount:N0} pyreals to pay the {PropertyManager.GetDouble("market_listing_cancellation_fee").Item * 100:N0}% cancellation fee. Cancellation aborted.");
                return;
            }
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
                else
                {
                    Log.Warning(
                        "Market cancel failed: biota not found for listing {ListingId} (ItemBiotaId={ItemBiotaId}, ItemGuid={ItemGuid}, WCID={WCID}, SellerAccountId={SellerAccountId}, SellerCharacterId={SellerCharacterId})",
                        listing.Id,
                        listing.ItemBiotaId,
                        listing.ItemGuid,
                        listing.ItemWeenieClassId,
                        listing.SellerAccountId,
                        listing.SellerCharacterId);

                    if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
                    {
                        item = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Market cancel failed: exception while recreating escrow item for listing {ListingId} (ItemBiotaId={ItemBiotaId}, ItemGuid={ItemGuid}, WCID={WCID})",
                    listing.Id,
                    listing.ItemBiotaId,
                    listing.ItemGuid,
                    listing.ItemWeenieClassId);

                if (item == null && !string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
                {
                    item = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                }
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

        MarketServiceLocator.PlayerMarketRepository.CancelListing(listing);
        var display = requestedIndex.HasValue ? requestedIndex.Value : listingId;
        SendTell(player, $"Cancelled listing #{display}." + (cancelChargeAmount > 0 ? $" Cancellation fee: {cancelChargeAmount:N0} pyreals." : ""));
    }

    private static void FinalizeConfirmedListing(Player player)
    {
        if (player?.Session == null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        var maxActive = PropertyManager
            .GetLong("market_max_active_listings_per_account", MarketServiceLocator.Config.MaxActiveListingsPerAccount)
            .Item;
        if (maxActive > 0)
        {
            try
            {
                // Ensure expirations are marked before we count.
                MarketServiceLocator.PlayerMarketRepository.ExpireListings(now);

                var activeCount = MarketServiceLocator.PlayerMarketRepository
                    .GetListingsForAccount(player.Character.AccountId, now)
                    .Count();

                // Unclaimed expired listings should still count toward the limit.
                // This prevents accounts from building up too many expired listings.
                var unclaimedExpiredCount = MarketServiceLocator.PlayerMarketRepository
                    .GetExpiredListingsForAccount(player.Character.AccountId, now)
                    .Count(l => !l.IsSold);

                var totalCount = activeCount + unclaimedExpiredCount;

                if (totalCount >= maxActive)
                {
                    ClearPendingItem(player);
                    var maxMsg = $"You already have {activeCount} active listing(s)";
                    if (unclaimedExpiredCount > 0)
                    {
                        maxMsg += $" and {unclaimedExpiredCount} expired listing(s) awaiting claim";
                    }
                    maxMsg += $". Maximum is {maxActive}. Cancel a listing or claim expired listings.";

                    SendTell(player, maxMsg);
                    return;
                }
            }
            catch
            {
                // If we can't validate the limit safely, don't block listing.
            }
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
            Player.SearchLocations.MyInventory,
            out _,
            out _,
            out var wasEquipped);
        if (item == null)
        {
            ClearPendingItem(player);
            SendTell(player, "I can't find that item anymore. Give it to me again.");
            return;
        }

        if (wasEquipped)
        {
            ClearPendingItem(player);
            SendTell(player, "You must first unequip that item before listing it.");
            return;
        }

        if (!IsSellable(item, out var reason))
        {
            ClearPendingItem(player);
            SendTell(player, reason);
            return;
        }

        // Escrow: remove the whole object from the player now.
        if (!player.TryRemoveFromInventoryWithNetworking(item.Guid, out _, Player.RemoveFromInventoryAction.GiveItem))
        {
            SendTell(player, "Unable to escrow item (failed to remove from inventory).");
            return;
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
                // Sigil Trinkets store their required-level gate on the secondary fields.
                if (item.WieldRequirements2 == WieldRequirement.Level && item.WieldDifficulty2.HasValue)
                {
                    wieldReq = item.WieldDifficulty2.Value;
                    itemTier = LootGenerationFactory.GetTierFromRequiredLevel(item.WieldDifficulty2.Value);
                    break;
                }

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
                        || item.ItemType == ItemType.TinkeringMaterial
                        || item.ItemType == ItemType.Writable
                        || item.ItemType == ItemType.ManaStone
                        || item.ItemType == ItemType.SpellComponents;

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
            itemTier);

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

        // Reject equipped items immediately.
        if (item.WielderId.HasValue && item.WielderId.Value == player.Guid.Full)
        {
            SendTell(player, "You must first unequip that item before listing it.");
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
