using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects;


public enum SigilTrinketColor
{
    Blue,
    Yellow,
    Red
}

public enum SigilTrinketType
{
    Compass,        // warrior - Shield/Twohand
    PuzzleBox,      // rogue - DualWield
    Scarab,         // caster - War/Life
    PocketWatch,    // warrior-rogue - PhysicalDefense
    Top,            // warrior-caster - MagicDefense
    Goggles         // rogue-caster - Perception/Deception
}

public enum SigilTrinketLifeMagicEffect
{
    CastProt,
    CastVuln,
    CastItemBuff,
    CastVitalRate
}

public enum SigilTrinketWarMagicEffect
{
    Duplicate,
    Detonate,
    Crushing
}

public enum SigilTrinketLifeWarMagicEffect
{
    Intensity,
    Shielding,
    Reduction
}

public enum SigilTrinketShieldEffect
{
    None,
    // PH2
    // PH3,
    // PH4
}

public enum SigilTrinketTwohandedCombatEffect
{
    None,
    // PH2
    // PH3,
    // PH4
}

public enum SigilTrinketShieldTwohandedCombatEffect
{
    Might,
    Aggression
    // PH3,
    // PH4
}

public enum SigilTrinketDualWieldEffect
{
    None
    // PH2,
    // PH3,
    // PH4
}

public enum SigilTrinketMissileEffect
{
    None
    // PH2,
    // PH3,
    // PH4
}

public enum SigilTrinketDualWieldMissileEffect
{
    Assailment,
    SwiftKiller
    // PH3,
    // PH4
}

public enum SigilTrinketThieveryEffect
{
    Treachery,
    // PH2,
    // PH3,
    // PH4
}

public enum SigilTrinketPerceptionEffect
{
    Exposure
    // PH2,
    // PH3,
    // PH4
}

public enum SigilTrinketDeceptionEffect
{
    Avoidance
    // PH2,
    // PH3,
    // PH4
}

public enum SigilTrinketPhysicalDefenseEffect
{
    Evasion
    // PH2,
    // PH3,
    // PH4
}

public enum SigilTrinketMagicDefenseEffect
{
    Absorption
    // PH2,
    // PH3,
    // PH4
}

public class SigilTrinket : WorldObject
{
    public static readonly int MaxLifeMagicEffectId = Enum.GetValues(typeof(SigilTrinketLifeMagicEffect)).Cast<int>().Max();
    public static readonly int MaxWarMagicEffectId = Enum.GetValues(typeof(SigilTrinketWarMagicEffect)).Cast<int>().Max();
    public static readonly int MaxLifeWarMagicEffectId = Enum.GetValues(typeof(SigilTrinketLifeWarMagicEffect)).Cast<int>().Max();
    public static readonly int MaxTwohandedCombatEffectId = Enum.GetValues(typeof(SigilTrinketTwohandedCombatEffect)).Cast<int>().Max();
    public static readonly int MaxShieldEffectId = Enum.GetValues(typeof(SigilTrinketShieldEffect)).Cast<int>().Max();
    public static readonly int MaxShieldTwohandedCombatEffectId = Enum.GetValues(typeof(SigilTrinketShieldTwohandedCombatEffect)).Cast<int>().Max();
    public static readonly int MaxDualWieldEffectId = Enum.GetValues(typeof(SigilTrinketDualWieldEffect)).Cast<int>().Max();
    public static readonly int MaxMissileEffectId = Enum.GetValues(typeof(SigilTrinketMissileEffect)).Cast<int>().Max();
    public static readonly int MaxDualWieldMissileEffectId = Enum.GetValues(typeof(SigilTrinketDualWieldMissileEffect)).Cast<int>().Max();
    public static readonly int MaxThieveryEffectId = Enum.GetValues(typeof(SigilTrinketThieveryEffect)).Cast<int>().Max();
    public static readonly int MaxPerceptionEffectId = Enum.GetValues(typeof(SigilTrinketPerceptionEffect)).Cast<int>().Max();
    public static readonly int MaxDeceptionEffectId = Enum.GetValues(typeof(SigilTrinketDeceptionEffect)).Cast<int>().Max();
    public static readonly int MaxPhysicalDefenseEffectId = Enum.GetValues(typeof(SigilTrinketPhysicalDefenseEffect)).Cast<int>().Max();
    public static readonly int MaxMagicDefenseEffectId = Enum.GetValues(typeof(SigilTrinketMagicDefenseEffect)).Cast<int>().Max();


