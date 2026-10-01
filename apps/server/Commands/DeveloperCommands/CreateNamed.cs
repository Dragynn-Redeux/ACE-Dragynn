using System;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Commands.DeveloperCommands;

public class CreateNamed
{
    /// <summary>
    /// Creates a named object in the world
    /// </summary>
    [CommandHandler(
        "createnamed",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        3,
        "Creates a named object in the world.",
        "<wcid or classname> <count> <name>"
    )]
    public static void HandleCreateNamed(Session session, params string[] parameters)
    {
        var weenie = GetWeenieForCreate(session, parameters[0]);

        if (weenie == null)
        {
            return;
        }

        if (!int.TryParse(parameters[1], out var count))
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"count must be an integer value", ChatMessageType.Broadcast)
            );
            return;
        }

        if (count < 1 || count > ushort.MaxValue)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"count must be a between 1 and {ushort.MaxValue}", ChatMessageType.Broadcast)
            );
            return;
        }

        var named = string.Join(" ", parameters, 2, parameters.Length - 2);

        WorldObject first = null;

        for (var i = 0; i < count; i++)
        {
            var obj = CreateObjectForCommand(session, weenie);

            if (obj == null)
            {
                return;
            }

            if (first == null)
            {
                first = obj;
            }

            obj.Name = named;

            obj.EnterWorld();
        }

        if (count == 1)
        {
            PlayerManager.BroadcastToAuditChannel(
                session.Player,
                $"{session.Player.Name} has created {first.Name} (0x{first.Guid:X8}) at {first.Location.ToLOCString()}."
            );
        }
        else
        {
            PlayerManager.BroadcastToAuditChannel(
                session.Player,
                $"{session.Player.Name} has created {count}x {first.Name} at {first.Location.ToLOCString()}."
            );
        }
    }

    /// <summary>
    /// Returns a weenie for a wcid or classname for /create, /createliveops, and /ci
    /// Performs some basic verifications for weenie types that are safe to spawn with these commands
    /// </summary>
    public static Weenie GetWeenieForCreate(Session session, string weenieDesc, bool forInventory = false)
    {
        Weenie weenie = null;

        if (uint.TryParse(weenieDesc, out var wcid))
        {
            weenie = DatabaseManager.World.GetCachedWeenie(wcid);
        }
        else
        {
            weenie = DatabaseManager.World.GetCachedWeenie(weenieDesc);
        }

        if (weenie == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"{weenieDesc} is not a valid weenie.", ChatMessageType.Broadcast)
            );
            return null;
        }

        if (!VerifyCreateWeenieType(weenie.WeenieType))
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You cannot spawn {weenie.ClassName} because it is a {weenie.WeenieType}",
                    ChatMessageType.Broadcast
                )
            );
            return null;
        }

        if (forInventory && weenie.IsStuck())
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You cannot spawn {weenie.ClassName} in your inventory because it cannot be picked up",
                    ChatMessageType.Broadcast
                )
            );
            return null;
        }

        return weenie;
    }

    public static bool VerifyCreateWeenieType(WeenieType weenieType)
    {
        switch (weenieType)
        {
            case WeenieType.Admin:
            case WeenieType.AI:
            case WeenieType.Allegiance:
            case WeenieType.BootSpot:
            case WeenieType.Channel:
            case WeenieType.CombatPet:
            case WeenieType.Deed:
            case WeenieType.Entity:
            case WeenieType.EventCoordinator:
            case WeenieType.Game:
            case WeenieType.GamePiece:
            case WeenieType.GScoreGatherer:
            case WeenieType.GScoreKeeper:
            case WeenieType.GSpellEconomy:
            case WeenieType.Hook:
            case WeenieType.House:
            case WeenieType.HousePortal:
            case WeenieType.HUD:
            case WeenieType.InGameStatKeeper:
            case WeenieType.LScoreKeeper:
            case WeenieType.LSpellEconomy:
            case WeenieType.Machine:
            case WeenieType.Pet:
            case WeenieType.ProjectileSpell:
            case WeenieType.Sentinel:
            case WeenieType.SlumLord:
            case WeenieType.SocialManager:
            case WeenieType.Storage:
            case WeenieType.Undef:
            case WeenieType.UNKNOWN__GUESSEDNAME32:

                return false;
        }
        return true;
    }

    public static Position LastSpawnPos;

    /// <summary>
    /// Creates WorldObjects from Weenies for /create, /createliveops, and /ci
    /// </summary>
    private static WorldObject CreateObjectForCommand(Session session, Weenie weenie)
    {
        var obj = WorldObjectFactory.CreateNewWorldObject(weenie);

        //if (obj.TimeToRot == null)
        //obj.TimeToRot = double.MaxValue;

        if (obj.WeenieType == WeenieType.Creature)
        {
            obj.Location = session.Player.Location.InFrontOf(5f, true);
        }
        else
        {
            var dist = Math.Max(2, obj.UseRadius ?? 2);

            obj.Location = session.Player.Location.InFrontOf(dist);
        }

        obj.InstanceId = session.Player.InstanceId; // it appears where the admin is, which can be an instance
        obj.Location.LandblockId = new LandblockId(obj.Location.GetCell(obj.InstanceId));

        LastSpawnPos = obj.Location;

        return obj;
    }
}
