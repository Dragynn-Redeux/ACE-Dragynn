using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class GameCastEmote
{
    // gamecastemote <message>
    [CommandHandler(
        "gamecastemote",
        AccessLevel.Developer,
        CommandHandlerFlag.None,
        1,
        "Sends text to all players, formatted exactly as entered.",
        "<message>\n" + "See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote."
    )]
    public static void HandleGameCastEmote(Session session, params string[] parameters)
    {
        // usage: "@gamecastemote <message>" or "@we <message"
        // Sends text to all players, formatted exactly as entered, with no prefix of any kind.
        // See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote.
        // @gamecastemote - Sends text to all players, formatted exactly as entered.

        var msg = string.Join(" ", parameters);
        msg = msg.Replace("\\n", "\n");
        //session.Player.HandleActionWorldBroadcast($"{msg}", ChatMessageType.WorldBroadcast);

        var sysMessage = new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast);
        PlayerManager.BroadcastToAll(sysMessage, msg, "World");
        PlayerManager.LogBroadcastChat(Channel.AllBroadcast, session?.Player, msg);
    }

    // we <message>
    [CommandHandler(
        "we",
        AccessLevel.Developer,
        CommandHandlerFlag.None,
        1,
        "Sends text to all players, formatted exactly as entered.",
        "<message>\n" + "See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote."
    )]
    public static void HandleWe(Session session, params string[] parameters)
    {
        // usage: "@gamecastemote <message>" or "@we <message"
        // Sends text to all players, formatted exactly as entered, with no prefix of any kind.
        // See Also: @gamecast, @gamecastemote, @gamecastlocal, @gamecastlocalemote.
        // @gamecastemote - Sends text to all players, formatted exactly as entered.

        HandleGameCastEmote(session, parameters);
    }
}
