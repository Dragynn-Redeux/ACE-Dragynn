using System.Collections.Generic;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Commands.DeveloperCommands;

public class Fumble
{
    private static readonly ILogger _log = Log.ForContext(typeof(Fumble));

    // fumble
    [CommandHandler("fumble", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 0)]
    public static void HandleFumble(Session session, params string[] parameters)
    {
        // @fumble - Forces the selected target to drop everything they contain to the ground.

        var objectId = ObjectGuid.Invalid;

        if (session.Player.HealthQueryTarget.HasValue)
        {
            objectId = new ObjectGuid((uint)session.Player.HealthQueryTarget);
        }
        else if (session.Player.ManaQueryTarget.HasValue)
        {
            objectId = new ObjectGuid((uint)session.Player.ManaQueryTarget);
        }
        else if (session.Player.CurrentAppraisalTarget.HasValue)
        {
            objectId = new ObjectGuid((uint)session.Player.CurrentAppraisalTarget);
        }

        if (objectId == ObjectGuid.Invalid)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You must select a player to force them to drop everything.",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        var wo = session.Player.CurrentLandblock?.GetObject(objectId);

        if (wo is null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"Unable to locate what you have selected.", ChatMessageType.Broadcast)
            );
        }
        else if (wo is Player player)
        {
            var items = new List<WorldObject>();
            var playerLoc = new Position(player.Location);

            foreach (var item in player.Inventory)
            {
                if (
                    player.TryRemoveFromInventoryWithNetworking(
                        item.Key,
                        out var worldObject,
                        Player.RemoveFromInventoryAction.DropItem
                    )
                )
                {
                    items.Add(worldObject);
                }
            }

            foreach (var item in player.EquippedObjects)
            {
                if (
                    player.TryDequipObjectWithNetworking(
                        item.Key.Full,
                        out var worldObject,
                        Player.DequipObjectAction.DropItem
                    )
                )
                {
                    items.Add(worldObject);
                }
            }

            player.SavePlayerToDatabase();

            foreach (var item in items)
            {
                item.Location = new Position(playerLoc);
                item.Location.PositionZ += .5f;
                item.Placement = Placement.Resting; // This is needed to make items lay flat on the ground.

                // increased precision for non-ethereal objects
                var ethereal = item.Ethereal;
                item.Ethereal = true;

                if (session.Player.CurrentLandblock?.AddWorldObject(item) ?? false)
                {
                    item.Location.LandblockId = new LandblockId(item.Location.GetCell(session.Player.InstanceId));

                    // try slide to new position
                    var transit = item.PhysicsObj.transition(
                        item.PhysicsObj.Position,
                        new Physics.Common.Position(item.Location),
                        false
                    );

                    if (transit != null && transit.SpherePath.CurCell != null)
                    {
                        item.PhysicsObj.SetPositionInternal(transit);

                        item.SyncLocation();

                        item.SendUpdatePosition(true);
                    }
                    item.Ethereal = ethereal;

                    if (item.Ethereal == null)
                    {
                        var defaultPhysicsState = (PhysicsState)(item.GetProperty(PropertyInt.PhysicsState) ?? 0);

                        item.Ethereal = defaultPhysicsState.HasFlag(PhysicsState.Ethereal);
                    }

                    item.EnqueueBroadcastPhysicsState();

                    // drop success
                    player.Session.Network.EnqueueSend(
                        new GameMessagePublicUpdateInstanceID(item, PropertyInstanceId.Container, ObjectGuid.Invalid),
                        new GameMessagePublicUpdateInstanceID(item, PropertyInstanceId.Wielder, ObjectGuid.Invalid),
                        new GameEventItemServerSaysMoveItem(player.Session, item),
                        new GameMessageUpdatePosition(item)
                    );

                    player.EnqueueBroadcast(new GameMessageSound(player.Guid, Sound.DropItem));

                    item.EmoteManager.OnDrop(player);
                    item.SaveBiotaToDatabase();
                }
                else
                {
                    _log.Warning(
                        "0x{ItemGuid}:{Item} for player {Player} lost from fumble failure.",
                        item.Guid,
                        item.Name,
                        player.Name
                    );
                }
            }
        }
        else
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You cannot force {wo.Name} to drop everything because it is not a player.",
                    ChatMessageType.Broadcast
                )
            );
        }
    }
}
