using System.Globalization;
using System.Linq;
using System.Numerics;
using ACE.Common.Extensions;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using static ACE.Server.Commands.DeveloperCommands.ContentCommands.ContentCommandUtilities;

namespace ACE.Server.Commands.DeveloperCommands.ContentCommands;

public class Nudge
{
    [CommandHandler(
        "nudge",
        AccessLevel.Developer,
        CommandHandlerFlag.None,
        1,
        "Adjusts the spawn position of a landblock instance",
        "<dir> <amount>\nDirections: x, y, z, north, south, west, east, northwest, northeast, southwest, southeast, n, s, w, e, nw, ne, sw, se, up, down, here"
    )]
    public static void HandleNudge(Session session, params string[] parameters)
    {
        WorldObject obj = null;

        var curParam = 0;

        if (parameters.Length == 3)
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

        var curPos = false;

        if (dir == null)
        {
            if (dirname.Equals("here") || dirname.Equals("to me"))
            {
                dir = Vector3.Zero;
                curPos = true;
            }
            else
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Invalid direction: {dirname}", ChatMessageType.Broadcast)
                );
                return;
            }
        }

        // get distance / amount
        var amount = 1.0f;
        if (curParam < parameters.Length)
        {
            if (!float.TryParse(parameters[curParam++], out amount))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Invalid amount: {amount}", ChatMessageType.Broadcast)
                );
                return;
            }
        }

        var nudge = dir * amount;

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

        if (curPos)
        {
            // ensure same landblock
            if ((instance.ObjCellId >> 16) != (session.Player.Location.Cell >> 16))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Failed to move {obj.Name} ({obj.Guid}) to current location -- different landblock",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            obj.Ethereal = true;
            obj.EnqueueBroadcastPhysicsState();

            var newLoc = new Position(session.Player.Location);

            // slide?
            var setPos = new Physics.Common.SetPosition(
                newLoc.PhysPosition(),
                Physics.Common.SetPositionFlags.Teleport /* | Physics.Common.SetPositionFlags.Slide */
            );
            var result = obj.PhysicsObj.SetPosition(setPos);

            if (result != Physics.Common.SetPositionError.OK)
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Failed to move {obj.Name} ({obj.Guid}) to current location: {result}",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            instance.AnglesX = obj.Location.RotationX;
            instance.AnglesY = obj.Location.RotationY;
            instance.AnglesZ = obj.Location.RotationZ;
            instance.AnglesW = obj.Location.RotationW;
        }
        else
        {
            // compare current position with home position
            // the nudge should be performed as an offset from home position
            if (
                instance.OriginX != obj.Location.PositionX
                || instance.OriginY != obj.Location.PositionY
                || instance.OriginZ != obj.Location.PositionZ
            )
            {
                //session.Network.EnqueueSend(new GameMessageSystemChat($"Moving {obj.Name} ({obj.Guid}) to home position: {obj.Location} to {instance.ObjCellId:X8} [{instance.OriginX} {instance.OriginY} {instance.OriginZ}]", ChatMessageType.Broadcast));

                var homePos = new Position(
                    instance.ObjCellId,
                    instance.OriginX,
                    instance.OriginY,
                    instance.OriginZ,
                    instance.AnglesX,
                    instance.AnglesY,
                    instance.AnglesZ,
                    instance.AnglesW
                );

                // slide?
                var setPos = new Physics.Common.SetPosition(
                    homePos.PhysPosition(),
                    Physics.Common.SetPositionFlags.Teleport /* | Physics.Common.SetPositionFlags.Slide*/
                );
                var result = obj.PhysicsObj.SetPosition(setPos);

                if (result != Physics.Common.SetPositionError.OK)
                {
                    session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Failed to move {obj.Name} ({obj.Guid}) to home position {homePos.ToLOCString()}",
                            ChatMessageType.Broadcast
                        )
                    );
                    return;
                }
            }

            // perform physics transition
            var newPos = new Physics.Common.Position(obj.PhysicsObj.Position);
            newPos.add_offset(nudge.Value);

            var transit = obj.PhysicsObj.transition(obj.PhysicsObj.Position, newPos, true);

            var errorMsg =
                $"{obj.Name} ({obj.Guid}) failed to move from {obj.PhysicsObj.Position.ACEPosition()} to {newPos.ACEPosition()}";

            if (transit == null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(errorMsg, ChatMessageType.Broadcast));
                return;
            }

            // ensure same landblock
            if ((transit.SpherePath.CurPos.ObjCellID >> 16) != (obj.PhysicsObj.Position.ObjCellID >> 16))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat($"{errorMsg} - cannot change landblock", ChatMessageType.Broadcast)
                );
                return;
            }

            obj.PhysicsObj.SetPositionInternal(transit);
        }

        // update ace location
        var prevLoc = new Position(obj.Location);
        obj.Location = obj.PhysicsObj.Position.ACEPosition();

        if (prevLoc.Landblock != obj.Location.Landblock)
        {
            LandblockManager.RelocateObjectForPhysics(obj, true);
        }

        // broadcast new position
        obj.SendUpdatePosition(true);

        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"{obj.Name} ({obj.Guid}) - moved from {prevLoc} to {obj.Location}",
                ChatMessageType.Broadcast
            )
        );

        // update sql
        instance.ObjCellId = obj.Location.Cell;
        instance.OriginX = obj.Location.PositionX;
        instance.OriginY = obj.Location.PositionY;
        instance.OriginZ = obj.Location.PositionZ;

        SyncInstances(session, landblock_id, instances);
    }
}
