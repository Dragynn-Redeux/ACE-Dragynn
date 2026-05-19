using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Factories;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Commands.DeveloperCommands;

public class CreateInventoryRandomObjects
{
    [CommandHandler(
        "cirand",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Creates random objects in your inventory.",
        "type (string or number) <num to create> defaults to 10 if omitted, max 50"
    )]
    public static void HandleCIRandom(Session session, params string[] parameters)
    {
        if (
            !Enum.TryParse(parameters[0], true, out WeenieType weenieType)
            || !Enum.IsDefined(typeof(WeenieType), weenieType)
        )
        {
            ChatPacket.SendServerMessage(
                session,
                $"{parameters[0]} is not a valid WeenieType",
                ChatMessageType.Broadcast
            );
            return;
        }

        if (!CreateNamed.VerifyCreateWeenieType(weenieType))
        {
            ChatPacket.SendServerMessage(
                session,
                $"{weenieType} is not a valid WeenieType for create commands",
                ChatMessageType.Broadcast
            );
            return;
        }

        var numItems = 10;

        if (parameters.Length > 1)
        {
            if (!int.TryParse(parameters[1], out numItems) || numItems < 1 || numItems > 50)
            {
                ChatPacket.SendServerMessage(
                    session,
                    $"<num to create> must be a number between 1 - 50",
                    ChatMessageType.Broadcast
                );
                return;
            }
        }

        var items = LootGenerationFactory.CreateRandomObjectsOfType(weenieType, numItems, null);

        var stuck = new List<WorldObject>();

        foreach (var item in items)
        {
            if (!item.Stuck)
            {
                session.Player.TryCreateInInventoryWithNetworking(item);
            }
            else
            {
                stuck.Add(item);
            }
        }

        if (stuck.Count != 0)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You cannot spawn {string.Join(", ", stuck.Select(i => i.WeenieClassName))} in your inventory because it cannot be picked up",
                    ChatMessageType.Broadcast
                )
            );
        }
    }
}
