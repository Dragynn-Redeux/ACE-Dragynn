using System;
using System.Linq;
using System.Text;
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
        var debugTrace = (caster as Player)?.DebugSpellcasting == true ? new ManaUsageTrace() : null;

        var baseCost = spell.BaseMana;
        if (debugTrace != null)
        {
            debugTrace.SpellBaseMana = baseCost;
        }

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
            if (debugTrace != null)
            {
                debugTrace.CastItemManaSubstitution = $"{castItem.Name} ItemManaCost={castItem.ItemManaCost ?? 0}";
            }
        }

        if (debugTrace != null)
        {
            debugTrace.CostAfterResourceAndItem = baseCost;
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

        if (debugTrace != null)
        {
            debugTrace.CostAfterSpecialAdds = baseCost;
            debugTrace.StanceInfo = "none";
        }

        // Overload - Increased cost up to 100% with Overload Charged stacks
        if (playerCaster is {OverloadStanceIsActive: true})
        {
            var manaCostPenalty = (1.0f + playerCaster.ManaChargeMeter);
            baseCost = (uint)(baseCost * manaCostPenalty);
            if (debugTrace != null)
            {
                debugTrace.StanceInfo = $"Overload stance, ManaChargeMeter={playerCaster.ManaChargeMeter:F3}, x{manaCostPenalty:F3}";
            }
        }

        // Battery - Reduced cost up to 50% with battery Charged stacks, up to 100% if Discharging
        else if (playerCaster is {BatteryStanceIsActive: true})
        {
            var manaCostReduction = (1.0f - playerCaster.ManaChargeMeter * 0.5f);
            baseCost = (uint)(baseCost * manaCostReduction);
            if (debugTrace != null)
            {
                debugTrace.StanceInfo = $"Battery stance, ManaChargeMeter={playerCaster.ManaChargeMeter:F3}, x{manaCostReduction:F3}";
            }
        }
        else if (playerCaster is {BatteryDischargeIsActive: true})
        {
            var manaCostReduction = (1.0f - playerCaster.DischargeLevel);
            baseCost = (uint)(baseCost * manaCostReduction);
            if (debugTrace != null)
            {
                debugTrace.StanceInfo = $"Battery DISCHARGE, DischargeLevel={playerCaster.DischargeLevel:F3}, x{manaCostReduction:F3}";
            }
        }

        if (debugTrace != null)
        {
            debugTrace.CostAfterStance = baseCost;
        }

        var abilityPenaltyMod = 0.0f;

        if (playerCaster is not null)
        {
            var phalanxPenaltyMod = playerCaster.PhalanxIsEffective ? 0.25f : 0.0f;
            var provokePenaltyMod = playerCaster.ProvokeIsActive ? 0.25f : 0.0f;
            var ripostePenaltyMod = playerCaster.RiposteIsActive ? 0.25f : 0.0f;
            var furyPenaltyMod = playerCaster.FuryEnrageIsActive ? 0.25f : 0.0f;
            var multiShotPenaltyMod = playerCaster.MultiShotIsActive ? 0.25f : 0.0f;
            var steadyStrikePenaltyMod = playerCaster.SteadyStrikeIsActive ? 0.25f : 0.0f;
            var smokescreenPenaltyMod = playerCaster.SmokescreenIsActive ? 0.25f : 0.0f;
            var backstabPenaltyMod = playerCaster.BackstabIsActive ? 0.25f : 0.0f;
            var shadowFlurryPenaltyMod = playerCaster.ShadowFlurryIsActive ? 0.25f : 0.0f;

            abilityPenaltyMod = phalanxPenaltyMod
                                + provokePenaltyMod
                                + ripostePenaltyMod
                                + furyPenaltyMod
                                + multiShotPenaltyMod
                                + steadyStrikePenaltyMod
                                + smokescreenPenaltyMod
                                + backstabPenaltyMod
                                + shadowFlurryPenaltyMod;
        }

        var manaCostMultiplierProp = PropertyManager.GetDouble("mana_cost_multiplier").Item;
        var manaCostMultiplier = manaCostMultiplierProp + abilityPenaltyMod;

        // Mana Conversion
        var manaConversion = caster.GetCreatureSkill(Skill.ManaConversion);

        // Casting difficulty for the mana conversion check comes from the spell's actual Power
        // (portal.dat casting difficulty), not the bucketed 1-7 spell tier.
        var difficulty = spell.Power;

        if (debugTrace != null)
        {
            debugTrace.AbilityPenaltyMod = abilityPenaltyMod;
            debugTrace.ManaCostMultiplierProp = manaCostMultiplierProp;
            debugTrace.ManaCostMultiplier = manaCostMultiplier;
            debugTrace.Difficulty = difficulty;
            debugTrace.ManaConversionCurrent = manaConversion.Current;
        }

        if (
            manaConversion.AdvancementClass < SkillAdvancementClass.Trained
            || spell.Flags.HasFlag(SpellFlags.IgnoresManaConversion)
        )
        {
            var untrainedManaCost = Convert.ToUInt32(baseCost * manaCostMultiplier);

            if (debugTrace != null)
            {
                debugTrace.ManaConversionApplied = false;
                debugTrace.ManaConversionSkipReason =
                    manaConversion.AdvancementClass < SkillAdvancementClass.Trained
                        ? $"Mana Conversion not trained (AdvancementClass {manaConversion.AdvancementClass})"
                        : "spell has SpellFlags.IgnoresManaConversion";
                debugTrace.CostAfterConversion = baseCost;
                debugTrace.FinalManaCost = untrainedManaCost;
                LogManaUsageTrace((Player)caster, spell, target, debugTrace);
            }

            return untrainedManaCost;
        }

        var robeManaConversionMod = 0.0;
        var robe = EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.Armor);
        if (robe != null)
        {
            robeManaConversionMod = robe.ManaConversionMod ?? 0;
        }

        var weaponManaConversionMod = GetWeaponManaConversionModifier(caster);

        var mana_conversion_skill = (uint)
            Math.Round(manaConversion.Current * (weaponManaConversionMod + robeManaConversionMod));

        if (debugTrace != null)
        {
            debugTrace.WeaponManaConversionMod = weaponManaConversionMod;
            debugTrace.RobeManaConversionMod = robeManaConversionMod;
            debugTrace.EffectiveManaConversionSkill = mana_conversion_skill;
        }

        // Final Calculation
        var manaCost = GetManaCost(caster, difficulty, baseCost, mana_conversion_skill, out var savedMana, debugTrace);

        var finalManaCost = Convert.ToUInt32(manaCost * manaCostMultiplier);

        if (debugTrace != null)
        {
            debugTrace.FinalManaCost = finalManaCost;
            LogManaUsageTrace((Player)caster, spell, target, debugTrace);
        }

        return finalManaCost;
    }

    /// <summary>
    /// Full per-cast breakdown of every factor that changes a spell's mana cost, populated only when
    /// the casting player has toggled <see cref="Player.DebugSpellcasting"/> (admin command "debug-spellcasting").
    /// </summary>
    private class ManaUsageTrace
    {
        public uint SpellBaseMana;
        public string CastItemManaSubstitution;
        public uint CostAfterResourceAndItem;
        public uint CostAfterSpecialAdds;
        public string StanceInfo;
        public uint CostAfterStance;
        public float AbilityPenaltyMod;
        public double ManaCostMultiplierProp;
        public double ManaCostMultiplier;
        public uint Difficulty;
        public uint ManaConversionCurrent;
        public bool ManaConversionApplied = true;
        public string ManaConversionSkipReason;
        public float WeaponManaConversionMod;
        public double RobeManaConversionMod;
        public uint EffectiveManaConversionSkill;
        public double SkillChanceCeiling;
        public double RollFloor;
        public double RawRoll;
        public double ReductionFraction;
        public uint CostBeforeConversion;
        public uint SavedMana;
        public uint CostAfterConversion;
        public bool SpecConversionApplied;
        public int SpecConversionAmount;
        public bool EvasiveStaminaRefundApplied;
        public uint EvasiveStaminaRefundAmount;
        public uint FinalManaCost;
    }

    private void LogManaUsageTrace(Player player, Spell spell, WorldObject target, ManaUsageTrace t)
    {
        var totalSaved = (long)t.SpellBaseMana - t.FinalManaCost;
        var totalPct = t.SpellBaseMana > 0 ? (double)totalSaved / t.SpellBaseMana * 100 : 0;

        var lines = new System.Collections.Generic.List<string>();

        if (!t.ManaConversionApplied)
        {
            lines.Add(
                $"[ManaDbg] {spell.Name} L{spell.Level} pow{t.Difficulty}: base {t.SpellBaseMana}"
                + $" -> stance {t.CostAfterStance} -> MC SKIPPED ({t.ManaConversionSkipReason})"
                + $" -> x{t.ManaCostMultiplier:F3} = {t.FinalManaCost}  (saved {totalSaved}, {totalPct:F0}%)"
            );
        }
        else
        {
            lines.Add(
                $"[ManaDbg] {spell.Name} L{spell.Level} pow{t.Difficulty}: base {t.SpellBaseMana}"
                + $" -> res/item {t.CostAfterResourceAndItem} -> +adds {t.CostAfterSpecialAdds}"
                + $" -> stance {t.CostAfterStance} -> conv {t.CostAfterConversion}"
                + $" -> x{t.ManaCostMultiplier:F3} = {t.FinalManaCost}  (saved {totalSaved}, {totalPct:F0}%)"
            );

            var gap = (long)t.EffectiveManaConversionSkill - t.Difficulty;
            lines.Add(
                $"[ManaDbg]  MC {t.ManaConversionCurrent} x(wpn {t.WeaponManaConversionMod:F3} + robe {t.RobeManaConversionMod:F3})"
                + $" = eff {t.EffectiveManaConversionSkill} vs {t.Difficulty}  gap {gap}"
                + $"  c {t.SkillChanceCeiling:F3} roll {t.RawRoll:F3} in[{t.RollFloor:F3},{t.SkillChanceCeiling:F3}]"
                + $" frac {t.ReductionFraction:F3}  saved {t.SavedMana}/{t.CostBeforeConversion}"
            );
        }

        var extras = new StringBuilder();
        if (t.StanceInfo != null && t.StanceInfo != "none")
        {
            extras.Append("stance[").Append(t.StanceInfo).Append("] ");
        }

        if (t.AbilityPenaltyMod != 0f || t.ManaCostMultiplierProp != 1.0)
        {
            extras.Append($"mult=prop {t.ManaCostMultiplierProp:F3}+abilPen {t.AbilityPenaltyMod:F3} ");
        }

        if (t.CastItemManaSubstitution != null)
        {
            extras.Append("itemManaCost[").Append(t.CastItemManaSubstitution).Append("] ");
        }

        if (t.SpecConversionApplied)
        {
            extras.Append($"spec+{t.SpecConversionAmount}hp/sp ");
        }

        if (t.EvasiveStaminaRefundApplied)
        {
            extras.Append($"evasive+{t.EvasiveStaminaRefundAmount}sp ");
        }

        if (extras.Length > 0)
        {
            lines.Add("[ManaDbg]  " + extras.ToString().TrimEnd());
        }

        _log.Information(string.Join("\n", lines));

        foreach (var line in lines)
        {
            player.Session?.Network.EnqueueSend(new GameMessageSystemChat(line, ChatMessageType.Magic));
        }
    }

    private static uint GetManaCost(
        Creature caster,
        uint difficulty,
        uint manaCost,
        uint manaConv,
        out uint savedMana,
        ManaUsageTrace trace = null
    )
    {
        if (trace != null)
        {
            trace.CostBeforeConversion = manaCost;
        }

        if (manaConv == 0 || manaCost <= 1)
        {
            savedMana = 0;
            if (trace != null)
            {
                trace.ManaConversionApplied = false;
                trace.ManaConversionSkipReason =
                    manaConv == 0 ? "effective mana conversion skill rounded to 0" : "cost <= 1";
                trace.CostAfterConversion = manaCost;
            }

            return manaCost;
        }

        const float maxManaReduction = 0.5f;

        var manaConModCeiling = SkillCheck.GetSkillChance(manaConv, difficulty);
        var manaConModFloor = manaConModCeiling * 0.5;
        var rawRoll = ThreadSafeRandom.Next((float)manaConModFloor, (float)manaConModCeiling);
        var reductionRoll = maxManaReduction * rawRoll;
        savedMana = (uint)Math.Round(manaCost * reductionRoll);

        manaCost -= savedMana;

        if (trace != null)
        {
            trace.SkillChanceCeiling = manaConModCeiling;
            trace.RollFloor = manaConModFloor;
            trace.RawRoll = rawRoll;
            trace.ReductionFraction = reductionRoll;
            trace.SavedMana = savedMana;
        }

        if (caster.GetCreatureSkill(Skill.ManaConversion).AdvancementClass == SkillAdvancementClass.Specialized
            && manaCost <= caster.Mana.Current)
        {
            var conversionAmount = (int)Math.Round(savedMana * 0.5f);
            caster.UpdateVitalDelta(caster.Health, conversionAmount);
            caster.UpdateVitalDelta(caster.Stamina, conversionAmount);
            if (trace != null)
            {
                trace.SpecConversionApplied = true;
                trace.SpecConversionAmount = conversionAmount;
            }
        }

        if (caster is Player { EvasiveStanceIsActive: true })
        {
            caster.UpdateVitalDelta(caster.Stamina, manaCost);
            if (trace != null)
            {
                trace.EvasiveStaminaRefundApplied = true;
                trace.EvasiveStaminaRefundAmount = manaCost;
            }
        }

        var clamped = Math.Max(manaCost, 1);
        if (trace != null)
        {
            trace.CostAfterConversion = clamped;
        }

        return clamped;
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
