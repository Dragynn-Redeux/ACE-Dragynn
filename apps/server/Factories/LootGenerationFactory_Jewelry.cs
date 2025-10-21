using System;
using System.Linq;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    private static WorldObject CreateJewelry(TreasureDeath profile, bool isMagical, bool mutate = true)
    {
        // 31% chance ring, 31% chance bracelet, 30% chance necklace 8% chance Trinket

        var jewelrySlot = ThreadSafeRandom.Next(1, 100);
        int jewelType;
        if (jewelrySlot <= 33)
        {
            jewelType = LootTables.ringItems[ThreadSafeRandom.Next(0, LootTables.ringItems.Length - 1)];
        }
        else if (jewelrySlot <= 66)
        {
            jewelType = LootTables.braceletItems[ThreadSafeRandom.Next(0, LootTables.braceletItems.Length - 1)];
        }
        else
        {
            jewelType = LootTables.necklaceItems[ThreadSafeRandom.Next(0, LootTables.necklaceItems.Length - 1)];
        }

        var wo = WorldObjectFactory.CreateNewWorldObject((uint)jewelType);

        if (wo != null && mutate)
        {
            MutateJewelry(wo, profile, isMagical);
        }

        return wo;
    }

    private static void MutateJewelry(WorldObject wo, TreasureDeath profile, bool isMagical, TreasureRoll roll = null)
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
            wo.GemCount = ThreadSafeRandom.Next(1, 5);
        }

        wo.GemType = RollGemType(profile.Tier);

        // assign magic
        AssignMagic(wo, profile, roll, false, isMagical);

        // item value
        //  if (wo.HasMutateFilter(MutateFilter.Value))     // fixme: data
        MutateValue(wo, profile.Tier, roll);

        wo.LongDesc = GetLongDesc(wo);

        var totalGearRatingPercentile = 0.0;

        if (profile.Tier > 1)
        {
            wo.WieldRequirements = WieldRequirement.Level;
            wo.WieldSkillType = 0;
            wo.WieldDifficulty = GetArmorLevelReq(profile.Tier);
        }
        TryMutateJewelryMods(wo, profile, roll, isMagical, out totalGearRatingPercentile);

        // Workmanship
        wo.Workmanship = GetJewelryWorkmanship(wo, totalGearRatingPercentile);
        AssignJewelSlots(wo);
        wo.BaseWard = (wo.WardLevel == null ? 0 : wo.WardLevel);
        wo.BaseMaxMana = (wo.ItemMaxMana == null ? 0 : wo.ItemMaxMana);
    }

    private static bool GetMutateJewelryData(uint wcid)
    {
        foreach (var jewelryTable in LootTables.jewelryTables)
        {
            if (jewelryTable.Contains((int)wcid))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryMutateJewelryMods(
        WorldObject wo,
        TreasureDeath td,
        TreasureRoll roll,
        bool isMagical,
        out double totalRollPercentile
    )
    {
        totalRollPercentile = 0.0f;

        var tier = td.Tier;
        var jewelryType = wo.ValidLocations;
        var qualityMod = td.LootQualityMod != 0.0f ? td.LootQualityMod : 0.0f;

        // Roll Ward
        var minWard = GetMaxWardOfTier(tier);
        wo.WardLevel = (int)(minWard * GetDiminishingRoll(td) + minWard);
        var maxWardRollPercentile = (float)wo.WardLevel / (GetMaxWardOfTier(8));

        // Necklaces
        if (jewelryType == EquipMask.NeckWear)
        {
            var minRating = (float)(tier - 1);
            var rollPercentile = GetDiminishingRoll(td);
            var ratingRoll = minRating * rollPercentile;
            var finalAmount = ratingRoll + minRating;
            var ratingPercentile = finalAmount / 14.0;

            var type = ThreadSafeRandom.Next(1, 2);
            switch (type)
            {
                case 1:
                    wo.GearHealingBoost = (int)finalAmount * 2;
                    break;
                case 2:
                    wo.GearMaxHealth = (int)finalAmount * 2;
                    break;
            }
            totalRollPercentile += ratingPercentile;

            totalRollPercentile = (totalRollPercentile + maxWardRollPercentile) / 2;

            return true;
        }
        // Rings
        else if (jewelryType == EquipMask.FingerWear)
        {
            wo.WardLevel /= 2;

            var minRating = (float)(tier - 1) / 2;
            var rollPercentile = GetDiminishingRoll(td);
            var ratingRoll = minRating * rollPercentile;
            var finalAmount = ratingRoll + minRating;
            var ratingPercentile = finalAmount / 7.0;

            var type = ThreadSafeRandom.Next(1, 2);
            switch (type)
            {
                case 1:
                    wo.GearCritDamage = (int)finalAmount;
                    break;
                case 2:
                    wo.GearCritDamageResist = (int)finalAmount;
                    break;
            }
            totalRollPercentile += ratingPercentile;

            totalRollPercentile = (totalRollPercentile + maxWardRollPercentile) / 2;

            return true;
        }
        // Bracelets
        else if (jewelryType == EquipMask.WristWear)
        {
            wo.WardLevel /= 2;

            var minRating = (float)(tier - 1) / 2;
            var rollPercentile = GetDiminishingRoll(td);
            var ratingRoll = minRating * rollPercentile;
            var finalAmount = ratingRoll + minRating;
            var ratingPercentile = finalAmount / 7.0;

            var type = ThreadSafeRandom.Next(1, 2);
            switch (type)
            {
                case 1:
                    wo.GearDamage = (int)finalAmount;
                    break;
                case 2:
                    wo.GearDamageResist = (int)finalAmount;
                    break;
            }
            totalRollPercentile += ratingPercentile;

            totalRollPercentile = (totalRollPercentile + maxWardRollPercentile) / 2;

            return true;
        }
        else
        {
            _log.Error($"TryMutateJewelryMods({wo.Name}, {td.TreasureType}, {roll.ItemType}): unknown item type");
            return false;
        }
    }

    private static UiEffects GetUiEffects(int element)
    {
        switch (element)
        {
            case 1:
                return UiEffects.Slashing;
            case 2:
                return UiEffects.Piercing;
            case 3:
                return UiEffects.Bludgeoning;
            case 4:
                return UiEffects.Acid;
            case 5:
                return UiEffects.Fire;
            case 6:
                return UiEffects.Frost;
            case 7:
                return UiEffects.Lightning;
            default:
                return UiEffects.Undef;
        }
    }

    private static SpellId GetSpellDID(int tier, int element, int spellType)
    {
        switch (element)
        {
            case 1:
                switch (spellType)
                {
                    case 1:
                        SpellId[] slashBolts =
                        {
                            SpellId.WhirlingBlade1,
                            SpellId.WhirlingBlade1,
                            SpellId.WhirlingBlade2,
                            SpellId.WhirlingBlade3,
                            SpellId.WhirlingBlade4,
                            SpellId.WhirlingBlade5,
                            SpellId.WhirlingBlade6,
                            SpellId.WhirlingBlade7
                        };
                        return slashBolts[tier - 1];
                    case 2:
                        SpellId[] slashStreaks =
                        {
                            SpellId.WhirlingBladeStreak1,
                            SpellId.WhirlingBladeStreak1,
                            SpellId.WhirlingBladeStreak2,
                            SpellId.WhirlingBladeStreak3,
                            SpellId.WhirlingBladeStreak4,
                            SpellId.WhirlingBladeStreak5,
                            SpellId.WhirlingBladeStreak6,
                            SpellId.WhirlingBladeStreak7
                        };
                        return slashStreaks[tier - 1];
                    case 3:
                        SpellId[] slashVolleys =
                        {
                            SpellId.BladeVolley1,
                            SpellId.BladeVolley1,
                            SpellId.BladeVolley2,
                            SpellId.BladeVolley3,
                            SpellId.BladeVolley4,
                            SpellId.BladeVolley5,
                            SpellId.BladeVolley6,
                            SpellId.BladeVolley7
                        };
                        return slashVolleys[tier - 1];
                    case 4:
                        SpellId[] slashBlasts =
                        {
                            SpellId.BladeBlast1,
                            SpellId.BladeBlast1,
                            SpellId.BladeBlast2,
                            SpellId.BladeBlast3,
                            SpellId.BladeBlast4,
                            SpellId.BladeBlast5,
                            SpellId.BladeBlast6,
                            SpellId.BladeBlast7
                        };
                        return slashBlasts[tier - 1];
                }
                break;
            case 2:
                switch (spellType)
                {
                    case 1:
                        SpellId[] pierceBolts =
                        {
                            SpellId.ForceBolt1,
                            SpellId.ForceBolt1,
                            SpellId.ForceBolt2,
                            SpellId.ForceBolt3,
                            SpellId.ForceBolt4,
                            SpellId.ForceBolt5,
                            SpellId.ForceBolt6,
                            SpellId.ForceBolt7
                        };
                        return pierceBolts[tier - 1];
                    case 2:
                        SpellId[] pierceStreaks =
                        {
                            SpellId.ForceStreak1,
                            SpellId.ForceStreak1,
                            SpellId.ForceStreak2,
                            SpellId.ForceStreak3,
                            SpellId.ForceStreak4,
                            SpellId.ForceStreak5,
                            SpellId.ForceStreak6,
                            SpellId.ForceStreak7
                        };
                        return pierceStreaks[tier - 1];
                    case 3:
                        SpellId[] pierceVolleys =
                        {
                            SpellId.ForceVolley1,
                            SpellId.ForceVolley1,
                            SpellId.ForceVolley2,
                            SpellId.ForceVolley3,
                            SpellId.ForceVolley4,
                            SpellId.ForceVolley5,
                            SpellId.ForceVolley6,
                            SpellId.ForceVolley7
                        };
                        return pierceVolleys[tier - 1];
                    case 4:
                        SpellId[] pierceBlasts =
                        {
                            SpellId.ForceBlast1,
                            SpellId.ForceBlast1,
                            SpellId.ForceBlast2,
                            SpellId.ForceBlast3,
                            SpellId.ForceBlast4,
                            SpellId.ForceBlast5,
                            SpellId.ForceBlast6,
                            SpellId.ForceBlast7
                        };
                        return pierceBlasts[tier - 1];
                }
                break;
            case 3:
                switch (spellType)
                {
                    case 1:
                        SpellId[] bludgeBolts =
                        {
                            SpellId.ShockWave1,
                            SpellId.ShockWave1,
                            SpellId.ShockWave2,
                            SpellId.ShockWave3,
                            SpellId.ShockWave4,
                            SpellId.ShockWave5,
                            SpellId.ShockWave6,
                            SpellId.ShockWave7
                        };
                        return bludgeBolts[tier - 1];
                    case 2:
                        SpellId[] bludgeStreaks =
                        {
                            SpellId.ShockwaveStreak1,
                            SpellId.ShockwaveStreak1,
                            SpellId.ShockwaveStreak2,
                            SpellId.ShockwaveStreak3,
                            SpellId.ShockwaveStreak4,
                            SpellId.ShockwaveStreak5,
                            SpellId.ShockwaveStreak6,
                            SpellId.ShockwaveStreak7
                        };
                        return bludgeStreaks[tier - 1];
                    case 3:
                        SpellId[] bludgeVolleys =
                        {
                            SpellId.BludgeoningVolley1,
                            SpellId.BludgeoningVolley1,
                            SpellId.BludgeoningVolley2,
                            SpellId.BludgeoningVolley3,
                            SpellId.BludgeoningVolley4,
                            SpellId.BludgeoningVolley5,
                            SpellId.BludgeoningVolley6,
                            SpellId.BludgeoningVolley7
                        };
                        return bludgeVolleys[tier - 1];
                    case 4:
                        SpellId[] bludgeBlasts =
                        {
                            SpellId.ShockBlast1,
                            SpellId.ShockBlast1,
                            SpellId.ShockBlast2,
                            SpellId.ShockBlast3,
                            SpellId.ShockBlast4,
                            SpellId.ShockBlast5,
                            SpellId.ShockBlast6,
                            SpellId.ShockBlast7
                        };
                        return bludgeBlasts[tier - 1];
                }
                break;
            case 4:
                switch (spellType)
                {
                    case 1:
                        SpellId[] acidBolts =
                        {
                            SpellId.AcidStream1,
                            SpellId.AcidStream1,
                            SpellId.AcidStream2,
                            SpellId.AcidStream3,
                            SpellId.AcidStream4,
                            SpellId.AcidStream5,
                            SpellId.AcidStream6,
                            SpellId.AcidStream7
                        };
                        return acidBolts[tier - 1];
                    case 2:
                        SpellId[] acidStreaks =
                        {
                            SpellId.AcidStreak1,
                            SpellId.AcidStreak1,
                            SpellId.AcidStreak2,
                            SpellId.AcidStreak3,
                            SpellId.AcidStreak4,
                            SpellId.AcidStreak5,
                            SpellId.AcidStreak6,
                            SpellId.AcidStreak7
                        };
                        return acidStreaks[tier - 1];
                    case 3:
                        SpellId[] acidVolleys =
                        {
                            SpellId.AcidVolley1,
                            SpellId.AcidVolley1,
                            SpellId.AcidVolley2,
                            SpellId.AcidVolley3,
                            SpellId.AcidVolley4,
                            SpellId.AcidVolley5,
                            SpellId.AcidVolley6,
                            SpellId.AcidVolley7
                        };
                        return acidVolleys[tier - 1];
                    case 4:
                        SpellId[] acidBlasts =
                        {
                            SpellId.AcidBlast2,
                            SpellId.AcidBlast2,
                            SpellId.AcidBlast2,
                            SpellId.AcidBlast3,
                            SpellId.AcidBlast4,
                            SpellId.AcidBlast5,
                            SpellId.AcidBlast6,
                            SpellId.AcidBlast7
                        };
                        return acidBlasts[tier - 1];
                }
                break;
            case 5:
                switch (spellType)
                {
                    case 1:
                        SpellId[] fireBolts =
                        {
                            SpellId.FlameBolt1,
                            SpellId.FlameBolt1,
                            SpellId.FlameBolt2,
                            SpellId.FlameBolt3,
                            SpellId.FlameBolt4,
                            SpellId.FlameBolt5,
                            SpellId.FlameBolt6,
                            SpellId.FlameBolt7
                        };
                        return fireBolts[tier - 1];
                    case 2:
                        SpellId[] fireStreaks =
                        {
                            SpellId.FlameStreak1,
                            SpellId.FlameStreak1,
                            SpellId.FlameStreak2,
                            SpellId.FlameStreak3,
                            SpellId.FlameStreak4,
                            SpellId.FlameStreak5,
                            SpellId.FlameStreak6,
                            SpellId.FlameStreak7
                        };
                        return fireStreaks[tier - 1];
                    case 3:
                        SpellId[] fireVolleys =
                        {
                            SpellId.FlameVolley1,
                            SpellId.FlameVolley1,
                            SpellId.FlameVolley2,
                            SpellId.FlameVolley3,
                            SpellId.FlameVolley4,
                            SpellId.FlameVolley5,
                            SpellId.FlameVolley6,
                            SpellId.FlameVolley7
                        };
                        return fireVolleys[tier - 1];
                    case 4:
                        SpellId[] fireBlasts =
                        {
                            SpellId.FlameBlast2,
                            SpellId.FlameBlast2,
                            SpellId.FlameBlast2,
                            SpellId.FlameBlast3,
                            SpellId.FlameBlast4,
                            SpellId.FlameBlast5,
                            SpellId.FlameBlast6,
                            SpellId.FlameBlast7
                        };
                        return fireBlasts[tier - 1];
                }
                break;
            case 6:
                switch (spellType)
                {
                    case 1:
                        SpellId[] coldBolts =
                        {
                            SpellId.FrostBolt1,
                            SpellId.FrostBolt1,
                            SpellId.FrostBolt2,
                            SpellId.FrostBolt3,
                            SpellId.FrostBolt4,
                            SpellId.FrostBolt5,
                            SpellId.FrostBolt6,
                            SpellId.FrostBolt7
                        };
                        return coldBolts[tier - 1];
                    case 2:
                        SpellId[] coldStreaks =
                        {
                            SpellId.FrostStreak1,
                            SpellId.FrostStreak1,
                            SpellId.FrostStreak2,
                            SpellId.FrostStreak3,
                            SpellId.FrostStreak4,
                            SpellId.FrostStreak5,
                            SpellId.FrostStreak6,
                            SpellId.FrostStreak7
                        };
                        return coldStreaks[tier - 1];
                    case 3:
                        SpellId[] coldVolleys =
                        {
                            SpellId.FrostVolley1,
                            SpellId.FrostVolley1,
                            SpellId.FrostVolley2,
                            SpellId.FrostVolley3,
                            SpellId.FrostVolley4,
                            SpellId.FrostVolley5,
                            SpellId.FrostVolley6,
                            SpellId.FrostVolley7
                        };
                        return coldVolleys[tier - 1];
                    case 4:
                        SpellId[] coldBlasts =
                        {
                            SpellId.FrostBlast1,
                            SpellId.FrostBlast1,
                            SpellId.FrostBlast2,
                            SpellId.FrostBlast3,
                            SpellId.FrostBlast4,
                            SpellId.FrostBlast5,
                            SpellId.FrostBlast6,
                            SpellId.FrostBlast7
                        };
                        return coldBlasts[tier - 1];
                }
                break;
            case 7:
                switch (spellType)
                {
                    case 1:
                        SpellId[] lightningBolts =
                        {
                            SpellId.LightningBolt1,
                            SpellId.LightningBolt1,
                            SpellId.LightningBolt2,
                            SpellId.LightningBolt3,
                            SpellId.LightningBolt4,
                            SpellId.LightningBolt5,
                            SpellId.LightningBolt6,
                            SpellId.LightningBolt7
                        };
                        return lightningBolts[tier - 1];
                    case 2:
                        SpellId[] lightningStreaks =
                        {
                            SpellId.LightningStreak1,
                            SpellId.LightningStreak1,
                            SpellId.LightningStreak2,
                            SpellId.LightningStreak3,
                            SpellId.LightningStreak4,
                            SpellId.LightningStreak5,
                            SpellId.LightningStreak6,
                            SpellId.LightningStreak7
                        };
                        return lightningStreaks[tier - 1];
                    case 3:
                        SpellId[] lightningVolleys =
                        {
                            SpellId.LightningVolley1,
                            SpellId.LightningVolley1,
                            SpellId.LightningVolley2,
                            SpellId.LightningVolley3,
                            SpellId.LightningVolley4,
                            SpellId.LightningVolley5,
                            SpellId.LightningVolley6,
                            SpellId.LightningVolley7
                        };
                        return lightningVolleys[tier - 1];
                    case 4:
                        SpellId[] lightningBlasts =
                        {
                            SpellId.LightningBlast1,
                            SpellId.LightningBlast1,
                            SpellId.LightningBlast2,
                            SpellId.LightningBlast3,
                            SpellId.LightningBlast4,
                            SpellId.LightningBlast5,
                            SpellId.LightningBlast6,
                            SpellId.LightningBlast7
                        };
                        return lightningBlasts[tier - 1];
                }
                break;
        }
        return SpellId.Undef;
    }

    private static int GetJewelryWorkmanship(WorldObject wo, double gearRatingPercentile)
    {
        var finalPercentile = gearRatingPercentile;
        //Console.WriteLine($"--FINAL: {finalPercentile}\n\n");

        // Workmanship Calculation
        return (int)Math.Max(Math.Round(finalPercentile * 10, 0), 1);
    }
}
