using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class MyLocation
{
    /// <summary>
    /// Shows the current player location, from the server perspective
    /// </summary>
    [CommandHandler(
        "myloc",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        0,
        "Shows the current player location, from the server perspective"
    )]
    public static void HandleMyLoc(Session session, params string[] parameters)
    {
        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"CurrentLandblock: {session.Player.CurrentLandblock.Id.Landblock:X4}",
                ChatMessageType.Broadcast
            )
        );
        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Instance: {InstanceManager.Describe(session.Player.InstanceId)}",
                ChatMessageType.Broadcast
            )
        );
        session.Network.EnqueueSend(
            new GameMessageSystemChat($"Location: {session.Player.Location.ToLOCString()}", ChatMessageType.Broadcast)
        );
        session.Network.EnqueueSend(
            new GameMessageSystemChat($"Physics : {session.Player.PhysicsObj.Position}", ChatMessageType.Broadcast)
        );
    }
}
