using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using System;

namespace ACE.Server.Commands.PlayerCommands;

public class Sort
{
    // sort
    [CommandHandler("sort", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0, "Sorts your pack and side packs: files items into named and inscribed packs, combines stacks, and orders items by kind (see /bank for pack tags).", "")]
    public static void HandleSort(Session session, params string[] parameters)
    {
        SortBag(session);
    }

    private static void SortBag(Session session, ulong discordChannel = 0)
    {
        var player = session.Player;
        if (player == null)
        {
            return;
        }

        // Progress start
        session.Network.EnqueueSend(new GameMessageSystemChat("Inventory sort starting...", ChatMessageType.System));

        // Helper comparison (descending weenieType, then itemType, then name)
        static int CompareForSort(WorldObject a, WorldObject b)
        {
            var cmp = Comparer<WeenieType>.Default.Compare(b.WeenieType, a.WeenieType);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = Comparer<ItemType>.Default.Compare(b.ItemType, a.ItemType);
            if (cmp != 0)
            {
                return cmp;
            }

            return string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
        }

        // Snapshot of main-pack items (exclude side packs / backpack-slot items) in server placement order
        var mainItems = player.Inventory.Values
            .Where(i => !i.UseBackpackSlot)
            .OrderBy(i => i.PlacementPosition ?? int.MaxValue)
            .ToList();

        if (mainItems.Count <= 1)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat("Nothing to sort (main pack empty or single item).", ChatMessageType.System));
            return;
        }

        // Find side-pack containers (all containers) and take snapshots of their inventories
        var allSidePacks = player.Inventory.Values
            .OfType<Container>()
            .OrderBy(c => c.PlacementPosition ?? int.MaxValue)
            .ToList();

        var containerSnapshots = new Dictionary<uint, List<WorldObject>>();
        var originalPackCounts = new Dictionary<uint, int>();
        foreach (var sp in allSidePacks)
        {
            var list = sp.Inventory.Values
                .OrderBy(i => i.PlacementPosition ?? int.MaxValue)
                .ToList();

            containerSnapshots[sp.Guid.Full] = list;
            originalPackCounts[sp.Guid.Full] = list.Count;
        }

        // Pre-categorize named side packs (the bank fills these first too, see NamedPacks)
        var salvageSidePacks = allSidePacks.Where(c => NamedPacks.KindOf(c) == NamedPackKind.SalvageCrate).ToList();

        var quiverSidePacks = allSidePacks.Where(c => NamedPacks.KindOf(c) == NamedPackKind.Quiver).ToList();

        var componentPouches = allSidePacks.Where(c => NamedPacks.KindOf(c) == NamedPackKind.ComponentPouch).ToList();

        var trophyPacks = allSidePacks.Where(c => NamedPacks.KindOf(c) == NamedPackKind.TrophyPack).ToList();

        var actionChain = new ActionChain();
        var movesScheduled = 0;
        var itemsMovedToSidePacks = 0;

        // General schedule helper that updates snapshots
        void ScheduleMove(WorldObject item, Container sourceContainer, Container targetPack)
        {
            if (item == null || targetPack == null)
            {
                return;
            }

            // skip if target == source
            if (sourceContainer != null && sourceContainer.Guid.Full == targetPack.Guid.Full)
            {
                return;
            }

            // placement simulated as append
            var targetSnapshot = containerSnapshots.ContainsKey(targetPack.Guid.Full)
                ? containerSnapshots[targetPack.Guid.Full]
                : new List<WorldObject>();

            var placement = targetSnapshot.Count;
            var itemGuid = item.Guid.Full;
            var containerGuid = targetPack.Guid.Full;

            actionChain.AddDelaySeconds(0.03);
            actionChain.AddAction(
                player,
                () =>
                {
                    player.HandleActionPutItemInContainer(itemGuid, containerGuid, placement);
                }
            );

            // update source snapshot
            if (sourceContainer == null) // main pack
            {
                mainItems.RemoveAll(i => i.Guid == item.Guid);
            }
            else
            {
                if (containerSnapshots.TryGetValue(sourceContainer.Guid.Full, out var srcSnap))
                {
                    srcSnap.RemoveAll(i => i.Guid == item.Guid);
                }
            }

            // update target snapshot: append
            if (!containerSnapshots.ContainsKey(targetPack.Guid.Full))
            {
                containerSnapshots[targetPack.Guid.Full] = new List<WorldObject>();
            }
            containerSnapshots[targetPack.Guid.Full].Add(item);

            movesScheduled++;
            itemsMovedToSidePacks++;
        }

        // Helper to schedule a chat in-chain
        void ScheduleChat(string text)
        {
            actionChain.AddAction(player, () => session.Network.EnqueueSend(new GameMessageSystemChat(text, ChatMessageType.System)));
        }

        // ---------- PART A: Move appropriate items FROM MAIN PACK into named side packs ----------
        // Trophy items -> "Trophy Pack"
        if (trophyPacks.Count > 0)
        {
            var trophyCandidates = mainItems.Where(i => NamedPacks.Takes(NamedPackKind.TrophyPack, i)).ToList();

            foreach (var trophy in trophyCandidates)
            {
                var target = trophyPacks.FirstOrDefault(sp => sp.CanAddToInventory(trophy));
                if (target != null)
                {
                    ScheduleMove(trophy, null, target);
                }
            }
        }

        // Ammunition -> "Quiver"
        if (quiverSidePacks.Count > 0)
        {
            var ammoCandidates = mainItems.Where(i => NamedPacks.Takes(NamedPackKind.Quiver, i)).ToList();

            foreach (var ammo in ammoCandidates)
            {
                var target = quiverSidePacks.FirstOrDefault(sp => sp.CanAddToInventory(ammo));
                if (target != null)
                {
                    ScheduleMove(ammo, null, target);
                }
            }
        }

        // Spell components -> "Component Pouch"
        if (componentPouches.Count > 0)
        {
            var compCandidates = mainItems.Where(i => NamedPacks.Takes(NamedPackKind.ComponentPouch, i)).ToList();

            foreach (var comp in compCandidates)
            {
                var target = componentPouches.FirstOrDefault(sp => sp.CanAddToInventory(comp));
                if (target != null)
                {
                    ScheduleMove(comp, null, target);
                }
            }
        }

        // Salvage -> "Salvage Crate"
        if (salvageSidePacks.Count > 0)
        {
            var salvageCandidates = mainItems.Where(i => NamedPacks.Takes(NamedPackKind.SalvageCrate, i)).ToList();

            foreach (var salvage in salvageCandidates)
            {
                var targetPack = salvageSidePacks.FirstOrDefault(sp => sp.CanAddToInventory(salvage));
                if (targetPack != null)
                {
                    ScheduleMove(salvage, null, targetPack);
                }
            }
        }

        // ---------- PART B: SCAN SIDE-PACKS and move misfiled items INTO their proper named side packs ----------
        void ScanSidePacksAndSchedule(Func<WorldObject, bool> predicate, List<Container> targetPacks)
        {
            if (targetPacks.Count == 0)
            {
                return;
            }

            var candidates = new List<(Container source, WorldObject item)>();

            foreach (var source in allSidePacks)
            {
                // skip target packs themselves
                if (targetPacks.Any(t => t.Guid.Full == source.Guid.Full))
                {
                    continue;
                }

                if (!containerSnapshots.TryGetValue(source.Guid.Full, out var srcSnap))
                {
                    continue;
                }

                foreach (var item in srcSnap.ToList()) // snapshot enumerated copy
                {
                    if (predicate(item))
                    {
                        candidates.Add((source, item));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            foreach (var (source, item) in candidates)
            {
                // skip if already in one of the correct packs (defensive)
                if (targetPacks.Any(t => t.Guid.Full == source.Guid.Full))
                {
                    continue;
                }

                var target = targetPacks.FirstOrDefault(sp => sp.CanAddToInventory(item));
                if (target != null)
                {
                    ScheduleMove(item, source, target);
                }
            }
        }

        // Scan side packs for trophy items
        ScanSidePacksAndSchedule(item => NamedPacks.Takes(NamedPackKind.TrophyPack, item), trophyPacks);

        // Scan side packs for ammunition
        ScanSidePacksAndSchedule(item => NamedPacks.Takes(NamedPackKind.Quiver, item), quiverSidePacks);

        // Scan side packs for spell components
        ScanSidePacksAndSchedule(item => NamedPacks.Takes(NamedPackKind.ComponentPouch, item), componentPouches);

        // Scan side packs for salvage
        ScanSidePacksAndSchedule(item => NamedPacks.Takes(NamedPackKind.SalvageCrate, item), salvageSidePacks);

        // ---------- PART B2: file items into carried packs inscribed for them (see /bank) ----------
        // A pack inscribed with category words ("weapons", "gems, keys", ...) collects those items from the main pack
        // and from other packs, best fit first: a pack inscribed for exactly that category before one that takes it
        // among others ("weapons" before "gear"). An item already in a pack that fits at least as well stays put.
        // Nothing is taken out of the named packs above (quivers, component pouches, trophy packs, salvage crates),
        // other specialized packs, or packs inscribed "keep", so this never fights the rules above from run to run.
        var taggedPacks = allSidePacks
            .Select(p => (Pack: p, Tags: BankCategories.ParseInscription(p.Inscription)))
            .Where(t => t.Tags.Categories != BankCategory.None)
            .ToList();

        if (taggedPacks.Count > 0)
        {
            var namedPacks = trophyPacks.Concat(quiverSidePacks).Concat(componentPouches).Concat(salvageSidePacks).ToList();

            int FreeSlots(Container pack)
            {
                containerSnapshots.TryGetValue(pack.Guid.Full, out var snapshot);
                return (pack.ItemCapacity ?? 0) - (snapshot?.Count ?? 0);
            }

            var sources = new List<(Container Pack, List<WorldObject> Items, BankCategory Tags)> { (null, mainItems, BankCategory.None) };

            foreach (var pack in allSidePacks)
            {
                var tags = BankCategories.ParseInscription(pack.Inscription);

                if (
                    tags.Keep
                    || (pack.MerchandiseItemTypes ?? 0) != 0
                    || namedPacks.Contains(pack)
                    || !containerSnapshots.TryGetValue(pack.Guid.Full, out var snapshot)
                )
                {
                    continue;
                }

                sources.Add((pack, snapshot, tags.Categories));
            }

            foreach (var (source, items, sourceTags) in sources)
            {
                foreach (var item in items.ToList())
                {
                    var category = BankCategories.Tags(item);
                    if (category == BankCategory.None)
                    {
                        continue;
                    }

                    var currentFit = BankCategories.PackFit(sourceTags, category);

                    var target = taggedPacks
                        .Where(t => t.Pack != source)
                        .Select(t => (t.Pack, Fit: BankCategories.PackFit(t.Tags.Categories, category)))
                        .Where(t => t.Fit > currentFit && FreeSlots(t.Pack) > 0 && t.Pack.CanHoldItemType(item))
                        .OrderByDescending(t => t.Fit)
                        .Select(t => t.Pack)
                        .FirstOrDefault();

                    if (target != null)
                    {
                        ScheduleMove(item, source, target);
                    }
                }
            }
        }

        // Emit a single combined per-pack message (covers moves from main + side packs)
        // Use the difference between originalPackCounts and containerSnapshots to avoid false positives.
        foreach (var pack in allSidePacks)
        {
            var guid = pack.Guid.Full;
            originalPackCounts.TryGetValue(guid, out var orig);
            containerSnapshots.TryGetValue(guid, out var snap);
            var after = snap?.Count ?? 0;
            var moved = after - orig;
            if (moved > 0)
            {
                var name = pack.Name ?? $"container {guid:X8}";
                ScheduleChat($"Moved {moved} item(s) to {name}.");
            }
        }

        // ---------- PART C: Combine stacks of the same item into as few stacks as possible ----------
        // Groups items by the same compatibility fields HandleActionStackableMerge checks
        // (WeenieClassId, TrophyQuality, SpellDID, Spell2, BoostValue) across the main pack and
        // every side pack, then greedily drains the smallest stacks into the largest so duplicates
        // end up as full stacks instead of scattered partial ones. HandleActionStackableMerge
        // supports merging across the player's own containers directly (no relocation needed
        // first), so this doesn't have to wait for Part D/E's per-container position sort.
        var stacksCombined = 0;
        {
            var stackLocations = new List<(WorldObject Item, List<WorldObject> OwningList)>();

            foreach (var stackItem in mainItems)
            {
                if ((stackItem.MaxStackSize ?? 1) > 1)
                {
                    stackLocations.Add((stackItem, mainItems));
                }
            }

            foreach (var snapshot in containerSnapshots.Values)
            {
                foreach (var stackItem in snapshot)
                {
                    if ((stackItem.MaxStackSize ?? 1) > 1)
                    {
                        stackLocations.Add((stackItem, snapshot));
                    }
                }
            }

            // Real StackSize is only updated when the scheduled merge actually runs later, so we
            // track the post-merge size ourselves to decide the remaining merges in this pass.
            var simulatedStackSize = stackLocations.ToDictionary(sl => sl.Item.Guid.Full, sl => sl.Item.StackSize ?? 1);

            var groups = stackLocations
                .GroupBy(sl => (sl.Item.WeenieClassId, sl.Item.TrophyQuality, sl.Item.SpellDID, sl.Item.Spell2, sl.Item.BoostValue))
                .Where(g => g.Count() > 1);

            foreach (var group in groups)
            {
                // Largest stacks absorb the smallest first, so the result is the fewest possible stacks.
                var members = group.OrderByDescending(sl => simulatedStackSize[sl.Item.Guid.Full]).ToList();

                var targetIdx = 0;
                var sourceIdx = members.Count - 1;

                while (targetIdx < sourceIdx)
                {
                    var target = members[targetIdx].Item;
                    var source = members[sourceIdx].Item;

                    var room = (target.MaxStackSize ?? 1) - simulatedStackSize[target.Guid.Full];
                    if (room <= 0)
                    {
                        targetIdx++;
                        continue;
                    }

                    var moveAmount = Math.Min(room, simulatedStackSize[source.Guid.Full]);

                    var sourceGuid = source.Guid.Full;
                    var targetGuid = target.Guid.Full;

                    actionChain.AddDelaySeconds(0.03);
                    actionChain.AddAction(player, () => player.HandleActionStackableMerge(sourceGuid, targetGuid, moveAmount));

                    simulatedStackSize[target.Guid.Full] += moveAmount;
                    simulatedStackSize[source.Guid.Full] -= moveAmount;

                    movesScheduled++;

                    if (simulatedStackSize[source.Guid.Full] <= 0)
                    {
                        members[sourceIdx].OwningList.RemoveAll(i => i.Guid == source.Guid);
                        stacksCombined++;
                        sourceIdx--;
                    }

                    if (simulatedStackSize[target.Guid.Full] >= (target.MaxStackSize ?? 1))
                    {
                        targetIdx++;
                    }
                }
            }
        }

        // ---------- PART D: Sort main pack minimally (insertion-style: only move misplaced items). ----------
        // Same order as /bank sort: by category, then each kind of item's own rules, then name (ItemSortOrder).
        var sortOrder = Comparer<WorldObject>.Create(ItemSortOrder.Compare);

        var desiredMain = mainItems.OrderBy(i => i, sortOrder).ToList();

        {
            var current = new List<WorldObject>(mainItems);

            for (var i = 0; i < desiredMain.Count; i++)
            {
                if (i >= current.Count)
                {
                    break;
                }

                var desiredItem = desiredMain[i];

                // already in correct slot
                if (current[i].Guid == desiredItem.Guid)
                {
                    continue;
                }

                var j = current.FindIndex(x => x.Guid == desiredItem.Guid);
                if (j < 0)
                {
                    // missing (maybe moved to sidepack) - skip
                    continue;
                }

                var itemGuid = desiredItem.Guid.Full;
                var containerGuid = player.Guid.Full; // player's main pack
                var placement = i;

                actionChain.AddDelaySeconds(0.03);
                actionChain.AddAction(
                    player,
                    () =>
                    {
                        player.HandleActionPutItemInContainer(itemGuid, containerGuid, placement);
                    }
                );

                // update simulated current list
                var itemObj = current[j];
                current.RemoveAt(j);
                current.Insert(i, itemObj);

                movesScheduled++;
            }
        }

        // ---------- PART E: Sort each side-pack minimally (same ordering rules) using snapshots ----------
        foreach (var sidePack in allSidePacks)
        {
            if (!containerSnapshots.TryGetValue(sidePack.Guid.Full, out var containerItems))
            {
                continue;
            }

            if (containerItems.Count <= 1)
            {
                continue;
            }

            // Salvage crates: directly assign PlacementPosition and send ContainId messages so
            // client updates immediately without needing HandleActionPutItemInContainer round-trips.
            if (salvageSidePacks.Any(sp => sp.Guid.Full == sidePack.Guid.Full))
            {
                var sortedSalvage = containerItems.OrderBy(i => i, sortOrder).ToList();

                for (var i = 0; i < sortedSalvage.Count; i++)
                {
                    sortedSalvage[i].PlacementPosition = i;
                    session.Network.EnqueueSend(new GameEventItemServerSaysContainId(session, sortedSalvage[i], sidePack));
                }

                movesScheduled += sortedSalvage.Count;
                continue;
            }

            var desiredContainer = containerItems.OrderBy(i => i, sortOrder).ToList();

            var current = new List<WorldObject>(containerItems);

            for (var i = 0; i < desiredContainer.Count; i++)
            {
                if (i >= current.Count)
                {
                    break;
                }

                var desiredItem = desiredContainer[i];

                if (current[i].Guid == desiredItem.Guid)
                {
                    continue;
                }

                var j = current.FindIndex(x => x.Guid == desiredItem.Guid);
                if (j < 0)
                {
                    continue;
                }

                var itemGuid = desiredItem.Guid.Full;
                var containerGuid = sidePack.Guid.Full;
                var placement = i;

                actionChain.AddDelaySeconds(0.03);
                actionChain.AddAction(
                    player,
                    () =>
                    {
                        player.HandleActionPutItemInContainer(itemGuid, containerGuid, placement);
                    }
                );

                var itemObj = current[j];
                current.RemoveAt(j);
                current.Insert(i, itemObj);

                movesScheduled++;
            }
        }

        // Final progress message action so clients see completion after moves processed
        if (actionChain.FirstElement != null)
        {
            var scheduled = movesScheduled;
            var movedToSide = itemsMovedToSidePacks;
            var combined = stacksCombined;

            actionChain.AddAction(
                player,
                () =>
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Inventory sort complete. Scheduled {scheduled} moves; moved {movedToSide} items into side packs; combined {combined} stacks.",
                            ChatMessageType.System
                        )
                    );
                }
            );

            actionChain.EnqueueChain();
        }
        else
        {
            session.Network.EnqueueSend(new GameMessageSystemChat("No moves necessary; inventory already sorted.", ChatMessageType.System));
        }
    }
}
