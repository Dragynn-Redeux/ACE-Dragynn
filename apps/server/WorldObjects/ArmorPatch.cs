using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class ArmorPatch : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public ArmorPatch(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public ArmorPatch(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    public static void BroadcastArmorPatch(Player player, string sourceName, WorldObject target, int patchesConsumed)
    {
        var plural = patchesConsumed > 1;

        if (plural)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} applies {patchesConsumed} {sourceName}es to the {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                ),
                LocalBroadcastRange,
                ChatMessageType.Craft
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} applies an {sourceName} to the {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                ),
                LocalBroadcastRange,
                ChatMessageType.Craft
            );
        }

        _log.Debug($"[ArmorPatch] {player.Name} {sourceName} to the {target.NameWithMaterial}.");
    }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (!RecipeManager.VerifyUse(player, source, target, true) || target.Workmanship == null)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (DestabilizedLootForge.TryBlockFurtherAlteration(player, target))
        {
            return;
        }

        if (source.ArmorPatchAmount.HasValue)
        {
            var amountToAdd = source.ArmorPatchAmount.Value;
            var targetTier = target.Tier ?? 1;

            var targetArmorSlots = target.ArmorSlots ?? 1;
            var armorPatchStackSize = source.StackSize ?? 1;

            if (target.ArmorLevel < 1)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Armor patches can only reinforce vestments with innate armor level.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }
            if (target.ArmorPatchApplied == true)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {target.NameWithMaterial} has already had an armor patch applied.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (TooPowerful(targetTier, amountToAdd))
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {target.NameWithMaterial} is too low quality to apply this armor patch.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (armorPatchStackSize < targetArmorSlots)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You need {targetArmorSlots} {source.Name}es to increase the armor level of {target.NameWithMaterial}.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (!confirmed)
            {
                var plural = targetArmorSlots > 1 ? "es" : "";

                if (
                    !player.ConfirmationManager.EnqueueSend(
                        new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                        $"Adding {targetArmorSlots} armor patch{plural} to {target.NameWithMaterial}, increasing its armor level by {amountToAdd}."
                    )
                )
                {
                    player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
                }
                else
                {
                    player.SendUseDoneEvent();
                }

                return;
            }

            var actionChain = new ActionChain();

            var animTime = 0.0f;

            player.IsBusy = true;

            if (player.CombatMode != CombatMode.NonCombat)
            {
                var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
                actionChain.AddDelaySeconds(stanceTime);

                animTime += stanceTime;
            }

            animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

            actionChain.AddAction(
                player,
                () =>
                {
                    target.ArmorLevel += source.ArmorPatchAmount;
                    target.ArmorPatchAmount = source.ArmorPatchAmount;
                    target.ArmorPatchApplied = true;
                    target.LongDesc += "\n\nThis item has had an armor patch applied and cannot receive another.";

                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                    player.TryConsumeFromInventoryWithNetworking(source, targetArmorSlots);
                    BroadcastArmorPatch(player, source.Name, target, targetArmorSlots);
                }
            );

            player.EnqueueMotion(actionChain, MotionCommand.Ready);

            actionChain.AddAction(
                player,
                () =>
                {
                    player.IsBusy = false;
                }
            );

            actionChain.EnqueueChain();

            player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
        }
    }

    private static bool TooPowerful(int targetTier, int amountToAdd)
    {
        var minimumTargetTier = 1;

        switch (amountToAdd)
        {
            case 25:
                minimumTargetTier = 1;
                break;
            case 50:
                minimumTargetTier = 3;
                break;
            case 100:
                minimumTargetTier = 5;
                break;
            case 200:
                minimumTargetTier = 7;
                break;
        }

        if (targetTier < minimumTargetTier)
        {
            return true;
        }

        return false;
    }
}
