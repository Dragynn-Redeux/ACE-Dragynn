using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.WorldObjects;

namespace ACE.Server.Commands.DeveloperCommands;

public class TeleAllTo
{
    // teleallto [char]
    [CommandHandler(
        "teleallto",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        0,
        "Teleports all players to a player. If no target is specified, all players will be teleported to you.",
        "[Player's Name]\n"
    )]
    public static void HandleTeleAllTo(Session session, params string[] parameters)
    {
        Player destinationPlayer = null;

        if (parameters.Length > 0)
        {
            destinationPlayer = PlayerManager.GetOnlinePlayer(parameters[0]);
        }

        if (destinationPlayer == null)
        {
            destinationPlayer = session.Player;
        }

        foreach (var player in PlayerManager.GetAllOnline())
        {
            if (player == destinationPlayer)
            {
                continue;
            }

            player.SetPosition(PositionType.TeleportedCharacter, new Position(player.Location));

            player.Teleport(new Position(destinationPlayer.Location), instanceId: destinationPlayer.InstanceId);
        }

        PlayerManager.BroadcastToAuditChannel(
            session.Player,
            $"{session.Player.Name} has teleported all online players to their location."
        );
    }
}
