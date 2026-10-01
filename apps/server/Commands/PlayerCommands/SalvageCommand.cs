using System;
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

namespace ACE.Server.Commands.PlayerCommands;

public class SalvageCommand
{
    private static readonly HashSet<uint> SalvageCrateWcids = new() { 1055060, 1055070, 1055080 };

    [CommandHandler("salvage", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0,
        "Salvage bag management. Use /salvage combine or /salvage refine <max_wk>.")]
    public static void HandleSalvage(Session session, params string[] parameters)
    {
        if (parameters.Length == 0)
        {
            ShowHelp(session);
            return;
        }

        switch (parameters[0].ToLowerInvariant())
        {
            case "combine":
                HandleCombine(session);
                break;
            case "refine":
                HandleRefine(session, parameters);
                break;
            default:
                ShowHelp(session);
                break;
        }
    }

    private static void ShowHelp(Session session)
    {
        session.Network.EnqueueSend(new GameMessageSystemChat(
            "Salvage Commands:\n" +
            "  /salvage combine — Combines all bags of the same material and workmanship together.\n" +
            "  /salvage refine <max_wk> — Upgrades bags of Workmanship <max_wk> into the next tier (consumes 2 source units per 1 gained).",
            ChatMessageType.System
        ));
    }

    // --- helpers ---

    private static List<WorldObject> GetAllSalvageBags(Player player)
    {
        var bags = new List<WorldObject>();

        foreach (var item in player.Inventory.Values)
        {
            if (item.WeenieType == WeenieType.Salvage)
            {
                bags.Add(item);
            }

            if (item is Container container)
            {
                foreach (var inner in container.Inventory.Values)
                {
                    if (inner.WeenieType == WeenieType.Salvage)
                    {
                        bags.Add(inner);
                    }
                }
            }
        }

        return bags;
    }

    private static Container GetSalvageCrate(Player player)
    {
        return player.Inventory.Values
            .OfType<Container>()
            .FirstOrDefault(c => SalvageCrateWcids.Contains(c.WeenieClassId));
    }

