using System;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class PlayScript
{
    [CommandHandler("pscript", AccessLevel.Developer, CommandHandlerFlag.RequiresWorld, 1)]
    public static void HandlePScript(Session session, params string[] parameters)
    {
        var wo = CommandHandlerHelper.GetLastAppraisedObject(session);
        if (wo == null)
        {
            return;
        }

        if (!Enum.TryParse<ACE.Entity.Enum.PlayScript>(parameters[0], true, out var pscript))
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"Couldn't find PlayScript.{parameters[0]}", ChatMessageType.Broadcast)
            );
            return;
        }
        wo.EnqueueBroadcast(new GameMessageScript(wo.Guid, pscript));
    }
}
