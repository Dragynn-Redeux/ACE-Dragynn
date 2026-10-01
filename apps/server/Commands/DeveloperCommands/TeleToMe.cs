using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class TeleToMe
{
    /// <summary>
    /// Teleports a player to your current location
    /// </summary>
    [CommandHandler(
        "teletome",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Teleports a player to your current location.",
        "PlayerName"
    )]
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