    private static void SendSortedCrateContainIds(Session session, Container crate)
    {
        var sorted = crate.Inventory.Values
            .OrderBy(Salvage.GetSalvageBagSortKey)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].PlacementPosition = i;
            session.Network.EnqueueSend(new GameEventItemServerSaysContainId(session, sorted[i], crate));
        }
    }

    // --- /salvage combine ---

    private static void HandleCombine(Session session)
    {
        var player = session.Player;
        var bags = GetAllSalvageBags(player);

        var groups = bags
            .GroupBy(b => ((int)(b.MaterialType ?? MaterialType.Unknown), (int)Math.Round(b.Workmanship ?? 1)))
            .Where(g => g.Count() > 1)
            .Select(g => g.OrderByDescending(b => b.Structure ?? 0).ToList())
            .ToList();

        if (groups.Count == 0)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                "No salvage bags can be combined (no duplicates found).",
                ChatMessageType.System
            ));
            return;
        }

        var mergeCount = groups.Sum(g => g.Count - 1);
        var msg = $"Combine {mergeCount} bag(s) into {groups.Count} bag(s)? Proceed?";

        if (!player.ConfirmationManager.EnqueueSend(
            new Confirmation_Custom(player.Guid, () => ExecuteCombine(session, groups)),
            msg
        ))
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                "A confirmation is already pending.",
                ChatMessageType.System
            ));
        }
    }

    private static void ExecuteCombine(Session session, List<List<WorldObject>> groups)
    {
        var player = session.Player;
        var actionChain = new ActionChain();
        var merged = 0;

        foreach (var group in groups)
        {
            var target = group[0];

            for (var i = 1; i < group.Count; i++)
            {
                var sourceRef = group[i];
                var targetRef = target;

                actionChain.AddDelaySeconds(0.03);
                actionChain.AddAction(player, () =>
                {
                    if (Salvage.PourSalvageBag(sourceRef, targetRef) <= 0)
                    {
                        return;
                    }

                    player.EnqueueBroadcast(new GameMessageUpdateObject(targetRef));

                    if ((sourceRef.Structure ?? 0) <= 0)
                    {
                        player.TryConsumeFromInventoryWithNetworking(sourceRef);
                        player.Session.Network.EnqueueSend(new GameMessageDeleteObject(sourceRef));
                    }
                    else
                    {
                        player.EnqueueBroadcast(new GameMessageUpdateObject(sourceRef));
                    }
                });

                merged++;
            }
        }

        var totalMerged = merged;
        actionChain.AddAction(player, () =>
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Combined {totalMerged} salvage bag(s).",
                ChatMessageType.System
            ));

            var crate = GetSalvageCrate(player);
            if (crate == null)
            {
                return;
            }

            var sortChain = new ActionChain();
            sortChain.AddDelaySeconds(0.1);
            sortChain.AddAction(player, () => SendSortedCrateContainIds(session, crate));
            sortChain.EnqueueChain();
        });

        actionChain.EnqueueChain();
    }

    // --- /salvage refine ---

    private static void HandleRefine(Session session, string[] parameters)
    {
        var player = session.Player;

        if (parameters.Length < 2 || !int.TryParse(parameters[1], out var maxWk) || maxWk < 1 || maxWk > 9)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                "Usage: /salvage refine <max_wk> (1-9).",
                ChatMessageType.System
            ));
            return;
        }

        var bags = GetAllSalvageBags(player);

        var byKey = bags
            .GroupBy(b => ((int)(b.MaterialType ?? MaterialType.Unknown), (int)Math.Round(b.Workmanship ?? 1)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var pairs = new List<(WorldObject source, WorldObject target)>();

        foreach (var source in bags)
        {
            var sourceWk = (int)Math.Round(source.Workmanship ?? 1);
            if (sourceWk > maxWk || (source.Structure ?? 0) < 2)
            {
                continue;
            }

            var mat = (int)(source.MaterialType ?? MaterialType.Unknown);
            var targetKey = (mat, sourceWk + 1);

            if (!byKey.TryGetValue(targetKey, out var candidates))
            {
                continue;
            }

            var target = candidates.FirstOrDefault(t => t.Guid != source.Guid);
            if (target != null)
            {
                pairs.Add((source, target));
            }
        }

        if (pairs.Count == 0)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                "No bags can be refined. A higher-tier bag of the same material must already exist as the upgrade target.",
                ChatMessageType.System
            ));
            return;
        }

        var msg = $"Refine {pairs.Count} bag(s) at 2:1 ratio? Proceed?";

        if (!player.ConfirmationManager.EnqueueSend(
            new Confirmation_Custom(player.Guid, () => ExecuteRefine(session, pairs)),
            msg
        ))
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                "A confirmation is already pending.",
                ChatMessageType.System
            ));
        }
    }

    private static void ExecuteRefine(Session session, List<(WorldObject source, WorldObject target)> pairs)
    {
        var player = session.Player;
        var actionChain = new ActionChain();
        var refined = 0;

        foreach (var (source, target) in pairs)
        {
            var sourceRef = source;
            var targetRef = target;

            actionChain.AddDelaySeconds(0.03);
            actionChain.AddAction(player, () =>
            {
                var sourceStruct = sourceRef.Structure ?? 0;
                if (sourceStruct < 2)
                {
                    return;
                }

                var targetWork = (int)(targetRef.Workmanship ?? 1);
                var targetSpace = (targetRef.MaxStructure ?? 1000) - (targetRef.Structure ?? 0);

                var unitsGained = sourceStruct / 2;
                if (unitsGained > targetSpace)
                {
                    unitsGained = targetSpace;
                }

                if (unitsGained < 1)
                {
                    return;
                }

                var unitsConsumed = unitsGained * 2;

                targetRef.Structure = (ushort)((targetRef.Structure ?? 0) + unitsGained);
                targetRef.Name = $"Salvage Wk{targetWork} ({targetRef.Structure})";
                player.EnqueueBroadcast(new GameMessageUpdateObject(targetRef));

                sourceRef.Structure = (ushort)(sourceStruct - unitsConsumed);
                sourceRef.Name = $"Salvage Wk{(int)(sourceRef.Workmanship ?? 1)} ({sourceRef.Structure})";

                if (sourceRef.Structure < 1)
                {
                    player.TryConsumeFromInventoryWithNetworking(sourceRef);
                    player.Session.Network.EnqueueSend(new GameMessageDeleteObject(sourceRef));
                }
                else
                {
                    player.EnqueueBroadcast(new GameMessageUpdateObject(sourceRef));
                }
            });

            refined++;
        }

        var totalRefined = refined;
        actionChain.AddAction(player, () =>
        {
            session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Refined {totalRefined} salvage bag(s).",
                ChatMessageType.System
            ));

            var crate = GetSalvageCrate(player);
            if (crate == null)
            {
                return;
            }

            var sortChain = new ActionChain();
            sortChain.AddDelaySeconds(0.1);
            sortChain.AddAction(player, () => SendSortedCrateContainIds(session, crate));
            sortChain.EnqueueChain();
        });

        actionChain.EnqueueChain();
    }
}
