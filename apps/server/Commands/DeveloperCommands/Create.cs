using System;
using System.Collections.Generic;
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

public class Create
{
    /// <summary>
    /// Creates an object or objects in the world
    /// </summary>
    [CommandHandler(
        "create",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Creates an object or objects in the world.",
        "<wcid or classname> (amount) (palette) (shade)\n"
        + "This will attempt to spawn the weenie you specify. If you include an amount to spawn, it will attempt to create that many of the object.\n"
        + "Stackable items will spawn in stacks of their max stack size. All spawns will be limited by the physics engine placement, which may prevent the number you specify from actually spawning."
        + "Be careful with large numbers, especially with ethereal weenies."
    )]
    public static void HandleCreate(Session session, params string[] parameters)
    {
        if (
            ParseCreateParameters(
                session,
                parameters,
                false,
                out var weenie,
                out var numToSpawn,
                out var palette,
                out var shade,
                out _
            )
        )
        {
            TryCreateObject(session, weenie, numToSpawn, palette, shade);
        }
    }

    private static bool ParseCreateParameters(
        Session session,
        string[] parameters,
        bool hasLifespan,
        out Weenie weenie,
        out int numToSpawn,
        out int? palette,
        out float? shade,
        out int? lifespan
    )
    {
        weenie = GetWeenieForCreate(session, parameters[0]);
        numToSpawn = 1;
        palette = null;
        shade = null;
        lifespan = null;

        if (weenie == null)
        {
            return false;
        }

        if (parameters.Length > 1)
        {
            if (!int.TryParse(parameters[1], out numToSpawn))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Amount to spawn must be a number between {int.MinValue} - {int.MaxValue}.",
                        ChatMessageType.Broadcast
                    )
                );
                return false;
            }
        }

        var idx = 2;

        if (hasLifespan)
        {
            if (parameters.Length > 2)
            {
                if (!int.TryParse(parameters[2], out var _lifespan))
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Lifespan must be a number between {int.MinValue} - {int.MaxValue}.",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                else
                {
                    lifespan = _lifespan;
                }
            }
            else
            {
                lifespan = 3600;
            }

            idx++;
        }

        if (parameters.Length > idx)
        {
            if (!int.TryParse(parameters[idx], out var _palette))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Palette must be number between {int.MinValue} - {int.MaxValue}.",
                        ChatMessageType.Broadcast
                    )
                );
                return false;
            }
            else
            {
                palette = _palette;
            }

            idx++;
        }

        if (parameters.Length > idx)
        {
            if (!float.TryParse(parameters[idx], out var _shade))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Shade must be number between {float.MinValue} - {float.MaxValue}.",
                        ChatMessageType.Broadcast
                    )
                );
                return false;
            }
            else
            {
                shade = _shade;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a weenie for a wcid or classname for /create, /createliveops, and /ci
    /// Performs some basic verifications for weenie types that are safe to spawn with these commands
    /// </summary>
    private static Weenie GetWeenieForCreate(Session session, string weenieDesc, bool forInventory = false)
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

    private static bool VerifyCreateWeenieType(WeenieType weenieType)
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

    /// <summary>
    /// Attempts to spawn some # of weenies in the world for /create or /createliveops
    /// </summary>
    private static void TryCreateObject(
        Session session,
        Weenie weenie,
        int numToSpawn,
        int? palette = null,
        float? shade = null,
        int? lifespan = null
    )
    {
        var obj = CreateObjectForCommand(session, weenie);

        if (obj == null || numToSpawn < 1)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"No object was created.", ChatMessageType.Broadcast)
            );
            return;
        }

        var objs = new List<WorldObject>();

        if (numToSpawn == 1)
        {
            objs.Add(obj);
        }
        else
        {
            if (weenie.IsStackable() && obj.MaxStackSize != null)
            {
                var fullStacks = numToSpawn / (int)obj.MaxStackSize;
                var lastStackAmount = numToSpawn % (int)obj.MaxStackSize;

                for (var i = 0; i < fullStacks; i++)
                {
                    var stack = CreateObjectForCommand(session, weenie);
                    stack.SetStackSize(obj.MaxStackSize);
                    objs.Add(stack);
                }
                if (lastStackAmount > 0)
                {
                    obj.SetStackSize(lastStackAmount);
                    objs.Add(obj);
                }
            }
            else
            {
                // The number of weenies to spawn will be limited by the physics engine.
                for (var i = 0; i < numToSpawn; i++)
                {
                    objs.Add(CreateObjectForCommand(session, weenie));
                }
            }
        }

        foreach (var w in objs)
        {
            if (palette != null)
            {
                w.PaletteTemplate = palette;
            }

            if (shade != null)
            {
                w.Shade = shade;
            }

            if (lifespan != null)
            {
                w.Lifespan = lifespan;
            }

            w.EnterWorld();
        }

        if (numToSpawn > 1)
        {
            PlayerManager.BroadcastToAuditChannel(
                session.Player,
                $"{session.Player.Name} has created {numToSpawn} {obj.Name} (0x{obj.Guid:X8}) near {obj.Location.ToLOCString()}."
            );
        }
        else
        {
            PlayerManager.BroadcastToAuditChannel(
                session.Player,
                $"{session.Player.Name} has created {obj.Name} (0x{obj.Guid:X8}) at {obj.Location.ToLOCString()}."
            );
        }
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
