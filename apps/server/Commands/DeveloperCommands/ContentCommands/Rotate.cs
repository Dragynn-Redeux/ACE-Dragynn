using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ACE.Common.Extensions;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics.Extensions;
using ACE.Server.WorldObjects;
using static ACE.Server.Commands.DeveloperCommands.ContentCommands.ContentCommandUtilities;

namespace ACE.Server.Commands.DeveloperCommands.ContentCommands;

public class Rotate
{
    [CommandHandler(
        "rotate",
        AccessLevel.Developer,
        CommandHandlerFlag.None,
        1,
        "Adjusts the rotation of a landblock instance",
        "<dir>\nDirections: north, south, west, east, northwest, northeast, southwest, southeast, n, s, w, e, nw, ne, sw, se, -or-\n0-360, with 0 being north, and 90 being west"
    )]
    public static void HandleRotate(Session session, params string[] parameters)
    {
        WorldObject obj = null;

        var curParam = 0;

        if (parameters.Length == 2)
        {
            if (
                !uint.TryParse(
                    parameters[curParam++].TrimStart("0x"),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var guid
                )
            )
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Invalid guid: {parameters[0]}", ChatMessageType.Broadcast)
                );
                return;
            }

            obj = session.Player.FindObject(guid, Player.SearchLocations.Landblock);

            if (obj == null)
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Couldn't find {parameters[0]}", ChatMessageType.Broadcast)
                );
                return;
            }
        }
        else
        {
            obj = CommandHandlerHelper.GetLastAppraisedObject(session);
        }

        if (obj == null)
        {
            return;
        }

        // ensure landblock instance (a static object in an instance has a guid of its own, and isn't a row in the world db)
        if (!obj.Guid.IsStatic() || obj.Guid.IsEphemeralStatic())
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{obj.Name} ({obj.Guid}) is not landblock instance",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        if (obj.PhysicsObj == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat($"{obj.Name} ({obj.Guid}) is not a physics object", ChatMessageType.Broadcast)
            );
            return;
        }

        // get direction
        var dirname = parameters[curParam++].ToLower();
        var dir = GetNudgeDir(dirname);

        var curRotate = false;

        if (dir == null)
        {
            if (float.TryParse(dirname, out var degrees))
            {
                var rads = degrees.ToRadians();
                var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rads);
                dir = Vector3.Transform(Vector3.UnitY, q);
            }
            else if (dirname.Equals("here") || dirname.Equals("me"))
            {
                dir = Vector3.Zero;
                curRotate = true;
            }
            else
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Invalid direction: {dirname}", ChatMessageType.Broadcast)
                );
                return;
            }
        }

        // get quaternion
        var newRotation = Quaternion.Identity;

        if (curRotate)
        {
            newRotation = session.Player.Location.Rotation;
        }
        else
        {
            var angle = Math.Atan2(-dir.Value.X, dir.Value.Y);
            newRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)angle);
        }

        newRotation = Quaternion.Normalize(newRotation);

        // get landblock for static guid
        var landblock_id = (ushort)(obj.Guid.Full >> 12);

        // get instances for landblock
        var instances = DatabaseManager.World.GetCachedInstancesByLandblock(landblock_id);

        // find instance
        var instance = instances.FirstOrDefault(i => i.Guid == obj.Guid.Full);

        if (instance == null)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Couldn't find instance for {obj.Name} ({obj.Guid})",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        session.Network.EnqueueSend(
            new GameMessageSystemChat($"{obj.Name} ({obj.Guid}) new rotation: {newRotation}", ChatMessageType.Broadcast)
        );

        // update physics / ace rotation
        obj.PhysicsObj.Position.Frame.Orientation = newRotation;
        obj.Location.Rotation = newRotation;

        // update instance
        instance.AnglesW = newRotation.W;
        instance.AnglesX = newRotation.X;
        instance.AnglesY = newRotation.Y;
        instance.AnglesZ = newRotation.Z;

        SyncInstances(session, landblock_id, instances);

        // broadcast new rotation
        obj.SendUpdatePosition(true);
    }
}
