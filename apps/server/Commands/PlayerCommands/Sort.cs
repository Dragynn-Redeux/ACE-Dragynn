using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity.Actions;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using ACE.Entity.Enum.Properties;
using System;

namespace ACE.Server.Commands.PlayerCommands;

public class Sort
{
    // sort
    [CommandHandler("sort", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0, "Show player main pack by WeenieType>ItemType>Name.", "")]
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

        // Pre-categorize named side packs
        var salvageSidePacks = allSidePacks
            .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name.Contains("Salvage Crate", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var quiverSidePacks = allSidePacks
            .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name.Contains("Quiver", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var componentPouches = allSidePacks
            .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name.Contains("Component Pouch", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var trophyPacks = allSidePacks
            .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name.Contains("Trophy Pack", StringComparison.OrdinalIgnoreCase))
            .ToList();

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
            var trophyCandidates = mainItems.Where(i =>
                (i.TrophyQuality ?? 0) > 0
                || (i.GetProperty((PropertyInt)476) ?? 0) > 0
            ).ToList();

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
            var ammoCandidates = mainItems.Where(i => i.WeenieType == WeenieType.Ammunition).ToList();

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
            var compCandidates = mainItems.Where(i => i.WeenieType == WeenieType.SpellComponent).ToList();

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
            var salvageCandidates = mainItems.Where(i => i.WeenieType == WeenieType.Salvage).ToList();

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
        ScanSidePacksAndSchedule(
            item => (item.TrophyQuality ?? 0) > 0 || (item.GetProperty((PropertyInt)476) ?? 0) > 0,
            trophyPacks
        );

        // Scan side packs for ammunition
        ScanSidePacksAndSchedule(
            item => item.WeenieType == WeenieType.Ammunition,
            quiverSidePacks
        );

        // Scan side packs for spell components
        ScanSidePacksAndSchedule(
            item => item.WeenieType == WeenieType.SpellComponent,
            componentPouches
        );

        // Scan side packs for salvage
        ScanSidePacksAndSchedule(
            item => item.WeenieType == WeenieType.Salvage,
            salvageSidePacks
        );

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

        // ---------- PART C: Sort main pack minimally (insertion-style: only move misplaced items). ----------
        var desiredMain = mainItems.OrderByDescending(i => i.WeenieType)
            .ThenByDescending(i => i.ItemType)
            .ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

        // ---------- PART D: Sort each side-pack minimally (same ordering rules) using snapshots ----------
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

            var desiredContainer = containerItems
                .OrderByDescending(i => i.WeenieType)
                .ThenByDescending(i => i.ItemType)
                .ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

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

            actionChain.AddAction(
                player,
                () =>
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Inventory sort complete. Scheduled {scheduled} moves; moved {movedToSide} items into side packs.",
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