    public static readonly List<SpellCategory> LifeBeneficialTriggerSpells =
    [
        SpellCategory.HealingRaising,
        SpellCategory.HealthRaising,
        SpellCategory.StaminaRaising,
        SpellCategory.ManaRaising
    ];

    public static readonly List<SpellCategory> LifeHarmfulTriggerSpells =
    [
        SpellCategory.HealingLowering,
        SpellCategory.HealthLowering,
        SpellCategory.StaminaLowering,
        SpellCategory.ManaLowering
    ];

    public static readonly List<SpellCategory> LifeIntensityTriggerCategories =
    [
        SpellCategory.HealingRaising,
        SpellCategory.HealthRaising,
        SpellCategory.HealthRestoring,
        SpellCategory.HealingLowering,
        SpellCategory.HealthLowering,
        SpellCategory.HealthDepleting,
        SpellCategory.StaminaRaising,
        SpellCategory.StaminaRestoring,
        SpellCategory.StaminaLowering,
        SpellCategory.StaminaDepleting,
        SpellCategory.ManaRaising,
        SpellCategory.ManaRestoring,
        SpellCategory.ManaLowering,
        SpellCategory.ManaDepleting
    ];

    public static readonly List<SpellCategory> WarProjectileTriggerCategories =
    [
        SpellCategory.SlashingMissile,
        SpellCategory.SlashingSeeker,
        SpellCategory.SlashingStrike,
        SpellCategory.SlashingStreak,
        SpellCategory.SlashingBlast,
        SpellCategory.SlashingBurst,
        SpellCategory.BladeVolley,
        SpellCategory.SlashingRing,
        SpellCategory.SlashingWall,
        SpellCategory.PiercingMissile,
        SpellCategory.PiercingSeeker,
        SpellCategory.PiercingStrike,
        SpellCategory.PiercingStreak,
        SpellCategory.PiercingBlast,
        SpellCategory.PiercingBurst,
        SpellCategory.ForceVolley,
        SpellCategory.PiercingRing,
        SpellCategory.PiercingWall,
        SpellCategory.BludgeoningMissile,
        SpellCategory.BludgeoningSeeker,
        SpellCategory.BludgeoningStrike,
        SpellCategory.BludgeoningStreak,
        SpellCategory.BludgeoningBlast,
        SpellCategory.BludgeoningBurst,
        SpellCategory.BludgeoningVolley,
        SpellCategory.BludgeoningRing,
        SpellCategory.BludgeoningWall,
        SpellCategory.AcidMissile,
        SpellCategory.AcidSeeker,
        SpellCategory.AcidStrike,
        SpellCategory.AcidStreak,
        SpellCategory.AcidBlast,
        SpellCategory.AcidBurst,
        SpellCategory.AcidVolley,
        SpellCategory.AcidRing,
        SpellCategory.AcidWall,
        SpellCategory.FireMissile,
        SpellCategory.FireSeeker,
        SpellCategory.FireStrike,
        SpellCategory.FireStreak,
        SpellCategory.FireBlast,
        SpellCategory.FireBurst,
        SpellCategory.FlameVolley,
        SpellCategory.FireRing,
        SpellCategory.FireWall,
        SpellCategory.ColdMissile,
        SpellCategory.ColdSeeker,
        SpellCategory.ColdStrike,
        SpellCategory.ColdStreak,
        SpellCategory.ColdBlast,
        SpellCategory.ColdBurst,
        SpellCategory.FrostVolley,
        SpellCategory.ColdRing,
        SpellCategory.ColdWall,
        SpellCategory.ElectricMissile,
        SpellCategory.ElectricSeeker,
        SpellCategory.ElectricStrike,
        SpellCategory.ElectricStreak,
        SpellCategory.ElectricBlast,
        SpellCategory.ElectricBurst,
        SpellCategory.LightningVolley,
        SpellCategory.ElectricRing,
        SpellCategory.ElectricWall
    ];

