using System;
using System.Collections.Generic;
using System.Globalization;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;

namespace ACE.Server.Commands.AdminCommands;

public class RZModify
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

    [CommandHandler(
        "rzmodify",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        1,
        "Modify a resonance zone by id using optional switches (no teleporting required).",
        "rzmodify <id> [name=<name>] [radius=<float>] [max=<float>] [shroud=<eventKey|clear>] [storm=<eventKey|clear>] [enabled=<true|false>]"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        if (session == null)
        {
            return;
        }

        if (parameters == null || parameters.Length < 1)
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                "Usage: /rzmodify <id> [name=...] [radius=...] [max=...] [shroud=...|clear] [storm=...|clear] [enabled=true|false]",
                ChatMessageType.Help
            );
            return;
        }

        uint id;
        if (!uint.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Invalid id. Usage: /rzmodify <id> ...", ChatMessageType.Help);
            return;
        }

        // Parse switches after the id
        var args = ParseNamedArgs(parameters.Length > 1 ? parameters[1..] : Array.Empty<string>());

        // Fetch zone by id, including disabled
        var row = DatabaseManager.ShardConfig.GetResonanceZoneEntryById((int)id);
        if (row == null)
        {
            CommandHandlerHelper.WriteOutputInfo(session, $"Zone id {id} not found.", ChatMessageType.Help);
            return;
        }

        // Track whether anything actually changed
        var anyChange = false;

        // name
        var newName = row.Name;
        string nameVal;
        if (args.TryGetValue("name", out nameVal))
        {
            newName = nameVal ?? "";
            anyChange = true;
        }

        // radius
        var newRadius = row.Radius;
        string radiusStr;
        if (args.TryGetValue("radius", out radiusStr))
        {
            float parsed;
            if (!float.TryParse(radiusStr, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Invalid radius value.", ChatMessageType.Help);
                return;
            }

            newRadius = parsed;
            anyChange = true;
        }

        // max distance
        var newMax = row.MaxDistance;
        string maxStr;
        if (args.TryGetValue("max", out maxStr))
        {
            float parsed;
            if (!float.TryParse(maxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Invalid max value.", ChatMessageType.Help);
                return;
            }

            newMax = parsed;
            anyChange = true;
        }

        // shroud key
        var newShroud = row.ShroudEventKey ?? "";
        string shroudVal;
        if (args.TryGetValue("shroud", out shroudVal))
        {
            if (string.Equals(shroudVal, "clear", StringComparison.OrdinalIgnoreCase))
            {
                newShroud = "";
            }
            else
            {
                newShroud = shroudVal ?? "";
            }

            anyChange = true;
        }

        // storm key
        var newStorm = row.StormEventKey ?? "";
        string stormVal;
        if (args.TryGetValue("storm", out stormVal))
        {
            if (string.Equals(stormVal, "clear", StringComparison.OrdinalIgnoreCase))
            {
                newStorm = "";
            }
            else
            {
                newStorm = stormVal ?? "";
            }

            anyChange = true;
        }

        // enabled
        bool? newEnabled = null;
        string enabledStr;
        if (args.TryGetValue("enabled", out enabledStr))
        {
            bool parsed;
            if (!bool.TryParse(enabledStr, out parsed))
            {
                CommandHandlerHelper.WriteOutputInfo(session, "Invalid enabled value (use true/false).", ChatMessageType.Help);
                return;
            }

            newEnabled = parsed;
            anyChange = true;
        }

        if (!anyChange)
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                "No changes specified. Usage: /rzmodify <id> [name=...] [radius=...] [max=...] [shroud=...|clear] [storm=...|clear] [enabled=true|false]",
                ChatMessageType.Help
            );
            return;
        }

        // Optional sanity checks (tight + safe; no silent weirdness)
        if (newRadius <= 0f || newMax <= 0f)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Radius and max must be > 0.", ChatMessageType.Help);
            return;
        }

        if (newRadius > newMax)
        {
            CommandHandlerHelper.WriteOutputInfo(session, "Radius cannot be greater than max.", ChatMessageType.Help);
            return;
        }

        var ok = DatabaseManager.ShardConfig.UpdateResonanceZoneEntry(
            id: row.Id,
            name: newName,
            radius: newRadius,
            maxDistance: newMax,
            shroudEventKey: newShroud,
            stormEventKey: newStorm,
            isEnabled: newEnabled
        );

        if (!ok)
        {
            CommandHandlerHelper.WriteOutputInfo(session, $"Failed to update zone ID={row.Id}.", ChatMessageType.Broadcast);
            return;
        }

        var enabledText = newEnabled.HasValue ? (newEnabled.Value ? "true" : "false") : "(unchanged)";

        CommandHandlerHelper.WriteOutputInfo(
            session,
            $"Updated zone ID={row.Id}: name='{newName}', radius={newRadius:0.##}, max={newMax:0.##}, shroud='{newShroud}', storm='{newStorm}', enabled={enabledText}.",
            ChatMessageType.Broadcast
        );
    }
}
