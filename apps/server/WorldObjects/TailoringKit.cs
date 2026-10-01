using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class TailoringKit : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public TailoringKit(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public TailoringKit(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        if (ArmorStyleKit.IsStyleItem(source.WeenieClassId))
        {
            ArmorStyleKit.UseObjectOnTarget(player, source, target, confirmed);
            return;
        }

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

        if (source.ArmorWeightClass == null)
        {
            if (target.ItemType != ItemType.Armor && target.ItemType != ItemType.Clothing || target.IsShield)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Only armor or clothing can be tailored.", ChatMessageType.Craft)
                );
                player.SendUseDoneEvent();
                return;
            }

            if (target.ArmorWeightClass == null)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            if (!confirmed)
            {
                if (
                    !player.ConfirmationManager.EnqueueSend(
                        new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                        $"Copy the appearance of the {target.Name}, destroying it in the process? It may only be applied to a piece of armor of the same weight class and coverage."
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
                    var pattern = WorldObjectFactory.CreateNewWorldObject(1053973);

                    RipArmorAppearance(player, source, target, pattern);
                    player.TryConsumeFromInventoryWithNetworking(source, 1);
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You create a pattern from the {target.Name}.",
                            ChatMessageType.Craft
                        )
                    );
                    player.TryConsumeFromInventoryWithNetworking(target);

                    player.TryCreateInInventoryWithNetworking(pattern);
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
        else
        {
            if (target.ArmorWeightClass == null || target.ArmorWeightClass != source.ArmorWeightClass)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{source.Name} may only be applied to a piece of armor from the same weight class.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (target.ClothingPriority == null || target.ClothingPriority != source.ClothingPriority)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{source.Name} may only be applied to a piece of armor with the same coverage.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            if (!confirmed)
            {
                if (
                    !player.ConfirmationManager.EnqueueSend(
                        new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                        $"Transferring the {source.Name}'s apperance onto the {target.Name}."
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
                    ApplyPattern(player, source, target);

                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You copy the appearance of the {source.Name} onto the {target.Name}.",
                            ChatMessageType.Craft
                        )
                    );
                    player.TryConsumeFromInventoryWithNetworking(source);
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

    public static void RipArmorAppearance(Player player, WorldObject source, WorldObject target, WorldObject pattern)
    {
        pattern.PaletteTemplate = target.PaletteTemplate;
        pattern.ClothingPriority = target.ClothingPriority;
        pattern.ArmorWeightClass = target.ArmorWeightClass;
        if (PropertyManager.GetBool("tailoring_intermediate_uieffects").Item)
        {
            pattern.UiEffects = target.UiEffects;
        }

        pattern.Dyable = target.Dyable;

        pattern.Shade = target.Shade;

        pattern.SetupTableId = target.SetupTableId;
        pattern.PaletteBaseId = target.PaletteBaseId;
        pattern.ClothingBase = target.ClothingBase;
        pattern.PhysicsTableId = target.PhysicsTableId;
        pattern.IconId = target.IconId;
        pattern.IconOverlayId = 0x060011F7;

        pattern.Name = target.Name;
        pattern.LongDesc =
            $"This {pattern.Name} Pattern may be applied to any piece of armor from the same weight class and with equivalent slot coverage.";
    }

    public static void ApplyPattern(Player player, WorldObject source, WorldObject target)
    {
        target.PaletteTemplate = source.PaletteTemplate;
        target.ClothingPriority = source.ClothingPriority;
        if (PropertyManager.GetBool("tailoring_intermediate_uieffects").Item)
        {
            target.UiEffects = source.UiEffects;
        }

        target.Dyable = source.Dyable;

        target.Shade = source.Shade;

        target.SetupTableId = source.SetupTableId;
        target.PaletteBaseId = source.PaletteBaseId;
        target.ClothingBase = source.ClothingBase;
        target.PhysicsTableId = source.PhysicsTableId;
        target.IconId = source.IconId;

        target.Name = source.Name;
    }
}
