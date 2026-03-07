using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public static class ForgeStagingService
{
    private const string ForgeTemplateName = "Resonance Forge";
    private const string FragmentStabilityPhaseOne = "fragment_stability_phase_one";
    private const long SecondPassUnlockThreshold = 9900;

    private const PropertyInt ForgePassCountProperty = (PropertyInt)10011;

    private const string FastPathHint =
        "Tip: Fast single-item mode is Item -> Forge. Click-forge mode is for all eligible or guided selection.";
    private const string SecondPassLockedMessage =
        "The forge's second pass remains sealed until phase one stability reaches 66%.";

    public static bool IsForgeTarget(WorldObject target)
    {
        if (target == null)
        {
            return false;
        }

        if (target is ResonanceForge)
        {
            return true;
        }

        var template = target.GetProperty(PropertyString.Template);
        return string.Equals(template, ForgeTemplateName, StringComparison.OrdinalIgnoreCase);
    }

    public static void HandleClickUse(Player player, WorldObject forgeTarget)
    {
        if (!IsUsable(player, forgeTarget))
        {
            return;
        }

        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (response)
                    {
                        ProcessAllEligibleFromInventory(player);
                    }
                    else
                    {
                        player.SendMessage(FastPathHint);
                        BeginGuidedSingleSelection(player);
                    }
                }
            ),
                "Process all unstable resonance items in your main pack? (Selecting No will allow you to choose a single item to process instead.)"
        );
    }

    public static bool TryHandleDirectItemFastPath(Player player, WorldObject forgeTarget, WorldObject item, bool itemWasEquipped)
    {
        if (!IsUsable(player, forgeTarget) || item == null)
        {
            return false;
        }

        if (itemWasEquipped)
        {
            player.SendTransientError("You must unequip that item first.");
            return false;
        }

        var preProcessForgePass = GetForgePassCount(item);

        if (!TryProcessItem(player, item, out var failureMessage))
        {
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                player.SendTransientError(failureMessage);
            }

            return false;
        }

        if (preProcessForgePass >= 1)
        {
            // Second-pass finalization flow has its own confirmation and messaging.
            return true;
        }

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The forge stabilizes the resonance within your {item.NameWithMaterial}.",
                ChatMessageType.Broadcast
            )
        );

        return true;
    }

    public static bool IsSecondPassUnlocked()
    {
        return PropertyManager.GetLong(FragmentStabilityPhaseOne).Item >= SecondPassUnlockThreshold;
    }

    public static int GetForgePassCount(WorldObject item)
    {
        if (item == null)
        {
            return 0;
        }

        return item.GetProperty(ForgePassCountProperty) ?? 0;
    }

    public static bool IsEligibleForFirstPass(WorldObject item, out string reason)
    {
        reason = null;

        if (item == null)
        {
            reason = "That item is unavailable.";
            return false;
        }

        if (DestabilizedLootForge.IsTerminallyDestabilized(item))
        {
            reason = "That item can no longer be altered.";
            return false;
        }

        if (item.GetProperty(PropertyBool.IsUnstable) != true)
        {
            reason = "The forge responds only to items with unstable resonance.";
            return false;
        }

        if (item.Lifespan != null)
        {
            reason = "The resonance within that item must be stabilized before the forge can process it.";
            return false;
        }

        return true;
    }

    private static bool IsUsable(Player player, WorldObject forgeTarget)
    {
        return player?.Session != null && forgeTarget != null && IsForgeTarget(forgeTarget);
    }

    private static void ProcessAllEligibleFromInventory(Player player)
    {
        var candidates = GetProcessableInventoryItems(player).ToList();

        var processed = 0;
        var skipped = 0;

        foreach (var item in candidates)
        {
            if (TryProcessItem(player, item, out _))
            {
                processed++;
            }
            else
            {
                skipped++;
            }
        }

        if (processed == 0 && skipped == 0)
        {
            player.SendTransientError("No items here carry unstable resonance.");
            return;
        }

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The forge stabilizes {processed:N0} item(s){(skipped > 0 ? $", skipping {skipped:N0}." : ".")}",
                ChatMessageType.Broadcast
            )
        );
    }

    private static void BeginGuidedSingleSelection(Player player)
    {
        var candidateGuids = GetProcessableInventoryItems(player).Select(i => i.Guid.Full).ToList();

        if (candidateGuids.Count == 0)
        {
            player.SendTransientError("No items here carry unstable resonance.");
            return;
        }

        PromptSelectCandidate(player, candidateGuids, 0);
    }

    private static void PromptSelectCandidate(Player player, List<uint> candidateGuids, int index)
    {
        if (index >= candidateGuids.Count)
        {
            player.SendTransientError("No item selected.");
            return;
        }

        var item = player.FindObject(candidateGuids[index], Player.SearchLocations.MyInventory);
        if (item == null)
        {
            PromptSelectCandidate(player, candidateGuids, index + 1);
            return;
        }

        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (response)
                    {
                        var selected = player.FindObject(item.Guid.Full, Player.SearchLocations.MyInventory);
                        if (selected == null)
                        {
                            player.SendTransientError("That item is no longer available.");
                            return;
                        }

                        if (!TryProcessItem(player, selected, out var failureMessage))
                        {
                            player.SendTransientError(failureMessage ?? "That item could not be processed.");
                            return;
                        }

                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"The forge stabilizes the resonance within your {selected.NameWithMaterial}.",
                                ChatMessageType.Broadcast
                            )
                        );
                    }
                    else
                    {
                        PromptSelectCandidate(player, candidateGuids, index + 1);
                    }
                }
            ),
            $"Process {item.NameWithMaterial}?"
        );
    }

    private static IEnumerable<WorldObject> GetProcessableInventoryItems(Player player)
    {
        if (player == null)
        {
            return Enumerable.Empty<WorldObject>();
        }

        return player
            .GetAllPossessions()
            .Where(i => i != null)
            .Where(i => i.ContainerId == player.Guid.Full)
            .Where(i => i.GetProperty(PropertyBool.IsUnstable) == true)
            .Where(i => !DestabilizedLootForge.IsTerminallyDestabilized(i));
    }

    private static bool TryProcessItem(Player player, WorldObject item, out string failureMessage)
    {
        failureMessage = null;

        if (item == null)
        {
            failureMessage = "That item is unavailable.";
            return false;
        }

        var forgePassCount = GetForgePassCount(item);
        if (forgePassCount >= 1)
        {
            if (!IsSecondPassUnlocked())
            {
                failureMessage = SecondPassLockedMessage;
                return false;
            }

            return DestabilizedLootForge.TryQueueFinalization(player, item, out failureMessage);
        }

        if (!IsEligibleForFirstPass(item, out failureMessage))
        {
            return false;
        }

        item.RemoveProperty(PropertyBool.IsUnstable);
        item.RemoveProperty(PropertyDataId.IconOverlay);
        item.SetProperty(ForgePassCountProperty, forgePassCount + 1);

        player.EnqueueBroadcast(new GameMessageUpdateObject(item));
        return true;
    }

    private sealed class ForgeConfirmation : Confirmation
    {
        private readonly Action<bool> _action;

        public ForgeConfirmation(ObjectGuid playerGuid, Action<bool> action)
            : base(playerGuid, ConfirmationType.Yes_No)
        {
            _action = action;
        }

        public override void ProcessConfirmation(bool response, bool timeout = false)
        {
            var player = Player;
            if (player == null)
            {
                return;
            }

            _action(!timeout && response);
        }
    }
}
