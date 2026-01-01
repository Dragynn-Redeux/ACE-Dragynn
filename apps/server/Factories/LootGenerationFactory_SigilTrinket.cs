using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;
using static ACE.Server.Factories.SigilTrinketConfig;

namespace ACE.Server.Factories;

public static partial class LootGenerationFactory
{
    public static WorldObject CreateSigilTrinket(TreasureDeath profile, SigilTrinketType sigilTrinketType, bool mutate = true, int? effectId = null, uint? wcidOverride = null, List<Skill> allowedSpecializedSkills = null)
    {
        var wcid = SigilTrinketWcids.Roll(profile.Tier, sigilTrinketType);

        var actualWcid = wcidOverride ?? (uint)wcid;

        var wo = WorldObjectFactory.CreateNewWorldObject(actualWcid);

        if (mutate)
        {
            MutateSigilTrinket(wo, profile, effectId, allowedSpecializedSkills);
        }

        return wo;
    }

    private static void MutateSigilTrinket(WorldObject wo, TreasureDeath profile, int? effectId = null, List<Skill> allowedSpecializedSkills = null)
    {
        if (wo is not SigilTrinket sigilTrinket)
        {
            return;
        }

        var tier = Math.Clamp(profile.Tier - 1, 1, 7);

        sigilTrinket.CooldownDuration = GetCooldown(profile);
        sigilTrinket.SigilTrinketTriggerChance = GetChance(profile);

        // Keep level requirement slot for item-level gating
        sigilTrinket.WieldRequirements2 = WieldRequirement.Level;
        sigilTrinket.WieldDifficulty2 = GetRequiredLevelPerTier(profile.Tier);
        sigilTrinket.WieldSkillType2 = 0;

        sigilTrinket.ItemMaxLevel = RollSigilTrinketLevel(profile);
        sigilTrinket.ItemBaseXp = GetBaseLevelCost(tier);
        sigilTrinket.ItemTotalXp = 0;
        sigilTrinket.Value = GetValuePerTier(tier);

        var setRoll = ThreadSafeRandom.Next(0, 4);
        sigilTrinket.EquipmentSetId = Aetheria.SigilToEquipmentSet[(Sigil)setRoll];

        // Icon overlay id comes from config tier icon ids
        if (TierIconIds.TryGetValue(sigilTrinket.ItemMaxLevel ?? 1, out var overlayId))
        {
            sigilTrinket.IconOverlayId = overlayId;
        }

        sigilTrinket.SigilTrinketHealthReserved = 0;
        sigilTrinket.SigilTrinketStaminaReserved = 0;
        sigilTrinket.SigilTrinketManaReserved = 0;

        // We'll set WieldSkillType to the primary skill for other internal checks (eg. RechargeSigilTrinket).
        // Config-driven AllowedSpecializedSkills (including multi-skill / combined keys) are applied below.
        switch (sigilTrinket.SigilTrinketType)
        {
            case (int)SigilTrinketType.Compass:
            {
                sigilTrinket.SigilTrinketHealthReserved = GetReservedVital(profile, false, true);

                // Candidate config keys (prefer combined first)
                var compassCandidates = new (string, List<Skill>)[]
                {
                    ("shieldTwohandedCompass", new List<Skill>{ Skill.Shield, Skill.TwoHandedCombat }),
                    ("shieldCompass", new List<Skill>{ Skill.Shield }),
                    ("twohandedCompass", new List<Skill>{ Skill.TwoHandedCombat })
                };

                // pass effectId so config-driven randomization picks from the map when present
                if (!TryApplyRandomMatchingMap(profile, sigilTrinket, effectId, compassCandidates, allowedSpecializedSkills))
                {
                    _log.Error("MutateSigilTrinket() - Could not find matching map for {Trinket}", sigilTrinket.Name);
                }

                break;
            }
            case (int)SigilTrinketType.PuzzleBox:
            {
                sigilTrinket.SigilTrinketHealthReserved = GetReservedVital(profile, true, true);
                sigilTrinket.SigilTrinketStaminaReserved = GetReservedVital(profile, true);

                var puzzleCandidates = new (string, List<Skill>)[]
                {
                    ("dualWieldMissilePuzzleBox", new List<Skill>{ Skill.DualWield, Skill.Bow }),
                    ("dualWieldPuzzleBox", new List<Skill>{ Skill.DualWield }),
                    ("missilePuzzleBox", new List<Skill>{ Skill.Bow }),
                    ("thieveryPuzzleBox", new List<Skill>{ Skill.Thievery })
                };

                if (!TryApplyRandomMatchingMap(profile, sigilTrinket, effectId, puzzleCandidates, allowedSpecializedSkills))
                {
                    _log.Error("MutateSigilTrinket() - Could not find matching map for {Trinket}", sigilTrinket.Name);
                }

                break;
            }
            case (int)SigilTrinketType.Scarab:
            {
                sigilTrinket.SigilTrinketHealthReserved = GetReservedVital(profile, true, true);
                sigilTrinket.SigilTrinketManaReserved = GetReservedVital(profile, true);

                var scarabCandidates = new (string, List<Skill>)[]
                {
                    ("lifeWarMagicScarab", new List<Skill>{ Skill.LifeMagic, Skill.WarMagic }),
                    ("lifeMagicScarab", new List<Skill>{ Skill.LifeMagic }),
                    ("warMagicScarab", new List<Skill>{ Skill.WarMagic })
                };

                if (!TryApplyRandomMatchingMap(profile, sigilTrinket, effectId, scarabCandidates, allowedSpecializedSkills))
                {
                    _log.Error("MutateSigilTrinket() - Could not find matching map for {Trinket}", sigilTrinket.Name);
                }

                break;
            }
            case (int)SigilTrinketType.PocketWatch:
            {
                sigilTrinket.SigilTrinketStaminaReserved = GetReservedVital(profile);

                var pocketCandidates = new (string, List<Skill>)[]
                {
                    ("physicalDefensePocketWatch", new List<Skill>{ Skill.PhysicalDefense })
                };

                if (!TryApplyRandomMatchingMap(profile, sigilTrinket, effectId, pocketCandidates, allowedSpecializedSkills))
                {
                    _log.Error("MutateSigilTrinket() - Could not find matching map for {Trinket}", sigilTrinket.Name);
                }

                break;
            }
            case (int)SigilTrinketType.Top:
            {
                sigilTrinket.SigilTrinketManaReserved = GetReservedVital(profile);

                var topCandidates = new (string, List<Skill>)[]
                {
                    ("magicDefenseTop", new List<Skill>{ Skill.MagicDefense })
                };

                if (!TryApplyRandomMatchingMap(profile, sigilTrinket, effectId, topCandidates, allowedSpecializedSkills))
                {
                    _log.Error("MutateSigilTrinket() - Could not find matching map for {Trinket}", sigilTrinket.Name);
                }

                break;
            }
            case (int)SigilTrinketType.Goggles:
            {
                sigilTrinket.SigilTrinketStaminaReserved = GetReservedVital(profile, true);
                sigilTrinket.SigilTrinketManaReserved = GetReservedVital(profile, true);

                var gogglesCandidates = new (string, List<Skill>)[]
                {
                    ("perceptionGoggles", new List<Skill>{ Skill.Perception }),
                    ("deceptionGoggles", new List<Skill>{ Skill.Deception })
                };

                if (!TryApplyRandomMatchingMap(profile, sigilTrinket, effectId, gogglesCandidates, allowedSpecializedSkills))
                {
                    _log.Error("MutateSigilTrinket() - Could not find matching map for {Trinket}", sigilTrinket.Name);
                }

                break;
            }
        }
    }

