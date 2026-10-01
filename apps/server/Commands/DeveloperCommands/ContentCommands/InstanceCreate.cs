using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Factories;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using static ACE.Server.Commands.DeveloperCommands.ContentCommands.ContentCommandUtilities;

namespace ACE.Server.Commands.DeveloperCommands.ContentCommands;

public class InstanceCreate
{
    [CommandHandler(
        "createinst",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Spawns a new wcid or classname as a landblock instance",
        "<wcid or classname>\n\nTo create a parent/child relationship: /createinst -p <parent guid> -c <wcid or classname>\nTo automatically get the parent guid from the last appraised object: /createinst -p -c <wcid or classname>\n\nTo manually specify a start guid: /createinst <wcid or classname> <start guid>\nStart guids can be in the range 0x000-0xFFF, or they can be prefixed with 0x7<landblock id>"
    )]
    public static void HandleCreateInst(Session session, params string[] parameters)
    {
        if (RefusedInAnInstance(session))
        {
            return;
        }

        var loc = new Position(session.Player.Location);

        var param = parameters[0];

        Weenie weenie = null;

        uint? parentGuid = null;

        var landblock = session.Player.CurrentLandblock.Id.Landblock;

        var firstStaticGuid = 0x70000000 | (uint)landblock << 12;

        if (parameters.Length > 1)
        {
            var allParams = string.Join(" ", parameters);

            var match = Regex.Match(allParams, @"-p ([\S]+) -c ([\S]+)", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var parentGuidStr = match.Groups[1].Value;
                param = match.Groups[2].Value;

                if (parentGuidStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    parentGuidStr = parentGuidStr.Substring(2);
                }

                if (
                    !uint.TryParse(
                        parentGuidStr,
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var _parentGuid
                    )
                )
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Couldn't parse parent guid {match.Groups[1].Value}",
                            ChatMessageType.Broadcast
                        )
                    );
                    return;
                }

                parentGuid = _parentGuid;

                if (parentGuid <= 0xFFF)
                {
                    parentGuid = firstStaticGuid | parentGuid;
                }
            }
            else if (parameters[1].StartsWith("-c", StringComparison.OrdinalIgnoreCase))
            {
                // get parent from last appraised object
                var parent = CommandHandlerHelper.GetLastAppraisedObject(session);

                if (parent == null)
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat($"Couldn't find parent object", ChatMessageType.Broadcast)
                    );
                    return;
                }

                parentGuid = parent.Guid.Full;
            }
        }

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

        // clear any cached instances for this landblock
        DatabaseManager.World.ClearCachedInstancesByLandblock(landblock);

        var instances = DatabaseManager.World.GetCachedInstancesByLandblock(landblock);

        // for link mode, ensure parent guid instance exists
        WorldObject parentObj = null;
        LandblockInstance parentInstance = null;

        if (parentGuid != null)
        {
            parentInstance = instances.FirstOrDefault(i => i.Guid == parentGuid);

            if (parentInstance == null)
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Couldn't find landblock instance for parent guid 0x{parentGuid:X8}",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            parentObj = session.Player.CurrentLandblock.GetObject(parentGuid.Value);

            if (parentObj == null)
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Couldn't find parent object 0x{parentGuid:X8}",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }
        }

        var nextStaticGuid = GetNextStaticGuid(landblock, instances);

        var maxStaticGuid = firstStaticGuid | 0xFFF;

        // manually specify a start guid?
        if (parameters.Length == 2)
        {
            if (
                uint.TryParse(
                    parameters[1].Replace("0x", ""),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var startGuid
                )
            )
            {
                if (startGuid <= 0xFFF)
                {
                    startGuid = firstStaticGuid | startGuid;
                }

                if (startGuid < firstStaticGuid || startGuid > maxStaticGuid)
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Landblock instance guid {startGuid:X8} must be between {firstStaticGuid:X8} and {maxStaticGuid:X8}",
                            ChatMessageType.Broadcast
                        )
                    );
                    return;
                }

                var existing = instances.FirstOrDefault(i => i.Guid == startGuid);

                if (existing != null)
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Landblock instance guid {startGuid:X8} already exists",
                            ChatMessageType.Broadcast
                        )
                    );
                    return;
                }
                nextStaticGuid = startGuid;
            }
        }

        if (nextStaticGuid > maxStaticGuid)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Landblock {landblock:X4} has reached the maximum # of static guids",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        // create and spawn object
        var entityWeenie = Database.Adapter.WeenieConverter.ConvertToEntityWeenie(weenie);

        var wo = WorldObjectFactory.CreateWorldObject(entityWeenie, new ObjectGuid(nextStaticGuid));

        if (wo == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Failed to create new object for {weenie.ClassId} - {weenie.ClassName}",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        var isLinkChild = parentInstance != null;

        if (!wo.Stuck && !isLinkChild)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{weenie.ClassId} - {weenie.ClassName} is missing PropertyBool.Stuck, cannot spawn as landblock instance unless it is a child object",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        // spawn as ethereal temporarily, to spawn directly on player position
        wo.Ethereal = true;
        wo.Location = new Position(loc);

        // even on flat ground, objects can sometimes fail to spawn at the player's current Z
        // Position.Z has some weird thresholds when moving around, but i guess the same logic doesn't apply when trying to spawn in...
        wo.Location.PositionZ += 0.05f;

        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Creating new landblock instance {(isLinkChild ? "child object " : "")}@ {loc.ToLOCString()}\n{wo.WeenieClassId} - {wo.Name} ({nextStaticGuid:X8})",
                ChatMessageType.Broadcast
            )
        );

        if (!wo.EnterWorld())
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat("Failed to spawn new object at this location", ChatMessageType.Broadcast)
            );
            return;
        }

        // create new landblock instance
        var instance = CreateLandblockInstance(wo, isLinkChild);

        instances.Add(instance);

        if (isLinkChild)
        {
            var link = new LandblockInstanceLink();

            link.ParentGuid = parentGuid.Value;
            link.ChildGuid = wo.Guid.Full;
            link.LastModified = DateTime.Now;

            parentInstance.LandblockInstanceLink.Add(link);

            parentObj.LinkedInstances.Add(instance);

            // ActivateLinks?
            parentObj.SetLinkProperties(wo);
            parentObj.ChildLinks.Add(wo);
            wo.ParentLink = parentObj;
        }

        SyncInstances(session, landblock, instances);
    }
}
