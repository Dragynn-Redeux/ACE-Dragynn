using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class StabilizationDevice : WorldObject
{
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
                // Remove Lifespan property (remove decay timer)
                if (target.Lifespan != null)
                {
                    target.RemoveProperty(PropertyInt.Lifespan);
                }

                // Scale item to player tier (bypasses UpgradeKit stack count validation)
                if (!UpgradeKit.UpgradeItem(player, target))
                {
                    if (debugStabilization)
                    {
                        _log.Information("[DEBUG][Stabilization] UpgradeItem failed");
                    }
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            "The stabilization failed. The item may not be compatible.",
                            ChatMessageType.Broadcast
                        )
                    );
                    return;
                }

                // Make account bound
                target.SetProperty(PropertyInt.Bonded, 1);

                // Broadcast updated state (IsUnstable flag remains for forge)
                player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                if (debugStabilization)
                {
                    _log.Information("[DEBUG][Stabilization] Stabilization successful for {Target}",
                        target.Name);
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
}
