using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

internal class DiscordChatStart
{
    [CommandHandler("DiscordChatStart", AccessLevel.Admin, CommandHandlerFlag.None, "")]
    public static void HandleDiscordChatStart(Session session, params string[] parameters)
    {
        CommandHandlerHelper.WriteOutputInfo(session, "Starting Discord chat bridge...", ChatMessageType.WorldBroadcast);

        _ = DiscordChatBridge.Start();
    }
}
