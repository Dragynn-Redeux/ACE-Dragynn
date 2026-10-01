using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands.WorldObjectCommands;

public class GetInfo
{
    [CommandHandler(
        "getinfo",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        "Shows basic info for the last appraised object."
    )]
    public static void HandleGetInfo(Session session, params string[] parameters)
    {
        var wo = CommandHandlerHelper.GetLastAppraisedObject(session);

        if (wo != null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"GUID: {wo.Guid}\nWeenieClassId: {wo.WeenieClassId}\nWeenieClassName: {wo.WeenieClassName}\nInstance: {InstanceManager.Describe(wo.InstanceId)}",
                    ChatMessageType.Broadcast
                )
            );
        }
    }
}
