using System;
using System.Globalization;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Database;


namespace ACE.Server.Commands.AdminCommands;

public class TeletoRZ
{
    // TeletoRZ <id> [tries]
    [CommandHandler(
        "teletorz",
        AccessLevel.Admin,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Teleport to a resonance zone by id (grounded, with retry around center).",
        "TeletoRZ <id> [tries]"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        var player = session.Player;
        if (player == null)
        {
            return;
        }

        if (parameters.Length < 1 ||
            !uint.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Usage: TeletoRZ <id> [tries]", ChatMessageType.Help);
            return;
        }

        var tries = 16;
        if (parameters.Length >= 2 &&
            int.TryParse(parameters[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTries))
        {
            tries = Math.Clamp(parsedTries, 1, 64);
        }

        var zone = DatabaseManager.ShardConfig.GetResonanceZoneEntryById((int)id);

        if (zone == null)
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                $"Zone id {id} not found.",
                ChatMessageType.Help
            );
            return;
        }


        // Build zone center Position
        var zonePos = new ACE.Entity.Position(
            zone.CellId,
            zone.X, zone.Y, zone.Z,
            zone.Qx, zone.Qy, zone.Qz, zone.Qw
        );

        // Convert zone center (landblock-local) -> world XY
        const float blockLen = 192f;

        var lbRaw = zonePos.LandblockId.Raw;
        var lbx = (int)(lbRaw >> 24);
        var lby = (int)((lbRaw >> 16) & 0xFF);

        var baseWorldX = (lbx * blockLen) + zonePos.PositionX;
        var baseWorldY = (lby * blockLen) + zonePos.PositionY;


        // Retry pattern: center first, then random points on expanding rings
        const float step = 1.25f;
        const float maxRadius = 20f;

        var rng = new Random(unchecked((int)DateTime.UtcNow.Ticks));
        var clearance = (float)PropertyManager.GetDouble("rz_teleport_clearance", 0.5).Item;
        var maxfall = (float)PropertyManager.GetDouble("rz_maxfall", 2).Item;

        ACE.Entity.Position grounded = null;
        var groundZ = 0f;

        for (var attempt = 0; attempt < tries; attempt++)
        {
           const float minRadius = 2.0f; // avoid exact center (center can be inside objects)

            var radius = Math.Min(maxRadius, minRadius + (attempt * step));
            var angle = rng.NextDouble() * Math.PI * 2;
            var worldX = baseWorldX + (float)(Math.Cos(angle) * radius);
            var worldY = baseWorldY + (float)(Math.Sin(angle) * radius);

           if (!WorldManager.TryBuildGroundedPosition(worldX, worldY, clearance, out var pos, out var gz))
            {
                continue;
            }

            // Match the real teleport logic: stand above ground by a safe offset
            const float minForcedFall = 0.5f;
            var zOffset = Math.Max(clearance, Math.Max(maxfall, minForcedFall));

            pos.PositionZ = gz + zOffset;

            grounded = pos;
            groundZ = gz;
            break;

        }

        if (grounded == null)
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                $"Failed to find a grounded landing spot near zone id {id} after {tries} tries.",
                ChatMessageType.Help
            );
            return;
        }

        var dest = new ACE.Entity.Position(
            grounded.LandblockId.Raw,
            grounded.PositionX,
            grounded.PositionY,
            grounded.PositionZ,
            player.Location.RotationX,
            player.Location.RotationY,
            player.Location.RotationZ,
            player.Location.RotationW
        );

        WorldManager.ThreadSafeTeleport(player, dest);

        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"Teleported to zone {zone.Name ?? "(unnamed)"} (id={zone.Id}) on LB 0x{dest.LandblockId.Raw:X8} (groundZ={groundZ:0.00}).",
            ChatMessageType.System
        );
    }
}
