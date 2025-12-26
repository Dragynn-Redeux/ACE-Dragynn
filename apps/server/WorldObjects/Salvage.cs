using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects;

public class Salvage : WorldObject
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public Salvage(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public Salvage(Biota biota)
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

        var combineSalvage = false;

        if (target is { WeenieType: WeenieType.Salvage })
        {
            if (source.Guid == target.Guid)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            if (source.MaterialType != target.MaterialType)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Salvage bags must be the same material to combine them.",
                        ChatMessageType.Broadcast
                    )
                );
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            if (target.Structure + source.Structure > 1000)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The resulting salvage bag cannot contain more than 1000 units.",
                        ChatMessageType.Broadcast
                    )
                );
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            combineSalvage = true;
        }

        var message = "";

        // check workmanship max
        if (!combineSalvage && target.NumTimesTinkered >= target.Workmanship)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.Name} can only be tinkered {target.Workmanship} times!",
                    ChatMessageType.Broadcast
                )
            );
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        // check salvage units
        var salvageCost = (target.ItemWorkmanship ?? 1) * (target.ArmorSlots ?? 1);

        if (PropertyManager.GetBool("bypass_crafting_checks").Item)
        {
            salvageCost = 0;
        }

        var units = salvageCost == 1 ? "unit" : "units";
        if (!combineSalvage && source.Structure < (ushort)salvageCost)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You require at least {salvageCost} {units} of {source?.MaterialType} to tinker the {target?.Name}.",
                    ChatMessageType.Broadcast
                )
            );
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }


        // verify player has skill trained + that salvage type can be used on target
        var useableType = false;

        if (!combineSalvage && source.MaterialType == null)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Something went wrong. Please notify an admin.",
                    ChatMessageType.System
                )
            );
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);

            _log.Error("UseObjectOnTarget() - source.MaterialType for {Source} is null. Aborting craft.", source);

            return;
        }


        if (TinkeringTarget.TryGetValue(source.MaterialType, out var tinkeringSkill) && !combineSalvage)
        {
            var skill = player.GetCreatureSkill(tinkeringSkill);

            if (skill.AdvancementClass < SkillAdvancementClass.Trained && PropertyManager.GetBool("bypass_crafting_checks").Item == false)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You are not trained in {skill.Skill.ToSentence()}.",
                        ChatMessageType.Broadcast
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            useableType = CheckTinkerType(player, source, target, tinkeringSkill);
        }

        if (!combineSalvage && !useableType)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        var salvageWorkmanship = source.Workmanship ?? 1;
        var itemWorkmanship = target.Workmanship ?? 1;

        // base difficulty - ranges from 0 to 180
        var baseDifficulty = itemWorkmanship * 20.0f - 20;

        // workmanship mod - ranges from -180 to +180
        var workmanshipMod = (salvageWorkmanship - itemWorkmanship) * 20.0f;

        // attempt mod - ranges from 1.1 to 2.0
        var attemptNum = Math.Clamp(target.NumTimesTinkered + 1, 1, 10);
        var attemptMod = attemptNum * 0.1f + 1.0;

        var difficulty = (int)Math.Max(((baseDifficulty - workmanshipMod) * attemptMod), 1);
        var craftingXpDifficulty = baseDifficulty * attemptMod;


        // roll skill check
        var creatureSkill = player.GetCreatureSkill(tinkeringSkill);
        var successChance = SkillCheck.GetSkillChance((int)creatureSkill.Current, difficulty);

        if (ImbueSalvage.Contains((MaterialType)source.MaterialType))
        {
            successChance /= 3.0f;
        }

        if (PropertyManager.GetBool("bypass_crafting_checks").Item || combineSalvage)
        {
            successChance = 1.0f;
        }

        var percent = (int)(successChance * 100);

        var craftingXpString = creatureSkill.Current < craftingXpDifficulty + 50 ? "will" : "will not";

        message =
            $"You determine that you have a {percent} percent chance to succeed and will require {salvageCost} {units} of salvage.\n\n" +
            $"This craft {craftingXpString} award xp towards your {tinkeringSkill}.";
        

        if (combineSalvage)
        {
            var sourceWork = source.Workmanship ?? 1.0f;
            var sourceStruct = source.Structure ?? 1;
            var targetWork = target.Workmanship ?? 1.0f;
            var targetStruct = target.Structure ?? 1;

            var combinedWork = ((sourceWork * sourceStruct) + (targetWork * targetStruct)) / (targetStruct + sourceStruct);
            combinedWork = (float)Math.Round(combinedWork, 2);

            var combinedStruct = sourceStruct + targetStruct;

            message = $"Combining this salvage will produce {combinedStruct} units with a workmanship of {combinedWork}. Would you like to proceed?";
        }

        if (!confirmed)
        {
            if (
                !player.ConfirmationManager.EnqueueSend(
                    new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                    message
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
                if (combineSalvage)
                {
                    CombineSalvage(player, source, target);
                }
                else
                {
                    HandleTinkering(
                        player,
                        source,
                        target,
                        successChance,
                        (int)craftingXpDifficulty,
                        creatureSkill,
                        tinkeringSkill
                    );
                }
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

    private static void CombineSalvage(Player player, WorldObject source, WorldObject target)
    {
        if (source is null || target is null)
        {
            return;
        }

        var sourceWork = source.Workmanship ?? 1.0;
        var sourceStruct = source.Structure ?? 1;
        var targetWork = target.Workmanship ?? 1.0;
        var targetStruct = target.Structure ?? 1;

        var newWork = ((sourceWork * sourceStruct) + (targetWork * targetStruct)) / (targetStruct + sourceStruct);
        Console.WriteLine(newWork);
        target.Workmanship = (float)Math.Round((newWork), 2);
        target.Structure += sourceStruct;
        target.Name = $"Salvage ({target.Structure})";

        UpdateObj(player, target);

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"You combine both bags of salvage.",
                ChatMessageType.Broadcast
            )
        );

        player.TryConsumeFromInventoryWithNetworking(source);
        player.Session.Network.EnqueueSend(new GameMessageDeleteObject(source));
    }

    public static bool CheckTinkerType(Player player, WorldObject source, WorldObject target, Skill tinkeringSkill)
    {
        if (
            tinkeringSkill == Skill.Blacksmithing && target.ItemType == ItemType.MeleeWeapon
            || target.WeenieType == WeenieType.Missile
            || target.ArmorWeightClass == 4
        )
        {
            return true;
        }

        if (tinkeringSkill == Skill.Jewelcrafting && target.ItemType == ItemType.Jewelry)
        {
            return true;
        }

        if (source.MaterialType == ACE.Entity.Enum.MaterialType.Linen)
        {
            if (target.ArmorResourcePenalty == null || target.ArmorResourcePenalty <= 0)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {target.Name} has no Resource Penalty to reduce!",
                        ChatMessageType.Broadcast
                    )
                );
                return false;
            }
        }

        if (tinkeringSkill == Skill.Tailoring && target.ArmorWeightClass == 2)
        {
            return true;
        }

        if (tinkeringSkill == Skill.Tailoring && target.ArmorWeightClass == 1 && target.ArmorLevel > 0)
        {
            return true;
        }

        if (tinkeringSkill == Skill.Woodworking && target.WeenieType == WeenieType.MissileLauncher)
        {
            return true;
        }

        if (ImbueSalvage.Contains((MaterialType)source.MaterialType))
        {
            if (
                target.ItemType == ItemType.MeleeWeapon
                || target.ItemType == ItemType.MissileWeapon
                || target.ItemType == ItemType.Caster
            )
            {
                return true;
            }
        }

        // wand checks
        if (target.ItemType == ItemType.Caster)
        {
            if (
                source.MaterialType == ACE.Entity.Enum.MaterialType.Malachite
                || source.MaterialType == ACE.Entity.Enum.MaterialType.Amethyst
                || source.MaterialType == ACE.Entity.Enum.MaterialType.Tourmaline
            )
            {
                return true;
            }
        }

        if (
            source.MaterialType == ACE.Entity.Enum.MaterialType.GreenGarnet
            || source.MaterialType == ACE.Entity.Enum.MaterialType.Opal
        )
        {
            if (target.ElementalDamageMod.HasValue)
            {
                return true;
            }
        }

        if (
            source.MaterialType == ACE.Entity.Enum.MaterialType.LavenderJade
            || source.MaterialType == ACE.Entity.Enum.MaterialType.RoseQuartz
        )
        {
            if (target.WeaponRestorationSpellsMod.HasValue)
            {
                return true;
            }
        }

        if (target is { WeenieType: WeenieType.Salvage}
            && source.MaterialType == target.MaterialType
            && source.Structure == 100
            && target.Structure == 100)
        {
            return true;
        }

        return false;
    }

    public static void HandleTinkering(
        Player player,
        WorldObject source,
        WorldObject target,
        double successChance,
        int difficulty,
        CreatureSkill skill,
        Skill tinkeringSkill
    )
    {
        var armorSlots = 1;

        if (target.ArmorWeightClass != null)
        {
            if (target.ArmorSlots != null)
            {
                armorSlots = (int)target.ArmorSlots;
            }
        }

        var successAmount = "";
        var success = ThreadSafeRandom.Next(0.0f, 1.0f) < successChance;

        if (success)
        {
            switch (source.MaterialType)
            {
                // Weapon - 1% Defense | Armor - 0.25% Defense + Shield Mod
                case ACE.Entity.Enum.MaterialType.Brass: // Brass
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponPhysicalDefense == null)
                        {
                            target.WeaponPhysicalDefense = 1.01;
                        }
                        else
                        {
                            target.WeaponPhysicalDefense += 0.01;
                        }

                        successAmount = "raising its Physical Defense modifier by 1%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        target.ArmorPhysicalDefMod += (0.0025 * armorSlots);
                        target.ArmorShieldMod += (0.0025 * armorSlots);

                        successAmount = $"raising its Physical Defense and Shield modifiers by {0.25 * armorSlots}%";
                    }
                    break;

                // Weapon - 5% base damage + 0.5% Defense | Armor - 0.125% Defense + Shield Mod and 5% Armor Level
                case ACE.Entity.Enum.MaterialType.Bronze: // Bronze
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponPhysicalDefense == null)
                        {
                            target.WeaponPhysicalDefense = 1.005;
                        }
                        else
                        {
                            target.WeaponPhysicalDefense += 0.005;
                        }

                        var damageBonus = (int)(target.BaseDamage * 0.05) < 1 ? 1 : (int)(target.BaseDamage * 0.05);
                        target.Damage += damageBonus;

                        successAmount =
                            $"raising its Damage by {damageBonus} and its Physical Defense modifier by 0.5%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorPhysicalDefMod += (0.00125 * armorSlots);
                        target.ArmorShieldMod += (0.00125 * armorSlots);
                        successAmount =
                            $"raising its Armor by {armorBonus} and its Physical Defense and Shield modifiers by {0.25 * armorSlots}%";
                    }
                    break;

                // Weapon - 1% attack | Armor - 0.25% AttackMod + 2H mod
                case ACE.Entity.Enum.MaterialType.Copper: // Copper
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponOffense == null)
                        {
                            target.WeaponOffense = 1.01;
                        }
                        else
                        {
                            target.WeaponOffense += 0.01;
                        }

                        successAmount = $"raising its Attack modifier by 1%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        target.ArmorAttackMod += (0.0025 * armorSlots);
                        target.ArmorTwohandedCombatMod += (0.0025 * armorSlots);

                        successAmount = $"raising its Attack and Two-Handed Combat modifiers by {0.25 * armorSlots}%";
                    }
                    break;

                // Weapon - 0.5% Attack and 5% damage | Armor - 0.125% Attack and 2h Mods + 5% ArmorLevel
                case ACE.Entity.Enum.MaterialType.Gold: // Gold
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponOffense == null)
                        {
                            target.WeaponOffense = 1.005;
                        }
                        else
                        {
                            target.WeaponOffense += 0.005;
                        }

                        var damageBonus = (int)(target.BaseDamage * 0.05) < 1 ? 1 : (int)(target.BaseDamage * 0.05);
                        target.Damage += damageBonus;

                        successAmount = $"raising its Damage by {damageBonus} and its Attack modifier by 0.5%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorAttackMod += (0.00125 * armorSlots);
                        target.ArmorTwohandedCombatMod += (0.00125 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Attack and Two-Handed Combat modifiers by {0.25 * armorSlots}%";
                    }
                    break;

                // Weapon - 7.5% Damage but +5 WeaponTime | Armor - 7.5% ArmorLevel but -0.25% Stam Penalty
                case ACE.Entity.Enum.MaterialType.Iron: // Iron
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        var damageBonus = (int)(target.BaseDamage * 0.075) < 1 ? 1 : (int)(target.BaseDamage * 0.075);
                        target.Damage += damageBonus;
                        target.WeaponTime += 5;

                        successAmount = $"raising its Damage by {damageBonus}, but increasing its Weapon Time by 5";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.075) < 1 ? 1 : (int)(target.BaseArmor * 0.075);
                        target.ArmorLevel += armorBonus;

                        target.ArmorResourcePenalty += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus}, but increasing its Resource Penalty by {0.25 * armorSlots}%";
                    }
                    break;

                // Weapon - 0.5% MagicD Mod + 5% Damage | Armor - 2 Ward and 5% ArmorLevel
                case ACE.Entity.Enum.MaterialType.Pyreal: // Pyreal
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponMagicalDefense == null)
                        {
                            target.WeaponMagicalDefense = 1.005;
                        }
                        else
                        {
                            target.WeaponMagicalDefense += 0.005;
                        }

                        var damageBonus = (int)(target.BaseDamage * 0.05) < 1 ? 1 : (int)(target.BaseDamage * 0.05);
                        target.Damage += damageBonus;

                        successAmount = $"raising its Damage by {damageBonus} and its Magic Defense modifier by 0.5%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        target.WardLevel += 2;
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        successAmount = $"raising its Armor by {armorBonus} and its Ward Level by 2";
                    }
                    break;

                // Weapon - 1% MagicD Mod | Armor - 3 Ward + 0.25% HP Regen
                case ACE.Entity.Enum.MaterialType.Silver: // Silver
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponMagicalDefense == null)
                        {
                            target.WeaponMagicalDefense = 1.01;
                        }
                        else
                        {
                            target.WeaponMagicalDefense += 0.01;
                        }

                        successAmount = $"raising its Magic Defense modifier by 1%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        target.WardLevel += 3;
                        target.ArmorHealthRegenMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Ward Level by 3 and its Health Regeneration modifier by {0.25 * armorSlots}%";
                    }
                    break;

                // Weapon - 0.5% Defense + 0.5% Offense    | Armor - 5% Armor + 0.25% HP Regen
                case ACE.Entity.Enum.MaterialType.Steel: // Steel
                    if (target.ItemType == ItemType.MeleeWeapon || target.WeenieType == WeenieType.Missile)
                    {
                        if (target.WeaponOffense == null)
                        {
                            target.WeaponOffense = 1.005;
                        }
                        else
                        {
                            target.WeaponOffense += 0.005;
                        }

                        if (target.WeaponPhysicalDefense == null)
                        {
                            target.WeaponPhysicalDefense = 1.005;
                        }
                        else
                        {
                            target.WeaponPhysicalDefense += 0.005;
                        }

                        successAmount = $"raising its Attack and Physical Defense modifiers by 0.5%";
                    }
                    if (target.ItemType == ItemType.Armor)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorHealthRegenMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Health Regeneration modifier by {0.25 * armorSlots}%";
                    }
                    break;

                // Tailoring: Cloth = WeightClass 1 | Leather = WeightClass 2

                // 7.5% Armor
                case ACE.Entity.Enum.MaterialType.Leather:
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.075) < 1 ? 1 : (int)(target.BaseArmor * 0.075);
                        target.ArmorLevel += armorBonus;

                        successAmount = $"raising its Armor by {armorBonus}";
                    }
                    break;

                // 10% bonus Armor but -0.25% resource penalty
                case ACE.Entity.Enum.MaterialType.ArmoredilloHide:
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.1) < 1 ? 1 : (int)(target.BaseArmor * 0.1);
                        target.ArmorLevel += armorBonus;

                        target.ArmorResourcePenalty += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus}, but increasing its Resource Penalty by {0.25 * armorSlots}%";
                    }
                    break;

                // Cloth - 5% Armor + 0.125% Mana + 0.125% ManaRegen | Leather - 5% Armor + 0.125% stam + 0.125% Stam Regen
                case ACE.Entity.Enum.MaterialType.GromnieHide:
                    if (target.ArmorWeightClass == 1)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorManaRegenMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Mana Regeneration modifier by {0.25 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorStaminaRegenMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Stamina Regeneration modifier by {0.25 * armorSlots}%";
                    }
                    break;

                // Leather - 5% Armor + 0.125% ArmorAttackMod  |   Cloth - 5% Armor + 0.125% War Magic Mod
                case ACE.Entity.Enum.MaterialType.ReedSharkHide:
                    if (target.ArmorWeightClass == 1)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorWarMagicMod += (0.00125 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its War Magic modifier by {0.125 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorAttackMod += (0.00125 * armorSlots);
                        target.ArmorDualWieldMod += (0.00125 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Attack and Dual-Wield modifiers by {0.125 * armorSlots}%";
                    }

                    break;

                // 5% Armor and 0.25% Reduced Resource Penalty
                case ACE.Entity.Enum.MaterialType.Linen:
                    if (target.ArmorWeightClass == 1)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorResourcePenalty -= (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and reducing its Resource Penalty by {0.25 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorResourcePenalty -= (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and reducing its Resource Penalty by {0.25 * armorSlots}%";
                    }
                    break;
                // Leather - 5% Armor and 0.25% to Thievery and Deception | Cloth - 0.25% LifeMagic + 0.25% ManaCon and 0.25% Perception
                case ACE.Entity.Enum.MaterialType.Satin:
                    if (target.ArmorWeightClass == 1)
                    {
                        target.ArmorLifeMagicMod += (0.0025 * armorSlots);
                        target.ArmorPerceptionMod += (0.0025 * armorSlots);
                        target.ArmorManaRegenMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Life Magic, Perception, and Mana Regeneration modifiers by {0.25 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorThieveryMod += (0.0025 * armorSlots);
                        target.ArmorDeceptionMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Thievery and Deception modifiers by {0.25 * armorSlots}%";
                    }
                    break;

                // Cloth - 5% Armor + 0.125% Life Magic and 0.25% Mana Mod | Leather - 5% Armor + 0.25% Perception + 0.25% Deception
                case ACE.Entity.Enum.MaterialType.Silk:
                    if (target.ArmorWeightClass == 1)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorPerceptionMod += (0.0025 * armorSlots);
                        target.ArmorDeceptionMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Perception and Deception modifiers by {0.25 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        var armorBonus = (int)(target.BaseArmor * 0.05) < 1 ? 1 : (int)(target.BaseArmor * 0.05);
                        target.ArmorLevel += armorBonus;

                        target.ArmorPerceptionMod += (0.0025 * armorSlots);
                        target.ArmorDeceptionMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Armor by {armorBonus} and its Perception and Deception modifiers by {0.25 * armorSlots}%";
                    }
                    break;
                // Cloth - 0.25% War Magic + 0.25% Mana + 0.25% Deception | Leather - 0.25% Attack, 0.25% Dual Wield, 0.25% Deception
                case ACE.Entity.Enum.MaterialType.Velvet:
                    if (target.ArmorWeightClass == 1)
                    {
                        target.ArmorWarMagicMod += (0.0025 * armorSlots);
                        target.ArmorLifeMagicMod += (0.0025 * armorSlots);
                        target.ArmorDeceptionMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its War Magic, Life Magic, and Deception modifiers by {0.25 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        target.ArmorAttackMod += (0.0025 * armorSlots);
                        target.ArmorDualWieldMod += (0.0025 * armorSlots);
                        target.ArmorPerceptionMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Attack, Dual-Wield, and Perception modifiers by {0.25 * armorSlots}%";
                    }
                    break;
                // Cloth - 0.25% Mana + 0.25% ManaRegen | Leather - Armor + 0.25% stam + 0.25% Stam Regen
                case ACE.Entity.Enum.MaterialType.Wool:
                    if (target.ArmorWeightClass == 1)
                    {
                        if (target.ArmorManaMod == null)
                        {
                            target.ArmorManaMod = (0.0025 * armorSlots);
                        }
                        else
                        {
                            target.ArmorManaMod += (0.0025 * armorSlots);
                        }

                        target.ArmorManaRegenMod += (0.0025 * armorSlots);

                        successAmount = $"raising its Mana and Mana Regeneration modifiers by {0.25 * armorSlots}%";
                    }
                    if (target.ArmorWeightClass == 2)
                    {
                        if (target.ArmorStaminaMod == null)
                        {
                            target.ArmorStaminaMod = (0.0025 * armorSlots);
                        }
                        else
                        {
                            target.ArmorStaminaMod += (0.0025 * armorSlots);
                        }

                        target.ArmorStaminaRegenMod += (0.0025 * armorSlots);

                        successAmount =
                            $"raising its Stamina and Stamina Regeneration modifiers by {0.25 * armorSlots}%";
                    }
                    break;

                // Woodworking

                // 1% MagicD Mod
                case ACE.Entity.Enum.MaterialType.Ebony: // Ebony
                    if (target.WeaponMagicalDefense == null)
                    {
                        target.WeaponMagicalDefense = 1.01;
                    }
                    else
                    {
                        target.WeaponMagicalDefense += 0.01;
                    }

                    successAmount = $"raising its Magic Defense modifier by 1%";

                    break;
                // 7.5% Damage
                case ACE.Entity.Enum.MaterialType.Mahogany: // Mahogany
                    target.DamageMod += 0.075f;
                    successAmount = $"raising its Damage modifier by 7.5%";
                    break;
                //  1% Defense Mod
                case ACE.Entity.Enum.MaterialType.Oak: // Oak
                    if (target.WeaponPhysicalDefense == null)
                    {
                        target.WeaponPhysicalDefense = 1.01;
                    }
                    else
                    {
                        target.WeaponPhysicalDefense += 0.01;
                    }

                    successAmount = $"raising its Physical Defense modifier by 1%";
                    break;
                // 1% Offense Mod
                case ACE.Entity.Enum.MaterialType.Pine: // Pine
                    if (target.WeaponOffense == null)
                    {
                        target.WeaponOffense = 1.01;
                    }
                    else
                    {
                        target.WeaponOffense += 0.01;
                    }

                    successAmount = $"raising its Attack modifier by 1%";
                    break;
                // 0.5% Offense and 0.5% Defense
                case ACE.Entity.Enum.MaterialType.Teak: // Teak
                    if (target.WeaponOffense == null)
                    {
                        target.WeaponOffense = 1.005;
                    }
                    else
                    {
                        target.WeaponOffense += 0.005;
                    }

                    if (target.WeaponPhysicalDefense == null)
                    {
                        target.WeaponPhysicalDefense = 1.005;
                    }
                    else
                    {
                        target.WeaponPhysicalDefense += 0.005;
                    }

                    successAmount = $"raising its Attack and Physical Defense modifiers by 1%";
                    break;

                // Spellcraft

                case ACE.Entity.Enum.MaterialType.Aquamarine:
                    target.ImbuedEffect = ImbuedEffectType.ColdRending;
                    target.IconUnderlayId = 0x06003353;
                    break;
                case ACE.Entity.Enum.MaterialType.BlackGarnet:
                    target.ImbuedEffect = ImbuedEffectType.PierceRending;
                    target.IconUnderlayId = 0x0600335B;
                    break;
                case ACE.Entity.Enum.MaterialType.BlackOpal:
                    target.ImbuedEffect = ImbuedEffectType.CriticalStrike;
                    target.IconUnderlayId = 0x06003358;
                    break;
                case ACE.Entity.Enum.MaterialType.Emerald:
                    target.ImbuedEffect = ImbuedEffectType.AcidRending;
                    target.IconUnderlayId = 0x06003355;
                    break;
                case ACE.Entity.Enum.MaterialType.FireOpal:
                    target.ImbuedEffect = ImbuedEffectType.CripplingBlow;
                    target.IconUnderlayId = 0x06003357;
                    break;

                case ACE.Entity.Enum.MaterialType.ImperialTopaz:
                    target.ImbuedEffect = ImbuedEffectType.SlashRending;
                    target.IconUnderlayId = 0x0600335C;
                    break;
                case ACE.Entity.Enum.MaterialType.Jet:
                    target.ImbuedEffect = ImbuedEffectType.ElectricRending;
                    target.IconUnderlayId = 0x06003354;
                    break;
                case ACE.Entity.Enum.MaterialType.RedGarnet:
                    target.ImbuedEffect = ImbuedEffectType.FireRending;
                    target.IconUnderlayId = 0x06003359;
                    break;
                case ACE.Entity.Enum.MaterialType.Sunstone:
                    target.ImbuedEffect = ImbuedEffectType.ArmorRending;
                    target.IconUnderlayId = 0x06003356;
                    break;
                case ACE.Entity.Enum.MaterialType.Tourmaline:
                    target.ImbuedEffect = ImbuedEffectType.WardRending;
                    target.IconUnderlayId = 0x06003356;
                    break;
                case ACE.Entity.Enum.MaterialType.WhiteSapphire:
                    target.ImbuedEffect = ImbuedEffectType.BludgeonRending;
                    target.IconUnderlayId = 0x0600335A;
                    break;

                // Wands ONLY

                // Pure Damage - 7.5%
                case ACE.Entity.Enum.MaterialType.GreenGarnet:
                    target.ElementalDamageMod = (target.ElementalDamageMod ?? 0.0f) + 0.075f;
                    successAmount = $"raising its Elemental Damage modifier by 7.5%";
                    break;

                // 5% Damage and 5% Mana conversion
                case ACE.Entity.Enum.MaterialType.Opal:
                    target.ElementalDamageMod = (target.ElementalDamageMod ?? 0.0f) + 0.075f;
                    target.ManaConversionMod = (target.ManaConversionMod ?? 0.0f) + 0.05f;
                    successAmount = $"raising its Elemental Damage and Mana Conversion modifiers by 5%";
                    break;

                // 1% Physical Defense
                case ACE.Entity.Enum.MaterialType.Malachite:
                    if (target.WeaponPhysicalDefense == null)
                    {
                        target.WeaponPhysicalDefense = 1.01;
                    }
                    else
                    {
                        target.WeaponPhysicalDefense += 0.01;
                    }

                    successAmount = $"raising its Physical Defense modifier by 1%";
                    break;

                // 1% Magic Defense
                case ACE.Entity.Enum.MaterialType.Amethyst:
                    if (target.WeaponMagicalDefense == null)
                    {
                        target.WeaponMagicalDefense = 1.01;
                    }
                    else
                    {
                        target.WeaponMagicalDefense += 0.01;
                    }

                    successAmount = $"raising its Magic Defense modifier by 1%";
                    break;

                // 7.5% Restoration Mod
                case ACE.Entity.Enum.MaterialType.LavenderJade:
                    target.WeaponRestorationSpellsMod += 0.075;
                    successAmount = $"raising its Restoration modifier by 7.5%";
                    break;

                // 5% Restoration and 5% Mana Conversion
                case ACE.Entity.Enum.MaterialType.RoseQuartz:
                    target.WeaponRestorationSpellsMod += 0.05;
                    target.ManaConversionMod = (target.ManaConversionMod ?? 0.0f) + 0.05f;
                    successAmount = $"raising its Restoration and Mana Conversion modifiers 5%";
                    break;

                // Jewelcrafting

                case ACE.Entity.Enum.MaterialType.Moonstone:
                    if (target.ItemMaxMana != null)
                    {
                        target.ItemMaxMana += 250;
                        successAmount = $"raising its Maximum Mana by 250";
                    }
                    break;
                case ACE.Entity.Enum.MaterialType.WhiteJade:
                    target.WardLevel += 1;
                    successAmount = $"raising its Ward Level by 1";
                    break;

                /*   case ACE.Entity.Enum.MaterialType.Agate:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Amber:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Azurite:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Bloodstone:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Carnelian:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Citrine:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Diamond:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.GreenJade:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Hematite:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.LapisLazuli:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;

                   case ACE.Entity.Enum.MaterialType.Onyx:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;

                   case ACE.Entity.Enum.MaterialType.Peridot:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.RedJade:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;

                   case ACE.Entity.Enum.MaterialType.Ruby:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Sapphire:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;

                   case ACE.Entity.Enum.MaterialType.SmokeyQuartz:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.TigerEye:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Turquoise:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;

                   case ACE.Entity.Enum.MaterialType.WhiteQuartz:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.YellowGarnet:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.YellowTopaz:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                   case ACE.Entity.Enum.MaterialType.Zircon:
                       Console.WriteLine(Skill.ItemTinkering);
                       break;
                */
            }
        }

        Player.TryAwardCraftingXp(player, skill, tinkeringSkill, difficulty, armorSlots);
        BroadcastTinkering(player, source, target, successChance, success, successAmount);
        TinkeringCleanup(player, source, target);
    }

    public static void TinkeringCleanup(Player player, WorldObject source, WorldObject target)
    {
        // write to tinker log
        if (target.TinkerLog != null)
        {
            target.TinkerLog += ",";
        }

        target.TinkerLog += (uint?)source.MaterialType ?? source.WeenieClassId;
        target.NumTimesTinkered++;

        // update salvage bag structure
        var workmanship = target.ItemWorkmanship ?? 1;

        var salvageCost = (ushort)((target.ArmorSlots ?? 1) * workmanship);

        if (PropertyManager.GetBool("bypass_crafting_checks").Item)
        {
            salvageCost = 0;
        }

        if (source.Structure >= salvageCost)
        {
            source.Structure -= salvageCost;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Your tinkering consumed {salvageCost} units of {RecipeManager.GetMaterialName(source.MaterialType ?? ACE.Entity.Enum.MaterialType.Unknown)}. The {target.Name} has been tinkered {target.NumTimesTinkered} times.",
                    ChatMessageType.Broadcast
                )
            );
        }

        source.Name = $"Salvage ({source.Structure})";

        UpdateObj(player, source);
        UpdateObj(player, target);
    }

    private static void UpdateObj(Player player, WorldObject obj)
    {
        player.EnqueueBroadcast(new GameMessageUpdateObject(obj));

        if (obj.CurrentWieldedLocation != null)
        {
            player.EnqueueBroadcast(new GameMessageObjDescEvent(player));
            return;
        }

        var invObj = player.FindObject(obj.Guid.Full, Player.SearchLocations.MyInventory);

        if (invObj != null)
        {
            player.MoveItemToFirstContainerSlot(obj);
        }

        if (obj.Structure < 1)
        {
            player.TryConsumeFromInventoryWithNetworking(obj);
            player.Session.Network.EnqueueSend(new GameMessageDeleteObject(obj));

            var salvageName = obj.NameWithMaterial.Replace(" (0)", "");
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The {salvageName} is consumed.", ChatMessageType.Broadcast));
        }
    }

    public static void BroadcastTinkering(
        Player player,
        WorldObject tool,
        WorldObject target,
        double chance,
        bool success,
        string successAmount
    )
    {
        var sourceName = Regex.Replace(tool.NameWithMaterial, @" \(\d+\)$", "");
        if (success)
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} successfully applies the {sourceName} (workmanship {(tool.Workmanship ?? 0):#.00}) to the {target.NameWithMaterial}, {successAmount}.",
                    ChatMessageType.Craft
                ),
                10f,
                ChatMessageType.Craft
            );
        }
        else
        {
            player.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{player.Name} fails to apply the {sourceName} (workmanship {(tool.Workmanship ?? 0):#.00}) to the {target.NameWithMaterial}.",
                    ChatMessageType.Craft
                ),
                10f,
                ChatMessageType.Craft
            );
        }

        _log.Debug(
            $"[TINKERING] {player.Name} {(success ? "successfully applies" : "fails to apply")} the {sourceName} (workmanship {(tool.Workmanship ?? 0):#.00}) to the {target.NameWithMaterial}.{(!success ? " The target is destroyed." : "")} | Chance: {chance}"
        );
    }

    public static void ReverseTinkers(Player player, WorldObject target)
    {
        if (target.NumTimesTinkered >= 0)
        {
            if (target.WardLevel != null)
            {
                target.WardLevel = target.BaseWard == 0 ? null : target.BaseWard;
            }

            if (target.ArmorLevel != null)
            {
                if (target.ArmorPatchAmount != null)
                {
                    target.ArmorLevel = target.BaseArmor + target.ArmorPatchAmount;
                }
                else
                {
                    target.ArmorLevel = target.BaseArmor == 0 ? null : target.BaseArmor;
                }
            }

            if (target.Damage != null)
            {
                target.Damage = target.BaseDamage == 0 ? null : target.BaseDamage;
            }

            if (target.WeaponTime != null)
            {
                target.WeaponTime = target.BaseWeaponTime == 0 ? null : target.BaseWeaponTime;
            }

            if (target.ArmorWarMagicMod != null)
            {
                target.ArmorWarMagicMod = target.BaseArmorWarMagicMod == 0 ? null : target.BaseArmorWarMagicMod;
            }

            if (target.ArmorLifeMagicMod != null)
            {
                target.ArmorLifeMagicMod = target.BaseArmorLifeMagicMod == 0 ? null : target.BaseArmorLifeMagicMod;
            }

            if (target.ArmorMagicDefMod != null)
            {
                target.ArmorMagicDefMod = target.BaseArmorMagicDefMod == 0 ? null : target.BaseArmorMagicDefMod;
            }

            if (target.ArmorPhysicalDefMod != null)
            {
                target.ArmorPhysicalDefMod =
                    target.BaseArmorPhysicalDefMod == 0 ? null : target.BaseArmorPhysicalDefMod;
            }

            if (target.ArmorMissileDefMod != null)
            {
                target.ArmorMissileDefMod = target.BaseArmorMissileDefMod == 0 ? null : target.BaseArmorMissileDefMod;
            }

            if (target.ArmorDualWieldMod != null)
            {
                target.ArmorDualWieldMod = target.BaseArmorDualWieldMod == 0 ? null : target.BaseArmorDualWieldMod;
            }

            if (target.ArmorRunMod != null)
            {
                target.ArmorRunMod = target.BaseArmorRunMod == 0 ? null : target.BaseArmorRunMod;
            }

            if (target.ArmorAttackMod != null)
            {
                target.ArmorAttackMod = target.BaseArmorAttackMod == 0 ? null : target.BaseArmorAttackMod;
            }

            if (target.ArmorHealthRegenMod != null)
            {
                target.ArmorHealthRegenMod =
                    target.BaseArmorHealthRegenMod == 0 ? null : target.BaseArmorHealthRegenMod;
            }

            if (target.ArmorStaminaRegenMod != null)
            {
                target.ArmorStaminaRegenMod =
                    target.BaseArmorStaminaRegenMod == 0 ? null : target.BaseArmorStaminaRegenMod;
            }

            if (target.ArmorManaRegenMod != null)
            {
                target.ArmorManaRegenMod = target.BaseArmorManaRegenMod == 0 ? null : target.BaseArmorManaRegenMod;
            }

            if (target.ArmorShieldMod != null)
            {
                target.ArmorShieldMod = target.BaseArmorShieldMod == 0 ? null : target.BaseArmorShieldMod;
            }

            if (target.ArmorPerceptionMod != null)
            {
                target.ArmorPerceptionMod = target.BaseArmorPerceptionMod == 0 ? null : target.BaseArmorPerceptionMod;
            }

            if (target.ArmorThieveryMod != null)
            {
                target.ArmorThieveryMod = target.BaseArmorThieveryMod == 0 ? null : target.BaseArmorThieveryMod;
            }

            if (target.WeaponWarMagicMod != null)
            {
                target.WeaponWarMagicMod = target.BaseWeaponWarMagicMod == 0 ? null : target.BaseWeaponWarMagicMod;
            }

            if (target.WeaponLifeMagicMod != null)
            {
                target.WeaponLifeMagicMod = target.BaseWeaponLifeMagicMod == 0 ? null : target.BaseWeaponLifeMagicMod;
            }

            if (target.WeaponRestorationSpellsMod != null)
            {
                target.WeaponRestorationSpellsMod =
                    target.BaseWeaponRestorationSpellsMod == 0 ? null : target.BaseWeaponRestorationSpellsMod;
            }

            if (target.ArmorHealthMod != null)
            {
                target.ArmorHealthMod = target.BaseArmorHealthMod == 0 ? null : target.BaseArmorHealthMod;
            }

            if (target.ArmorStaminaMod != null)
            {
                target.ArmorStaminaMod = target.BaseArmorStaminaMod == 0 ? null : target.BaseArmorStaminaMod;
            }

            if (target.ArmorManaMod != null)
            {
                target.ArmorManaMod = target.BaseArmorManaMod == 0 ? null : target.BaseArmorManaMod;
            }

            if (target.ArmorResourcePenalty != null)
            {
                target.ArmorResourcePenalty =
                    target.BaseArmorResourcePenalty == 0 ? null : target.BaseArmorResourcePenalty;
            }

            if (target.ArmorDeceptionMod != null)
            {
                target.ArmorDeceptionMod = target.BaseArmorDeceptionMod == 0 ? null : target.BaseArmorDeceptionMod;
            }

            if (target.ArmorTwohandedCombatMod != null)
            {
                target.ArmorTwohandedCombatMod =
                    target.BaseArmorTwohandedCombatMod == 0 ? null : target.BaseArmorTwohandedCombatMod;
            }

            if (target.WeaponPhysicalDefense != null)
            {
                target.WeaponPhysicalDefense =
                    target.BaseWeaponPhysicalDefense == 0 ? null : target.BaseWeaponPhysicalDefense;
            }

            if (target.WeaponMagicalDefense != null)
            {
                target.WeaponMagicalDefense =
                    target.BaseWeaponMagicalDefense == 0 ? null : target.BaseWeaponMagicalDefense;
            }

            if (target.WeaponOffense != null)
            {
                target.WeaponOffense = target.BaseWeaponOffense == 0 ? null : target.BaseWeaponOffense;
            }

            if (target.DamageMod != null)
            {
                target.DamageMod = target.BaseDamageMod == 0 ? null : target.BaseDamageMod;
            }

            if (target.ElementalDamageMod != null)
            {
                target.ElementalDamageMod = target.BaseElementalDamageMod == 0 ? null : target.BaseElementalDamageMod;
            }

            if (target.ManaConversionMod != null)
            {
                target.ManaConversionMod = target.BaseManaConversionMod == 0 ? null : target.BaseManaConversionMod;
            }

            if (target.ImbuedEffect > 0)
            {
                target.ImbuedEffect = 0;
            }

            if (target.IconUnderlayId != null)
            {
                target.IconUnderlayId = null;
            }

            target.NumTimesTinkered = 0;
            target.TinkerLog = "";
            target.LongDesc = $"{target.Name}";

            UpdateObj(player, target);

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You have successfully reverted all of the tinkering done to {RecipeManager.GetMaterialName(target.MaterialType ?? ACE.Entity.Enum.MaterialType.Unknown)} {target.Name}.",
                    ChatMessageType.Craft
                )
            );
        }
    }

    public static List<string> WorkmanshipNames = new List<string>()
    {
        "Poorly crafted", // 1
        "Well-crafted", // 2
        "Finely crafted", // 3
        "Exquisitely crafted", // 4
        "Magnificent", // 5
        "Nearly flawless", // 6
        "Flawless", // 7
        "Utterly flawless", // 8
        "Incomparable", // 9
        "Priceless" // 10
    };

    public static List<MaterialType> ImbueSalvage = new List<MaterialType>
    {
        ACE.Entity.Enum.MaterialType.Aquamarine,
        ACE.Entity.Enum.MaterialType.BlackGarnet,
        ACE.Entity.Enum.MaterialType.BlackOpal,
        ACE.Entity.Enum.MaterialType.Emerald,
        ACE.Entity.Enum.MaterialType.FireOpal,
        ACE.Entity.Enum.MaterialType.ImperialTopaz,
        ACE.Entity.Enum.MaterialType.Jet,
        ACE.Entity.Enum.MaterialType.RedGarnet,
        ACE.Entity.Enum.MaterialType.Sunstone,
        ACE.Entity.Enum.MaterialType.Tourmaline,
        ACE.Entity.Enum.MaterialType.WhiteSapphire
    };

    public static Dictionary<MaterialType?, Skill> TinkeringTarget = new Dictionary<MaterialType?, Skill>
    {
        // Spellcrafting

        { ACE.Entity.Enum.MaterialType.Amethyst, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Aquamarine, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.BlackGarnet, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.BlackOpal, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Emerald, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.FireOpal, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.GreenGarnet, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.ImperialTopaz, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Jet, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.LavenderJade, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Malachite, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Opal, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.RedGarnet, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.RoseQuartz, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Sunstone, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.Tourmaline, Skill.Spellcrafting },
        { ACE.Entity.Enum.MaterialType.WhiteSapphire, Skill.Spellcrafting },
        // Jewelcrafting
        { ACE.Entity.Enum.MaterialType.Agate, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Amber, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Azurite, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Bloodstone, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Carnelian, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Citrine, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Diamond, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.GreenJade, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Hematite, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.LapisLazuli, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Moonstone, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Onyx, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Peridot, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.RedJade, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Ruby, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Sapphire, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Serpentine, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.SmokeyQuartz, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.TigerEye, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Turquoise, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.WhiteJade, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.WhiteQuartz, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.YellowGarnet, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.YellowTopaz, Skill.Jewelcrafting },
        { ACE.Entity.Enum.MaterialType.Zircon, Skill.Jewelcrafting },
        // Tailoring
        { ACE.Entity.Enum.MaterialType.Leather, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.ArmoredilloHide, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.GromnieHide, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.ReedSharkHide, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.Linen, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.Satin, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.Silk, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.Velvet, Skill.Tailoring },
        { ACE.Entity.Enum.MaterialType.Wool, Skill.Tailoring },
        // Blacksmithing
        { ACE.Entity.Enum.MaterialType.Brass, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Bronze, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Copper, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Gold, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Iron, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Pyreal, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Silver, Skill.Blacksmithing },
        { ACE.Entity.Enum.MaterialType.Steel, Skill.Blacksmithing },
        // Stoneworking
        /*
        {ACE.Entity.Enum.MaterialType.Alabaster, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Granite, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Marble, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Obsidian, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Sandstone, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Ceramic, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Porcelain, "Stoneworking"},
        {ACE.Entity.Enum.MaterialType.Ivory, "Stoneworking"}, */

        // Woodworking
        { ACE.Entity.Enum.MaterialType.Wood, Skill.Woodworking },
        { ACE.Entity.Enum.MaterialType.Ebony, Skill.Woodworking },
        { ACE.Entity.Enum.MaterialType.Mahogany, Skill.Woodworking },
        { ACE.Entity.Enum.MaterialType.Oak, Skill.Woodworking },
        { ACE.Entity.Enum.MaterialType.Pine, Skill.Woodworking },
        { ACE.Entity.Enum.MaterialType.Teak, Skill.Woodworking }
    };
}
