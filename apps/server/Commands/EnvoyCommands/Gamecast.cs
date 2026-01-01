using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.EnvoyCommands;

public class Gamecast
{
    // gamecast <message>
    [CommandHandler(
        "gamecast",
        AccessLevel.Envoy,
        CommandHandlerFlag.None,
        1,
        "Sends a world-wide broadcast.",
        "<message>\n"
        + "This command sends a world-wide broadcast to everyone in the game. Text is prefixed with 'Broadcast from (admin-name)> '.\n"
        + "See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote."
    )]
    public static void HandleGamecast(Session session, params string[] parameters)
    {
        // > Broadcast from usage: @gamecast<message>
        // This command sends a world-wide broadcast to everyone in the game. Text is prefixed with 'Broadcast from (admin-name)> '.
        // See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote.
        // @gamecast - Sends a world-wide broadcast.

        //session.Player.HandleActionWorldBroadcast($"Broadcast from {session.Player.Name}> {string.Join(" ", parameters)}", ChatMessageType.WorldBroadcast);

        var msg =
            $"Broadcast from <{(session != null ? session.Player.Name : "System")}>: {string.Join(" ", parameters)}";
        var sysMessage = new GameMessageSystemChat(msg, ChatMessageType.System);
        PlayerManager.BroadcastToAll(sysMessage, msg, "System");
        PlayerManager.LogBroadcastChat(Channel.AllBroadcast, session?.Player, msg);
    }
}
