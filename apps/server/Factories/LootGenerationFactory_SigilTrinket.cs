using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    private static readonly List<Dictionary<string, uint>> IconColorIds =
    [
        new Dictionary<string, uint>() // 0 - Compass
        {
            { "black", 100690601 }, // black
            { "gray", 100690594 },
            { "olive", 100690602 }, // brown
            { "white", 100690596 }, // white
            { "purple", 100690600 }, // purple
            { "blue", 100690595 }, // blue
            { "green", 100690598 }, // green
            { "yellow", 100690566 }, // yellow
            { "orange", 100690602 }, // orange
            { "red", 100690597 }, // red
            { "iron", 100690599 } //
        },

        new Dictionary<string, uint>() // 1 - Puzzle Box
        {
            { "black", 100690665 }, // black
            { "gray", 100690658 }, // gray
            { "olive", 100690661 }, // teal
            { "white", 100690664 }, // white
            { "purple", 100690663 }, // purple
            { "blue", 100690657 }, // blue
            { "green", 100690660 }, // green
            { "yellow", 100690662 }, // yellow
            { "orange", 100690666 }, // brown
            { "red", 100690659 }, // red
            { "iron", 0 } // iron
        },

        new Dictionary<string, uint>() // 2 - Scarabs
        {
            { "black", 100690698 }, // black
            { "gray", 100690701 }, // gray
            { "olive", 100690705 }, // olive
            { "white", 100690704 }, // white
            { "purple", 100690707 }, // purple
            { "blue", 100690706 }, // blue
            { "green", 100690700 }, // green
            { "yellow", 100690699 }, // yellow
            { "orange", 100690702 }, // orange
            { "red", 100693226 }, // red
            { "iron", 100690703 } // iron
        },

        new Dictionary<string, uint>() // 3 - Pocket Watch
        {
            { "black", 100690620 }, // black
            { "gray", 0 },
            { "olive", 100690619 }, // brown
            { "white", 100690614 }, // white
            { "purple", 100690618 }, // purple
            { "blue", 100690613 }, // blue
            { "green", 100690616 }, // green
            { "yellow", 100690592 }, // yellow
            { "orange", 100690593 }, // orange
            { "red", 100690615 }, // red
            { "iron", 100690617 } //
        },

        new Dictionary<string, uint>() // 4 - Top
        {
            { "black", 100690676 }, // black
            { "gray", 100690669 }, // gray
            { "olive", 100690672 }, // teal
            { "white", 100690675 }, // white
            { "purple", 100690674 }, // purple
            { "blue", 100690668 }, // blue
            { "green", 100690671 }, // green
            { "yellow", 100690673 }, // yellow
            { "orange", 100690677 }, // brown
            { "red", 100690670 }, // red
            { "iron", 0 } // iron
        },

        new Dictionary<string, uint>() // 5 - Goggles
        {
        { "black", 100690611 }, // black
        { "gray", 100690610 }, // gray
        { "olive", 100690612 }, // brown
        { "white", 100690604 }, // white
        { "purple", 100690609 }, // purple
        { "blue", 100690603 }, // blue
        { "green", 100690606 }, // green
        { "yellow", 100690608 }, // yellow
        { "orange", 100690607 }, // orange
        { "red", 100690605 }, // red
        { "iron", 100690703 } // iron
        },
    ];

    private static readonly Dictionary<string, uint> VulnProtIconOverlayIds = new()
    {
        { "black", 100689523 }, // black
        { "white", 100689533 }, // white
        { "purple", 100689536 }, // purple
        { "blue", 100689535 }, // blue
        { "green", 100689531 }, // green
        { "yellow", 100689537 }, // yellow
        { "orange", 100689532 }, // orange
        { "red", 100689534 } // red
    };

    private static readonly Dictionary<string, int> PaletteTemplateColors = new()
    {
        { "black", 2 }, // black (lead)
        { "gray", 20 }, // gray (silver)
        { "olive", 18 }, // olive (yellowBrown)
        { "white", 61 }, // white
        { "purple", 39 }, // purple (black)
        { "blue", 77 }, // blue (blueGreen)
        { "green", 8 }, // green
        { "yellow", 21 }, // yellow (gold)
        { "orange", 19 }, // orange (copper)
        { "red", 14 }, // red
        { "iron", 82 } // iron (pinkPurple)
    };

    private static readonly Dictionary<string, uint> VulnSpellIds = new()
    {
        { "black", 25 }, // black
        { "white", 1048 }, // white
        { "purple", 1084 }, // purple
        { "blue", 1060 }, // blue
        { "green", 521 }, // green
        { "yellow", 1151 }, // yellow
        { "orange", 1127 }, // orange
        { "red", 21 } // red
    };

    private static readonly Dictionary<string, string> VulnSpellNames = new()
    {
        { "black", "Imperil Other" }, // black
        { "white", "Bludgeoning Vulnerability Other" }, // white
        { "purple", "Lightning Vulnerability Other" }, // purple
        { "blue", "Cold Vulnerability Other" }, // blue
        { "green", "Acid Vulnerability Other" }, // green
        { "yellow", "Piercing Vulnerability Other" }, // yellow
        { "orange", "Slashing Vulnerability Other" }, // orange
        { "red", "Fire Vulnerability Other" } // red
    };

    private static readonly Dictionary<string, uint> ProtSpellIds = new()
    {
        { "black", 23 }, // black
        { "white", 1024 }, // white
        { "purple", 1072 }, // purple
        { "blue", 1036 }, // blue
        { "green", 509 }, // green
        { "yellow", 1139 }, // yellow
        { "orange", 1115 }, // orange
        { "red", 19 } // red
    };

    private static readonly Dictionary<string, string> ProtSpellNames = new()
    {
        { "black", "Armor Other" }, // black
        { "white", "Bludgeoning Protection Other" }, // white
        { "purple", "Lightning Protection Other" }, // purple
        { "blue", "Cold Protection Other" }, // blue
        { "green", "Acid Protection Other" }, // green
        { "yellow", "Piercing Protection Other" }, // yellow
        { "orange", "Slashing Protection Other" }, // orange
        { "red", "Fire Protection Other" } // red
    };

    private static readonly Dictionary<string, string> DuplicationElementString = new()
    {
        { "white", " Bludgeoning" }, // white
        { "purple", " Lightning" }, // purple
        { "blue", " Cold" }, // blue
        { "green", "n Acid" }, // green
        { "yellow", " Piercing" }, // yellow
        { "orange", " Slashing" }, // orange
        { "red", " Fire" } // red
    };

    private static readonly Dictionary<string, int> ElementId = new()
    {
        { "white", 0x4 }, // white - blunt
        { "purple", 0x40 }, // purple - electric
        { "blue", 0x8 }, // blue - cold
        { "green", 0x20 }, // green - acid
        { "yellow", 0x2 }, // yellow - pierce
        { "orange", 0x1 }, // orange - slash
        { "red", 0x10 } // red - fire
    };

    private static readonly Dictionary<string, uint> OverlayIds = new()
    {
        { "life", 100689496 },
        { "war", 100689495 },
    };

    private static WorldObject CreateSigilTrinket(TreasureDeath profile, SigilTrinketType sigilTrinketType,  bool mutate = true)
    {
        var wcid = SigilTrinketWcids.Roll(profile.Tier, sigilTrinketType);

        var wo = WorldObjectFactory.CreateNewWorldObject((uint)wcid);

        if (mutate)
        {
            MutateSigilTrinket(wo, profile);
        }

        return wo;
    }

    private static void MutateSigilTrinket(WorldObject wo, TreasureDeath profile)
    {
        if (wo is not SigilTrinket sigilTrinket)
        {
            return;
        }

        sigilTrinket.CooldownDuration = GetCooldown(profile);
        sigilTrinket.SigilTrinketTriggerChance = GetChance(profile);
        sigilTrinket.SigilTrinketMaxTier = Math.Clamp(profile.Tier - 1, 1, 7);
        sigilTrinket.WieldRequirements = WieldRequirement.Training;
        sigilTrinket.WieldDifficulty = 3; // Specialized
        sigilTrinket.ItemMaxLevel = Math.Clamp(profile.Tier - 1, 1, 7);
        sigilTrinket.ItemBaseXp = GetBaseLevelCost(profile);
        sigilTrinket.ItemTotalXp = 0;

        sigilTrinket.SigilTrinketHealthReserved = 0;
        sigilTrinket.SigilTrinketStaminaReserved = 0;
        sigilTrinket.SigilTrinketManaReserved = 0;

        var wieldSkillRng = ThreadSafeRandom.Next(0, 1);
        switch (sigilTrinket.SigilTrinketType)
        {
            case (int)SigilTrinketType.Compass:
                sigilTrinket.SigilTrinketHealthReserved = GetReservedVital(profile, false, true);
                sigilTrinket.WieldSkillType = wieldSkillRng == 0 ? (int)Skill.Shield : (int)Skill.TwoHandedCombat;

                if (sigilTrinket.WieldSkillType == (int)Skill.Shield)
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxShieldEffectId);

                    SetShieldCompassStats(profile, sigilTrinket);
                }
                else
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxTwohandedCombatEffectId);

                    SetTwohandedCombatCompassStats(profile, sigilTrinket);
                }
                break;
            case (int)SigilTrinketType.PuzzleBox:
                sigilTrinket.SigilTrinketHealthReserved = GetReservedVital(profile, true, true);
                sigilTrinket.SigilTrinketStaminaReserved = GetReservedVital(profile, true);
                sigilTrinket.WieldSkillType = wieldSkillRng == 0 ? (int)Skill.DualWield : (int)Skill.Thievery;

                if (sigilTrinket.WieldSkillType == (int)Skill.DualWield)
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxDualWieldEffectId);

                    SetDualWieldPuzzleBoxStats(profile, sigilTrinket);
                }
                else
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxThieveryEffectId);

                    SetThieveryPuzzleBoxStats(profile, sigilTrinket);
                }
                break;
            case (int)SigilTrinketType.Scarab:
                sigilTrinket.SigilTrinketHealthReserved = GetReservedVital(profile, true, true);
                sigilTrinket.SigilTrinketManaReserved = GetReservedVital(profile, true);
                sigilTrinket.WieldSkillType = wieldSkillRng == 0 ? (int)Skill.LifeMagic : (int)Skill.WarMagic;

                if (sigilTrinket.WieldSkillType == (int)Skill.LifeMagic)
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxLifeMagicEffectId);

                    SetLifeMagicScarabStats(profile, sigilTrinket);
                }
                else
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxWarMagicEffectId);

                    SetWarMagicScarabStats(profile, sigilTrinket);
                }
                break;
            case (int)SigilTrinketType.PocketWatch:
                sigilTrinket.SigilTrinketStaminaReserved = GetReservedVital(profile);
                sigilTrinket.WieldSkillType = (int)Skill.PhysicalDefense;
                sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxPhysicalDefenseEffectId);

                SetPhysicalDefensePocketWatchStats(profile, sigilTrinket);
                break;
            case (int)SigilTrinketType.Top:
                sigilTrinket.SigilTrinketManaReserved = GetReservedVital(profile);
                sigilTrinket.WieldSkillType = (int)Skill.MagicDefense;
                sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxMagicDefenseEffectId);

                SetMagicDefenseTopStats(profile, sigilTrinket);
                break;
            case (int)SigilTrinketType.Goggles:
                sigilTrinket.SigilTrinketStaminaReserved = GetReservedVital(profile, true);
                sigilTrinket.SigilTrinketManaReserved = GetReservedVital(profile, true);
                sigilTrinket.WieldSkillType = wieldSkillRng == 0 ? (int)Skill.Perception : (int)Skill.Deception;

                if (sigilTrinket.WieldSkillType == (int)Skill.Perception)
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxPerceptionEffectId);

                    SetPerceptionGogglesStats(profile, sigilTrinket);
                }
                else
                {
                    sigilTrinket.SigilTrinketEffectId = ThreadSafeRandom.Next(0, SigilTrinket.MaxDeceptionEffectId);

                    SetDeceptionGogglesStats(profile, sigilTrinket);
                }
                break;
        }
    }

    private static void SetLifeMagicScarabStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        var element = ThreadSafeRandom.Next(0, 7);
        var color = element switch
        {
            1 => "white",
            2 => "purple",
            3 => "blue",
            4 => "green",
            5 => "yellow",
            6 => "orange",
            7 => "red",
            _ => "black"
        };

        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketLifeMagicEffect.ScarabCastProt:
            {
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["white"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["white"];
                sigilTrinket.IconOverlayId = VulnProtIconOverlayIds[color];

                sigilTrinket.Name += " of Protection";
                sigilTrinket.SigilTrinketCastSpellId = ProtSpellIds[color];
                sigilTrinket.Use =
                    $"Whenever the wielder casts a Heal, Revitalize, or Mana Boost spell, "
                    + $"there is a chance the target will also gain {ProtSpellNames[color]}.\n\n"
                    + $"The level of the buff is equal to the level of the casted spell, but cannot surpass the Max Level value.";
                break;
            }
            case (int)SigilTrinketLifeMagicEffect.ScarabCastVuln:
            {
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["black"];
                sigilTrinket.IconOverlayId = VulnProtIconOverlayIds[color];

                sigilTrinket.Name += " of Vulnerability";
                sigilTrinket.SigilTrinketCastSpellId = VulnSpellIds[color];
                sigilTrinket.Use =
                    $"Whenever the wielder casts a Harm, Enfeeble, or Mana Drain spell, "
                    + $"there is a chance the target will also be inflicted with {VulnSpellNames[color]}.\n\n"
                    + $"The level of the debuff is equal to the level of the casted spell, but cannot surpass the Max Level value.";
                break;
            }
            case (int)SigilTrinketLifeMagicEffect.ScarabCastVitalRate:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["green"];

                sigilTrinket.Name += " of Growth";
                sigilTrinket.Use =
                    "Whenever the wielder casts a Heal or Harm spell, there is a chance the target will also be affected by Regeneration or Fester, respectively.\n\n"
                    + "Whenever the wielder casts a Revitalize or Enfeeble spell, there is a chance the target will also be affected by Rejuvenation or Exhaustion, respectively.\n\n"
                    + "Whenever the wielder casts a Mana Boost or Mana Drain spell, there is a chance the target will also be affected by Mana Renewal or Mana Depletion.\n\n"
                    + "The level of the buff is equal to the level of the casted spell, but cannot surpass the Max Level value.";
                break;
            case (int)SigilTrinketLifeMagicEffect.ScarabCastItemBuff:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["purple"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["purple"];

                sigilTrinket.Name += " of Artifice";
                sigilTrinket.Use =
                    "Whenever the wielder casts a Heal spell, there is a chance the target will also be affected by Defender Other.\n\n"
                    + "Whenever the wielder casts a Revitalize spell, there is a chance the target will also be affected by Blood Drinker.\n\n"
                    + "Whenever the wielder casts a Mana Boost spell, there is a chance the target will also be affected by Spirit Drinker.\n\n"
                    + "The level of the buff is equal to the level of the casted spell, but cannot surpass the Max Level value.";
                break;
            case (int)SigilTrinketLifeMagicEffect.ScarabIntensity:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["red"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["red"];

                sigilTrinket.SigilTrinketIntensity = GetIntensity(profile);

                sigilTrinket.Name += " of Intensity";
                sigilTrinket.Use =
                    $"Whenever the wielder casts any spell, there is a chance the spell will "
                    + $"gain bonus intensity.\n\n"
                    + $"Intensity increases damage and restoration amounts.\n\n"
                    + $"Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketLifeMagicEffect.ScarabShield:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["gray"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["gray"];

                sigilTrinket.Name += " of Shielding";
                sigilTrinket.Use =
                    $"Whenever the wielder casts any spell, there is a chance they will "
                    + $"gain a protective buff, increasing damage reduction rating by 25 for 12 seconds.\n\n"
                    + $"Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketLifeMagicEffect.ScarabManaReduction:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["blue"];

                sigilTrinket.SigilTrinketManaReserved = 0;
                sigilTrinket.SigilTrinketReductionAmount = GetReductionAmount(profile);
                sigilTrinket.CooldownDuration -= 5;

                sigilTrinket.Name += " of Reduction";
                sigilTrinket.Use =
                    $"Whenever the wielder casts any spell, there is a chance that the cost of the spell will be reduced.\n\n"
                    + "Only affects spells that are less than or equal to the Max level value.";
                break;
        }
    }

    private static void SetWarMagicScarabStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketWarMagicEffect.ScarabIntensity:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["red"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["red"];

                sigilTrinket.SigilTrinketIntensity = GetIntensity(profile);

                sigilTrinket.Name += " of Intensity";
                sigilTrinket.Use =
                    $"Whenever the wielder casts any spell, there is a chance the spell will "
                    + $"gain bonus intensity.\n\n"
                    + $"Intensity increases damage and restoration amounts.\n\n"
                    + $"Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketWarMagicEffect.ScarabShield:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["gray"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["gray"];

                sigilTrinket.Name += " of Shielding";
                sigilTrinket.Use =
                    $"Whenever the wielder casts any spell, there is a chance they will "
                    + $"gain a protective buff, increasing damage reduction rating by 25 for 12 seconds.\n\n"
                    + $"Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketWarMagicEffect.ScarabManaReduction:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["blue"];

                sigilTrinket.SigilTrinketManaReserved = 0;
                sigilTrinket.SigilTrinketReductionAmount = GetReductionAmount(profile);
                sigilTrinket.CooldownDuration -= 5;

                sigilTrinket.Name += " of Reduction";
                sigilTrinket.Use =
                    $"Whenever the wielder casts any spell, there is a chance that the cost of the spell will be reduced.\n\n"
                    + "Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketWarMagicEffect.ScarabDuplicate:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["olive"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["olive"];
                sigilTrinket.Name += " of Duplication";
                sigilTrinket.Use =
                    $"Whenever the wielder casts a War spell, there is a chance that the spell will be duplicated and cast against the same target.\n\n"
                    + "Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketWarMagicEffect.ScarabDetonate:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["yellow"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["yellow"];
                sigilTrinket.Name += " of Detonation";
                sigilTrinket.Use =
                    $"Whenever the wielder damages a creature with a War spell, there is a chance that the spell will detonate on impact, causing an explosion of the same element.\n\n"
                    + "Only affects spells that are less than or equal to the Max level value.";
                break;
            case (int)SigilTrinketWarMagicEffect.ScarabCrit:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["orange"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Scarab]["orange"];

                sigilTrinket.SigilTrinketTriggerChance = 0;

                sigilTrinket.Name += " of Crushing";
                sigilTrinket.Use =
                    $"Whenever the wielder performs a critical strike with a War spell, they have a chance to gain a 50% critical damage boost for 12 seconds.\n\n"
                    + "Only affects spells that are less than or equal to the Max level value.";
                break;
        }
    }

    private static void SetShieldCompassStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketShieldEffect.Might:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["red"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["red"];

                sigilTrinket.Name += " of Might";

                var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);
                sigilTrinket.Use =
                    $"Whenever the wielder attacks with more than 50% power, there is a chance that a normal hit will be converted into a critical hit.\n\n" +
                    $"Can only occur while using a shield, with a wield requirement of up to {wieldReq}.";
                break;
            case (int)SigilTrinketShieldEffect.Aggression:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["olive"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["olive"];

                sigilTrinket.Name += " of Aggression";

                wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);
                sigilTrinket.Use =
                    $"Whenever the wielder attacks with more than 50% power, they have a chance to generate double threat towards that enemy.\n\n"
                    + $"Can only occur while using a shield, with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketShieldEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketShieldEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetTwohandedCombatCompassStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketTwohandedCombatEffect.Might:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["red"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["red"];

                sigilTrinket.Name += " of Might";

                var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);
                sigilTrinket.Use =
                    $"Whenever the wielder attacks with more than 50% power, there is a chance that a normal hit will be converted into a critical hit.\n\n" +
                    $"Can only occur while using a two-handed weapon, with a wield requirement of up to {wieldReq}.";
                break;
            case (int)SigilTrinketTwohandedCombatEffect.Aggression:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["olive"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["olive"];

                sigilTrinket.Name += " of Aggression";

                wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);
                sigilTrinket.Use =
                    $"Whenever the wielder attacks with more than 50% power, they have a chance to generate increased threat towards that enemy.\n\n"
                    + $"Can only occur while using a two-handed weapon, with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketTwohandedCombatEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketTwohandedCombatEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Compass]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetDualWieldPuzzleBoxStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketDualWieldEffect.Assailment:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["red"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["red"];

                sigilTrinket.Name += " of Assailment";

                var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);
                sigilTrinket.Use =
                    $"Whenever the wielder performs an critical hit on an enemy, they have a chance to gain a 50% critical damage buff, additively, for 10 seconds.\n\n"
                    + $"Can only occur while dual-wielding, with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketDualWieldEffect.PH2:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["blue"];
            //
            //     sigilTrinket.Name += " of PH2";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketDualWieldEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketDualWieldEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetThieveryPuzzleBoxStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);

        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketThieveryEffect.Treachery:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["green"];

                sigilTrinket.Name += " of Treachery";
                sigilTrinket.Use =
                    $"Whenever the wielder performs a sneak attack critical hit on an enemy, they have a chance to deal double critical damage.\n\n"
                    + $"Can only occur while performing sneak attacks, using a weapon with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketThieveryEffect.PH2:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["blue"];
            //
            //     sigilTrinket.Name += " of PH2";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketThieveryEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketThieveryEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PuzzleBox]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetPhysicalDefensePocketWatchStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);

        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketPhysicalDefenseEffect.Evasion:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["yellow"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PocketWatch]["yellow"];

                sigilTrinket.Name += " of Evasion";
                sigilTrinket.Use =
                    $"Whenever the wielder receives a glancing blow, it has a chance to become a full evade.\n\n"
                    + $"Can only occur while wielding a weapon with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketPhysicalDefenseEffect.PH2:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PocketWatch]["blue"];
            //
            //     sigilTrinket.Name += " of PH2";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketPhysicalDefenseEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PocketWatch]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketPhysicalDefenseEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.PocketWatch]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetMagicDefenseTopStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);

        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketMagicDefenseEffect.Absorption:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["purple"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Top]["purple"];

                sigilTrinket.Name += " of Absorption";
                sigilTrinket.Use =
                    $"Whenever the wielder is damaged by a spell, they have a chance to prevent half of the damage and convert it into mana gained.\n\n"
                    + $"Can only occur while wielding a weapon with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketMagicDefenseEffect.PH2:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Top]["blue"];
            //
            //     sigilTrinket.Name += " of PH2";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketMagicDefenseEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Top]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketMagicDefenseEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Top]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetPerceptionGogglesStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);

        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketPerceptionEffect.Exposure:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["red"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["red"];

                sigilTrinket.Name += " of Exposure";
                sigilTrinket.Use =
                    $"Whenever the wielder successfully uses the Expose Weakness ability, they have a chance to gain a 25% damage boost for 10 seconds.\n\n"
                    + $"Can only occur while wielding a weapon with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketPerceptionEffect.PH2:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["blue"];
            //
            //     sigilTrinket.Name += " of PH2";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketPerceptionEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketPerceptionEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private static void SetDeceptionGogglesStats(TreasureDeath profile, SigilTrinket sigilTrinket)
    {
        var wieldReq = GetWieldDifficultyPerTier((sigilTrinket.SigilTrinketMaxTier ?? 1) + 1);

        switch (sigilTrinket.SigilTrinketEffectId)
        {
            case (int)SigilTrinketDeceptionEffect.Avoidance:
                sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
                sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["black"];

                sigilTrinket.Name += " of Avoidance";
                sigilTrinket.Use =
                    $"Whenever the wielder performs an attack with more than 50% power, they have a chance for the attack to generate no threat towards the target.\n\n"
                    + $"Can only occur while using a weapon, with a wield requirement of up to {wieldReq}.";
                break;
            // case (int)SigilTrinketDeceptionEffect.PH2:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["blue"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["blue"];
            //
            //     sigilTrinket.Name += " of PH2";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketDeceptionEffect.PH3:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["green"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["green"];
            //
            //     sigilTrinket.Name += " of PH3";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
            // case (int)SigilTrinketDeceptionEffect.PH4:
            //     sigilTrinket.PaletteTemplate = PaletteTemplateColors["black"];
            //     sigilTrinket.IconId = IconColorIds[(int)SigilTrinketType.Goggles]["black"];
            //
            //     sigilTrinket.Name += " of PH4";
            //     sigilTrinket.Use =
            //         $"(PH)";
            //     break;
        }
    }

    private const double MaxChance = 0.75;
    private const double MinChance = 0.25;

    private const double MaxCooldown = 20.0;
    private const double MinCooldown = 10.0;

    private const double MaxReservedVital = 0.2;
    private const double MinReservedVital = 0.1;

    private const double MaxIntensity = 0.75;
    private const double MinIntensity = 0.25;

    private const double MaxReduction = 0.75;
    private const double MinReduction = 0.25;

    private static double GetChance(TreasureDeath treasureDeath)
    {
        const double range = MaxChance - MinChance;
        var roll = range * GetDiminishingRoll(treasureDeath);

        return MinChance + roll;
    }

    private static double GetCooldown(TreasureDeath treasureDeath)
    {
        const double range = MaxCooldown - MinCooldown;
        var roll = range * GetDiminishingRoll(treasureDeath);

        return MaxCooldown - roll;
    }

    /// <summary>
    /// Roll for reserved vital amount. 50% less reserved for hybrid trinkets and 50% less reserved for health vitals.
    /// </summary>
    private static double GetReservedVital(TreasureDeath treasureDeath, bool hybrid = false, bool health = false)
    {
        const double range = MaxReservedVital - MinReservedVital;
        var hybridMultiplier = hybrid ? 0.5 : 1.0;
        var healthMultiplier = health ? 0.5 : 1.0;
        var roll = range * hybridMultiplier * healthMultiplier * GetDiminishingRoll(treasureDeath);

        return MaxReservedVital * hybridMultiplier * healthMultiplier - roll;
    }

    private static double GetIntensity(TreasureDeath treasureDeath)
    {
        const double range = MaxIntensity - MinIntensity;
        var roll = range * GetDiminishingRoll(treasureDeath);

        return MinIntensity + roll;
    }

    private static double GetReductionAmount(TreasureDeath treasureDeath)
    {
        const double range = MaxReduction - MinReduction;
        var roll = range * GetDiminishingRoll(treasureDeath);

        return MinReduction + roll;
    }

    private static uint GetBaseLevelCost(TreasureDeath treasureDeath)
    {
        switch (treasureDeath.Tier)
        {
            default:
                return 10000;
            case 3:
                return 50000;
            case 4:
                return 100000;
            case 5:
                return 250000;
            case 6:
                return 500000;
            case 7:
                return 1000000;
            case 8:
                return 2000000;
        }
    }
}
