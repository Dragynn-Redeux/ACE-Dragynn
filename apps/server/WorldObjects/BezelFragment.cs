using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using MotionCommand = ACE.Entity.Enum.MotionCommand;

namespace ACE.Server.WorldObjects;

public class BezelFragment : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public BezelFragment(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public BezelFragment(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    private static string GetFragmentsString(int amount)
    {
        return amount > 1 ? "Bezel Fragments" : "Bezel Fragment";
    }

    private static void BroadcastAddSocket(Player player, WorldObject target, int numberOfFragmentsConsumed)
    {
        // send local broadcast
        player.EnqueueBroadcast(
            new GameMessageSystemChat(
                $"{player.Name} adds 1 socket to the {target.NameWithMaterial}, consuming {numberOfFragmentsConsumed} {GetFragmentsString(numberOfFragmentsConsumed)}.",
                ChatMessageType.Broadcast
            ),
            8f,
            ChatMessageType.Broadcast
        );
    }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    /// <summary>
    /// Weapons, shields, casters, jewelry, and armor can receive sockets.
    /// </summary>
    private static bool IsSocketableItem(WorldObject target)
    {
        return target is { WeenieType: WeenieType.MeleeWeapon }
            or { WeenieType: WeenieType.MissileLauncher }
            or { WeenieType: WeenieType.Caster }
            or { WeenieType: WeenieType.Missile, WeaponSkill: Skill.ThrownWeapon }
            or { CombatUse: ACE.Entity.Enum.CombatUse.Shield }
            or { ItemType: ItemType.Jewelry }
            or { WeenieType: WeenieType.Clothing, ArmorWeightClass: (int)ACE.Entity.Enum.ArmorWeightClass.Heavy }
            or { ArmorWeightClass: (int)ACE.Entity.Enum.ArmorWeightClass.Cloth or (int)ACE.Entity.Enum.ArmorWeightClass.Light };
    }

    private static bool TryRequireSocketEligibleStage(Player player, WorldObject target, bool endUse)
    {
        if (target.Retained)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} is retained and cannot be altered.",
                    ChatMessageType.Craft
                )
            );

            if (endUse)
            {
                player.SendUseDoneEvent();
            }

            return true;
        }

        if (ForgeStageDisplay.IsAllowedStage(target, ForgeStage.Stable, ForgeStage.Destabilized))
        {
            return false;
        }

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The {target.NameWithMaterial} must be stable before a socket can be added.",
                ChatMessageType.Craft
            )
        );

        if (endUse)
        {
            player.SendUseDoneEvent();
        }

        return true;
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        var targetWorkmanship = target.ItemWorkmanship ?? 1;
        var fragmentsRequired = targetWorkmanship * targetWorkmanship;

        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (target.WeenieType == source.WeenieType)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (!RecipeManager.VerifyUse(player, source, target, true))
        {
            if (!confirmed)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            }
            else
            {
                player.SendTransientError(
                    "Either you or one of the items involved does not pass the requirements for this craft interaction."
                );
            }

            return;
        }

        if (target.Workmanship == null || target.Tier == null)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (!IsSocketableItem(target))
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{source.Name} cannot be used with {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (TryRequireSocketEligibleStage(player, target, true))
        {
            return;
        }

        if ((source.StackSize ?? 1) < fragmentsRequired)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You require a stack of {fragmentsRequired} {GetFragmentsString(fragmentsRequired)} to add a socket to {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var itemSocketLimit = LootGenerationFactory.ItemSocketLimit(target);

        if ((target.JewelSockets ?? 0) >= itemSocketLimit)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{target.NameWithMaterial} already has the maximum number of sockets.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (!confirmed)
        {
            var confirmationMessage =
                $"Add 1 jewel socket to {target.NameWithMaterial}?\n\n" +
                $"{fragmentsRequired} {GetFragmentsString(fragmentsRequired)} will be consumed.\n\n";

            if (!player.ConfirmationManager.EnqueueSend(new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid), confirmationMessage))
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
                if (!RecipeManager.VerifyUse(player, source, target, true))
                {
                    player.SendTransientError(
                        "Either you or one of the items involved does not pass the requirements for this craft interaction."
                    );
                    return;
                }

                if (TryRequireSocketEligibleStage(player, target, false))
                {
                    return;
                }

                // the stack may have been split or the item socketed while the confirmation was open
                var currentSockets = target.JewelSockets ?? 0;

                if ((source.StackSize ?? 1) < fragmentsRequired || currentSockets >= itemSocketLimit)
                {
                    player.SendTransientError(
                        "Either you or one of the items involved does not pass the requirements for this craft interaction."
                    );
                    return;
                }

                if (!player.TryConsumeFromInventoryWithNetworking(source, fragmentsRequired))
                {
                    return;
                }

                target.JewelSockets = currentSockets + 1;

                player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                BroadcastAddSocket(player, target, fragmentsRequired);
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