    private static int RollSigilTrinketLevel(TreasureDeath profile)
    {
        var roll = 100 * GetDiminishingRoll(profile);
        var tier = Math.Clamp(profile.Tier - 1, 1, 7);

        var bonusLevel = roll switch
        {
            > 99 => 3,
            > 89 => 2,
            > 69 => 1,
            _ => 0
        };

        return tier + bonusLevel;
    }

    // Helper to choose config map, pick an effect id from map, and set AllowedSpecializedSkills.
    static bool TryApplyRandomMatchingMap(TreasureDeath profile, SigilTrinket sigil, int? requestedEffectId, (string MapName, List<Skill> Skills)[] candidates, List<Skill> allowedSpecializedSkills = null)
    {
        // collect all matching maps first
        var matches = new List<(IReadOnlyDictionary<int, SigilStatConfig> Map, List<Skill> Skills)>();

        foreach (var (mapName, skills) in candidates)
        {
            if (!TryGetMap(mapName, out var map))
            {
                continue;
            }

            var keys = new List<int>(map.Keys);
            if (keys.Count == 0)
            {
                continue;
            }

            matches.Add((map, skills));
        }

        if (matches.Count == 0)
        {
            return false;
        }

        // If an effectId is forced, prefer candidates according to the provided allowedSpecializedSkills.
        (IReadOnlyDictionary<int, SigilStatConfig> Map, List<Skill> Skills)? chosen = null;

        if (requestedEffectId.HasValue)
        {
            var forcedId = requestedEffectId.Value;

            // helper to compare skill lists as sets (order/duplicates not significant)
            static bool SkillSetsEqual(IReadOnlyList<Skill> a, IReadOnlyList<Skill> b)
            {
                if (a == null || b == null)
                {
                    return false;
                }

                if (a.Count != b.Count)
                {
                    return false;
                }

                var sa = new HashSet<Skill>(a);
                var sb = new HashSet<Skill>(b);
                return sa.SetEquals(sb);
            }

            var allowed = allowedSpecializedSkills;

            if (allowed != null && allowed.Count > 0)
            {
                // 1) Prefer a candidate that exactly matches allowedSpecializedSkills and contains the requested effect id.
                for (var i = 0; i < matches.Count; ++i)
                {
                    var (map, skills) = matches[i];
                    if (SkillSetsEqual(skills, allowed) && map.ContainsKey(forcedId))
                    {
                        chosen = matches[i];
                        break;
                    }
                }

                // 2) If none contains the requested effect id, prefer the candidate that matches allowedSpecializedSkills.
                if (chosen == null)
                {
                    for (var i = 0; i < matches.Count; ++i)
                    {
                        var (map, skills) = matches[i];
                        if (SkillSetsEqual(skills, allowed))
                        {
                            chosen = matches[i];
                            break;
                        }
                    }
                }
            }

            // 3) If allowedSpecializedSkills not provided or above attempts failed, prefer any map that contains the requested effect id.
            if (chosen == null)
            {
                for (var i = 0; i < matches.Count; ++i)
                {
                    var (map, skills) = matches[i];
                    if (map.ContainsKey(forcedId))
                    {
                        chosen = matches[i];
                        break;
                    }
                }
            }
        }

        // If still not chosen, pick one matching map uniformly at random
        if (chosen == null)
        {
            var chosenMapIndex = ThreadSafeRandom.Next(0, matches.Count - 1);
            chosen = matches[chosenMapIndex];
        }

        var chosenMap = chosen.Value.Map;
        var chosenSkills = chosen.Value.Skills;

        // choose effect id from the chosen map (prefer requestedEffectId if present)
        int chosenEffectId;
        if (requestedEffectId.HasValue && chosenMap.ContainsKey(requestedEffectId.Value))
        {
            chosenEffectId = requestedEffectId.Value;
        }
        else
        {
            var keys = new List<int>(chosenMap.Keys);
            var idx = ThreadSafeRandom.Next(0, keys.Count - 1);
            chosenEffectId = keys[idx];
        }

        sigil.SigilTrinketEffectId = chosenEffectId;
        sigil.AllowedSpecializedSkills = chosenSkills;

        ApplyConfigMap(profile, sigil, (IReadOnlyDictionary<int, SigilStatConfig>)chosenMap);
        return true;
    }

