using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class GameCastLocal
{
    // gamecastlocal <message>
    [CommandHandler(
        "gamecastlocal",
        AccessLevel.Developer,
        CommandHandlerFlag.None,
        1,
        "Sends a server-wide broadcast.",
        "<message>\n"
        + "This command sends the specified text to every player on the current server.\n"
        + "See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote."
    )]
    public static void HandleGameCastLocal(Session session, params string[] parameters)
    {
        // Local Server Broadcast from
        // usage: @gamecastlocal<message>
        // This command sends the specified text to every player on the current server.
        // See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote.
        // @gamecastlocal Sends a server-wide broadcast.

        // Since we only have one server, this command will just call the other one
        HandleGamecast(session, parameters);
    }

    public static void HandleGamecast(Session session, params string[] parameters)
    {
        // > Broadcast from usage: @gamecast<message>
        // This command sends a world-wide broadcast to everyone in the game. Text is prefixed with 'Broadcast from (admin-name)> '.
        // See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote.
        // @gamecast - Sends a world-wide broadcast.

        //session.Player.HandleActionWorldBroadcast($"Broadcast from {session.Player.Name}> {string.Join(" ", parameters)}", ChatMessageType.WorldBroadcast);

        var msg =
            $"Broadcast from {(session != null ? session.Player.Name : "System")}> {string.Join(" ", parameters)}";
        var sysMessage = new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast);
        PlayerManager.BroadcastToAll(sysMessage, msg, "System");
        PlayerManager.LogBroadcastChat(Channel.AllBroadcast, session?.Player, msg);
    }
}
