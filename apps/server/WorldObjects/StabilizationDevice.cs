using System;
using System.Collections.Generic;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Managers;

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
                new GameMessageSystemChat("This item is not unstable.", ChatMessageType.Broadcast)
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
                if (!UpgradeKit.UpgradeItem(player, target))
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

                // Success: clear timer and mark bound
                target.RemoveProperty(PropertyInt.Lifespan);
                target.SetProperty(PropertyInt.Bonded, 1);

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
                        $"You use the {source.Name} to stabilize {target.Name}. The decay timer has been removed and the item's power has been enhanced.",
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
