using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

public class RZDel
{
    [CommandHandler(
        "rzdel",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        1,
        "Deletes a resonance zone by id.",
        "rzdel (int id)"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var id))
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Please input a valid zone id.", ChatMessageType.Help);
            return;
        }

        var ok = DatabaseManager.ShardConfig.DeleteResonanceZoneEntry(id);

        CommandHandlerHelper.WriteOutputInfo(
            session,
            ok ? $"Deleted zone ID={id}." : $"Zone ID={id} not found.",
            ChatMessageType.Broadcast
        );
    }
}
