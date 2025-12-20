using System;
using System.Globalization;
using System.Linq;
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
        "Lists enabled resonance zones near your current location.",
        "rzlist [float range]"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        if (session?.Player?.Location == null)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "No player location available.", ChatMessageType.Help);
            return;
        }

        var range = 200f;
        if (parameters.Length >= 1 &&
            !float.TryParse(parameters[0], NumberStyles.Float, CultureInfo.InvariantCulture, out range))
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Please input a valid range.", ChatMessageType.Help);
            return;
        }

        var playerLoc = session.Player.Location;
        var rows = DatabaseManager.ShardConfig.GetResonanceZoneEntriesEnabled();

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
            .Where(x => x.Dist <= range)
            .OrderBy(x => x.Dist)
            .ToList();

        if (matches.Count == 0)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "No zones found near you.", ChatMessageType.Broadcast);
            return;
        }

        // column widths
        const int wId = 4;
        const int wDist = 6;
        const int wName = 20;
        const int wEffects = 40;
        const int wArea = 10;

        static string Fit(string s, int width)
        {
            s ??= "";
            if (s.Length <= width) return s.PadRight(width);
            if (width <= 1) return s.Substring(0, width);
            return s.Substring(0, width - 1) + "…";
        }

        static string FitCenter(string s, int width)
        {
            s ??= "";
            if (s.Length >= width)
                return s.Substring(0, width);

            var totalPad = width - s.Length;
            var padLeft = totalPad / 2;
            var padRight = totalPad - padLeft;

            return new string(' ', padLeft) + s + new string(' ', padRight);
        }

        // header
        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"{FitCenter("ID", wId)}  {FitCenter("Dist", wDist)}  {Fit("Name", wName)}  {Fit("Effects", wEffects)}  {FitCenter("Area", wArea)}",
            ChatMessageType.Broadcast);

        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"{FitCenter("---", wId)}  {FitCenter("------", wDist)}  {Fit("--------------------", wName)}  {Fit("----------------------------------------", wEffects)}  {FitCenter("----------", wArea)}",
            ChatMessageType.Broadcast);

        foreach (var m in matches)
        {
            var r = m.Row;
            var dist = m.Dist;

            var effects = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(r.ShroudEventKey))
                effects.Add($"shroud({r.ShroudEventKey})");
            if (!string.IsNullOrWhiteSpace(r.StormEventKey))
                effects.Add($"storm({r.StormEventKey})");

            var effectsText = effects.Count > 0 ? string.Join(", ", effects) : "none";
            var areaText = $"{r.Radius:0.#}/{r.MaxDistance:0.#}";

            CommandHandlerHelper.WriteOutputInfo(
                session,
                $"{FitCenter(r.Id.ToString(), wId)}  {FitCenter(dist.ToString("0.00"), wDist)}  {Fit(r.Name, wName)}  {Fit(effectsText, wEffects)}  {FitCenter(areaText, wArea)}",
                ChatMessageType.Broadcast);
        }

        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"{matches.Count} zone(s) listed.",
            ChatMessageType.Broadcast);
    }
}
