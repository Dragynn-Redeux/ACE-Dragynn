using System;
using ACE.Database.Models.World;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Factories;
using ACE.Server.Factories.Enum;
using ACE.Server.Network;
using Serilog;

namespace ACE.Server.Commands.DeveloperCommands;

public class CreateInventoryLoot
{
    private static readonly ILogger _log = Log.ForContext(typeof(CreateInventoryLoot));

    [CommandHandler(
        "ciloot",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Generates randomized loot in player's inventory",
        "<tier> optional: <# items>"
    )]
    public static void HandleCILoot(Session session, params string[] parameters)
    {
        var tier = 1;
        int.TryParse(parameters[0], out tier);
        tier = Math.Clamp(tier, 1, 8);

        var numItems = 1;
        if (parameters.Length > 1)
        {
            int.TryParse(parameters[1], out numItems);
        }

        // Create a dummy treasure profile for passing in tier value
        var profile = new TreasureDeath
        {
            Tier = tier,
            LootQualityMod = 0,
            MagicItemTreasureTypeSelectionChances = 9, // 8 or 9?
        };

        for (var i = 0; i < numItems; i++)
        {
            //var wo = LootGenerationFactory.CreateRandomLootObjects(profile, true);
            var wo = LootGenerationFactory.CreateRandomLootObjects_New(profile, TreasureItemCategory.MagicItem, null);
            if (wo != null)
            {
                session.Player.TryCreateInInventoryWithNetworking(wo);
            }
            else
            {
                _log.Error(
                    "{Player}.HandleCILoot: LootGenerationFactory.CreateRandomLootObjects({LootTier}) returned null",
                    session.Player.Name,
                    tier
                );
            }
        }
    }
}
