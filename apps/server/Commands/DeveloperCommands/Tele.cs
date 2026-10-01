using System.Collections.Generic;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using ACE.Server.Physics.Common;

namespace ACE.Server.Commands.DeveloperCommands;

public class Tele
{
    // tele [name,] <longitude> <latitude>
    [CommandHandler(
        "tele",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Teleports you(or a player) to some location.",
        "[name] <longitude> <latitude>\nExample: /tele 0n0w\nExample: /tele plats4days 37s,67w\n"
            + "This command teleports yourself (or the specified character) to the given longitude and latitude."
    )]
    public static void HandleTele(Session session, params string[] parameters)
    {
        // Used PhatAC source to implement most of this.  Thanks Pea!

        // usage: @tele [name] longitude latitude
        // This command teleports yourself (or the specified character) to the given longitude and latitude.
        // @tele - Teleports you(or a player) to some location.

        if (session.Player.IsAdvocate && session.Player.AdvocateLevel < 5)
        {
            return;
        }

        var aceParams = new List<CommandParameterHelpers.ACECommandParameter>()
        {
            new CommandParameterHelpers.ACECommandParameter()
            {
                Type = CommandParameterHelpers.ACECommandParameterType.OnlinePlayerNameOrIid,
                Required = false,
                DefaultValue = session.Player
            },
            new CommandParameterHelpers.ACECommandParameter()
            {
                Type = CommandParameterHelpers.ACECommandParameterType.Location,
                Required = true,
                ErrorMessage = "You must supply a location to teleport to.\nExample: /tele 37s,67w"
            }
        };
        if (!CommandParameterHelpers.ResolveACEParameters(session, parameters, aceParams))
        {
            return;
        }

        // Check if water block
        var landblock = LScape.get_landblock(aceParams[1].AsPosition.LandblockId.Raw);
        if (landblock == null)
        {
            // the persistent world has no such landblock: it only exists as an instance
            ChatPacket.SendServerMessage(
                session,
                $"Landblock 0x{aceParams[1].AsPosition.LandblockId.Landblock:X4} only exists as an instance, and can't be teleported to",
                ChatMessageType.Broadcast
            );
            return;
        }

        if (landblock.WaterType == LandDefs.WaterType.EntirelyWater)
        {
            ChatPacket.SendServerMessage(
                session,
                $"Landblock 0x{aceParams[1].AsPosition.LandblockId.Landblock:X4} is entirely filled with water, and is impassable",
                ChatMessageType.Broadcast
            );
            return;
        }

        ChatPacket.SendServerMessage(
            session,
            $"Position: [Cell: 0x{aceParams[1].AsPosition.LandblockId.Landblock:X4} | Offset: {aceParams[1].AsPosition.PositionX}, "
                + $"{aceParams[1].AsPosition.PositionY}, {aceParams[1].AsPosition.PositionZ} | Facing: {aceParams[1].AsPosition.RotationX}, {aceParams[1].AsPosition.RotationY}, "
                + $"{aceParams[1].AsPosition.RotationZ}, {aceParams[1].AsPosition.RotationW}]",
            ChatMessageType.Broadcast
        );

        aceParams[0].AsPlayer.Teleport(aceParams[1].AsPosition);
    }
}