    private static void ApplyConfigMap(TreasureDeath profile, SigilTrinket sigilTrinket, IReadOnlyDictionary<int, SigilStatConfig> map)
    {
        if (!sigilTrinket.SigilTrinketEffectId.HasValue)
        {
            return;
        }

        var effectId = sigilTrinket.SigilTrinketEffectId.Value;
        if (!map.TryGetValue(effectId, out var cfg))
        {
            return;
        }

        // Palette / Icon
        if (cfg.PaletteKey is not null && PaletteTemplateColors.TryGetValue(cfg.PaletteKey, out var palette))
        {
            sigilTrinket.PaletteTemplate = palette;
        }

        if (cfg.IconColorKey is not null && IconColorIds.Count > 0)
        {
            var typeIndex = Math.Clamp(sigilTrinket.SigilTrinketType ?? 0, 0, IconColorIds.Count - 1);
            var iconMap = IconColorIds[typeIndex];
            if (iconMap.TryGetValue(cfg.IconColorKey, out var iconId))
            {
                sigilTrinket.IconId = iconId;
            }
        }

        // Name suffix
        sigilTrinket.Name += cfg.NameSuffix ?? string.Empty;

        // Intensity / Reduction / ManaReservation / Cooldown / TriggerChance
        if (cfg.SetIntensity)
        {
            sigilTrinket.SigilTrinketIntensity = GetIntensity(profile);
        }

        if (cfg.SetReduction)
        {
            sigilTrinket.SigilTrinketManaReserved = 0;
            sigilTrinket.SigilTrinketReductionAmount = GetReductionAmount(profile);
        }

        if (cfg.SetManaReservedZero)
        {
            sigilTrinket.SigilTrinketManaReserved = 0;
        }

        // cooldown: multiplicative adjustment if provided (not 1.0)
        if (cfg.CooldownMultiplier != 1.0)
        {
            sigilTrinket.CooldownDuration *= cfg.CooldownMultiplier;
        }

        // trigger chance: multiplicative adjustment if provided (not 1.0)
        if (cfg.TriggerChanceMultiplier < 1.0)
        {
            var baseChance = sigilTrinket.SigilTrinketTriggerChance ?? 0.0;
            var newChance = baseChance * cfg.TriggerChanceMultiplier;
            sigilTrinket.SigilTrinketTriggerChance = Math.Clamp(newChance, 0.0, 1.0);
        }
        else if (cfg.TriggerChanceMultiplier > 1.0)
        {
            var baseChance = sigilTrinket.SigilTrinketTriggerChance ?? 0.0;
            var newChance = 1 - (baseChance * (1 / cfg.TriggerChanceMultiplier));
            sigilTrinket.SigilTrinketTriggerChance = Math.Clamp(newChance, 0.0, 1.0);
        }

        if (cfg.ZeroTriggerChance)
        {
            sigilTrinket.SigilTrinketTriggerChance = 0;
        }

        // Use text - support {wieldReq} placeholder
        if (!string.IsNullOrEmpty(cfg.UseText))
        {
            var useText = cfg.UseText;

            // Build a short wield requirement string from any Training/Skill wield slots
            var skillNames = new List<string>();
            if (skillNames.Count > 0)
            {
                // Deduplicate and preserve order
                var unique = new List<string>();
                foreach (var s in skillNames)
                {
                    if (!unique.Contains(s))
                    {
                        unique.Add(s);
                    }
                }

                var wieldReqStr = unique.Count == 1
                    ? $"Requires specialized {unique[0]}"
                    : $"Requires specialized {string.Join(" or ", unique)}";

                useText = useText.Replace("{wieldReq}", wieldReqStr);
            }

            sigilTrinket.Use = useText;
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

    private static uint GetBaseLevelCost(int tier)
    {
        switch (tier)
        {
            default:
                return 10000;
            case 2:
                return 50000;
            case 3:
                return 100000;
            case 4:
                return 250000;
            case 5:
                return 500000;
            case 6:
                return 1000000;
            case 7:
                return 2000000;
        }
    }

    private static int GetValuePerTier(int tier)
    {
        switch (tier)
        {
            default:
                return 100;
            case 2:
                return 200;
            case 3:
                return 300;
            case 4:
                return 400;
            case 5:
                return 500;
            case 6:
                return 750;
            case 7:
                return 1000;
        }
    }
}
