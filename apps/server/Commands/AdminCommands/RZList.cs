using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

public class RZList
{
    [CommandHandler(
        "rzlist",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        0,
        "Lists resonance zones near your current location.",
        "rzlist [all|enabled|disabled] [range] [filter]"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        if (session == null || session.Player == null || session.Player.Location == null)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "No player location available.", ChatMessageType.Help);
            return;
        }

        // Defaults
        var scope = "enabled";   // enabled | disabled | all
        var range = 200f;
        var explicitRange = false;
        var ignoreRange = false;
        string filter = null;

        // Parse args (order-independent)
        var filterParts = new List<string>();

        foreach (var raw in parameters)
        {
            var token = raw == null ? null : raw.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
            {
                scope = "all";
                continue;
            }

            if (string.Equals(token, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                scope = "disabled";
                continue;
            }

            if (string.Equals(token, "enabled", StringComparison.OrdinalIgnoreCase))
            {
                scope = "enabled";
                continue;
            }

            float parsedRange;
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRange))
            {
                range = parsedRange;
                explicitRange = true;
                continue;
            }

            filterParts.Add(token);
        }

        if (filterParts.Count > 0)
        {
            filter = string.Join(" ", filterParts).ToLowerInvariant();
        }

        // If admin asked for all/disabled and did not specify a range, show everything
        if (!explicitRange && (scope == "all" || scope == "disabled"))
        {
            ignoreRange = true;
        }

        // Fetch rows based on scope
        IReadOnlyList<ACE.Database.Models.Shard.ResonanceZoneRow> rows;
        if (scope == "all")
        {
            rows = DatabaseManager.ShardConfig.GetResonanceZoneEntriesAll();
        }
        else if (scope == "disabled")
        {
            rows = DatabaseManager.ShardConfig.GetResonanceZoneEntriesDisabled();
        }
        else
        {
            rows = DatabaseManager.ShardConfig.GetResonanceZoneEntriesEnabled();
        }

        var playerLoc = session.Player.Location;

        var matches = rows
            .Select(r =>
            {
                var zonePos = new Position(
                    r.CellId,
                    r.X, r.Y, r.Z,
                    r.Qx, r.Qy, r.Qz, r.Qw);

                var dist = playerLoc.DistanceTo(zonePos);
                return new { Row = r, Dist = dist };
            })
            .Where(x =>
            {
                if (ignoreRange)
                {
                    return true;
                }

                return x.Dist <= range;
            })
            .Where(x =>
            {
                if (filter == null)
                {
                    return true;
                }

                var haystack =
                    (x.Row.Name + " " + x.Row.ShroudEventKey + " " + x.Row.StormEventKey)
                    .ToLowerInvariant();

                return haystack.Contains(filter);
            })
            .OrderBy(x => x.Dist)
            .ToList();

        if (matches.Count == 0)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "No zones found.", ChatMessageType.Broadcast);
            return;
        }

        // Column widths
        const int wId = 4;
        const int wEn = 2;
        const int wDist = 6;
        const int wName = 20;
        const int wEffects = 40;
        const int wArea = 10;

        static string Fit(string text, int width)
        {
            if (text == null)
            {
                text = "";
            }

            if (text.Length <= width)
            {
                return text.PadRight(width);
            }

            if (width <= 1)
            {
                return text.Substring(0, width);
            }

            return text.Substring(0, width - 1) + "…";
        }

        static string FitCenter(string text, int width)
        {
            if (text == null)
            {
                text = "";
            }

            if (text.Length >= width)
            {
                return text.Substring(0, width);
            }

            var totalPad = width - text.Length;
            var padLeft = totalPad / 2;
            var padRight = totalPad - padLeft;

            return new string(' ', padLeft) + text + new string(' ', padRight);
        }

        // Header
        CommandHandlerHelper.WriteOutputInfo(
            session,
            FitCenter("ID", wId) + "  " +
            FitCenter("En", wEn) + "  " +
            FitCenter("Dist", wDist) + "  " +
            Fit("Name", wName) + "  " +
            Fit("Effects", wEffects) + "  " +
            FitCenter("Area", wArea),
            ChatMessageType.Broadcast);

        CommandHandlerHelper.WriteOutputInfo(
            session,
            FitCenter("---", wId) + "  " +
            FitCenter("--", wEn) + "  " +
            FitCenter("------", wDist) + "  " +
            Fit("--------------------", wName) + "  " +
            Fit("----------------------------------------", wEffects) + "  " +
            FitCenter("----------", wArea),
            ChatMessageType.Broadcast);

        foreach (var m in matches)
        {
            var r = m.Row;
            var effects = new List<string>();

            if (!string.IsNullOrWhiteSpace(r.ShroudEventKey))
            {
                effects.Add("shroud(" + r.ShroudEventKey + ")");
            }

            if (!string.IsNullOrWhiteSpace(r.StormEventKey))
            {
                effects.Add("storm(" + r.StormEventKey + ")");
            }

            var effectsText = effects.Count > 0 ? string.Join(", ", effects) : "none";
            var areaText = r.Radius.ToString("0.#") + "/" + r.MaxDistance.ToString("0.#");
            var enabledText = r.IsEnabled ? "Y" : "N";

            CommandHandlerHelper.WriteOutputInfo(
                session,
                FitCenter(r.Id.ToString(), wId) + "  " +
                FitCenter(enabledText, wEn) + "  " +
                FitCenter(m.Dist.ToString("0.00"), wDist) + "  " +
                Fit(r.Name, wName) + "  " +
                Fit(effectsText, wEffects) + "  " +
                FitCenter(areaText, wArea),
                ChatMessageType.Broadcast);
        }

        CommandHandlerHelper.WriteOutputInfo(
            session,
            matches.Count + " zone(s) listed.",
            ChatMessageType.Broadcast);
    }
}
