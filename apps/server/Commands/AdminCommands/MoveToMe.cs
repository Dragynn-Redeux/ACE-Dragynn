using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Commands.AdminCommands;

public class MoveToMe
{
    [CommandHandler(
        "movetome",
        AccessLevel.Admin,
        CommandHandlerFlag.RequiresWorld,
        "Moves the last appraised object to the current player location."
    )]
    public static void HandleMoveToMe(Session session, params string[] parameters)
    {
        var obj = CommandHandlerHelper.GetLastAppraisedObject(session);

        if (obj == null)
        {
            return;
        }

        if (obj.CurrentLandblock == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{obj.Name} ({obj.Guid}) is not a landblock object",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        if (obj is Player)
        {
            HandleTeleToMe(session, new string[] { obj.Name });
            return;
        }

        var prevLoc = obj.Location;
        var newLoc = new Position(session.Player.Location);
        newLoc.Rotation = prevLoc.Rotation; // keep previous rotation

        var setPos = new Physics.Common.SetPosition(
            newLoc.PhysPosition(),
            Physics.Common.SetPositionFlags.Teleport | Physics.Common.SetPositionFlags.Slide
        );
        var result = obj.PhysicsObj.SetPosition(setPos);

        if (result != Physics.Common.SetPositionError.OK)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Failed to move {obj.Name} ({obj.Guid}) to current location: {result}",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }
        session.Network.EnqueueSend(
            new GameMessageSystemChat($"Moving {obj.Name} ({obj.Guid}) to current location", ChatMessageType.Broadcast)
        );

        obj.Location = obj.PhysicsObj.Position.ACEPosition();

        if (prevLoc.Landblock != obj.Location.Landblock)
        {
            LandblockManager.RelocateObjectForPhysics(obj, true);
        }

        obj.SendUpdatePosition(true);
    }

    public static void HandleTeleToMe(Session session, params string[] parameters)
    {
        var playerName = string.Join(" ", parameters);
        var player = PlayerManager.GetOnlinePlayer(playerName);
        if (player == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"Player {playerName} was not found.", ChatMessageType.Broadcast)
            );
            return;
        }
        var currentPos = new Position(player.Location);
        player.Teleport(session.Player.Location, instanceId: session.Player.InstanceId);
        player.SetPosition(PositionType.TeleportedCharacter, currentPos);
        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat($"{session.Player.Name} has teleported you.", ChatMessageType.Magic)
        );

        PlayerManager.BroadcastToAuditChannel(
            session.Player,
            $"{session.Player.Name} has teleported {player.Name} to them."
        );
    }
}
