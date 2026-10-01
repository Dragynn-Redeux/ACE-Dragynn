using System.Linq;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using static ACE.Server.Commands.DeveloperCommands.ContentCommands.ContentCommandUtilities;

namespace ACE.Server.Commands.DeveloperCommands.ContentCommands;

public class EncounterRemove
{
    [CommandHandler(
        "removeenc",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        "Removes the last appraised object from the encounters table"
    )]
    public static void HandleRemoveEnc(Session session, params string[] parameters)
    {
        if (RefusedInAnInstance(session))
        {
            return;
        }

        var obj = CommandHandlerHelper.GetLastAppraisedObject(session);

        if (obj == null)
        {
            return;
        }

        // find root generator
        while (obj.Generator != null)
        {
            obj = obj.Generator;
        }

        var cellX = (int)obj.Location.PositionX / 24;
        var cellY = (int)obj.Location.PositionY / 24;

        var landblock = (ushort)obj.Location.Landblock;

        // clear any cached encounters for this landblock
        DatabaseManager.World.ClearCachedEncountersByLandblock(landblock);

        // get existing encounters for this landblock
        var encounters = DatabaseManager.World.GetCachedEncountersByLandblock(landblock, out var wasCached);

        // check for existing encounter
        var encounter = encounters.FirstOrDefault(i =>
            i.CellX == cellX && i.CellY == cellY && i.WeenieClassId == obj.WeenieClassId
        );

        if (encounter == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Couldn't find encounter for {obj.WeenieClassId} - {obj.Name}",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Removing encounter @ landblock {obj.Location.Landblock:X4}, cellX={cellX}, cellY={cellY}\n{obj.WeenieClassId} - {obj.Name}",
                ChatMessageType.Broadcast
            )
        );

        encounters.Remove(encounter);

        SyncEncounters(session, landblock, encounters);

        // this is needed for any generators that don't have GeneratorDestructionType
        DestroyAll(obj);
    }
}
