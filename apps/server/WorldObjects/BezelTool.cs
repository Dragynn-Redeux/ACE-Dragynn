using System;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using MotionCommand = ACE.Entity.Enum.MotionCommand;

namespace ACE.Server.WorldObjects;

public class BezelTool : WorldObject
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public BezelTool(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public BezelTool(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    private static void BroadcastAddSocket(
        Player player,
        WorldObject target,
        int  numberOfFragmentsConsumed,
        bool success
    )
    {
        var bezelFragmentsString = numberOfFragmentsConsumed > 1 ? "Bezel Fragments" : "Bezel Fragment";

        // send local broadcast
        if (success)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} successfully add 1 socket to the {target.NameWithMaterial}, consuming {numberOfFragmentsConsumed} {bezelFragmentsString}.",
                    ChatMessageType.Broadcast
                ),
                8f,
                ChatMessageType.Broadcast
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} fails to add 1 socket to the {target.NameWithMaterial}, consuming {numberOfFragmentsConsumed} {bezelFragmentsString}.",
                    ChatMessageType.Broadcast
                ),
                8f,
                ChatMessageType.Broadcast
            );
        }
    }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        var targetWorkmanship = Math.Clamp((target.ItemWorkmanship ?? 1) - 1, 1, 10);
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

        if (target.Retained)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.NameWithMaterial} is retained and cannot be altered.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var bezelToolSkillType = source.WeenieClassId switch
        {
            BezelToolBlacksmithing => Skill.Blacksmithing,
            BezelToolTailoring => Skill.Tailoring,
            BezelToolWoodworking => Skill.Woodworking,
            BezelToolSpellcrfting => Skill.Spellcrafting,
            BezelToolJewelcrafting => Skill.Jewelcrafting,
            _ => throw new ArgumentOutOfRangeException()
        };

        switch (bezelToolSkillType)
        {
            case Skill.Blacksmithing:
                if (target is not {WeenieType: WeenieType.MeleeWeapon}
                    && target is not {CombatUse: ACE.Entity.Enum.CombatUse.Shield}
                    && target is not {WeenieType: WeenieType.Missile, WeaponSkill: Skill.ThrownWeapon}
                    && target is not {WeenieType: WeenieType.Clothing, ArmorWeightClass: (int)ACE.Entity.Enum.ArmorWeightClass.Heavy})
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

                break;
            case Skill.Tailoring:
                if (target is not {ArmorWeightClass: (int)ACE.Entity.Enum.ArmorWeightClass.Cloth} and not {ArmorWeightClass: (int)ACE.Entity.Enum.ArmorWeightClass.Light})
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

                break;
            case Skill.Woodworking:
                if (target is not {WeenieType: WeenieType.MissileLauncher})
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

                break;
            case Skill.Spellcrafting:
                if (target is not {WeenieType: WeenieType.Caster})
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

                break;
            case Skill.Jewelcrafting:
                if (target is not {ItemType: ItemType.Jewelry})
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

                break;
        }

        if (player.GetCreatureSkill(bezelToolSkillType).AdvancementClass < SkillAdvancementClass.Trained)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You are not trained in {bezelToolSkillType}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var heldBezelFragmentWcids = player.GetInventoryItemsOfWCID(1053975);
        var heldBezelFragments = heldBezelFragmentWcids.Sum(bezelFragmentWcid => bezelFragmentWcid.StackSize ?? 0);

        var hasStackWithRequiredAmount = false;
        foreach (var heldBezelFragmentWcid in heldBezelFragmentWcids)
        {
            if (heldBezelFragmentWcid.StackSize >= fragmentsRequired)
            {
                hasStackWithRequiredAmount = true;
                break;
            }
        }

        if (heldBezelFragments < fragmentsRequired || !hasStackWithRequiredAmount)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You must have a stack of {fragmentsRequired} Bezel Fragments in your inventory to add a socket to {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var itemSocketLimit = LootGenerationFactory.ItemSocketLimit(target);
        var currentSockets = target.JewelSockets ?? 0;

        if (currentSockets >= itemSocketLimit)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{target.NameWithMaterial} already has the maximum number of sockets that a {source.Name} can add.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var playerSkillLevel = player.GetCreatureSkill(bezelToolSkillType).Current;
        var difficulty = GetDifficulty(target.Tier);
        var chance = SkillCheck.GetSkillChance((int)playerSkillLevel, difficulty);

        if (!confirmed)
        {
            var bezelFragmentsString = fragmentsRequired > 1 ? "Bezel Fragments" : "Bezel Fragment";
            var confirmationMessage = $"You have a {Math.Round(chance * 100)}% of adding 1 jewel socket to {target.NameWithMaterial}. {fragmentsRequired} {bezelFragmentsString} will be consumed.\n\n";

            if (!player.ConfirmationManager.EnqueueSend(new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid), confirmationMessage))
            {
                player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
            }
            else
            {
                player.SendUseDoneEvent();
            }

            if (PropertyManager.GetBool("craft_exact_msg").Item)
            {
                var exactMsg = $"You have a 100% chance of adding 1 jewel socket to {target.NameWithMaterial}.";

                player.Session.Network.EnqueueSend(new GameMessageSystemChat(exactMsg, ChatMessageType.Craft));
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

                var success = ThreadSafeRandom.Next(0.0f, 1.0f) < chance;

                if (success)
                {
                    var newSockets = currentSockets + 1;
                    target.SetProperty(PropertyInt.JewelSockets, newSockets);

                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                    Player.TryAwardCraftingXp(player, player.GetCreatureSkill(bezelToolSkillType), bezelToolSkillType, difficulty, 1, 3.0f);
                }
                else
                {
                    Player.TryAwardCraftingXp(player, player.GetCreatureSkill(bezelToolSkillType), bezelToolSkillType, difficulty);
                }

                foreach (var heldBezelFragmentWcid in heldBezelFragmentWcids)
                {
                    if (heldBezelFragmentWcid.StackSize >= fragmentsRequired)
                    {
                        player.TryConsumeFromInventoryWithNetworking(heldBezelFragmentWcid, fragmentsRequired);
                        break;
                    }
                }

                BroadcastAddSocket(player, target, fragmentsRequired, success);
            }
        );

        player.EnqueueMotion(actionChain, MotionCommand.Ready);

        actionChain.AddAction(
            player,
            () =>
            {
                // player.SendUseDoneEvent();
                 player.IsBusy = false;
            }
        );

        actionChain.EnqueueChain();

        player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
    }

    private static int GetDifficulty(int? targetTier)
    {
        var adjustedTier = (targetTier ?? 8) - 1;
        switch (adjustedTier)
        {
            case < 6:
                return adjustedTier * 20;
            case 6:
                return 150;
            default:
                return 200;
        }
    }

    private const uint BezelToolBlacksmithing = 1053976;
    private const uint BezelToolTailoring = 1053977;
    private const uint BezelToolWoodworking = 1053978;
    private const uint BezelToolSpellcrfting = 1053979;
    private const uint BezelToolJewelcrafting = 1053980;

    public static bool IsBezelTool(WorldObject worldObject)
    {
        return worldObject.WeenieClassId is BezelToolBlacksmithing
            or BezelToolTailoring
            or BezelToolWoodworking
            or BezelToolSpellcrfting
            or BezelToolJewelcrafting;
    }
}
