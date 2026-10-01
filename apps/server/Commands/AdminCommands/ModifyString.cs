using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

public class ModifyString
{
    [CommandHandler(
        "modifystring",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        2,
        "Modifies a server property that is a string",
        "modifystring (string) (string)"
    )]
    public static void HandleModifyServerStringProperty(Session session, params string[] parameters)
    {
        var value = ValueOf(parameters);

        if (PropertyManager.ModifyString(parameters[0], value))
        {
            CommandHandlerHelper.WriteOutputInfo(session, "String property successfully updated!");
            PlayerManager.BroadcastToAuditChannel(
                session?.Player,
                $"Successfully changed server string property {parameters[0]} to {value}"
            );
        }
        else
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                "Unknown string property was not updated. Type showprops for a list of properties."
            );
        }
    }

    /// <summary>
    /// Everything after the name of the property, as one value, so that a value with spaces doesn't need quotes
    /// (without this only its first word was stored)
    /// </summary>
    internal static string ValueOf(string[] parameters)
    {
        return string.Join(" ", parameters.Skip(1));
    }
}
