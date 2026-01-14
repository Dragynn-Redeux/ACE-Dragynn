using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

internal class DiscordChatStop
{
    [CommandHandler("DiscordChatStop", AccessLevel.Admin, CommandHandlerFlag.None, "")]
    public static void HandleDiscordChatStop(Session session, params string[] parameters)
    {
        CommandHandlerHelper.WriteOutputInfo(session, "Stopping Discord chat bridge...", ChatMessageType.WorldBroadcast);
        DiscordChatBridge.Stop();
    }
}
