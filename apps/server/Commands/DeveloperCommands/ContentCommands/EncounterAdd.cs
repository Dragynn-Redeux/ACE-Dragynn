using System;
using System.Linq;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using static ACE.Server.Commands.DeveloperCommands.ContentCommands.ContentCommandUtilities;

namespace ACE.Server.Commands.DeveloperCommands.ContentCommands;

public class EncounterAdd
{
    [CommandHandler(
        "addenc",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Spawns a new wcid or classname in the current outdoor cell as an encounter",
        "<wcid or classname>"
    )]
    public static void HandleAddEncounter(Session session, params string[] parameters)
    {
        if (RefusedInAnInstance(session))
        {
            return;
        }

        var param = parameters[0];

        Weenie weenie = null;

        if (uint.TryParse(param, out var wcid))
        {
            weenie = DatabaseManager.World.GetWeenie(wcid); // wcid
        }
        else
        {
            weenie = DatabaseManager.World.GetWeenie(param); // classname
        }

        if (weenie == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"Couldn't find weenie {param}", ChatMessageType.Broadcast)
            );
            return;
        }

        var pos = session.Player.Location;

        if ((pos.Cell & 0xFFFF) >= 0x100)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat("You must be outdoors to create an encounter!", ChatMessageType.Broadcast)
            );
            return;
        }

        var cellX = (int)pos.PositionX / 24;
        var cellY = (int)pos.PositionY / 24;

        var landblock = (ushort)pos.Landblock;

        // clear any cached encounters for this landblock
        DatabaseManager.World.ClearCachedEncountersByLandblock(landblock);

        // get existing encounters for this landblock
        var encounters = DatabaseManager.World.GetCachedEncountersByLandblock(landblock, out var wasCached);

        // check for existing encounter
        if (encounters.Any(i => i.CellX == cellX && i.CellY == cellY))
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat("This cell already contains an encounter!", ChatMessageType.Broadcast)
            );
            return;
        }

        // spawn encounter
        var wo = SpawnEncounter(weenie, cellX, cellY, pos, session);

        if (wo == null)
        {
            return;
        }

        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Creating new encounter @ landblock {pos.Landblock:X4}, cellX={cellX}, cellY={cellY}\n{wo.WeenieClassId} - {wo.Name}",
                ChatMessageType.Broadcast
            )
        );

        // add a new encounter (verifications?)
        var encounter = new Encounter();
        encounter.Landblock = (int)pos.Landblock;
        encounter.CellX = cellX;
        encounter.CellY = cellY;
        encounter.WeenieClassId = weenie.ClassId;
        encounter.LastModified = DateTime.Now;

        encounters.Add(encounter);

        // write encounters to sql file / load into db
        SyncEncounters(session, landblock, encounters);
    }
}
