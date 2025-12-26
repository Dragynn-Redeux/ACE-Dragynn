using System;
using System.Linq;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    public uint CalculateManaUsage(Creature caster, Spell spell, WorldObject target = null)
    {
        var baseCost = spell.BaseMana;

        var manaResourcePen = 1.0f;
        if (spell.School != MagicSchool.PortalMagic)
        {
            manaResourcePen = (float)(1 + GetArmorResourcePenalty());
        }

        baseCost = (uint)(baseCost * manaResourcePen);

        // for casting spells built into a casting implement, use the ItemManaCost
        var castItem = caster.GetEquippedWand();
        if (castItem != null && (castItem.SpellDID ?? 0) == spell.Id)
        {
            baseCost = (uint)(castItem.ItemManaCost ?? 0);
        }

        if (
            (spell.School == MagicSchool.PortalMagic)
            && (spell.MetaSpellType == SpellType.Enchantment)
            && (spell.Category >= SpellCategory.ArmorValueRaising)
            && (spell.Category <= SpellCategory.AcidicResistanceLowering)
            && target is Player targetPlayer
        )
        {
            var numTargetItems = 1;
            if (targetPlayer != null)
            {
                numTargetItems = targetPlayer.EquippedObjects.Values.Count(i =>
                    (i is Clothing || i.IsShield) && i.IsEnchantable
                );
            }

            baseCost += spell.ManaMod * (uint)numTargetItems;
        }
        else if ((spell.Flags & SpellFlags.FellowshipSpell) != 0)
        {
            var numFellows = 0;
            if (this is Player { Fellowship: not null } player)
            {
                var magicSkill = GetCreatureSkill(spell.School).Current;
                var maxRange = Math.Min(spell.BaseRangeConstant + magicSkill * spell.BaseRangeMod, Player.MaxRadarRange_Outdoors);

                foreach (var fellowshipMember in player.Fellowship.GetFellowshipMembers().Values)
                {
                    if (fellowshipMember == this)
                    {
                        continue;
                    }

                    if (GetDistance(fellowshipMember) < maxRange)
                    {
                        numFellows++;
                    }
                }
            }

            baseCost += spell.ManaMod * (uint)numFellows;
        }

        var playerCaster = caster as Player;

        // Overload - Increased cost up to 100% with Overload Charged stacks
        if (playerCaster is {OverloadStanceIsActive: true})
        {
            var manaCostPenalty = (1.0f + playerCaster.ManaChargeMeter);
            baseCost = (uint)(baseCost * manaCostPenalty);
        }

        // Battery - Reduced cost up to 50% with battery Charged stacks, up to 100% if Discharging
        else if (playerCaster is {BatteryStanceIsActive: true})
        {
            var manaCostReduction = (1.0f - playerCaster.ManaChargeMeter * 0.5f);
            baseCost = (uint)(baseCost * manaCostReduction);
        }
        else if (playerCaster is {BatteryDischargeIsActive: true})
        {
            var manaCostReduction = (1.0f - playerCaster.DischargeLevel);
            baseCost = (uint)(baseCost * manaCostReduction);
        }

        var abilityPenaltyMod = 0.0f;

        if (playerCaster is not null)
        {
            var phalanxPenaltyMod = playerCaster.PhalanxIsActive ? 0.25f : 0.0f;
            var provokePenaltyMod = playerCaster.ProvokeIsActive ? 0.25f : 0.0f;
            var ripostePenaltyMod = playerCaster.RiposteIsActive ? 0.25f : 0.0f;
            var furyPenaltyMod = playerCaster.FuryEnrageIsActive ? 0.25f : 0.0f;
            var multiShotPenaltyMod = playerCaster.MultiShotIsActive ? 0.25f : 0.0f;
            var steadyStrikePenaltyMod = playerCaster.SteadyStrikeIsActive ? 0.25f : 0.0f;
            var smokescreenPenaltyMod = playerCaster.SmokescreenIsActive ? 0.25f : 0.0f;
            var backstabPenaltyMod = playerCaster.BackstabIsActive ? 0.25f : 0.0f;

            abilityPenaltyMod = phalanxPenaltyMod
                                + provokePenaltyMod
                                + ripostePenaltyMod
                                + furyPenaltyMod
                                + multiShotPenaltyMod
                                + steadyStrikePenaltyMod
                                + smokescreenPenaltyMod
                                + backstabPenaltyMod;
        }

        var manaCostMultiplier = PropertyManager.GetDouble("mana_cost_multiplier").Item + abilityPenaltyMod;

        // Mana Conversion
        var manaConversion = caster.GetCreatureSkill(Skill.ManaConversion);

        if (
            manaConversion.AdvancementClass < SkillAdvancementClass.Trained
            || spell.Flags.HasFlag(SpellFlags.IgnoresManaConversion)
        )
        {
            return Convert.ToUInt32(baseCost * manaCostMultiplier);
        }

        var difficulty = spell.Level * 50;

        var robeManaConversionMod = 0.0;
        var robe = EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.Armor);
        if (robe != null)
        {
            robeManaConversionMod = robe.ManaConversionMod ?? 0;
        }

        var mana_conversion_skill = (uint)
            Math.Round(manaConversion.Current * (GetWeaponManaConversionModifier(caster) + robeManaConversionMod));

        // Final Calculation
        var manaCost = GetManaCost(caster, difficulty, baseCost, mana_conversion_skill);

        return Convert.ToUInt32(manaCost * manaCostMultiplier);
    }

    private static uint GetManaCost(Creature caster, uint difficulty, uint manaCost, uint manaConv)
    {
        if (manaConv == 0 || manaCost <= 1)
        {
            return manaCost;
        }

        const float maxManaReduction = 0.5f;

        var manaConModCeiling = SkillCheck.GetSkillChance(manaConv, difficulty);
        var manaConModFloor = manaConModCeiling * 0.5;
        var reductionRoll = maxManaReduction * ThreadSafeRandom.Next((float)manaConModFloor, (float)manaConModCeiling);
        var savedMana = (uint)Math.Round(manaCost * reductionRoll);

        manaCost -= savedMana;

        if (caster.GetCreatureSkill(Skill.ManaConversion).AdvancementClass == SkillAdvancementClass.Specialized
            && manaCost <= caster.Mana.Current)
        {
            var conversionAmount = (int)Math.Round(savedMana * 0.5f);
            caster.UpdateVitalDelta(caster.Health, conversionAmount);
            caster.UpdateVitalDelta(caster.Stamina, conversionAmount);
        }

        if (caster is Player { EvasiveStanceIsActive: true })
        {
            caster.UpdateVitalDelta(caster.Stamina, manaCost);
        }

        return Math.Max(manaCost, 1);
    }

    /// <summary>
    /// Handles equipping an item casting a spell on player or creature
    /// </summary>
    public bool CreateItemSpell(WorldObject item, uint spellID)
    {
        var spell = new Spell(spellID);

        if (spell.NotFound)
        {
            if (this is Player player)
            {
                if (spell._spellBase == null)
                {
                    player.Session.Network.EnqueueSend(
                        new GameEventCommunicationTransientString(player.Session, $"SpellID {spellID} Invalid.")
                    );
                }
                else
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System)
                    );
                }
            }
            return false;
        }

        // TODO: look into condensing this
        switch (spell.School)
        {
            case MagicSchool.CreatureEnchantment:
            case MagicSchool.LifeMagic:

                HandleCastSpell(spell, this, item, equip: true);
                break;

            case MagicSchool.PortalMagic:

                if (spell.HasItemCategory || spell.IsPortalSpell)
                {
                    HandleCastSpell(spell, this, item, item, equip: true);
                }
                else
                {
                    HandleCastSpell(spell, item, item, item, equip: true);
                }

                break;
        }

        return true;
    }

    /// <summary>
    /// Removes an item's spell from the appropriate enchantment registry (either the wielder, or the item)
    /// </summary>
    /// <param name="silent">if TRUE, silently removes the spell, without sending a message to the target player</param>
    public void RemoveItemSpell(WorldObject item, uint spellId, bool silent = false)
    {
        if (item == null)
        {
            return;
        }

        var spell = new Spell(spellId);

        if (spell._spellBase == null)
        {
            if (this is Player player)
            {
                player.Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(player.Session, $"SpellId {spellId} Invalid.")
                );
            }

            return;
        }

        var target = spell.School == MagicSchool.PortalMagic && !spell.HasItemCategory ? item : this;

        // Retrieve enchantment on target and remove it, if present
        var propertiesEnchantmentRegistry = target.EnchantmentManager.GetEnchantment(spellId, item.Guid.Full);

        if (propertiesEnchantmentRegistry != null)
        {
            if (!silent)
            {
                target.EnchantmentManager.Remove(propertiesEnchantmentRegistry);
            }
            else
            {
                target.EnchantmentManager.Dispel(propertiesEnchantmentRegistry);
            }
        }
    }

    /// <summary>
    /// Returns the creature's effective magic defense skill
    /// with item.WeaponMagicDefense and imbues factored in
    /// </summary>
    public uint GetEffectiveMagicDefense()
    {
        var current = GetCreatureSkill(Skill.MagicDefense).Current;
        var weaponDefenseMod = GetWeaponMagicDefenseModifier(this);
        var defenseImbues = (uint)GetDefenseImbues(ImbuedEffectType.MagicDefense);

        var effectiveMagicDefense = (uint)Math.Round((current * weaponDefenseMod) + defenseImbues);

        //Console.WriteLine($"EffectiveMagicDefense: {effectiveMagicDefense}");

        return effectiveMagicDefense;
    }
}
