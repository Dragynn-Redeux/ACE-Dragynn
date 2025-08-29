using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity.Actions;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class ReloadLandblock
{
    [CommandHandler(
        "reload-landblock",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        "Reloads the current landblock."
    )]
    public static void HandleReloadLandblocks(Session session, params string[] parameters)
    {
        var landblock = session.Player.CurrentLandblock;

        var landblockId = landblock.Id.Raw | 0xFFFF;

        session.Network.EnqueueSend(
            new GameMessageSystemChat($"Reloading 0x{landblockId:X8}", ChatMessageType.Broadcast)
        );

        // destroy all non-player server objects
        landblock.DestroyAllNonPlayerObjects();

        // clear landblock cache
        DatabaseManager.World.ClearCachedInstancesByLandblock(landblock.Id.Landblock);

        landblock.LandblockLethalityMod = 0.0;
        landblock.LandblockLootQualityMod = 0.0;

        // reload landblock
        var actionChain = new ActionChain();
        actionChain.AddDelayForOneTick();
        actionChain.AddAction(
            session.Player,
            () =>
            {
                landblock.Init(true);
            }
        );
        actionChain.EnqueueChain();
    }
}