    public int? SigilTrinketColor
    {
        get => GetProperty(PropertyInt.SigilTrinketColor);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.SigilTrinketColor);
            }
            else
            {
                SetProperty(PropertyInt.SigilTrinketColor, value.Value);
            }
        }
    }

    public int? SigilTrinketType
    {
        get => GetProperty(PropertyInt.SigilTrinketType);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.SigilTrinketType);
            }
            else
            {
                SetProperty(PropertyInt.SigilTrinketType, value.Value);
            }
        }
    }

    public int? SigilTrinketSkill
    {
        get => GetProperty(PropertyInt.SigilTrinketSkill);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.SigilTrinketSkill);
            }
            else
            {
                SetProperty(PropertyInt.SigilTrinketSkill, value.Value);
            }
        }
    }

    public int? SigilTrinketEffectId
    {
        get => GetProperty(PropertyInt.SigilTrinketEffectId);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.SigilTrinketEffectId);
            }
            else
            {
                SetProperty(PropertyInt.SigilTrinketEffectId, value.Value);
            }
        }
    }

    public int? SigilTrinketMaxTier
    {
        get => GetProperty(PropertyInt.SigilTrinketMaxTier);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.SigilTrinketMaxTier);
            }
            else
            {
                SetProperty(PropertyInt.SigilTrinketMaxTier, value.Value);
            }
        }
    }

    public uint? SigilTrinketTriggerSpellId
    {
        get => GetProperty(PropertyDataId.SigilTrinketTriggerSpellId);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyDataId.SigilTrinketTriggerSpellId);
            }
            else
            {
                SetProperty(PropertyDataId.SigilTrinketTriggerSpellId, value.Value);
            }
        }
    }

    public uint? SigilTrinketCastSpellId
    {
        get => GetProperty(PropertyDataId.SigilTrinketCastSpellId);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyDataId.SigilTrinketCastSpellId);
            }
            else
            {
                SetProperty(PropertyDataId.SigilTrinketCastSpellId, value.Value);
            }
        }
    }

    public double? SigilTrinketTriggerChance
    {
        get => GetProperty(PropertyFloat.SigilTrinketTriggerChance);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SigilTrinketTriggerChance);
            }
            else
            {
                SetProperty(PropertyFloat.SigilTrinketTriggerChance, value.Value);
            }
        }
    }

    public double? SigilTrinketHealthReserved
    {
        get => GetProperty(PropertyFloat.SigilTrinketHealthReserved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SigilTrinketHealthReserved);
            }
            else
            {
                SetProperty(PropertyFloat.SigilTrinketHealthReserved, value.Value);
            }
        }
    }

    public double? SigilTrinketStaminaReserved
    {
        get => GetProperty(PropertyFloat.SigilTrinketStaminaReserved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SigilTrinketStaminaReserved);
            }
            else
            {
                SetProperty(PropertyFloat.SigilTrinketStaminaReserved, value.Value);
            }
        }
    }

    public double? SigilTrinketManaReserved
    {
        get => GetProperty(PropertyFloat.SigilTrinketManaReserved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SigilTrinketManaReserved);
            }
            else
            {
                SetProperty(PropertyFloat.SigilTrinketManaReserved, value.Value);
            }
        }
    }

    public double? SigilTrinketReductionAmount
    {
        get => GetProperty(PropertyFloat.SigilTrinketReductionAmount);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SigilTrinketReductionAmount);
            }
            else
            {
                SetProperty(PropertyFloat.SigilTrinketReductionAmount, value.Value);
            }
        }
    }

    public double? SigilTrinketIntensity
    {
        get => GetProperty(PropertyFloat.SigilTrinketIntensity);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SigilTrinketIntensity);
            }
            else
            {
                SetProperty(PropertyFloat.SigilTrinketIntensity, value.Value);
            }
        }
    }

    public Spell TriggerSpell { get; set; }
    public uint? SpellLevel { get; set; }
    public bool IsWeaponSpell { get; set; }
    public WorldObject SigilTrinketTarget { get; set; }
    public Creature CreatureToCastSpellFrom { get; set; }
    public bool UseProgression { get; set; }
    public float SpellIntensityMultiplier { get; set; }
    public float SpellStatModValMultiplier { get; set; }

    public double NextTrinketTriggerTime = 0;

    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public SigilTrinket(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public SigilTrinket(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues()
    {
        IsWeaponSpell = true;
        UseProgression = true;
        SpellIntensityMultiplier = 1.0f;
        SpellStatModValMultiplier = 1.0f;
    }

    public void RechargeSigilTrinket(Hotspot manaField, Player player)
    {
        if (manaField == null)
        {
            return;
        }

        if (player == null)
        {
            return;
        }

        var forwardCommand = player.CurrentMotionState.MotionState.ForwardCommand;
        if (forwardCommand != MotionCommand.MeditateState)
        {
            return;
        }

        if (WieldSkillType != null && player.GetCreatureSkill((Skill)WieldSkillType).AdvancementClass < SkillAdvancementClass.Trained)
        {
            return;
        }

        if (Structure < MaxStructure)
        {
            IncreaseStructure(50, player);
        }
    }

    private void ResetSigilTrinketBonusStat()
    {
        GearCrit = 0;
        GearCritDamage = 0;
        GearDamageResist = 0;
        GearMaxHealth = 0;
        WardLevel = 0;
    }

    public void SetSigilTrinketBonusStat(int bonusStat, int amount)
    {
        ResetSigilTrinketBonusStat();

        SigilTrinketBonusStat = bonusStat;
        SigilTrinketBonusStatAmount = amount;

        switch (bonusStat)
        {
            case 1:
                GearCrit = amount;
                break;
            case 2:
                GearCritDamage = amount;
                break;
            case 3:
                GearDamageResist = amount;
                break;
            case 4:
                GearMaxHealth = amount;
                break;
            case 5:
                WardLevel = amount;
                break;
        }
    }

    private void IncreaseStructure(int amount, Player player)
    {
        Structure = (ushort)Math.Min((Structure ?? 0) + amount, MaxStructure ?? 0);

        player?.Session.Network.EnqueueSend(new GameMessagePublicUpdatePropertyInt(this, PropertyInt.Structure, (int)Structure));

        var isWielded = player != null && player == Wielder;

        if (Structure < MaxStructure)
        {
            if (!isWielded)
            {
                return;
            }

            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"Your {Name} gains a charge!", ChatMessageType.Magic));
            player.EnqueueBroadcast(new GameMessageScript(player.Guid, PlayScript.RestrictionEffectBlue));
        }
        else
        {
            if (!isWielded)
            {
                return;
            }

            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"Your {Name} gains a charge, it is now fully charged!", ChatMessageType.Magic));
            player.EnqueueBroadcast(new GameMessageScript(player.Guid, PlayScript.PortalStorm));
        }
    }

    public void DecreaseStructure(int amount, Player player, bool showDecreaseEffect = true)
    {
        Structure = (ushort)Math.Max((Structure ?? 0) - amount, 0);

        player?.Session.Network.EnqueueSend(new GameMessagePublicUpdatePropertyInt(this, PropertyInt.Structure, (int)Structure));
    }

    public void OnEquip(Player player)
    {
        var equippedTrinkets = player.GetEquippedSigilTrinkets();

        if (equippedTrinkets == null)
        {
            return;
        }

        var reservedHealth = 1.0;
        var reservedStamina = 1.0;
        var reservedMana = 1.0;

        foreach (var trinket in equippedTrinkets)
        {
            reservedHealth -= trinket.SigilTrinketHealthReserved ?? 0.0;
            reservedStamina -= trinket.SigilTrinketStaminaReserved ?? 0.0;
            reservedMana -= trinket.SigilTrinketManaReserved ?? 0.0;
        }

        ActivateReservedVital(player, (float)reservedHealth, Vital.Health);
        ActivateReservedVital(player, (float)reservedStamina, Vital.Stamina);
        ActivateReservedVital(player, (float)reservedMana, Vital.Mana);
    }

    public void OnDequip(Player player)
    {
        DispelAllSpellRegistriesOfId(player, SpellId.HealthReservationPenalty);
        DispelAllSpellRegistriesOfId(player, SpellId.StaminaReservationPenalty);
        DispelAllSpellRegistriesOfId(player, SpellId.ManaReservationPenalty);

        OnEquip(player);
    }

    private void ActivateReservedVital(Player player, float reservedVital, Vital vital)
    {
        if (reservedVital >= 1.0f)
        {
            return;
        }

        SpellId spellId;

        switch (vital)
        {
            case Vital.Health:
                spellId = SpellId.HealthReservationPenalty;
                break;
            case Vital.Stamina:
                spellId = SpellId.StaminaReservationPenalty;
                break;
            default:
                spellId = SpellId.ManaReservationPenalty;
                break;
        }

        var spell = new Spell(spellId);

        var addResult = player.EnchantmentManager.Add(spell, null, null, true);
        addResult.Enchantment.StatModValue = reservedVital;

        player.Session.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(player, addResult.Enchantment)));
        player.HandleSpellHooks(spell);
    }

    private static void DispelAllSpellRegistriesOfId(Player player, SpellId spellId)
    {
        while (player.EnchantmentManager.HasSpell((uint)spellId))
        {
            var propertiesEnchantmentRegistry = player.EnchantmentManager.GetEnchantment((uint)spellId);
            if (propertiesEnchantmentRegistry is null)
            {
                break;
            }

            player.EnchantmentManager.Dispel(propertiesEnchantmentRegistry);
            player.HandleSpellHooks(new Spell(propertiesEnchantmentRegistry.SpellId));
        }
    }

    /// <summary>
    /// Trinkets gain improvements to their stats on level up.
    /// Max Charges increases by 50 per level.
    /// Other stats increase by a randomized amount per level up.
    /// </summary>
    public void OnLevelUp()
    {
        MaxStructure += 50;

        if (SigilTrinketHealthReserved > 0 && SigilTrinketStaminaReserved > 0)
        {
            var adjustedVitalReserved = ThreadSafeRandom.Next(0.0005f, 0.0015f);
            SigilTrinketHealthReserved -= adjustedVitalReserved;
            SigilTrinketStaminaReserved -= adjustedVitalReserved;
        }
        else if (SigilTrinketHealthReserved > 0 && SigilTrinketManaReserved > 0)
        {
            var adjustedVitalReserved = ThreadSafeRandom.Next(0.0005f, 0.0015f);
            SigilTrinketHealthReserved -= adjustedVitalReserved;
            SigilTrinketManaReserved -= adjustedVitalReserved;
        }
        else if (SigilTrinketStaminaReserved > 0 && SigilTrinketManaReserved > 0)
        {
            var adjustedVitalReserved = ThreadSafeRandom.Next(0.0005f, 0.0015f);
            SigilTrinketStaminaReserved -= adjustedVitalReserved;
            SigilTrinketManaReserved -= adjustedVitalReserved;
        }
        else if (SigilTrinketStaminaReserved > 0)
        {
            var adjustedVitalReserved = ThreadSafeRandom.Next(0.001f, 0.003f);
            SigilTrinketStaminaReserved -= adjustedVitalReserved;
        }
        else if (SigilTrinketManaReserved > 0)
        {
            var adjustedVitalReserved = ThreadSafeRandom.Next(0.001f, 0.003f);
            SigilTrinketManaReserved -= adjustedVitalReserved;
        }

        if (CooldownDuration > 0)
        {
            var adjustedCoodlownDuration = ThreadSafeRandom.Next(0.1f, 0.3f);
            CooldownDuration -= adjustedCoodlownDuration;
        }

        if (SigilTrinketTriggerChance > 0)
        {
            var adjustedTriggerChance = ThreadSafeRandom.Next(0.01f, 0.03f);
            SigilTrinketTriggerChance += adjustedTriggerChance;
        }

        if (SigilTrinketReductionAmount > 0)
        {
            var adjustedReductionAmount = ThreadSafeRandom.Next(0.01f, 0.03f);
            SigilTrinketReductionAmount += adjustedReductionAmount;
        }

        if (SigilTrinketIntensity > 1.0)
        {
            var adjustedIntensity = ThreadSafeRandom.Next(0.01f, 0.03f);
            SigilTrinketIntensity += adjustedIntensity;
        }
    }

    public void StartCooldown(Player player)
    {
        player.EnchantmentManager.StartCooldown(this);
    }

    public static bool IsSigilTrinket(uint wcid)
    {
        uint[] trinketWcids =
        {
            (int)Factories.Enum.WeenieClassName.sigilScarabBlue,
            (int)Factories.Enum.WeenieClassName.sigilScarabYellow,
            (int)Factories.Enum.WeenieClassName.sigilScarabRed
        };

        return trinketWcids.Contains(wcid);
    }

    public static string GetEffectName(Skill skill, int effectId)
    {
        switch (skill)
        {
            case Skill.LifeMagic:
                return ((SigilTrinketLifeMagicEffect)effectId).ToString();
            case Skill.WarMagic:
                return ((SigilTrinketWarMagicEffect)effectId).ToString();
            case Skill.Shield:
                return ((SigilTrinketShieldEffect)effectId).ToString();
            case Skill.TwoHandedCombat:
                return ((SigilTrinketTwohandedCombatEffect)effectId).ToString();
            case Skill.DualWield:
                return ((SigilTrinketDualWieldEffect)effectId).ToString();
            case Skill.Thievery:
                return ((SigilTrinketThieveryEffect)effectId).ToString();
            case Skill.AssessPerson:
                return ((SigilTrinketPerceptionEffect)effectId).ToString();
            case Skill.Deception:
                return ((SigilTrinketDeceptionEffect)effectId).ToString();
            case Skill.PhysicalDefense:
                return ((SigilTrinketPhysicalDefenseEffect)effectId).ToString();
            case Skill.MagicDefense:
                return ((SigilTrinketMagicDefenseEffect)effectId).ToString();
        }

        return null;
    }

    /// <summary>
    /// Persistent storage for allowed specialized skills (OR semantics).
    /// Stored as a comma-separated list of skill numeric IDs in PropertyString.SigilTrinketAllowedSpecializedSkills.
    /// Example stored value: "5,8" (integer Skill enum values).
    /// </summary>
    public List<Skill> AllowedSpecializedSkills
    {
        get
        {
            var raw = GetProperty(PropertyString.SigilTrinketAllowedSpecializedSkills);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var list = new List<Skill>(parts.Length);
            foreach (var p in parts)
            {
                if (int.TryParse(p, out var v))
                {
                    // defensive: ensure value maps to Skill enum
                    if (Enum.IsDefined(typeof(Skill), v))
                    {
                        list.Add((Skill)v);
                    }
                }
                else
                {
                    // support legacy storage by name (optional)
                    if (Enum.TryParse<Skill>(p, true, out var sk))
                    {
                        list.Add(sk);
                    }
                }
            }

            return list.Count > 0 ? list : null;
        }
        set
        {
            if (value == null || value.Count == 0)
            {
                RemoveProperty(PropertyString.SigilTrinketAllowedSpecializedSkills);
                return;
            }

            var vals = value.Select(s => ((int)s).ToString()).ToArray();
            var raw = string.Join(",", vals);
            SetProperty(PropertyString.SigilTrinketAllowedSpecializedSkills, raw);
        }
    }
}
