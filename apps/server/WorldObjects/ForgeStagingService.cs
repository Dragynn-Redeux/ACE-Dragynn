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

    private const PropertyInt ForgePassCountProperty = PropertyInt.ForgePassCount;

    private const string FirstPassBulkPrompt = "Process all resonance-stabilized items in your main pack? (Selecting No will allow you to choose a single item to process instead.)";
    private const string UnlockedTopLevelPrompt = "The forge now stands open to destabilization. Select Yes to inspect destabilization-ready items, or No to work resonance-stabilized items instead.";
    private const string UnlockedDiscoveryMessage = "You have no eligible items for the forge.";
    private const string NoFirstPassItemsMessage = "You have no resonance-stabilized items.";
    private const string SecondPassLockedMessage = "The forge's deeper workings remain dormant.";

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

        if (!IsSecondPassUnlocked())
        {
            PromptFirstPassOptions(player);
            return;
        }

        var firstPassCandidates = GetBulkProcessableInventoryItems(player).ToList();
        var secondPassCandidates = GetSecondPassInventoryItems(player).ToList();
        var ingredientCount = DestabilizedLootForge.GetAvailableIngredientCount(player);

        if (secondPassCandidates.Count == 0)
        {
            if (firstPassCandidates.Count > 0)
            {
                PromptFirstPassOptions(player);
                return;
            }

            SendForgeMessage(player, UnlockedDiscoveryMessage);
            return;
        }

        if (firstPassCandidates.Count == 0)
        {
            BeginSecondPassPhase(player, secondPassCandidates, ingredientCount);
            return;
        }

        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (response)
                    {
                        BeginSecondPassPhase(player);
                    }
                    else
                    {
                        PromptFirstPassOptions(player);
                    }
                }
            ),
            UnlockedTopLevelPrompt
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
            SendForgeMessage(player, "You must unequip that item first.");
            return false;
        }

        if (!TryProcessItem(player, item, true, out var handledByFirstPass, out var failureMessage))
        {
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                SendForgeMessage(player, failureMessage);
            }

            return false;
        }

        if (handledByFirstPass)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The forge stabilizes the resonance within your {item.NameWithMaterial}.",
                    ChatMessageType.Broadcast
                )
            );
        }

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

    public static bool IsEligibleForSecondPass(WorldObject item, out string reason)
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

        if (GetForgePassCount(item) < 1)
        {
            reason = "That item is not yet ready for destabilization.";
            return false;
        }

        return DestabilizedLootEffects.CanDestabilize(item, out reason);
    }

    private static bool IsUsable(Player player, WorldObject forgeTarget)
    {
        return player?.Session != null && forgeTarget != null && IsForgeTarget(forgeTarget);
    }

    private static void PromptFirstPassOptions(Player player)
    {
        var candidateGuids = GetBulkProcessableInventoryItems(player).Select(item => item.Guid.Full).ToList();

        if (candidateGuids.Count == 0)
        {
            SendForgeMessage(player, NoFirstPassItemsMessage);
            return;
        }

        if (candidateGuids.Count == 1)
        {
            PromptSelectCandidate(player, candidateGuids, 0);
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
                        BeginGuidedSingleSelection(player);
                    }
                }
            ),
            FirstPassBulkPrompt
        );
    }

    private static void BeginSecondPassPhase(Player player)
    {
        var secondPassCandidates = GetSecondPassInventoryItems(player).ToList();
        var ingredientCount = DestabilizedLootForge.GetAvailableIngredientCount(player);
        BeginSecondPassPhase(player, secondPassCandidates, ingredientCount);
    }

    private static void BeginSecondPassPhase(Player player, List<WorldObject> secondPassCandidates, int ingredientCount)
    {
        if (secondPassCandidates.Count == 0)
        {
            SendForgeMessage(player, HasFirstPassCandidates(player) ? NoFirstPassItemsMessage : UnlockedDiscoveryMessage);
            return;
        }

        if (ingredientCount <= 0)
        {
            if (!HasFirstPassCandidates(player))
            {
                var requiredTotal = DestabilizedLootForge.GetRequiredIngredientCountForItems(secondPassCandidates.Count);
                var ingredientName = DestabilizedLootForge.GetRequiredIngredientName();
                SendForgeMessage(
                    player,
                    $"You have {secondPassCandidates.Count} destabilize-eligible item(s), but need {requiredTotal} {ingredientName} to process them."
                );
                return;
            }

            var neededTotal = DestabilizedLootForge.GetRequiredIngredientCountForItems(secondPassCandidates.Count);
            var neededName = DestabilizedLootForge.GetRequiredIngredientName();
            PromptFollowUpToFirstPass(
                player,
                $"You have {secondPassCandidates.Count} destabilize-eligible item(s), but need {neededTotal} {neededName} to process them. Would you like to work resonance-stabilized items instead?"
            );
            return;
        }

        if (ingredientCount < secondPassCandidates.Count)
        {
            PromptSecondPassCapacitySummary(player, secondPassCandidates, ingredientCount);
            return;
        }

        if (secondPassCandidates.Count > 1)
        {
            PromptSecondPassFullCapacitySummary(player, secondPassCandidates, ingredientCount);
            return;
        }

        BeginGuidedSecondPassSelection(player, secondPassCandidates);
    }

    private static void PromptFollowUpToFirstPass(Player player, string message)
    {
        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (response)
                    {
                        PromptFirstPassOptions(player);
                    }
                }
            ),
            message
        );
    }

    private static void PromptSecondPassCapacitySummary(Player player, List<WorldObject> candidates, int ingredientCount)
    {
        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (response)
                    {
                        BeginGuidedSecondPassSelection(player, candidates);
                    }
                    else if (HasFirstPassCandidates(player))
                    {
                        PromptFirstPassOptions(player);
                    }
                }
            ),
            $"You can destabilize {ingredientCount} of {candidates.Count} eligible item(s) with your current ingredients. Continue?"
        );
    }

    private static void PromptSecondPassFullCapacitySummary(Player player, List<WorldObject> candidates, int ingredientCount)
    {
        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (response)
                    {
                        ProcessAllEligibleSecondPass(player, candidates, ingredientCount);
                    }
                    else
                    {
                        BeginGuidedSecondPassSelection(player, candidates);
                    }
                }
            ),
            $"You can destabilize {ingredientCount} of {candidates.Count} eligible item(s) with your current ingredients. Destabilize all?"
        );
    }

    private static void ProcessAllEligibleSecondPass(Player player, List<WorldObject> candidates, int ingredientCount)
    {
        if (player == null)
        {
            return;
        }

        var processed = 0;
        var skipped = 0;
        var remainingCapacity = Math.Max(0, ingredientCount);

        foreach (var candidate in candidates)
        {
            if (remainingCapacity <= 0)
            {
                break;
            }

            var item = player.FindObject(candidate.Guid.Full, Player.SearchLocations.MyInventory);
            if (item == null || !IsEligibleForSecondPass(item, out _))
            {
                skipped++;
                continue;
            }

            if (DestabilizedLootForge.TryFinalizeImmediately(player, item, out var failureMessage))
            {
                processed++;
                remainingCapacity--;
                continue;
            }

            skipped++;
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                SendForgeMessage(player, failureMessage);
            }
        }

        if (processed == 0 && skipped == 0)
        {
            SendForgeMessage(player, UnlockedDiscoveryMessage);
            return;
        }

        SendForgeMessage(
            player,
            $"The forge destabilizes {processed:N0} item(s){(skipped > 0 ? $", leaving {skipped:N0} untouched." : ".")}"
        );
    }

    private static void BeginGuidedSecondPassSelection(Player player, List<WorldObject> candidates)
    {
        if (candidates.Count == 0)
        {
            if (HasFirstPassCandidates(player))
            {
                PromptFirstPassOptions(player);
            }
            else
            {
                SendForgeMessage(player, UnlockedDiscoveryMessage);
            }

            return;
        }

        PromptSecondPassCandidate(player, candidates, 0);
    }

    private static void PromptSecondPassCandidate(Player player, List<WorldObject> candidates, int index)
    {
        if (index >= candidates.Count)
        {
            if (HasFirstPassCandidates(player))
            {
                PromptFirstPassOptions(player);
            }

            return;
        }

        var item = player.FindObject(candidates[index].Guid.Full, Player.SearchLocations.MyInventory);
        if (item == null || !IsEligibleForSecondPass(item, out _))
        {
            PromptSecondPassCandidate(player, candidates, index + 1);
            return;
        }

        player.ConfirmationManager.EnqueueSend(
            new ForgeConfirmation(
                player.Guid,
                response =>
                {
                    if (!response)
                    {
                        PromptSecondPassCandidate(player, candidates, index + 1);
                        return;
                    }

                    var selected = player.FindObject(item.Guid.Full, Player.SearchLocations.MyInventory);
                    if (selected == null)
                    {
                        SendForgeMessage(player, "That item is no longer available.");
                        PromptSecondPassCandidate(player, candidates, index + 1);
                        return;
                    }

                    if (!DestabilizedLootForge.TryFinalizeImmediately(player, selected, out var failureMessage, () => PromptPostSecondPassPhase(player)))
                    {
                        if (!string.IsNullOrWhiteSpace(failureMessage))
                        {
                            SendForgeMessage(player, failureMessage);
                        }

                        PromptPostSecondPassPhase(player);
                    }
                }
            ),
            $"Destabilize {item.NameWithMaterial}? This cannot be undone. The item will no longer accept further tinkering. Requires 1 {DestabilizedLootForge.GetRequiredIngredientName()} from your pack, consumed on success."
        );
    }

    private static void PromptPostSecondPassPhase(Player player)
    {
        var secondPassCandidates = GetSecondPassInventoryItems(player).ToList();
        var ingredientCount = DestabilizedLootForge.GetAvailableIngredientCount(player);

        if (secondPassCandidates.Count > 0 && ingredientCount > 0)
        {
            player.ConfirmationManager.EnqueueSend(
                new ForgeConfirmation(
                    player.Guid,
                    response =>
                    {
                        if (response)
                        {
                            BeginSecondPassPhase(player);
                        }
                        else if (HasFirstPassCandidates(player))
                        {
                            PromptFirstPassOptions(player);
                        }
                    }
                ),
                "Would you like to continue destabilizing prepared items? Selecting No will let you work resonance-stabilized items instead."
            );
            return;
        }

        if (HasFirstPassCandidates(player))
        {
            PromptFirstPassOptions(player);
        }
    }

    private static void ProcessAllEligibleFromInventory(Player player)
    {
        var candidates = GetBulkProcessableInventoryItems(player).ToList();

        var processed = 0;
        var skipped = 0;

        foreach (var item in candidates)
        {
            if (TryProcessItem(player, item, false, out _, out _))
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
            SendForgeMessage(player, "You have no resonance-stabilized items.");
            return;
        }

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The forge stabilizes {processed:N0} item(s){(skipped > 0 ? $", leaving {skipped:N0} untouched." : ".")}",
                ChatMessageType.Broadcast
            )
        );
    }

    private static void BeginGuidedSingleSelection(Player player)
    {
        var candidateGuids = GetBulkProcessableInventoryItems(player).Select(i => i.Guid.Full).ToList();

        if (candidateGuids.Count == 0)
        {
            SendForgeMessage(player, NoFirstPassItemsMessage);
            return;
        }

        PromptSelectCandidate(player, candidateGuids, 0);
    }

    private static void PromptSelectCandidate(Player player, List<uint> candidateGuids, int index)
    {
        if (index >= candidateGuids.Count)
        {
            SendForgeMessage(player, "No item selected.");
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
                            SendForgeMessage(player, "That item is no longer available.");
                            return;
                        }

                        if (!TryProcessItem(player, selected, false, out var handledByFirstPass, out var failureMessage))
                        {
                            SendForgeMessage(player, failureMessage ?? "The forge cannot work that item.");
                            return;
                        }

                        if (handledByFirstPass)
                        {
                            player.Session.Network.EnqueueSend(
                                new GameMessageSystemChat(
                                    $"The forge stabilizes the resonance within your {selected.NameWithMaterial}.",
                                    ChatMessageType.Broadcast
                                )
                            );
                        }
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

    private static IEnumerable<WorldObject> GetBulkProcessableInventoryItems(Player player)
    {
        if (player == null)
        {
            return Enumerable.Empty<WorldObject>();
        }

        return player
            .GetAllPossessions()
            .Where(i => i != null)
            .Where(i => i.ContainerId == player.Guid.Full)
            .Where(IsEligibleInventoryCandidateForFirstPass);
    }

    private static bool IsEligibleInventoryCandidateForFirstPass(WorldObject item)
    {
        if (item == null)
        {
            return false;
        }

        if (DestabilizedLootForge.IsTerminallyDestabilized(item))
        {
            return false;
        }

        if (GetForgePassCount(item) != 0)
        {
            return false;
        }

        return item.GetProperty(PropertyBool.IsUnstable) == true && item.Lifespan == null;
    }

    private static IEnumerable<WorldObject> GetSecondPassInventoryItems(Player player)
    {
        if (player == null)
        {
            return Enumerable.Empty<WorldObject>();
        }

        return player
            .GetAllPossessions()
            .Where(i => i != null)
            .Where(i => i.ContainerId == player.Guid.Full)
            .Where(i => IsEligibleForSecondPass(i, out _));
    }

    private static bool TryProcessItem(Player player, WorldObject item, bool immediateSecondPass, out bool handledByFirstPass, out string failureMessage)
    {
        handledByFirstPass = false;
        failureMessage = null;

        if (item == null)
        {
            failureMessage = "That item is unavailable.";
            return false;
        }

        if (DestabilizedLootForge.IsTerminallyDestabilized(item))
        {
            failureMessage = "That item has already been destabilized.";
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

            return immediateSecondPass
                ? DestabilizedLootForge.TryFinalizeImmediately(player, item, out failureMessage)
                : DestabilizedLootForge.TryQueueFinalization(player, item, out failureMessage);
        }

        if (!IsEligibleForFirstPass(item, out failureMessage))
        {
            return false;
        }

        item.RemoveProperty(PropertyBool.IsUnstable);
        item.SetProperty(ForgePassCountProperty, forgePassCount + 1);
        ForgeStageDisplay.ApplyStageOverlay(item);
        handledByFirstPass = true;

        player.EnqueueBroadcast(new GameMessageUpdateObject(item));
        return true;
    }

    private static bool HasFirstPassCandidates(Player player)
    {
        return GetBulkProcessableInventoryItems(player).Any();
    }

    private static void SendForgeMessage(Player player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        player.Session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.Craft));
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
