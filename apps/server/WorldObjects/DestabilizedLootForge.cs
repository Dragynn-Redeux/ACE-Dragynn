using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public static class DestabilizedLootForge
{
    private const PropertyBool TerminalDestabilizedLockProperty = (PropertyBool)10011;
    private const PropertyInt ForgePassCountProperty = (PropertyInt)10011;

    private const string ConfirmMessagePlaceholder =
        "[PLACEHOLDER] Finalize destabilize on this item? This cannot be undone.";
    private const string SuccessMessagePlaceholder =
        "[PLACEHOLDER] The forge tears resonance patterns into your item.";
    private const string FailureMessagePlaceholder =
        "[PLACEHOLDER] The forge rejects the destabilize attempt.";
    private const string ExceptionalMessagePlaceholder =
        "[PLACEHOLDER] Exceptional resonance cascade detected.";

    public static bool IsTerminallyDestabilized(WorldObject item)
    {
        return item?.GetProperty(TerminalDestabilizedLockProperty) == true;
    }

    public static bool TryQueueFinalization(Player player, WorldObject item, out string failureMessage)
    {
        failureMessage = null;

        if (player == null || item == null)
        {
            failureMessage = "That item is unavailable.";
            return false;
        }

        if (IsTerminallyDestabilized(item))
        {
            failureMessage = "That item is already terminally destabilized.";
            return false;
        }

        player.ConfirmationManager.EnqueueSend(
            new DestabilizeConfirmation(
                player.Guid,
                response =>
                {
                    if (!response)
                    {
                        player.SendTransientError("Destabilize cancelled.");
                        return;
                    }

                    ExecuteFinalization(player, item.Guid);
                }
            ),
            ConfirmMessagePlaceholder
        );

        return true;
    }

    private static void ExecuteFinalization(Player player, ObjectGuid itemGuid)
    {
        var item = player.FindObject(itemGuid.Full, Player.SearchLocations.MyInventory);
        if (item == null)
        {
            player.SendTransientError("That item is no longer available.");
            return;
        }

        if (IsTerminallyDestabilized(item))
        {
            player.SendTransientError("That item is already terminally destabilized.");
            return;
        }

        // Placeholder hook: ingredient reservation/consumption sequence can be inserted here.
        var rollResult = DestabilizedLootEffects.ApplyDestabilize(item);
        if (!rollResult.Success)
        {
            player.SendTransientError(rollResult.FailureReason ?? FailureMessagePlaceholder);
            return;
        }

        item.SetProperty(TerminalDestabilizedLockProperty, true);
        item.SetProperty(ForgePassCountProperty, (item.GetProperty(ForgePassCountProperty) ?? 1) + 1);
        item.Bonded = BondedStatus.Bonded;

        player.EnqueueBroadcast(new GameMessageUpdateObject(item));

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"{SuccessMessagePlaceholder} ({rollResult.AppliedPackageCount} package(s) applied.)",
                ChatMessageType.Broadcast
            )
        );

        if (rollResult.ExceptionalExtraPackageCount > 0)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{ExceptionalMessagePlaceholder} (+{rollResult.ExceptionalExtraPackageCount} extra package(s)).",
                    ChatMessageType.Broadcast
                )
            );
        }
    }

    private sealed class DestabilizeConfirmation : Confirmation
    {
        private readonly Action<bool> _action;

        public DestabilizeConfirmation(ObjectGuid playerGuid, Action<bool> action)
            : base(playerGuid, ConfirmationType.Yes_No)
        {
            _action = action;
        }

        public override void ProcessConfirmation(bool response, bool timeout = false)
        {
            var player = Player;
            if (player == null)
            {
                return;
            }

            _action(!timeout && response);
        }
    }
}
