using System.Globalization;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using DbResonanceZoneEntry = ACE.Database.Models.Shard.ResonanceZoneRow;
using System.Collections.Generic;
using System;

namespace ACE.Server.Commands.AdminCommands;

public class StormAdd
{
    private static Dictionary<string, string> ParseNamedArgs(string[] parameters)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in parameters)
        {
            var idx = p.IndexOf('=');
            if (idx <= 0 || idx == p.Length - 1)
            {
                continue;
            }

            var key = p.Substring(0, idx).Trim();
            var value = p.Substring(idx + 1).Trim();

            dict[key] = value;
        }

        return dict;
    }
    private const float MatchTolerance = 0.25f;

    [CommandHandler(
        "stormadd",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        4,
        "Adds/updates a Portal Storm zone at your current location (updates an existing row if present).",
        "stormadd (float radius) (float maxDistance) (string zoneName) (string stormEventKey)"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        if (session?.Player?.Location == null)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "No player location available.", ChatMessageType.Help);
            return;
        }

        var args = ParseNamedArgs(parameters);

        if (!args.TryGetValue("radius", out var radiusStr) ||
            !args.TryGetValue("max", out var maxStr) ||
            !args.TryGetValue("name", out var zoneName) ||
            !args.TryGetValue("event", out var stormKey))
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                "Usage: /stormadd name=<name> radius=<float> max=<float> event=<stormEventKey>",
                ChatMessageType.Help);
            return;
        }

        if (!float.TryParse(radiusStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) ||
            !float.TryParse(maxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxDistance))
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Invalid radius or max value.", ChatMessageType.Help);
            return;
        }

        var loc = session.Player.Location;

        var cellId = loc.LandblockId.Raw;
        var x = loc.PositionX;
        var y = loc.PositionY;
        var z = loc.PositionZ;

        var existing = DatabaseManager.ShardConfig.FindResonanceZoneNear(cellId, x, y, z, MatchTolerance);

        if (string.IsNullOrWhiteSpace(stormKey)) { /* reject */ }
        
        if (existing != null)
        {
            var ok = DatabaseManager.ShardConfig.UpdateResonanceZoneEntry(
                id: existing.Id,
                name: zoneName,
                radius: radius,
                maxDistance: maxDistance,
                shroudEventKey: null,
                stormEventKey: stormKey,
                isEnabled: null
            );

            CommandHandlerHelper.WriteOutputInfo(
                session,
                ok
                    ? $"Updated storm on zone ID={existing.Id} name='{zoneName}' storm='{stormKey}'."
                    : $"Failed to update zone ID={existing.Id}.",
                ChatMessageType.Broadcast
            );
            return;
        }

        var row = new DbResonanceZoneEntry
        {
            IsEnabled = true,
            CellId = cellId,

            X = x,
            Y = y,
            Z = z,

            Qx = loc.RotationX,
            Qy = loc.RotationY,
            Qz = loc.RotationZ,
            Qw = loc.RotationW,

            Radius = radius,
            MaxDistance = maxDistance,

            Name = zoneName,
            ShroudEventKey = "",
            StormEventKey = stormKey
        };

        var id = DatabaseManager.ShardConfig.InsertResonanceZoneEntry(row);

        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"Inserted storm zone ID={id} name='{zoneName}' storm='{stormKey}'.",
            ChatMessageType.Broadcast
        );
    }
}
