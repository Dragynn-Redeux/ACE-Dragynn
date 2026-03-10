using System;
using System.Collections.Generic;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Managers;
using static ACE.Server.Factories.SigilTrinketConfig;

namespace ACE.Server.WorldObjects;

public class StabilizationDevice : WorldObject
{
    private static readonly PropertyInt[] DebugIntProperties =
    {
        PropertyInt.WieldDifficulty,
        PropertyInt.Damage,
        PropertyInt.ArmorLevel,
        PropertyInt.WardLevel,
        PropertyInt.ItemMaxMana,
        PropertyInt.ItemCurMana,
        PropertyInt.GearDamage,
        PropertyInt.GearDamageResist,
        PropertyInt.GearCritDamage,
        PropertyInt.GearCritResist,
        PropertyInt.GearMaxHealth,
        PropertyInt.GearMaxStamina,
        PropertyInt.GearMaxMana,
        PropertyInt.Bonded,
        PropertyInt.Lifespan
    };

    private static readonly PropertyFloat[] DebugFloatProperties =
    {
        PropertyFloat.DamageMod,
        PropertyFloat.ElementalDamageMod,
        PropertyFloat.WeaponRestorationSpellsMod,
        PropertyFloat.WeaponOffense,
        PropertyFloat.WeaponPhysicalDefense,
        PropertyFloat.WeaponMagicalDefense,
        PropertyFloat.WeaponLifeMagicMod,
        PropertyFloat.WeaponWarMagicMod,
        PropertyFloat.ManaRate
    };

    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public StabilizationDevice(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public StabilizationDevice(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private static void SetEphemeralValues() { }

    private bool DebugStabilization => PropertyManager.GetBool("debug_stabilization").Item;

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        if (DebugStabilization)
        {
            _log.Information(
                "[DEBUG][Stabilization] HandleActionUseOnTarget source={Source}, target={Target}",
                Name,
                target?.Name);
        }

        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        var debugStabilization = PropertyManager.GetBool("debug_stabilization").Item;

        if (debugStabilization)
        {
            _log.Information(
                "[DEBUG][Stabilization] UseObjectOnTarget confirmed={Confirmed}, source={Source}, target={Target}, IsUnstable={IsUnstable}",
                confirmed,
                source.Name,
                target.Name,
                target.GetProperty(PropertyBool.IsUnstable));
        }

        if (player.IsBusy)
        {
            if (debugStabilization)
            {
                _log.Information("[DEBUG][Stabilization] Player is busy");
            }
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (target.GetProperty(PropertyBool.IsUnstable) != true)
        {
            if (debugStabilization)
            {
                _log.Information("[DEBUG][Stabilization] Target is not unstable");
            }
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "This item carries no unstable resonance for the device to stabilize.",
                    ChatMessageType.Broadcast
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (target.Lifespan == null)
        {
            if (debugStabilization)
            {
                _log.Information("[DEBUG][Stabilization] Target is already stabilized");
            }
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "This item is already stabilized and cannot be stabilized again.",
                    ChatMessageType.Broadcast
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (!confirmed)
        {
            if (
                !player.ConfirmationManager.EnqueueSend(
                    new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                    $"Use {source.Name} on {target.Name}?"
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
                // Preserve Lifespan in case upgrade fails; only clear it on success
                var originalLifespan = target.Lifespan;
                var beforeSnapshot = debugStabilization ? CaptureSnapshot(target) : default;

                // Scale item to player tier (bypasses UpgradeKit stack count validation)
                var upgradeSucceeded = target is SigilTrinket sigilTrinket
                    ? StabilizeSigilTrinketForPlayer(player, sigilTrinket)
                    : UpgradeKit.UpgradeItem(player, target, 0, UpgradeKit.UpgradeContext.Stabilization);

                if (!upgradeSucceeded)
                {
                    // Restore Lifespan if upgrade failed and it was accidentally removed
                    if (originalLifespan.HasValue && target.Lifespan == null)
                    {
                        target.SetProperty(PropertyInt.Lifespan, originalLifespan.Value);
                    }

                    if (debugStabilization)
                    {
                        var afterFailedUpgradeSnapshot = CaptureSnapshot(target);
                        _log.Information(
                            "[DEBUG][Stabilization] UpgradeItem failed target={Target} guid={Guid} delta={Delta}",
                            target.Name,
                            target.Guid,
                            BuildDeltaSummary(beforeSnapshot, afterFailedUpgradeSnapshot)
                        );
                    }
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            "The stabilization failed. The item may not be compatible.",
                            ChatMessageType.Broadcast
                        )
                    );
                    return;
                }

                // Success: clear timer and mark bound to the current character for wield checks.
                target.RemoveProperty(PropertyInt.Lifespan);
                target.SetProperty(PropertyInt.Bonded, 1);
                target.AllowedWielder = player.Guid.Full;
                target.CraftsmanName = player.Name;

                // Broadcast updated state (IsUnstable flag remains for forge)
                player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                if (debugStabilization)
                {
                    var afterSnapshot = CaptureSnapshot(target);
                    _log.Information(
                        "[DEBUG][Stabilization] Stabilization successful target={Target} guid={Guid} delta={Delta}",
                        target.Name,
                        target.Guid,
                        BuildDeltaSummary(beforeSnapshot, afterSnapshot)
                    );
                }
                
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The forge stabilizes the resonance within your {target.NameWithMaterial}.",
                        ChatMessageType.Craft
                    )
                );

                // Consume the device
                player.TryConsumeFromInventoryWithNetworking(source, 1);
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

    private static bool StabilizeSigilTrinketForPlayer(Player player, SigilTrinket sigilTrinket)
    {
        if (player == null || sigilTrinket == null)
        {
            return false;
        }

        var targetTier = Math.Clamp(player.GetPlayerTier(player.Level ?? 1), 1, 8);
        var currentRequirement = sigilTrinket.WieldDifficulty2 ?? sigilTrinket.WieldDifficulty ?? 1;
        var currentTier = Math.Clamp(LootGenerationFactory.GetTierFromRequiredLevel(currentRequirement), 1, 8);

        var currentBand = Math.Clamp(currentTier - 1, 1, 7);
        var targetBand = Math.Clamp(targetTier - 1, 1, 7);

        sigilTrinket.WieldDifficulty2 = LootGenerationFactory.GetRequiredLevelPerTier(targetTier);

        var currentMaxLevel = sigilTrinket.ItemMaxLevel ?? currentBand;
        var currentBonus = Math.Clamp(currentMaxLevel - currentBand, 0, 3);
        sigilTrinket.ItemMaxLevel = Math.Clamp(targetBand + currentBonus, 1, 10);

        sigilTrinket.ItemBaseXp = LootGenerationFactory.GetBaseLevelCost(targetBand);
        sigilTrinket.ItemTotalXp = 0;
        sigilTrinket.Value = LootGenerationFactory.GetValuePerTier(targetBand);

        if (sigilTrinket is { MaxStructure: > 0, Structure: not null })
        {
            var percent = (double)sigilTrinket.Structure.Value / sigilTrinket.MaxStructure.Value;
            percent = Math.Clamp(percent, 0.0, 1.0);

            var adjustedStructure = (int)Math.Round(sigilTrinket.MaxStructure.Value * percent, MidpointRounding.AwayFromZero);
            adjustedStructure = Math.Clamp(adjustedStructure, 0, sigilTrinket.MaxStructure.Value);
            sigilTrinket.Structure = (ushort)adjustedStructure;
        }

        ClampSigilStatsToGenerationBounds(sigilTrinket);

        return true;
    }

    private static void ClampSigilStatsToGenerationBounds(SigilTrinket sigilTrinket)
    {
        ResolveReservedBounds(sigilTrinket, out var healthMin, out var healthMax, out var staminaMin, out var staminaMax, out var manaMin, out var manaMax);

        if (sigilTrinket.SigilTrinketHealthReserved is > 0)
        {
            sigilTrinket.SigilTrinketHealthReserved = Math.Clamp(sigilTrinket.SigilTrinketHealthReserved.Value, healthMin, healthMax);
        }

        if (sigilTrinket.SigilTrinketStaminaReserved is > 0)
        {
            sigilTrinket.SigilTrinketStaminaReserved = Math.Clamp(sigilTrinket.SigilTrinketStaminaReserved.Value, staminaMin, staminaMax);
        }

        if (sigilTrinket.SigilTrinketManaReserved is > 0)
        {
            sigilTrinket.SigilTrinketManaReserved = Math.Clamp(sigilTrinket.SigilTrinketManaReserved.Value, manaMin, manaMax);
        }

        if (sigilTrinket.SigilTrinketIntensity is > 0)
        {
            sigilTrinket.SigilTrinketIntensity = Math.Clamp(sigilTrinket.SigilTrinketIntensity.Value, MinIntensity, MaxIntensity);
        }

        if (sigilTrinket.SigilTrinketReductionAmount is > 0)
        {
            sigilTrinket.SigilTrinketReductionAmount = Math.Clamp(sigilTrinket.SigilTrinketReductionAmount.Value, MinReduction, MaxReduction);
        }

        if (TryResolveSigilConfig(sigilTrinket, out var cfg))
        {
            ResolveCooldownBounds(cfg, out var cooldownMin, out var cooldownMax);
            if (sigilTrinket.CooldownDuration is > 0)
            {
                sigilTrinket.CooldownDuration = Math.Clamp(sigilTrinket.CooldownDuration.Value, cooldownMin, cooldownMax);
            }

            if (cfg.ZeroTriggerChance)
            {
                sigilTrinket.SigilTrinketTriggerChance = 0;
            }
            else if (sigilTrinket.SigilTrinketTriggerChance is > 0)
            {
                ResolveTriggerBounds(cfg, out var triggerMin, out var triggerMax);
                sigilTrinket.SigilTrinketTriggerChance = Math.Clamp(sigilTrinket.SigilTrinketTriggerChance.Value, triggerMin, triggerMax);
            }

            if (cfg.SetManaReservedZero)
            {
                sigilTrinket.SigilTrinketManaReserved = 0;
            }
        }
        else
        {
            if (sigilTrinket.CooldownDuration is > 0)
            {
                sigilTrinket.CooldownDuration = Math.Clamp(sigilTrinket.CooldownDuration.Value, MinCooldown, MaxCooldown);
            }

            if (sigilTrinket.SigilTrinketTriggerChance is > 0)
            {
                sigilTrinket.SigilTrinketTriggerChance = Math.Clamp(sigilTrinket.SigilTrinketTriggerChance.Value, MinChance, MaxChance);
            }
        }
    }

    private static void ResolveReservedBounds(
        SigilTrinket sigilTrinket,
        out double healthMin,
        out double healthMax,
        out double staminaMin,
        out double staminaMax,
        out double manaMin,
        out double manaMax)
    {
        healthMin = 0;
        healthMax = 0;
        staminaMin = 0;
        staminaMax = 0;
        manaMin = 0;
        manaMax = 0;

        var type = sigilTrinket.SigilTrinketType ?? -1;

        switch ((SigilTrinketType)type)
        {
            case SigilTrinketType.Compass:
                healthMin = MinReservedVital * 0.5;
                healthMax = MaxReservedVital * 0.5;
                break;
            case SigilTrinketType.PuzzleBox:
                healthMin = MinReservedVital * 0.25;
                healthMax = MaxReservedVital * 0.25;
                staminaMin = MinReservedVital * 0.5;
                staminaMax = MaxReservedVital * 0.5;
                break;
            case SigilTrinketType.Scarab:
                healthMin = MinReservedVital * 0.25;
                healthMax = MaxReservedVital * 0.25;
                manaMin = MinReservedVital * 0.5;
                manaMax = MaxReservedVital * 0.5;
                break;
            case SigilTrinketType.PocketWatch:
                staminaMin = MinReservedVital;
                staminaMax = MaxReservedVital;
                break;
            case SigilTrinketType.Top:
                manaMin = MinReservedVital;
                manaMax = MaxReservedVital;
                break;
            case SigilTrinketType.Goggles:
                staminaMin = MinReservedVital * 0.5;
                staminaMax = MaxReservedVital * 0.5;
                manaMin = MinReservedVital * 0.5;
                manaMax = MaxReservedVital * 0.5;
                break;
        }
    }

    private static void ResolveCooldownBounds(SigilStatConfig cfg, out double min, out double max)
    {
        var mult = cfg.CooldownMultiplier;
        var low = MinCooldown * mult;
        var high = MaxCooldown * mult;

        min = Math.Min(low, high);
        max = Math.Max(low, high);
    }

    private static void ResolveTriggerBounds(SigilStatConfig cfg, out double min, out double max)
    {
        var mult = cfg.TriggerChanceMultiplier;
        if (mult <= 0)
        {
            min = 0;
            max = 1;
            return;
        }

        if (mult < 1.0)
        {
            min = MinChance * mult;
            max = MaxChance * mult;
            return;
        }

        if (mult > 1.0)
        {
            var low = 1 - (MaxChance * (1 / mult));
            var high = 1 - (MinChance * (1 / mult));

            min = Math.Min(low, high);
            max = Math.Max(low, high);
            return;
        }

        min = MinChance;
        max = MaxChance;
    }

    private static bool TryResolveSigilConfig(SigilTrinket sigilTrinket, out SigilStatConfig config)
    {
        config = null;

        if (!sigilTrinket.SigilTrinketEffectId.HasValue)
        {
            return false;
        }

        var candidates = GetCandidatesByType(sigilTrinket.SigilTrinketType);
        if (candidates == null || candidates.Length == 0)
        {
            return false;
        }

        var effectId = sigilTrinket.SigilTrinketEffectId.Value;
        var allowed = sigilTrinket.AllowedSpecializedSkills;

        IReadOnlyDictionary<int, SigilStatConfig> chosenMap = null;

        if (allowed != null && allowed.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                if (!TryGetMap(candidate.MapName, out var map))
                {
                    continue;
                }

                if (!SkillSetsEqual(candidate.Skills, allowed))
                {
                    continue;
                }

                if (map.ContainsKey(effectId))
                {
                    chosenMap = map;
                    break;
                }
            }
        }

        if (chosenMap == null)
        {
            foreach (var candidate in candidates)
            {
                if (!TryGetMap(candidate.MapName, out var map))
                {
                    continue;
                }

                if (map.ContainsKey(effectId))
                {
                    chosenMap = map;
                    break;
                }
            }
        }

        if (chosenMap == null)
        {
            return false;
        }

        return chosenMap.TryGetValue(effectId, out config);
    }

    private static (string MapName, List<Skill> Skills)[] GetCandidatesByType(int? sigilType)
    {
        switch ((SigilTrinketType?)sigilType)
        {
            case SigilTrinketType.Compass:
                return
                [
                    ("shieldTwohandedCompass", new List<Skill> { Skill.Shield, Skill.TwoHandedCombat }),
                    ("shieldCompass", new List<Skill> { Skill.Shield }),
                    ("twohandedCompass", new List<Skill> { Skill.TwoHandedCombat })
                ];
            case SigilTrinketType.PuzzleBox:
                return
                [
                    ("dualWieldMissilePuzzleBox", new List<Skill> { Skill.DualWield, Skill.Bow }),
                    ("dualWieldPuzzleBox", new List<Skill> { Skill.DualWield }),
                    ("missilePuzzleBox", new List<Skill> { Skill.Bow }),
                    ("thieveryPuzzleBox", new List<Skill> { Skill.Thievery })
                ];
            case SigilTrinketType.Scarab:
                return
                [
                    ("lifeWarMagicScarab", new List<Skill> { Skill.LifeMagic, Skill.WarMagic }),
                    ("lifeMagicScarab", new List<Skill> { Skill.LifeMagic }),
                    ("warMagicScarab", new List<Skill> { Skill.WarMagic })
                ];
            case SigilTrinketType.PocketWatch:
                return
                [
                    ("physicalDefensePocketWatch", new List<Skill> { Skill.PhysicalDefense })
                ];
            case SigilTrinketType.Top:
                return
                [
                    ("magicDefenseTop", new List<Skill> { Skill.MagicDefense })
                ];
            case SigilTrinketType.Goggles:
                return
                [
                    ("perceptionGoggles", new List<Skill> { Skill.Perception }),
                    ("deceptionGoggles", new List<Skill> { Skill.Deception })
                ];
            default:
                return null;
        }
    }

    private static bool SkillSetsEqual(IReadOnlyList<Skill> left, IReadOnlyList<Skill> right)
    {
        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        var leftSet = new HashSet<Skill>(left);
        var rightSet = new HashSet<Skill>(right);
        return leftSet.SetEquals(rightSet);
    }

    private readonly record struct StabilizationSnapshot(
        Dictionary<PropertyInt, int?> Ints,
        Dictionary<PropertyFloat, double?> Floats,
        int SpellCount
    );

    private static StabilizationSnapshot CaptureSnapshot(WorldObject target)
    {
        var ints = new Dictionary<PropertyInt, int?>(DebugIntProperties.Length);
        foreach (var property in DebugIntProperties)
        {
            ints[property] = target.GetProperty(property);
        }

        var floats = new Dictionary<PropertyFloat, double?>(DebugFloatProperties.Length);
        foreach (var property in DebugFloatProperties)
        {
            floats[property] = target.GetProperty(property);
        }

        var spellCount = target.Biota.PropertiesSpellBook?.Count ?? 0;

        return new StabilizationSnapshot(ints, floats, spellCount);
    }

    private static string BuildDeltaSummary(StabilizationSnapshot before, StabilizationSnapshot after)
    {
        var changes = new List<string>();

        foreach (var property in DebugIntProperties)
        {
            var beforeValue = before.Ints[property];
            var afterValue = after.Ints[property];

            if (beforeValue != afterValue)
            {
                changes.Add($"{property}:{FormatInt(beforeValue)}->{FormatInt(afterValue)}");
            }
        }

        foreach (var property in DebugFloatProperties)
        {
            var beforeValue = before.Floats[property];
            var afterValue = after.Floats[property];

            if (beforeValue != afterValue)
            {
                changes.Add($"{property}:{FormatDouble(beforeValue)}->{FormatDouble(afterValue)}");
            }
        }

        if (before.SpellCount != after.SpellCount)
        {
            changes.Add($"SpellCount:{before.SpellCount}->{after.SpellCount}");
        }

        return changes.Count > 0 ? string.Join(", ", changes) : "no tracked changes";
    }

    private static string FormatInt(int? value)
    {
        return value?.ToString() ?? "null";
    }

    private static string FormatDouble(double? value)
    {
        return value?.ToString("0.###") ?? "null";
    }
}
