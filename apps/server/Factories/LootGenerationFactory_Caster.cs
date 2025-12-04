using System;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    /// <summary>
    /// Creates Caster (Wand, Staff, Orb)
    /// </summary>
    public static WorldObject CreateCaster(
        TreasureDeath profile,
        bool isMagical,
        int wield = -1,
        bool forceWar = false,
        bool mutate = true
    )
    {
        // Refactored 11/20/19  - HarliQ
        var casterWeenie = 0;
        var subType = 0;
        var element = 0;

        if (wield == -1)
        {
            wield = RollWieldDifficulty(profile.Tier, TreasureWeaponType.Caster);
        }

        // Getting the caster Weenie needed.

        // Determine caster type: 0 = Orb, 1 = Scepter, 2 = Baton, 3 = Staff
        subType = ThreadSafeRandom.Next(0, LootTables.TimelineCasterWeaponsMatrix.Length - 1);
        var subTypeLength = ThreadSafeRandom.Next(0, LootTables.TimelineCasterWeaponsMatrix[subType].Length - 1);

        element = ThreadSafeRandom.Next(0, subTypeLength);
        casterWeenie = LootTables.TimelineCasterWeaponsMatrix[subType][element];

        var wo = WorldObjectFactory.CreateNewWorldObject((uint)casterWeenie);

        if (wo != null && mutate)
        {
            MutateCaster(wo, profile, isMagical, wield, null);
        }

        return wo;
    }

    private static void MutateCaster(
        WorldObject wo,
        TreasureDeath profile,
        bool isMagical,
        int? wieldDifficulty = null,
        TreasureRoll roll = null
    )
    {
        wo.Tier = GetTierValue(profile);

        // Life/War
        if (ThreadSafeRandom.Next(0, 1) == 0)
        {
            wo.WieldSkillType2 = (int)Skill.LifeMagic;
            wo.WeaponSkill = Skill.LifeMagic;
            wo.W_DamageType = DamageType.Nether;
            wo.UiEffects = UiEffects.Nether;
            SetLifeText(wo);
        }
        else
        {
            wo.WieldSkillType2 = (int)Skill.WarMagic;
            wo.WeaponSkill = Skill.WarMagic;

            // Add element/material to low tier orb/wand/scepter/staff
            if (
                wo.WeenieClassId == 2366
                || wo.WeenieClassId == 2547
                || wo.WeenieClassId == 2548
                || wo.WeenieClassId == 2472
            )
            {
                RollCasterElement(profile, wo);
            }
        }

        // Material
        var materialType = GetMaterialType(wo, profile.Tier);
        if (materialType > 0)
        {
            wo.MaterialType = materialType;
        }

        // Item color
        if (wo.WeenieClassId == 2548)
        {
            MutateScepterColor(wo);
        }
        else
        {
            MutateColor(wo);
        }

        // Wield Reqs
        wo.WieldRequirements = WieldRequirement.RawAttrib;
        wo.WieldDifficulty = GetWieldDifficultyPerTier(profile.Tier);
        wo.WieldSkillType = GetWeaponPrimaryAttribute((Skill)wo.WieldSkillType2);

        wo.WieldRequirements2 = WieldRequirement.Training;
        wo.WieldDifficulty2 = 2;

        // Roll Elemental Damage Mod
        TryMutateCasterWeaponDamage(wo, profile, out var damagePercentile);

        // T0 casters have damage penalty
        if (profile.Tier == 1)
        {
            var damagePenaltyRoll = ThreadSafeRandom.Next(0, 25);
            var finalRating = -25 + (int)(damagePenaltyRoll * GetDiminishingRoll(profile));
            wo.GearDamage = finalRating;
            wo.DamageRating = finalRating;
        }

        // Roll Weapon Mods
        TryMutateWeaponMods(wo, profile, out var modsPercentile, true);

        TryMutateWeaponSubtypeBonuses(wo, profile, out var subtypeBonusPercentile);

        // gem count / gem material
        if (wo.GemCode != null)
        {
            wo.GemCount = GemCountChance.Roll(wo.GemCode.Value, profile.Tier);
        }
        else
        {
            wo.GemCount = ThreadSafeRandom.Next(1, 5);
        }

        wo.GemType = RollGemType(profile.Tier);

        // workmanship
        wo.ItemWorkmanship = GetCasterWorkmanship(wo, damagePercentile, modsPercentile, subtypeBonusPercentile);

        // burden?

        // spells
        AssignMagic(wo, profile, roll, false, isMagical);
        wo.ItemSpellcraft = RollSpellcraft(wo, profile);

        // item value
        //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
        MutateValue(wo, profile.Tier, roll);

        // long description
        wo.LongDesc = GetLongDesc(wo);

        wo.ItemDifficulty = null;

        if (profile.Tier == 1)
        {
            //wo.Name += " (damaged)";
        }

        // assign jewel slots
        AssignJewelSlots(wo);

        wo.BaseElementalDamageMod = (wo.ElementalDamageMod == null ? 0 : wo.ElementalDamageMod);
        wo.BaseWeaponRestorationSpellsMod = (wo.WeaponRestorationSpellsMod == null ? 0 : wo.WeaponRestorationSpellsMod);
        wo.BaseWeaponPhysicalDefense = (wo.WeaponPhysicalDefense == null ? 0 : wo.WeaponPhysicalDefense);
        wo.BaseWeaponMagicalDefense = (wo.WeaponMagicalDefense == null ? 0 : wo.WeaponMagicalDefense);
        wo.BaseManaConversionMod = (wo.ManaConversionMod == null ? 0 : wo.ManaConversionMod);
        wo.BaseWeaponWarMagicMod = (wo.WeaponWarMagicMod == null ? 0 : wo.WeaponWarMagicMod);
        wo.BaseWeaponLifeMagicMod = (wo.WeaponLifeMagicMod == null ? 0 : wo.WeaponLifeMagicMod);
    }

    private static void SetLifeText(WorldObject wo)
    {
        if (wo.Name.Contains("Slashing"))
        {
            wo.Name = wo.Name.Replace("Slashing", "Life");
        }
        else if (wo.Name.Contains("Blunt"))
        {
            wo.Name = wo.Name.Replace("Blunt", "Life");
        }
        else if (wo.Name.Contains("Piercing"))
        {
            wo.Name = wo.Name.Replace("Piercing", "Life");
        }
        else if (wo.Name.Contains("Acid"))
        {
            wo.Name = wo.Name.Replace("Acid", "Life");
        }
        else if (wo.Name.Contains("Fire"))
        {
            wo.Name = wo.Name.Replace("Fire", "Life");
        }
        else if (wo.Name.Contains("Frost"))
        {
            wo.Name = wo.Name.Replace("Frost", "Life");
        }
        else if (wo.Name.Contains("Electric"))
        {
            wo.Name = wo.Name.Replace("Electric", "Life");
        }
    }

    private static void MutateCaster_SpellDID(WorldObject wo, TreasureDeath profile)
    {
        var firstSpell = CasterSlotSpells.Roll(wo);

        var spellLevels = SpellLevelProgression.GetSpellLevels(firstSpell);

        if (spellLevels == null)
        {
            _log.Error($"MutateCaster_SpellDID: couldn't find {firstSpell}");
            return;
        }

        if (spellLevels.Count != 8)
        {
            _log.Error($"MutateCaster_SpellDID: found {spellLevels.Count} spell levels for {firstSpell}, expected 8");
            return;
        }

        var spellLevel = Math.Clamp(profile.Tier - 1, 1, 7);

        wo.SpellDID = (uint)spellLevels[spellLevel - 1];

        var spell = new Server.Entity.Spell(wo.SpellDID.Value);

        var castableMod = CasterSlotSpells.IsOrb(wo) ? 5.0f : 2.5f;

        //wo.ItemManaCost = (int)(spell.BaseMana * castableMod);
        wo.ItemManaCost = (int)spell.BaseMana;

        wo.ItemUseable = Usable.SourceWieldedTargetRemoteNeverWalk;

        wo.CooldownId = 1001;
        wo.CooldownDuration = 10;
    }

    private static string GetCasterScript(bool isElemental = false)
    {
        var elementalStr = isElemental ? "elemental" : "non_elemental";

        return $"Casters.caster_{elementalStr}.txt";
    }

    private static bool GetMutateCasterData(uint wcid)
    {
        for (var i = 0; i < LootTables.CasterWeaponsMatrix.Length; i++)
        {
            var table = LootTables.CasterWeaponsMatrix[i];

            for (var element = 0; element < table.Length; element++)
            {
                if (wcid == table[element])
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Return caster subtype
    /// Orb = 0, Scepter = 1, Wand/Baton = 2, Staff = 3
    /// </summary>
    public static int GetCasterSubType(WorldObject wo)
    {
        var subType = 0;
        if (wo.Tier <= 5)
        {
            for (var i = 0; i < LootTables.TimelineCasterWeaponsMatrix[0].Length; i++)
            {
                if (wo.WeenieClassId == LootTables.TimelineCasterWeaponsMatrix[0][i])
                {
                    subType = i;
                }
            }
        }
        else
        {
            for (var i = 1; i < LootTables.TimelineCasterWeaponsMatrix.Length; i++)
            {
                foreach (var type in LootTables.TimelineCasterWeaponsMatrix[i])
                {
                    if (wo.WeenieClassId == type)
                    {
                        subType = i - 1;
                    }
                }
            }
        }

        return subType;
    }

    private static bool RollMagicSkillMod(TreasureDeath treasureDeath, WorldObject wo)
    {
        var magicSkillMod = 0.0f;
        var maxMagicSkillMod = (treasureDeath.Tier * 2.5f) / 100;
        var lootQualityMod = treasureDeath.LootQualityMod * 100;
        var roll = ThreadSafeRandom.Next((int)lootQualityMod, 100);

        if (treasureDeath.Tier == 1)
        {
            //switch (roll)
            //{
            //    case <= 50: elementBonus += 0.0f; break;
            //    case <= 75: elementBonus += 0.005f; break;
            //    case <= 90: elementBonus += 0.01f; break;
            //    case <= 95: elementBonus += 0.015f; break;
            //    case <= 99: elementBonus += 0.02f; break;
            //    case 100: elementBonus += 0.025f; break;
            //}
        }
        else if (treasureDeath.Tier == 2)
        {
            switch (roll)
            {
                case <= 50:
                    magicSkillMod += 0.0f;
                    break;
                case <= 75:
                    magicSkillMod += (float)ThreadSafeRandom.Next(0, 2) / 2 / 100;
                    break;
                case <= 90:
                    magicSkillMod += (float)ThreadSafeRandom.Next(2, 4) / 2 / 100;
                    break;
                case <= 95:
                    magicSkillMod += (float)ThreadSafeRandom.Next(4, 6) / 2 / 100;
                    break;
                case <= 99:
                    magicSkillMod += (float)ThreadSafeRandom.Next(6, 8) / 2 / 100;
                    break;
                case 100:
                    magicSkillMod += (float)ThreadSafeRandom.Next(8, 10) / 2 / 100;
                    break;
            }
        }
        else if (treasureDeath.Tier == 3)
        {
            switch (roll)
            {
                case <= 50:
                    magicSkillMod += 0.0f;
                    break;
                case <= 75:
                    magicSkillMod += (float)ThreadSafeRandom.Next(0, 3) / 2 / 100;
                    break;
                case <= 90:
                    magicSkillMod += (float)ThreadSafeRandom.Next(3, 6) / 2 / 100;
                    break;
                case <= 95:
                    magicSkillMod += (float)ThreadSafeRandom.Next(6, 9) / 2 / 100;
                    break;
                case <= 99:
                    magicSkillMod += (float)ThreadSafeRandom.Next(9, 12) / 2 / 100;
                    break;
                case 100:
                    magicSkillMod += (float)ThreadSafeRandom.Next(12, 15) / 2 / 100;
                    break;
            }
        }
        else
        {
            switch (roll)
            {
                case <= 50:
                    magicSkillMod += 0.0f;
                    break;
                case <= 75:
                    magicSkillMod += (float)(maxMagicSkillMod - 0.10 + (float)ThreadSafeRandom.Next(0, 4) / 2 / 100);
                    break;
                case <= 90:
                    magicSkillMod += (float)(maxMagicSkillMod - 0.10 + (float)ThreadSafeRandom.Next(4, 8) / 2 / 100);
                    break;
                case <= 95:
                    magicSkillMod += (float)(maxMagicSkillMod - 0.10 + (float)ThreadSafeRandom.Next(8, 12) / 2 / 100);
                    break;
                case <= 99:
                    magicSkillMod += (float)(maxMagicSkillMod - 0.10 + (float)ThreadSafeRandom.Next(12, 16) / 2 / 100);
                    break;
                case 100:
                    magicSkillMod += (float)(maxMagicSkillMod - 0.10 + (float)ThreadSafeRandom.Next(16, 20) / 2 / 100);
                    break;
            }
        }

        // TODO Make MagicSkillMod float for casters
        //if (magicSkillMod >= 0.1f)
        //    wo.WeaponOffense += magicSkillMod;

        return true;
    }

    /// <summary>
    /// Rolls for the Element for casters
    /// </summary>
    private static void RollCasterElement(TreasureDeath profile, WorldObject wo)
    {
        var elementType = 0;
        var uiEffect = 0;

        var roll = ThreadSafeRandom.Next(1, 7);
        switch (roll)
        {
            case 1:
                elementType = 0x1; // slash
                uiEffect = 0x0400;
                break;
            case 2:
                elementType = 0x2; // pierce
                uiEffect = 0x0800;
                break;
            case 3:
                elementType = 0x4; // bludge
                uiEffect = 0x0200;
                break;
            case 4:
                elementType = 0x8; // cold
                uiEffect = 0x0080;
                break;
            case 5:
                elementType = 0x10; // fire
                uiEffect = 0x0020;
                break;
            case 6:
                elementType = 0x20; // acid
                uiEffect = 0x0100;
                break;
            case 7:
                elementType = 0x40; // electric
                uiEffect = 0x0040;
                break;
        }
        wo.W_DamageType = (DamageType)elementType;
        wo.UiEffects = (UiEffects)uiEffect;
    }

    /// <summary>
    /// Rolls Bonus Ward Cleaving for Orbs
    /// 0% to 20% (up to 10% based on tier)
    /// </summary>
    private static void RollBonusWardCleaving(TreasureDeath treasureDeath, WorldObject wo, out float percentile)
    {
        float[] minMod = { 0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f };

        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, minMod.Length);
        var wardCleavingMod = 0.1f * GetDiminishingRoll(treasureDeath);
        wardCleavingMod += minMod[tier];

        const float maxMod = 0.2f;
        percentile = wardCleavingMod / maxMod;

        wardCleavingMod = 1.0f - wardCleavingMod;

        wo.SetProperty(PropertyFloat.IgnoreWard, wardCleavingMod);
    }

    /// <summary>
    /// Rolls Bonus Armor Cleaving for Spears
    /// 0% to 20% (up to 10% based on tier)
    /// </summary>
    private static void RollBonusArmorCleaving(TreasureDeath treasureDeath, WorldObject wo, out float percentile)
    {
        float[] minMod = { 0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f };

        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, minMod.Length);
        var armorCleavingMod = 0.1f * GetDiminishingRoll(treasureDeath);
        armorCleavingMod += minMod[tier];

        const float maxMod = 0.2f;
        percentile = armorCleavingMod / maxMod;

        armorCleavingMod = 1.0f - armorCleavingMod;

        wo.SetProperty(PropertyFloat.IgnoreArmor, armorCleavingMod);
    }

    /// <summary>
    /// Rolls Bonus Stamina Cost Reduction for Swords and UA
    /// 0% to 20% (up to 10% based on tier)
    /// </summary>
    private static void RollBonusStaminaCostReduction(TreasureDeath treasureDeath, WorldObject wo, out float percentile)
    {
        float[] minMod = { 0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f };

        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, minMod.Length);
        var staminaCostReductionMod = 0.1f * GetDiminishingRoll(treasureDeath);
        staminaCostReductionMod += minMod[tier];

        wo.SetProperty(PropertyFloat.StaminaCostReductionMod, staminaCostReductionMod);

        var maxMod = 0.2f;
        percentile = staminaCostReductionMod / maxMod;
    }

    /// <summary>
    /// Rolls Bonus Crit Chance for Axes, Daggers, and Scepters
    /// 0% to 10% (up to 5% based on tier)
    /// </summary>
    private static void RollBonusCritChance(TreasureDeath treasureDeath, WorldObject wo, out float percentile)
    {
        float[] minMod = { 0.0f, 0.01f, 0.015f, 0.02f, 0.025f, 0.03f, 0.04f, 0.05f };

        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, minMod.Length);
        var critChanceMod = 0.05f * GetDiminishingRoll(treasureDeath);
        critChanceMod += minMod[tier];

        wo.SetProperty(PropertyFloat.CriticalFrequency, 0.1f + critChanceMod);

        var maxMod = 0.1f;
        percentile = critChanceMod / maxMod;
    }

    /// <summary>
    /// Rolls Bonus Crit Damage for Maces, Staves (melee), and Wands/Batons
    /// 0% to 100% (up to 50% based on tier)
    /// </summary>
    private static void RollBonusCritDamage(TreasureDeath treasureDeath, WorldObject wo, out float percentile)
    {
        float[] minMod = { 0.0f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f };

        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, minMod.Length);
        var critDamageMod = 0.5f * GetDiminishingRoll(treasureDeath);
        critDamageMod += minMod[tier];

        if (wo.GetProperty(PropertyFloat.CriticalMultiplier) != null)
        {
            var currentCritMultipier = wo.GetProperty(PropertyFloat.CriticalMultiplier);
            var newCritMultipier = currentCritMultipier + 1.0f + critDamageMod;
            wo.SetProperty(PropertyFloat.CriticalMultiplier, (float)newCritMultipier);
        }
        else
        {
            var newCritMultipier = 1.0f + critDamageMod;
            wo.SetProperty(PropertyFloat.CriticalMultiplier, (float)newCritMultipier);
        }

        var maxMod = 1.0f;
        percentile = critDamageMod / maxMod;
    }

    /// <summary>
    /// Rolls Bonus Defense Mods for Staves (caster). (Up to +20% based on tier)
    /// </summary>
    private static float RollBonusDefenseMod(TreasureDeath treasureDeath, WorldObject wo, out float percentile)
    {
        float[] minMod = { 0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f };

        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, minMod.Length);
        var defenseMod = 0.1f * GetDiminishingRoll(treasureDeath);
        defenseMod += minMod[tier];

        var maxMod = 0.2f;
        percentile = defenseMod / maxMod;

        return defenseMod;
    }

    /// <summary>
    /// Rolls for the ManaConversionMod for casters
    /// </summary>
    private static double RollManaConversionMod(int tier)
    {
        var magicMod = 0;

        var chance = 0;
        switch (tier)
        {
            case 1:
            case 2:
                magicMod = 0;
                break;
            case 3:
                chance = ThreadSafeRandom.Next(1, 1000);
                if (chance > 900)
                {
                    magicMod = 5;
                }
                else if (chance > 800)
                {
                    magicMod = 4;
                }
                else if (chance > 700)
                {
                    magicMod = 3;
                }
                else if (chance > 600)
                {
                    magicMod = 2;
                }
                else if (chance > 500)
                {
                    magicMod = 1;
                }

                break;
            case 4:
                chance = ThreadSafeRandom.Next(1, 1000);
                if (chance > 900)
                {
                    magicMod = 10;
                }
                else if (chance > 800)
                {
                    magicMod = 9;
                }
                else if (chance > 700)
                {
                    magicMod = 8;
                }
                else if (chance > 600)
                {
                    magicMod = 7;
                }
                else if (chance > 500)
                {
                    magicMod = 6;
                }
                else
                {
                    magicMod = 5;
                }

                break;
            case 5:
                chance = ThreadSafeRandom.Next(1, 1000);
                if (chance > 900)
                {
                    magicMod = 10;
                }
                else if (chance > 800)
                {
                    magicMod = 9;
                }
                else if (chance > 700)
                {
                    magicMod = 8;
                }
                else if (chance > 600)
                {
                    magicMod = 7;
                }
                else if (chance > 500)
                {
                    magicMod = 6;
                }
                else
                {
                    magicMod = 5;
                }

                break;
            case 6:
                chance = ThreadSafeRandom.Next(1, 1000);
                if (chance > 900)
                {
                    magicMod = 10;
                }
                else if (chance > 800)
                {
                    magicMod = 9;
                }
                else if (chance > 700)
                {
                    magicMod = 8;
                }
                else if (chance > 600)
                {
                    magicMod = 7;
                }
                else if (chance > 500)
                {
                    magicMod = 6;
                }
                else
                {
                    magicMod = 5;
                }

                break;
            case 7:
                chance = ThreadSafeRandom.Next(1, 1000);
                if (chance > 900)
                {
                    magicMod = 10;
                }
                else if (chance > 800)
                {
                    magicMod = 9;
                }
                else if (chance > 700)
                {
                    magicMod = 8;
                }
                else if (chance > 600)
                {
                    magicMod = 7;
                }
                else if (chance > 500)
                {
                    magicMod = 6;
                }
                else
                {
                    magicMod = 5;
                }

                break;
            default:
                chance = ThreadSafeRandom.Next(1, 1000);
                if (chance > 900)
                {
                    magicMod = 10;
                }
                else if (chance > 800)
                {
                    magicMod = 9;
                }
                else if (chance > 700)
                {
                    magicMod = 8;
                }
                else if (chance > 600)
                {
                    magicMod = 7;
                }
                else if (chance > 500)
                {
                    magicMod = 6;
                }
                else
                {
                    magicMod = 5;
                }

                break;
        }

        var manaDMod = magicMod / 100.0;

        return manaDMod;
    }

    /// <summary>
    /// Rolls for ElementalDamageMod for caster weapons
    /// </summary>
    private static double RollElementalDamageMod(int? wield, TreasureDeath treasureDeath = null)
    {
        double elementBonus = 0;

        int[] maxModPerTier = { 10, 20, 30, 40, 50, 75, 100, 126 };
        if (treasureDeath != null)
        {
            var maxMod = (float)maxModPerTier[treasureDeath.Tier - 1] / 100;
            var minMod = (float)maxModPerTier[treasureDeath.Tier - 1] / 200;
            var bonusAmount = (maxMod - minMod) * GetDiminishingRoll(treasureDeath);

            elementBonus = minMod + bonusAmount;
        }

        elementBonus += 1;

        return elementBonus;
    }

    /// <summary>
    /// Rolls for RestorationSpellMod for caster weapons
    /// </summary>
    private static double RollRestorationSpellsMod(int? wield, TreasureDeath treasureDeath = null)
    {
        var restorationMod = 0.0;

        int[] maxModPerTier = { 10, 20, 30, 40, 50, 75, 100, 126 };
        if (treasureDeath != null)
        {
            var maxMod = (float)maxModPerTier[treasureDeath.Tier - 1] / 100;
            var minMod = (float)maxModPerTier[treasureDeath.Tier - 1] / 200;
            var bonusAmount = (maxMod - minMod) * GetDiminishingRoll(treasureDeath);

            restorationMod = minMod + bonusAmount + 1;
        }

        return restorationMod;
    }

    private static void MutateScepterColor(WorldObject wo)
    {
        wo.IgnoreCloIcons = true;

        switch (wo.W_DamageType)
        {
            case DamageType.Undef:
                wo.IconId = 0x06008950;
                wo.PaletteTemplate = 0;
                wo.UiEffects = UiEffects.BoostHealth;
                break;
            case DamageType.Slash:
                wo.IconId = 0x06008951;
                wo.PaletteTemplate = 4;
                wo.UiEffects = (UiEffects)0x0400;
                break;
            case DamageType.Pierce:
                wo.IconId = 0x06008952;
                wo.PaletteTemplate = 21;
                wo.UiEffects = (UiEffects)0x0800;
                break;
            case DamageType.Bludgeon:
                wo.IconId = 0x06008953;
                wo.PaletteTemplate = 61;
                wo.UiEffects = (UiEffects)0x0200;
                break;
            case DamageType.Acid:
                wo.IconId = 0x06008954;
                wo.PaletteTemplate = 8;
                wo.UiEffects = (UiEffects)0x0100;
                break;
            case DamageType.Fire:
                wo.IconId = 0x06008955;
                wo.PaletteTemplate = 14;
                wo.UiEffects = (UiEffects)0x0020;
                break;
            case DamageType.Cold:
                wo.IconId = 0x06008956;
                wo.PaletteTemplate = 2;
                wo.UiEffects = (UiEffects)0x0080;
                break;
            case DamageType.Electric:
                wo.IconId = 0x06008957;
                wo.PaletteTemplate = 82;
                wo.UiEffects = (UiEffects)0x0040;
                break;
        }
    }

    private static void TryMutateCasterWeaponDamage(WorldObject wo, TreasureDeath profile, out double damagePercentile)
    {
        damagePercentile = 0;

        var tier = Math.Clamp(profile.Tier - 1, 0, 7);
        if (tier > 0)
        {
            float damageRoll;

            // Calculate Max, Min, and Roll
            var maxDamageMod = GetCasterMaxDamageMod()[tier];
            var minDamageMod = GetCasterMinDamageMod()[tier];
            var diminishedDamageModRoll = (maxDamageMod - minDamageMod) * GetDiminishingRoll(profile);

            damageRoll = minDamageMod + diminishedDamageModRoll;

            if (wo.WieldSkillType2 == (int)Skill.WarMagic)
            {
                wo.ElementalDamageMod = damageRoll;
                wo.WeaponRestorationSpellsMod = (damageRoll - 1.0) * 0.25 + 1.0; // 25% of damage roll
            }
            else
            {
                wo.ElementalDamageMod = (damageRoll - 1.0) * 0.5 + 1.0; // 50% of damage roll
                wo.WeaponRestorationSpellsMod = wo.ElementalDamageMod; // 50% of damage roll
            }

            var maxPossibleDamage = GetCasterMaxDamageMod()[7];
            damagePercentile = ((double)damageRoll - 1) / (maxPossibleDamage - 1);
        }
    }

    private static int GetCasterWorkmanship(
        WorldObject wo,
        double damagePercentile,
        double modsPercentile,
        float subtypeBonusesPercentile
    )
    {
        var divisor = 4; // Damage x2 + Mods + Subtype

        // Average Percentile
        var finalPercentile = (damagePercentile * 2 + modsPercentile + subtypeBonusesPercentile) / divisor;

        //Console.WriteLine($"{wo.Name}\n -Mods %: {modsPercentile}\n" + $" -Damage %: {damagePercentile}\n" + $" -Divisor: {divisor}\n" +
        //    $" --FINAL: {finalPercentile}\n\n");

        // Workmanship Calculation
        return Math.Clamp((int)Math.Round(finalPercentile * 10, 0), 1, 10);
    }
}
