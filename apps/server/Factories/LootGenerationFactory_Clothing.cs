using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;
using WeenieClassName = ACE.Entity.Enum.WeenieClassName;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    private static WorldObject CreateArmor(
        TreasureDeath profile,
        bool isMagical,
        bool isArmor,
        TreasureItemType treasureItenmType = TreasureItemType.Undef,
        LootBias lootBias = LootBias.UnBiased,
        bool mutate = true
    )
    {
        var minType = 0;
        var maxType = 1;

        var armorWeenie = 0;

        if (treasureItenmType == TreasureItemType.ArmorWarrior)
        {
            switch (profile.Tier)
            {
                case 1:
                case 2:
                default:
                    maxType = (int)LootTables.ArmorTypeWarrior.ChainmailArmor;
                    break;
                case 3:
                case 4:
                    maxType = (int)LootTables.ArmorTypeWarrior.ScalemailArmor;
                    break;
                case 5:
                    maxType = (int)LootTables.ArmorTypeWarrior.CovenantArmor;
                    break;
                case 6:
                    maxType = (int)LootTables.ArmorTypeWarrior.NariyidArmor;
                    break;
                case 7:
                case 8:
                    maxType = (int)LootTables.ArmorTypeWarrior.OlthoiCeldonArmor;
                    break;
            }
            LootTables.ArmorTypeWarrior armorType;

            armorType = (LootTables.ArmorTypeWarrior)ThreadSafeRandom.Next(minType, maxType);
            var table = LootTables.GetLootTable(armorType);
            var rng = ThreadSafeRandom.Next(0, table.Length - 1);

            armorWeenie = table[rng];
        }
        else if (treasureItenmType == TreasureItemType.ArmorRogue)
        {
            switch (profile.Tier)
            {
                case 1:
                case 2:
                default:
                    maxType = (int)LootTables.ArmorTypeRogue.StuddedLeatherArmor;
                    break;
                case 3:
                case 4:
                    maxType = (int)LootTables.ArmorTypeRogue.YoroiArmor;
                    break;
                case 5:
                    maxType = (int)LootTables.ArmorTypeRogue.KoujiaArmor;
                    break;
                case 6:
                    maxType = (int)LootTables.ArmorTypeRogue.LoricaArmor;
                    break;
                case 7:
                case 8:
                    maxType = (int)LootTables.ArmorTypeRogue.OlthoiKoujiaArmor;
                    break;
            }

            LootTables.ArmorTypeRogue armorType;

            armorType = (LootTables.ArmorTypeRogue)ThreadSafeRandom.Next(minType, maxType);
            var table = LootTables.GetLootTable(armorType);
            var rng = ThreadSafeRandom.Next(0, table.Length - 1);

            armorWeenie = table[rng];
        }
        else if (treasureItenmType == TreasureItemType.ArmorCaster)
        {
            switch (profile.Tier)
            {
                case 1:
                case 2:
                case 3:
                case 4:
                default:
                    maxType = (int)LootTables.ArmorTypeCaster.RobesAndCloth;
                    break;
                case 5:
                    maxType = (int)LootTables.ArmorTypeCaster.AmuliArmor;
                    break;
                case 6:
                    maxType = (int)LootTables.ArmorTypeCaster.ChiranArmor;
                    break;
                case 7:
                case 8:
                    maxType = (int)LootTables.ArmorTypeCaster.OlthoiAmuliArmor;
                    break;
            }

            LootTables.ArmorTypeCaster armorType;

            armorType = (LootTables.ArmorTypeCaster)ThreadSafeRandom.Next(minType, maxType);
            var table = LootTables.GetLootTable(armorType);
            var rng = ThreadSafeRandom.Next(0, table.Length - 1);

            armorWeenie = table[rng];
        }

        var wo = WorldObjectFactory.CreateNewWorldObject((uint)armorWeenie);

        if (wo != null && mutate)
        {
            MutateArmor(wo, profile, isMagical);
        }

        return wo;
    }

    private static void MutateArmor(
        WorldObject wo,
        TreasureDeath profile,
        bool isMagical,
        LootTables.ArmorType armorType = LootTables.ArmorType.Undef,
        TreasureRoll roll = null
    )
    {
        // material type
        var materialType = GetMaterialType(wo, profile.Tier);
        if (materialType > 0)
        {
            wo.MaterialType = materialType;
        }

        // item color
        MutateColor(wo);

        // gem count / gem material
        if (wo.GemCode != null)
        {
            wo.GemCount = GemCountChance.Roll(wo.GemCode.Value, profile.Tier);
        }
        else
        {
            wo.GemCount = ThreadSafeRandom.Next(1, 6);
        }

        wo.GemType = RollGemType(profile.Tier);

        // burden
        MutateBurden(wo, profile, false);

        // weight class
        var armorWeightClass = GetArmorWeightClass(wo.WeenieClassId);
        wo.ArmorWeightClass = (int)armorWeightClass;

        // wield requirements (attribute, type, amount)
        wo.WieldSkillType = 0;

        if (profile.Tier > 0)
        {
            // clothing has a level requirement
            if (wo.ArmorWeightClass == (int)ArmorWeightClass.None)
            {
                wo.WieldRequirements = WieldRequirement.Level;
                wo.WieldDifficulty = GetArmorLevelReq(profile.Tier);
            }
            // armor req based on weight class
            else
            {
                wo.WieldRequirements = WieldRequirement.RawAttrib;
                wo.WieldSkillType = GetWeightClassAttributeReq((ArmorWeightClass)wo.ArmorWeightClass);
                wo.WieldDifficulty = GetWieldDifficultyPerTier(profile.Tier);
            }
        }

        AssignArmorLevel(wo, profile);

        // Set Stamina/Mana Penalty
        var mod = GetArmorResourcePenalty(wo) * (wo.ArmorSlots ?? 1);
        wo.SetProperty(PropertyFloat.ArmorResourcePenalty, mod);

        // Spells
        AssignMagic(wo, profile, roll, true, isMagical);

        var totalSkillModPercentile = 0.0;
        var totalGearRatingPercentile = 0.0;
        if (roll != null)
        {
            TryMutateGearRating(wo, profile, roll, out totalGearRatingPercentile);

            TryMutateArmorSkillMod(wo, profile, roll, out totalSkillModPercentile);

            MutateArmorModVsType(wo, profile);

            NormalizeProtectionLevels(wo);
        }

        // workmanship
        //Console.WriteLine($"\n\n{wo.Name}");
        wo.ItemWorkmanship = GetArmorWorkmanship(wo, totalSkillModPercentile, totalGearRatingPercentile);

        // item value
        //if (wo.HasMutateFilter(MutateFilter.Value))   // fixme: data
        MutateValue(wo, profile.Tier, roll);

        wo.LongDesc = GetLongDesc(wo);

        AssignJewelSlots(wo);
    }

    /// <summary>
    /// Assign a final AL and Ward value based upon tier
    /// Used values given at https://asheron.fandom.com/wiki/Loot#Armor_Levels for setting the AL mod values
    /// so as to not exceed the values listed in that table
    /// </summary>
    private static void AssignArmorLevel(WorldObject wo, TreasureDeath treasureDeath)
    {
        var tier = treasureDeath.Tier;

        if (wo.ArmorType == null)
        {
            _log.Warning($"[LOOT] Missing PropertyInt.ArmorType on loot item {wo.WeenieClassId} - {wo.Name}");
            return;
        }

        var baseArmorLevel = wo.ArmorLevel ?? 75;

        if (tier < 2)
        {
            return;
        }

        var armorSlots = wo.ArmorSlots ?? 1;

        // Get Armor/Ward Level
        var baseWardLevel = wo.ArmorWeightClass == (int)ArmorWeightClass.Cloth ? 7 : 5;

        switch (wo.ArmorStyle)
        {
            case (int)ArmorStyle.Amuli:
            case (int)ArmorStyle.Chiran:
            case (int)ArmorStyle.OlthoiAmuli:
                baseArmorLevel = 80;
                baseWardLevel = 6;
                break;
            case (int)ArmorStyle.Leather:
            case (int)ArmorStyle.Yoroi:
            case (int)ArmorStyle.Lorica:
                baseArmorLevel = 80;
                break;
            case (int)ArmorStyle.StuddedLeather:
            case (int)ArmorStyle.Koujia:
            case (int)ArmorStyle.OlthoiKoujia:
                baseArmorLevel = 85;
                break;
            case (int)ArmorStyle.Chainmail:
            case (int)ArmorStyle.Scalemail:
            case (int)ArmorStyle.Nariyid:
                baseArmorLevel = 90;
                break;
            case (int)ArmorStyle.Platemail:
            case (int)ArmorStyle.Celdon:
            case (int)ArmorStyle.OlthoiCeldon:
                baseArmorLevel = 95;
                break;
            case (int)ArmorStyle.Covenant:
            case (int)ArmorStyle.OlthoiArmor:
                baseArmorLevel = 100;
                break;
        }

        switch ((int)wo.WeenieClassId)
        {
            case (int)WeenieClassName.W_BUCKLER_CLASS: // Buckler
                baseArmorLevel = 80;
                baseWardLevel = 5;
                break;
            // case (int)WeenieClassName.W_SHIELDKITE_CLASS: // Small Kite Shield
            // case (int)WeenieClassName.W_SHIELDROUND_CLASS: // Small Round Shield
            //     baseArmorLevel = 80;
            //     baseWardLevel = 5;
            //     break;
            case (int)WeenieClassName.W_SHIELDKITE_CLASS: // Kite Shield
            case (int)WeenieClassName.W_SHIELDROUND_CLASS: // Round Shield
                baseArmorLevel = 85;
                baseWardLevel = 6;
                break;
            case (int)WeenieClassName.W_SHIELDKITELARGE_CLASS: // Large Kite Shield
            case (int)WeenieClassName.W_SHIELDROUNDLARGE_CLASS: // Large Round Shield
                baseArmorLevel = 90;
                baseWardLevel = 7;
                break;
            case (int)WeenieClassName.W_SHIELDTOWER_CLASS: // Tower Shield
                baseArmorLevel = 95;
                baseWardLevel = 8;
                break;
            case (int)WeenieClassName.W_SHIELDCOVENANT_CLASS: // Covenant Shield
                baseArmorLevel = 100;
                baseWardLevel = 10;
                break;
        }

        // Final Calculation
        var newArmorLevel = baseArmorLevel * (tier - 1) + GetDiminishingRoll(treasureDeath) * baseArmorLevel;
        var newWardLevel = baseWardLevel * (tier - 1) * armorSlots + GetDiminishingRoll(treasureDeath) * baseWardLevel;

        // Assign levels
        wo.SetProperty(PropertyInt.ArmorLevel, (int)newArmorLevel);
        wo.SetProperty(PropertyInt.WardLevel, (int)newWardLevel);

        if ((wo.ResistMagic == null || wo.ResistMagic < 9999) && wo.ArmorLevel >= 1000)
        {
            _log.Warning($"[LOOT] Standard armor item exceeding upper AL threshold {wo.WeenieClassId} - {wo.Name}");
        }
    }

    private static WorldObject CreateSocietyArmor(TreasureDeath profile, bool mutate = true)
    {
        var society = 0;
        var armortype = 0;

        if (profile.TreasureType >= 2971 && profile.TreasureType <= 2980)
        {
            society = 0; // CH
        }
        else if (profile.TreasureType >= 2981 && profile.TreasureType <= 2990)
        {
            society = 1; // EW
        }
        else if (profile.TreasureType >= 2991 && profile.TreasureType <= 3000)
        {
            society = 2; // RB
        }

        switch (profile.TreasureType)
        {
            case 2971:
            case 2981:
            case 2991:
                armortype = 0; // BP
                break;
            case 2972:
            case 2982:
            case 2992:
                armortype = 1; // Gauntlets
                break;
            case 2973:
            case 2983:
            case 2993:
                armortype = 2; // Girth
                break;
            case 2974:
            case 2984:
            case 2994:
                armortype = 3; // Greaves
                break;
            case 2975:
            case 2985:
            case 2995:
                armortype = 4; // Helm
                break;
            case 2976:
            case 2986:
            case 2996:
                armortype = 5; // Pauldrons
                break;
            case 2977:
            case 2987:
            case 2997:
                armortype = 6; // Tassets
                break;
            case 2978:
            case 2988:
            case 2998:
                armortype = 7; // Vambraces
                break;
            case 2979:
            case 2989:
            case 2999:
                armortype = 8; // Sollerets
                break;
            default:
                break;
        }

        var societyArmorWeenie = LootTables.SocietyArmorMatrix[armortype][society];
        var wo = WorldObjectFactory.CreateNewWorldObject((uint)societyArmorWeenie);

        if (wo != null && mutate)
        {
            MutateSocietyArmor(wo, profile, true);
        }

        return wo;
    }

    private static void MutateSocietyArmor(
        WorldObject wo,
        TreasureDeath profile,
        bool isMagical,
        TreasureRoll roll = null
    )
    {
        // why is this a separate method??

        var materialType = GetMaterialType(wo, profile.Tier);
        if (materialType > 0)
        {
            wo.MaterialType = materialType;
        }

        if (wo.GemCode != null)
        {
            wo.GemCount = GemCountChance.Roll(wo.GemCode.Value, profile.Tier);
        }
        else
        {
            wo.GemCount = ThreadSafeRandom.Next(1, 6);
        }

        wo.GemType = RollGemType(profile.Tier);

        wo.ItemWorkmanship = WorkmanshipChance.Roll(profile.Tier, profile.LootQualityMod);

        wo.Value = Roll_ItemValue(wo, profile.Tier);

        // looks like society armor always had impen on it
        AssignMagic(wo, profile, roll, true, isMagical);

        AssignArmorLevel(wo, profile);

        wo.LongDesc = GetLongDesc(wo);

        // try mutate burden, if MutateFilter exists
        MutateBurden(wo, profile, false);

    }

    private static WorldObject CreateCloak(TreasureDeath profile, bool mutate = true)
    {
        // even chance between 11 different types of cloaks
        var cloakType = ThreadSafeRandom.Next(0, LootTables.Cloaks.Length - 1);

        var cloakWeenie = LootTables.Cloaks[cloakType];

        var wo = WorldObjectFactory.CreateNewWorldObject((uint)cloakWeenie);

        if (wo != null && mutate)
        {
            MutateCloak(wo, profile);
        }

        return wo;
    }

    private static void MutateCloak(WorldObject wo, TreasureDeath profile, TreasureRoll roll = null)
    {
        wo.ItemMaxLevel = CloakChance.Roll_ItemMaxLevel(profile);

        // wield difficulty, based on ItemMaxLevel
        switch (wo.ItemMaxLevel)
        {
            case 1:
                wo.WieldDifficulty = 30;
                break;
            case 2:
                wo.WieldDifficulty = 60;
                break;
            case 3:
                wo.WieldDifficulty = 90;
                break;
            case 4:
                wo.WieldDifficulty = 120;
                break;
            case 5:
                wo.WieldDifficulty = 150;
                break;
        }

        wo.IconOverlayId = IconOverlay_ItemMaxLevel[wo.ItemMaxLevel.Value - 1];

        // equipment set
        wo.EquipmentSetId = CloakChance.RollEquipmentSet();

        // proc spell
        var surgeSpell = CloakChance.RollProcSpell();

        if (surgeSpell != SpellId.Undef)
        {
            wo.ProcSpell = (uint)surgeSpell;

            // Cloaked In Skill is the only self-targeted spell
            if (wo.ProcSpell == (uint)SpellId.CloakAllSkill)
            {
                wo.ProcSpellSelfTargeted = true;
            }
            else
            {
                wo.ProcSpellSelfTargeted = false;
            }

            wo.CloakWeaveProc = 1;
        }
        else
        {
            // Damage Reduction proc
            wo.CloakWeaveProc = 2;
        }

        // material type
        wo.MaterialType = GetMaterialType(wo, profile.Tier);

        // workmanship
        wo.Workmanship = WorkmanshipChance.Roll(profile.Tier, profile.LootQualityMod);

        if (roll != null && profile.Tier == 8)
        {
            TryMutateGearRating(wo, profile, roll, out var totalGearRatingPercentile);
        }

        // item value
        //if (wo.HasMutateFilter(MutateFilter.Value))
        MutateValue(wo, profile.Tier, roll);
    }

    private static void MutateArmorModVsType(WorldObject wo, TreasureDeath profile)
    {
        // for the PropertyInt.MutateFilters found in py16 data,
        // items either had all of these, or none of these

        // only the elemental types could mutate
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsFire);
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsCold);
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsAcid);
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsElectric);
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsSlash);
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsPierce);
        TryMutateArmorModVsType(wo, profile, PropertyFloat.ArmorModVsBludgeon);
    }

    private static bool TryMutateArmorModVsType(WorldObject wo, TreasureDeath profile, PropertyFloat prop)
    {
        var armorModVsType = wo.GetProperty(prop);

        if (armorModVsType == null)
        {
            return false;
        }

        var baseMod = wo.GetProperty(prop) ?? 0;

        // Target range for armor that has 1.0 base protection: 0.4 to 1.6. Rare chance for -0.05 or +0.05.
        // Loot Quality Mod contributes.
        var minimumRoll = 0.0f;
        if (profile is not null)
        {
            minimumRoll = (float)(1 - Math.Exp(-1 * profile.LootQualityMod));
        }

        var roll = ThreadSafeRandom.Next(minimumRoll, 1.2f);
        roll -= 0.6;

        // rare chance for outliers
        var outlierRoll = ThreadSafeRandom.Next(1, 10);
        switch (outlierRoll)
        {
            case 1:
                roll -= 0.05;
                break;
            case 10:
                roll += 0.05;
                break;
        }

        var newMod = baseMod + roll;

        wo.SetProperty(prop, newMod);

        return true;
    }

    private static void NormalizeProtectionLevels(WorldObject wo)
    {
        if (wo.ArmorLevel is null ||
            wo.ArmorModVsAcid is null ||
            wo.ArmorModVsBludgeon is null ||
            wo.ArmorModVsCold is null ||
            wo.ArmorModVsElectric is null ||
            wo.ArmorModVsFire is null ||
            wo.ArmorModVsPierce is null ||
            wo.ArmorModVsSlash is null)
        {
            _log.Error("LootGeneration_Clothing.NormalizeProtectionLevels({WorldObject}) - Armor or Protection Level is null", wo.Name);
            return; 
        }

        var protectionLevelSum =
            wo.ArmorModVsSlash
            + wo.ArmorModVsPierce
            + wo.ArmorModVsBludgeon
            + wo.ArmorModVsAcid
            + wo.ArmorModVsFire
            + wo.ArmorModVsCold
            + wo.ArmorModVsElectric;

        var scalar = protectionLevelSum / 7.0;

        wo.ArmorLevel = (int)(wo.ArmorLevel.Value * scalar);
        wo.ArmorModVsSlash /= scalar;
        wo.ArmorModVsPierce /= scalar;
        wo.ArmorModVsBludgeon /= scalar;
        wo.ArmorModVsAcid /= scalar;
        wo.ArmorModVsFire /= scalar;
        wo.ArmorModVsCold /= scalar;
        wo.ArmorModVsElectric /= scalar;
    }

    private static bool TryMutateGearRating(
        WorldObject wo,
        TreasureDeath profile,
        TreasureRoll roll,
        out double totalGearRatingPercentile
    )
    {
        totalGearRatingPercentile = 0;

        if (profile.Tier < 6)
        {
            return false;
        }

        var tier = profile.Tier;
        var weightType = wo.ArmorWeightClass;

        var gearRatingAmount1 = GetGearRatingAmount(tier, profile, out var gearRatingPercentile1);
        var gearRatingAmount2 = GetGearRatingAmount(tier, profile, out var gearRatingPercentile2);

        totalGearRatingPercentile += gearRatingPercentile1;
        totalGearRatingPercentile += gearRatingPercentile2;
        totalGearRatingPercentile /= 2;

        if (gearRatingAmount1 == 0 && gearRatingAmount2 == 0)
        {
            return false;
        }

        var armorSlots = wo.ArmorSlots ?? 1;

        if (weightType == (int)ArmorWeightClass.Cloth)
        {
            wo.GearDamage = gearRatingAmount1 * armorSlots;
            wo.GearHealingBoost = gearRatingAmount2 * armorSlots;
        }
        else if (weightType == (int)ArmorWeightClass.Light)
        {
            wo.GearCritDamage = gearRatingAmount1 * armorSlots;
            wo.GearCrit = gearRatingAmount2 * armorSlots;
        }
        else if (weightType == (int)ArmorWeightClass.Heavy)
        {
            wo.GearDamageResist = gearRatingAmount1 * armorSlots;
            wo.GearCritResist = (gearRatingAmount2 + 1) * armorSlots;
        }
        else if (roll.ItemType == TreasureItemType_Orig.Clothing)
        {
            return false;
        }
        else
        {
            _log.Error($"TryMutateGearRating({wo.Name}, {weightType}): unknown weight class");
            return false;
        }

        return true;
    }

    private static bool TryMutateArmorSkillMod(
        WorldObject wo,
        TreasureDeath profile,
        TreasureRoll roll,
        out double highestModPercentile
    )
    {
        highestModPercentile = 0.0f;
        var modPercentile = 0.0f;

        var qualityMod = profile.LootQualityMod != 0.0f ? profile.LootQualityMod : 0.0f;

        var potentialTypes = new List<int>();
        var numTypes = wo.ArmorType == (int)LootTables.ArmorType.MiscClothing ? 3 : 5;
        for (var i = 1; i <= numTypes; i++)
        {
            potentialTypes.Add(i);
        }

        var rolledTypes = GetRolledTypes(potentialTypes, qualityMod);

        float numRolledTypesMultiplier;
        switch (rolledTypes.Count)
        {
            default:
            case 1:
                numRolledTypesMultiplier = 1.0f;
                break; // 100% per mod, 100% total.
            case 2:
                numRolledTypesMultiplier = 0.75f;
                break; // 75% per mod, 150% total.
            case 3:
                numRolledTypesMultiplier = 0.5833f;
                break; // 58.33% per mod, 175% total.
            case 4:
                numRolledTypesMultiplier = 0.475f;
                break; // 47.5% per mod, 190% total.
            case 5:
                numRolledTypesMultiplier = 0.4f;
                break; // 40% per mod, 200% total.
        }

        var weightType = wo.ArmorWeightClass;
        var armorSlotsMod = (wo.ArmorSlots ?? 1.0f) / 10;

        // roll mod values for types
        if (wo.ArmorType == (int)LootTables.ArmorType.MiscClothing && wo.ArmorWeightClass == 0)
        {
            var miscClothingMultiplier = 0.5f;

            foreach (var type in rolledTypes)
            {
                var amount =
                    GetArmorSkillAmount(profile, wo, out modPercentile)
                    * numRolledTypesMultiplier
                    * miscClothingMultiplier;
                highestModPercentile = modPercentile > highestModPercentile ? modPercentile : highestModPercentile;

                switch (type)
                {
                    case 1:
                        wo.ArmorHealthMod = amount;
                        break;
                    case 2:
                        wo.ArmorStaminaMod = amount;
                        break;
                    case 3:
                        wo.ArmorManaMod = amount;
                        break;
                }
            }
        }
        else if (weightType == (int)ArmorWeightClass.Cloth)
        {
            wo.ArmorWarMagicMod = 0.0;
            wo.ArmorLifeMagicMod = 0.0;
            wo.ArmorPerceptionMod = 0.0;
            wo.ArmorDeceptionMod = 0.0;
            wo.ArmorManaRegenMod = 0.0;
            wo.ManaConversionMod = 0.0;

            foreach (var type in rolledTypes)
            {
                var amount =
                    GetArmorSkillAmount(profile, wo, out modPercentile) * numRolledTypesMultiplier * armorSlotsMod;
                highestModPercentile = modPercentile > highestModPercentile ? modPercentile : highestModPercentile;

                switch (type)
                {
                    case 1:
                        wo.ArmorWarMagicMod = amount;
                        break;
                    case 2:
                        wo.ArmorLifeMagicMod = amount;
                        break;
                    case 3:
                        if (ThreadSafeRandom.Next(0, 1) == 0)
                        {
                            wo.ArmorPerceptionMod = amount;
                        }
                        else
                        {
                            wo.ArmorDeceptionMod = amount;
                        }

                        break;
                    case 4:
                        wo.ArmorManaRegenMod = amount;
                        break;
                    case 5:
                        wo.ManaConversionMod = amount;
                        break;
                }
            }
        }
        else if (weightType == (int)ArmorWeightClass.Light)
        {
            wo.ArmorAttackMod = 0.0;
            wo.ArmorDualWieldMod = 0.0;
            wo.ArmorShieldMod = 0.0;
            wo.ArmorThieveryMod = 0.0;
            wo.ArmorRunMod = 0.0;
            wo.ArmorStaminaMod = 0.0;
            wo.ArmorPerceptionMod = 0.0;
            wo.ArmorDeceptionMod = 0.0;

            foreach (var type in rolledTypes)
            {
                var amount =
                    GetArmorSkillAmount(profile, wo, out modPercentile) * numRolledTypesMultiplier * armorSlotsMod;
                highestModPercentile = modPercentile > highestModPercentile ? modPercentile : highestModPercentile;

                switch (type)
                {
                    case 1:
                        wo.ArmorAttackMod = amount;
                        break;
                    case 2:
                        if (IsShieldWcid(wo))
                        {
                            wo.ArmorShieldMod = amount;
                        }
                        else
                        {
                            wo.ArmorDualWieldMod = amount;
                        }

                        break;
                    case 3:
                        if (ThreadSafeRandom.Next(0, 1) == 0)
                        {
                            wo.ArmorThieveryMod = amount;
                        }
                        else
                        {
                            wo.ArmorRunMod = amount;
                        }

                        break;
                    case 4:
                        wo.ArmorStaminaRegenMod = amount;
                        break;
                    case 5:
                        if (ThreadSafeRandom.Next(0, 1) == 0)
                        {
                            wo.ArmorPerceptionMod = amount;
                        }
                        else
                        {
                            wo.ArmorDeceptionMod = amount;
                        }

                        break;
                }
            }
        }
        else if (weightType == (int)ArmorWeightClass.Heavy)
        {
            wo.ArmorAttackMod = 0.0;
            wo.ArmorPhysicalDefMod = 0.0;
            wo.ArmorMagicDefMod = 0.0;
            wo.ArmorShieldMod = 0.0;
            wo.ArmorTwohandedCombatMod = 0.0;
            wo.ArmorPerceptionMod = 0.0;
            wo.ArmorDeceptionMod = 0.0;
            wo.ArmorHealthRegenMod = 0.0;

            foreach (var type in rolledTypes)
            {
                var amount =
                    GetArmorSkillAmount(profile, wo, out modPercentile) * numRolledTypesMultiplier * armorSlotsMod;
                highestModPercentile = modPercentile > highestModPercentile ? modPercentile : highestModPercentile;

                switch (type)
                {
                    case 1:
                        wo.ArmorAttackMod = amount;
                        break;
                    case 2:
                        wo.ArmorPhysicalDefMod = amount;
                        wo.ArmorMagicDefMod = amount;
                        break;
                    case 3:
                        if (IsShieldWcid(wo) || ThreadSafeRandom.Next(0, 1) == 0)
                        {
                            wo.ArmorShieldMod = amount;
                        }
                        else
                        {
                            wo.ArmorTwohandedCombatMod = amount;
                        }

                        break;
                    case 4:
                        if (ThreadSafeRandom.Next(0, 1) == 0)
                        {
                            wo.ArmorPerceptionMod = amount;
                        }
                        else
                        {
                            wo.ArmorDeceptionMod = amount;
                        }

                        break;
                    case 5:
                        wo.ArmorHealthRegenMod = amount;
                        break;
                }
            }
        }
        else
        {
            _log.Error($"TryMutateGearRating({wo.Name}, {weightType}): unknown weight class");
            return false;
        }

        if (wo.ArmorLevel != null)
        {
            wo.BaseArmor = wo.ArmorLevel;
        }

        if (wo.WardLevel != null)
        {
            wo.BaseWard = wo.WardLevel;
        }

        if (wo.ArmorWarMagicMod != null)
        {
            wo.BaseArmorWarMagicMod = wo.ArmorWarMagicMod;
        }

        if (wo.ArmorLifeMagicMod != null)
        {
            wo.BaseArmorLifeMagicMod = wo.ArmorLifeMagicMod;
        }

        if (wo.ArmorMagicDefMod != null)
        {
            wo.BaseArmorMagicDefMod = wo.ArmorMagicDefMod;
        }

        if (wo.ArmorPhysicalDefMod != null)
        {
            wo.BaseArmorPhysicalDefMod = wo.ArmorPhysicalDefMod;
        }

        if (wo.ArmorMissileDefMod != null)
        {
            wo.BaseArmorMissileDefMod = wo.ArmorMissileDefMod;
        }

        if (wo.ArmorDualWieldMod != null)
        {
            wo.BaseArmorDualWieldMod = wo.ArmorDualWieldMod;
        }

        if (wo.ArmorRunMod != null)
        {
            wo.BaseArmorRunMod = wo.ArmorRunMod;
        }

        if (wo.ArmorAttackMod != null)
        {
            wo.BaseArmorAttackMod = wo.ArmorAttackMod;
        }

        if (wo.ArmorHealthRegenMod != null)
        {
            wo.BaseArmorHealthRegenMod = wo.ArmorHealthRegenMod;
        }

        if (wo.ArmorStaminaRegenMod != null)
        {
            wo.BaseArmorStaminaRegenMod = wo.ArmorStaminaRegenMod;
        }

        if (wo.ArmorManaRegenMod != null)
        {
            wo.BaseArmorManaRegenMod = wo.ArmorManaRegenMod;
        }

        if (wo.ArmorShieldMod != null)
        {
            wo.BaseArmorShieldMod = wo.ArmorShieldMod;
        }

        if (wo.ArmorPerceptionMod != null)
        {
            wo.BaseArmorPerceptionMod = wo.ArmorPerceptionMod;
        }

        if (wo.ArmorThieveryMod != null)
        {
            wo.BaseArmorThieveryMod = wo.ArmorThieveryMod;
        }

        if (wo.ArmorHealthMod != null)
        {
            wo.BaseArmorHealthMod = wo.ArmorHealthMod;
        }

        if (wo.ArmorStaminaMod != null)
        {
            wo.BaseArmorStaminaMod = wo.ArmorStaminaMod;
        }

        if (wo.ArmorManaMod != null)
        {
            wo.BaseArmorManaMod = wo.ArmorManaMod;
        }

        if (wo.ArmorResourcePenalty != null)
        {
            wo.BaseArmorResourcePenalty = wo.ArmorResourcePenalty;
        }

        if (wo.ArmorDeceptionMod != null)
        {
            wo.BaseArmorDeceptionMod = wo.ArmorDeceptionMod;
        }

        if (wo.ArmorTwohandedCombatMod != null)
        {
            wo.BaseArmorTwohandedCombatMod = wo.ArmorTwohandedCombatMod;
        }

        return true;
    }

    private static int GetRollForArmorMod(float lootQualityMod)
    {
        var qualityMod = lootQualityMod * 100;
        //var tempRateIncrease = 60;

        return ThreadSafeRandom.Next((int)qualityMod, 100);
    }

    private static List<int> GetRolledTypes(List<int> potentialTypes, float qualityMod)
    {
        var rolledTypes = new List<int>();
        var numPotentialTypes = potentialTypes.Count;
        var numTypes = ThreadSafeRandom.Next(1, numPotentialTypes);

        for (var i = 0; i < numTypes; i++)
        {
            var type = potentialTypes[ThreadSafeRandom.Next(0, potentialTypes.Count - 1)];
            potentialTypes.Remove(type);
            rolledTypes.Add(type);
        }

        return rolledTypes;
    }

    private static ArmorWeightClass GetArmorWeightClass(uint armorWcid)
    {
        foreach (var wcid in LootTables.Cloth)
        {
            if (wcid == armorWcid)
            {
                return ArmorWeightClass.Cloth;
            }
        }
        foreach (var wcid in LootTables.Light)
        {
            if (wcid == armorWcid)
            {
                return ArmorWeightClass.Light;
            }
        }
        foreach (var wcid in LootTables.Heavy)
        {
            if (wcid == armorWcid)
            {
                return ArmorWeightClass.Heavy;
            }
        }
        return ArmorWeightClass.None;
    }

    private static WieldAttributeType GetWieldAttributeType(ArmorWeightClass armorWeightClass)
    {
        switch (armorWeightClass)
        {
            case ArmorWeightClass.Cloth:
                return WieldAttributeType.Self;

            case ArmorWeightClass.Light:
                return WieldAttributeType.Quickness;

            case ArmorWeightClass.Heavy:
                return WieldAttributeType.Strength;

            default:
                return WieldAttributeType.Invalid;
        }
    }

    private static int GetGearRatingAmount(int tier, TreasureDeath td, out float gearRatingPercentile)
    {
        var lootQualityMod = td.LootQualityMod;
        var roll = ThreadSafeRandom.Next(lootQualityMod, 1);

        var maxMod = Math.Max(tier - 4, 0);
        var mod = (int)(maxMod * roll);

        var maxPossibleMod = 3;
        gearRatingPercentile = (float)mod / maxPossibleMod;

        return mod;
    }

    private static double GetArmorSkillAmount(TreasureDeath treasureDeath, WorldObject wo, out float modPercentile)
    {
        var tier = Math.Clamp(treasureDeath.Tier - 1, 0, 7);
        float[] bonusModRollPerTier = { 0.0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.075f, 0.1f };

        var doubleMod = wo.ValidLocations == EquipMask.HeadWear ? 2.0f : 1.0f; // Headwear gets double-value mods
        var minMod = 0.1f * doubleMod;
        var rollPercentile = GetDiminishingRoll(treasureDeath);
        var statRoll = minMod * rollPercentile;
        var armorMod = minMod + statRoll + bonusModRollPerTier[tier];

        var maxPossibleMod = minMod + minMod + bonusModRollPerTier[7];
        modPercentile = (armorMod - minMod) / (maxPossibleMod - minMod);

        //Console.WriteLine($"GetArmorSkillAmount() \n" +
        //    $" -Tier: {tier}\n" +
        //    $" -DiminishingRoll: {statRoll}, Mod: {armorMod}, MaxMod: {maxPossibleMod}, ModPercentile: {modPercentile}");

        return armorMod;
    }

    private static float GetArmorResourcePenalty(WorldObject wo)
    {
        var mod = 0.0f;

        if (wo.ArmorWeightClass == (int)ArmorWeightClass.Heavy)
        {
            mod = 0.05f;
        }

        switch (wo.ArmorStyle)
        {
            case (int)ArmorStyle.Amuli:
            case (int)ArmorStyle.Chiran:
            case (int)ArmorStyle.OlthoiAmuli:
                mod = 0.02f;
                break;
            case (int)ArmorStyle.StuddedLeather:
            case (int)ArmorStyle.Koujia:
            case (int)ArmorStyle.OlthoiKoujia:
                mod = 0.02f;
                break;
            case (int)ArmorStyle.Chainmail:
            case (int)ArmorStyle.Scalemail:
            case (int)ArmorStyle.Nariyid:
                mod = 0.03f;
                break;
            case (int)ArmorStyle.Platemail:
            case (int)ArmorStyle.Celdon:
            case (int)ArmorStyle.OlthoiCeldon:
                mod = 0.04f;
                break;
            case (int)ArmorStyle.Covenant:
            case (int)ArmorStyle.OlthoiArmor:
                mod = 0.05f;
                break;
        }

        switch ((int)wo.WeenieClassId)
        {
            case 44: // Buckler
                mod = 0.05f;
                break;
            case 1050111: // Small Kite Shield
            case 1050112: // Small Round Shield
                mod = 0.05f;
                break;
            case 91: // Kite Shield
            case 93: // Round Shield
                mod = 0.1f;
                break;
            case 92: // Large Kite Shield
            case 94: // Large Round Shield
                mod = 0.15f;
                break;
            case 95: // Tower Shield
                mod = 0.2f;
                break;
            case 21158: // Covenant Shield
                mod = 0.25f;
                break;
        }

        return mod;
    }

    private static int GetArmorWorkmanship(WorldObject wo, double skillModsPercentile, double gearRatingPercentile)
    {
        var divisor = 0;
        var sum = 0.0;

        // Armor + Protection Levels
        var maxArmorLevel = GetMaxArmorLevel(wo);
        var armorLevelPercentile = 0.0f;

        var avgProtectionLevel = GetAverageProtectionLevel(wo);
        var minProtectionLevel = 0.25f;
        var maxProtectionLevel = 1.5f;
        var protectionLevelPercentile = 1.0f;
        if (avgProtectionLevel != 0)
        {
            protectionLevelPercentile =
                (avgProtectionLevel - minProtectionLevel) / (maxProtectionLevel - minProtectionLevel);
        }

        if (wo.ItemType != ItemType.Clothing)
        {
            if (wo.ArmorLevel > 0 && maxArmorLevel > 0)
            {
                armorLevelPercentile = (float)wo.ArmorLevel / maxArmorLevel;
                armorLevelPercentile += protectionLevelPercentile;
                armorLevelPercentile /= 2;

                sum += armorLevelPercentile;
                divisor++;
            }
            else
            {
                armorLevelPercentile =
                    (avgProtectionLevel - minProtectionLevel) / (maxProtectionLevel - minProtectionLevel);

                sum += armorLevelPercentile;
                divisor++;
            }
        }

        // Ward
        var maxWardLevel = GetMaxWardLevel(wo);
        var wardLevelPercentile = 0.0f;
        if (wo.WardLevel > 0 && maxWardLevel > 0)
        {
            wardLevelPercentile = (float)wo.WardLevel / maxWardLevel;

            sum += wardLevelPercentile;
            divisor++;
        }

        // Armor Skill Mods
        if (skillModsPercentile == float.NaN)
        {
            skillModsPercentile = 0;
        }

        sum += skillModsPercentile;
        divisor++;

        // Gear Ratings
        if (wo.ItemType != ItemType.Clothing)
        {
            sum += gearRatingPercentile;
            divisor++;
        }
        // Average Percentile
        var finalPercentile = sum / divisor;
        //Console.WriteLine($" -MaxArmor: {maxArmorLevel} - ArmorLevel: {wo.ArmorLevel} -MaxProt: 1.5 -ProtLevel: {avgProtectionLevel} -Armor/Protection %: {armorLevelPercentile}\n" + $" -MaxWard: {maxWardLevel} -WardLevel: {wo.WardLevel} -Ward %: {wardLevelPercentile}\n" + $" -Mods %: {skillModsPercentile}\n" + $" -Ratings %: {gearRatingPercentile}\n" + $" -Divisor: {divisor}\n" +
        //    $" --FINAL: {finalPercentile}\n\n");

        // Workmanship Calculation
        return (int)Math.Clamp(Math.Round(finalPercentile * 10, 0), 1, 10);
    }

    private static int GetMaxArmorLevel(WorldObject wo)
    {
        var maxArmorLevel = 700;

        if (wo.ArmorStyle == null && wo.ItemType != ItemType.Clothing)
        {
            _log.Error("GetMaxArmorLevel(WorldObject {WorldObject} ({ID})) - ArmorStyle is null. Defaulting to {MaxArmorLevel}", wo.Name, wo.WeenieClassId, maxArmorLevel);
        }

        var armorStyle = (ArmorStyle)(wo.ArmorStyle ?? 0);

        switch (armorStyle)
        {
            case ArmorStyle.Cloth:
                maxArmorLevel = 525;
                break;
            case ArmorStyle.Amuli:
            case ArmorStyle.Chiran:
                maxArmorLevel = 560;
                break;
            case ArmorStyle.Leather:
            case ArmorStyle.Yoroi:
            case ArmorStyle.Lorica:
                maxArmorLevel = 560;
                break;
            case ArmorStyle.StuddedLeather:
            case ArmorStyle.Koujia:
            case ArmorStyle.OlthoiKoujia:
                maxArmorLevel = 595;
                break;
            case ArmorStyle.Chainmail:
            case ArmorStyle.Scalemail:
                maxArmorLevel = 630;
                break;
            case ArmorStyle.Platemail:
            case ArmorStyle.Celdon:
            case ArmorStyle.OlthoiCeldon:
            case ArmorStyle.Nariyid:
                maxArmorLevel = 665;
                break;
            case ArmorStyle.OlthoiArmor:
            case ArmorStyle.Covenant:
                maxArmorLevel = 700;
                break;
        }

        return (int)(maxArmorLevel * 1.1f);
    }

    private static int GetMaxWardLevel(WorldObject wo)
    {
        var wardLevel = 49;

        if (wo.ArmorStyle == null && wo.ItemType != ItemType.Clothing)
        {
            _log.Error("GetMaxWardLevel(WorldObject {WorldObject} ({ID})) - ArmorStyle is null. Defaulting to {MaxWardLevel}", wo.Name, wo.WeenieClassId, wardLevel);
        }

        var armorStyle = (ArmorStyle)(wo.ArmorStyle ?? 0);
        var armorSlots = wo.ArmorSlots ?? 1;

        switch (armorStyle)
        {
            case ArmorStyle.Cloth:
                wardLevel = 49;
                break;
            case ArmorStyle.Amuli:
            case ArmorStyle.Chiran:
                wardLevel = 42;
                break;
            case ArmorStyle.Leather:
            case ArmorStyle.Yoroi:
            case ArmorStyle.Lorica:
            case ArmorStyle.StuddedLeather:
            case ArmorStyle.Koujia:
            case ArmorStyle.OlthoiKoujia:
            case ArmorStyle.Chainmail:
            case ArmorStyle.Scalemail:
            case ArmorStyle.Platemail:
            case ArmorStyle.Celdon:
            case ArmorStyle.OlthoiCeldon:
            case ArmorStyle.Nariyid:
                wardLevel = 35;
                break;
            case ArmorStyle.OlthoiArmor:
            case ArmorStyle.Covenant:
                wardLevel = 28;
                break;
        }

        return (int)(wardLevel * armorSlots * 1.1f);
    }

    private static float GetAverageProtectionLevel(WorldObject wo)
    {
        var amount = 0.0;
        var divisor = 0;

        if (wo.ArmorModVsSlash != null)
        {
            amount += (float)wo.ArmorModVsSlash;
            divisor++;
        }
        if (wo.ArmorModVsPierce != null)
        {
            amount += (float)wo.ArmorModVsPierce;
            divisor++;
        }
        if (wo.ArmorModVsFire != null)
        {
            amount += (float)wo.ArmorModVsFire;
            divisor++;
        }
        if (wo.ArmorModVsCold != null)
        {
            amount += (float)wo.ArmorModVsCold;
            divisor++;
        }
        if (wo.ArmorModVsAcid != null)
        {
            amount += (float)wo.ArmorModVsAcid;
            divisor++;
        }
        if (wo.ArmorModVsElectric != null)
        {
            amount += (float)wo.ArmorModVsElectric;
            divisor++;
        }
        if (wo.ArmorModVsBludgeon != null)
        {
            amount += (float)wo.ArmorModVsBludgeon;
            divisor++;
        }

        if (divisor != 7)
        {
            _log.Error("Mutate Error during GetAverageProtectionLevel() - Armor Protection Levels returned null for {Wo}.", wo);
        }

        amount /= divisor;

        return (float)amount;
    }

    private static bool IsShieldWcid(WorldObject wo)
    {
        var shieldWcids = LootTables.Shields;

        if (shieldWcids.Contains((int)wo.WeenieClassId))
        {
            return true;
        }

        return false;
    }

    private static int GetWeightClassAttributeReq(ArmorWeightClass weightClass)
    {
        switch (weightClass)
        {
            default:
            case ArmorWeightClass.Heavy:
                return 1; // Strength
            case ArmorWeightClass.Light:
                return 4; // Coordination
            case ArmorWeightClass.Cloth:
                return 6; // Self
        }
    }
}
