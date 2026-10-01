using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Network;

namespace ACE.Server.Commands.DeveloperCommands;

public class MoveTo
{
    /// <summary>
    /// This function is just used to exercise the ability to have player movement without animation.   Once we are solid on this it can be removed.   Og II
    /// </summary>
    [CommandHandler(
        "MoveTo",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        0,
        "Used to test the MoveToObject message.   It will spawn a training wand in front of you and then move to that object.",
        "moveto\n" + "optional parameter distance if omitted 10f"
    )]
    public static void HandleMoveTo(Session session, params string[] parameters)
    {
        var distance = 10.0f;
        ushort trainingWandTarget = 12748;

        if ((parameters?.Length > 0))
        {
            distance = Convert.ToInt16(parameters[0]);
        }

        var loot = WorldObjectFactory.CreateNewWorldObject(trainingWandTarget);
        loot.Location = session.Player.Location.InFrontOf((loot.UseRadius ?? 2) > 2 ? loot.UseRadius.Value : 2);
        loot.InstanceId = session.Player.InstanceId;
        loot.Location.LandblockId = new LandblockId(loot.Location.GetCell(loot.InstanceId));

        loot.EnterWorld();

        session.Player.HandleActionPutItemInContainer(loot.Guid.Full, session.Player.Guid.Full);
    }
}
