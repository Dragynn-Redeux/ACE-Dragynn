using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Physics.Managers;

namespace ACE.Server.Commands.AdminCommands;

public class InstanceCommand
{
    private const string Usage =
        "instance [list] | info [player | 0xguid] | open <template> [new] | here [radius] | enter <id> [player] | leave [player] | move <id> [0xguid] | close <id>\n"
        + "  list: the templates that are set up and the instances that are open\n"
        + "  info [player | 0xguid]: which instance you are in, and where in it, and the same for the object you have selected. Give a player's name, or the guid of any object in the world (0x80001234), to see that one instead\n"
        + "  open <template> [new]: goes into the instance of an island (or any other template that is set up), and makes it if there is none. new makes another one\n"
        + "  here [radius]: opens a private copy of the landblock you are in (and the ones within radius, up to 3) and takes you into it. Nothing in it is saved.\n"
        + "  enter <id> [player]: goes into an open instance. 0 is the persistent world. Give a player's name to send them instead of yourself\n"
        + "  leave [player]: goes back to where the instance sends players. Give a player's name to send them\n"
        + "  move <id> [0xguid]: moves the object you have selected, or the one with that guid wherever it is, to instance <id> at the place it is now. Only objects that were made while the server was running and are lying on the ground can be moved: not players, and not the objects a landblock is made of\n"
        + "  close <id>: shuts an instance down, sending everyone in it out";

    [CommandHandler(
        "instance",
        AccessLevel.Admin,
        CommandHandlerFlag.RequiresWorld,
        0,
        "Shows, opens, enters, leaves and closes instances, and moves things between them: private copies of landblocks that nothing in is saved.",
        Usage
    )]
    public static void HandleInstance(Session session, params string[] parameters)
    {
        var player = session.Player;
        var subcommand = parameters.Length > 0 ? parameters[0].ToLowerInvariant() : "list";

        switch (subcommand)
        {
            case "list":
                List(player);
                break;

            case "info":
                Info(session, parameters);
                break;

            case "open":
                Open(player, parameters);
                break;

            case "here":
                Here(player, parameters);
                break;

            case "enter":
                Enter(player, parameters);
                break;

            case "leave":
                Leave(player, parameters);
                break;

            case "move":
                Move(session, parameters);
                break;

            case "close":
                Close(player, parameters);
                break;

            default:
                player.SendMessage($"Usage: {Usage}", ChatMessageType.System);
                break;
        }
    }

    private static void List(WorldObjects.Player player)
    {
        var templates = InstanceManager.GetTemplates();
        var instances = InstanceManager.GetInstances();

        player.SendMessage($"-----------Instance templates ({templates.Count})-----------", ChatMessageType.System);
        foreach (var template in templates)
        {
            player.SendMessage(
                $"{template.Name}: {template.Footprint.Count} landblock(s){(template.HasBoundary ? $", {template.Boundary.Count} of them ring" : "")}{(template.InstanceOnly ? ", instance only" : "")}{(template.Personal ? ", one for each player" : "")}",
                ChatMessageType.System
            );
        }

        var timeout = InstanceManager.EmptyTimeout();

        player.SendMessage($"-----------Open instances ({instances.Count})-----------", ChatMessageType.System);
        foreach (var instance in instances)
        {
            var state = instance.IsClosing
                ? "shutting down"
                : instance.MemberCount > 0
                    ? $"{instance.MemberCount} player(s) in it"
                    : $"empty for {(InstanceManager.UtcNow() - instance.EmptySince)?.TotalMinutes:N1} of {timeout.TotalMinutes:N0} min";

            player.SendMessage($"{instance.Id}: {instance.Template.Name} - {state}", ChatMessageType.System);
        }
    }

    private static void Open(WorldObjects.Player player, string[] parameters)
    {
        var template = parameters.Length > 1 ? InstanceManager.GetTemplate(parameters[1]) : null;

        if (template == null)
        {
            player.SendMessage(
                parameters.Length > 1
                    ? $"There is no template {parameters[1]}. /instance list shows the ones there are."
                    : $"Usage: {Usage}",
                ChatMessageType.System
            );
            return;
        }

        var makeAnother = parameters.Length > 2 && parameters[2].Equals("new", StringComparison.OrdinalIgnoreCase);

        var instance = makeAnother
            ? InstanceManager.Create(template)
            : InstanceManager.FindOrCreate(template, null, out _);

        InstanceManager.Enter(player, instance);
    }

    private static void Here(WorldObjects.Player player, string[] parameters)
    {
        if (player.InstanceId != Landblock.PersistentInstance)
        {
            player.SendMessage("You are already in an instance. Leave it first.", ChatMessageType.System);
            return;
        }

        var radius = 0;
        if (parameters.Length > 1 && !int.TryParse(parameters[1], out radius))
        {
            player.SendMessage($"Usage: {Usage}", ChatMessageType.System);
            return;
        }

        radius = Math.Clamp(radius, 0, 3);

        var location = player.Location;
        var footprint = new List<LandblockId>();

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var x = location.LandblockX + dx;
                var y = location.LandblockY + dy;

                if (x >= 0 && x <= 254 && y >= 0 && y <= 254)
                {
                    footprint.Add(new LandblockId((byte)x, (byte)y));
                }
            }
        }

        // The place they were standing is where they come back to. It is not a registered template, so it doesn't affect anyone logging in.
        var template = new InstanceTemplate(
            $"here-{location.LandblockId.Landblock:X4}",
            footprint,
            new Position(location),
            new Position(location)
        );

        var instance = InstanceManager.Create(template);

        player.SendMessage(
            $"Opened instance {instance.Id} ({footprint.Count} landblock(s)). It is deleted {InstanceManager.EmptyTimeout().TotalMinutes:N0} minutes after the last player leaves it.",
            ChatMessageType.System
        );

        InstanceManager.Enter(player, instance);
    }

    private static void Info(Session session, string[] parameters)
    {
        var admin = session.Player;
        var argument = string.Join(" ", parameters.Skip(1)).Trim();

        if (argument.Length > 0)
        {
            var subject = FindSubject(argument, out var problem);

            admin.SendMessage(
                subject != null ? DescribeObject($"{subject.Name} (0x{subject.Guid.Full:X8})", subject) : problem,
                ChatMessageType.System
            );
            return;
        }

        admin.SendMessage(DescribeObject("You", admin), ChatMessageType.System);

        // what the other admin commands call selected: the object that was last appraised
        if (admin.RequestedAppraisalTarget != null)
        {
            var selected = CommandHandlerHelper.GetLastAppraisedObject(session);

            if (selected != null && selected != admin)
            {
                admin.SendMessage(
                    DescribeObject($"Selected {selected.Name} (0x{selected.Guid.Full:X8})", selected),
                    ChatMessageType.System
                );
            }
        }
    }

    /// <summary>
    /// The player with that name, or the object with that guid (0x80001234) wherever it is: it does not have to be near, or in the same instance
    /// </summary>
    private static WorldObjects.WorldObject FindSubject(string argument, out string problem)
    {
        problem = null;

        if (argument.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(argument.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var guid))
            {
                problem = $"{argument} is not a guid. Guids are written like 0x80001234.";
                return null;
            }

            var wo = ServerObjectManager.GetObjectA(guid)?.WeenieObj?.WorldObject;

            if (wo == null)
            {
                problem = $"There is no object 0x{guid:X8} in the world.";
            }

            return wo;
        }

        var player = PlayerManager.GetOnlinePlayer(argument);

        if (player == null)
        {
            problem = $"Player {argument} was not found.";
        }

        return player;
    }

    private static string DescribeObject(string label, WorldObjects.WorldObject wo)
    {
        var text = new StringBuilder();

        text.Append($"{label}: instance {InstanceManager.Describe(wo.InstanceId)}");

        var landblock = wo.CurrentLandblock;

        if (landblock == null)
        {
            text.Append("\n   it is not in a landblock (it is in a container, or worn)");
            return text.ToString();
        }

        text.Append(
            $"\n   landblock {landblock.Id.Landblock:X4}: {InstanceManager.DescribePlace(wo.InstanceId, landblock.Id)}"
        );

        // These all have to agree. If they don't, something moved the object without moving everything that belongs to it.
        uint? physics = wo.PhysicsObj?.Instance;
        uint? cell = wo.PhysicsObj?.CurCell?.Instance;

        if (landblock.Instance != wo.InstanceId || physics != wo.InstanceId || (cell.HasValue && cell != wo.InstanceId))
        {
            text.Append(
                $"\n   MISMATCH: the object says {wo.InstanceId}, its landblock {landblock.Instance}, its physics {physics?.ToString() ?? "nothing"}, the cell it is in {cell?.ToString() ?? "nothing"}"
            );
        }

        // The position of a player comes from their client, which can be somewhere the server has stopped it from going
        var positionLandblock = wo.Location?.LandblockId;

        if (
            positionLandblock.HasValue
            && (
                wo.InstanceId == Landblock.PersistentInstance
                    ? InstanceManager.IsInstanceOnly(positionLandblock.Value)
                    : !InstanceManager.IsInFootprint(wo.InstanceId, positionLandblock.Value)
            )
        )
        {
            text.Append(
                $"\n   MISMATCH: its position says landblock {positionLandblock.Value.Landblock:X4}, which is {InstanceManager.DescribePlace(wo.InstanceId, positionLandblock.Value)}, but it is in landblock {landblock.Id.Landblock:X4}"
            );
        }

        return text.ToString();
    }

    private static void Enter(WorldObjects.Player admin, string[] parameters)
    {
        if (parameters.Length < 2 || !uint.TryParse(parameters[1], out var id))
        {
            admin.SendMessage($"Usage: {Usage}", ChatMessageType.System);
            return;
        }

        var player = GetPlayerToMove(admin, parameters, 2);

        if (player == null)
        {
            return;
        }

        // 0 is the persistent world
        if (id == Landblock.PersistentInstance)
        {
            LeaveInstance(admin, player);
            return;
        }

        var instance = InstanceManager.Get(id);

        if (instance == null)
        {
            admin.SendMessage($"There is no instance {id}.", ChatMessageType.System);
            return;
        }

        if (instance.IsClosing)
        {
            admin.SendMessage($"Instance {id} is shutting down.", ChatMessageType.System);
            return;
        }

        InstanceManager.Enter(player, instance);

        Audit(admin, player, $"into instance {id}");
    }

    private static void Leave(WorldObjects.Player admin, string[] parameters)
    {
        var player = GetPlayerToMove(admin, parameters, 1);

        if (player != null)
        {
            LeaveInstance(admin, player);
        }
    }

    private static void LeaveInstance(WorldObjects.Player admin, WorldObjects.Player player)
    {
        if (!InstanceManager.Leave(player))
        {
            admin.SendMessage(
                player == admin ? "You are not in an instance." : $"{player.Name} is not in an instance.",
                ChatMessageType.System
            );
            return;
        }

        Audit(admin, player, "out of their instance");
    }

    /// <summary>
    /// The player whose name starts at parameters[firstNameIndex], or the admin if there is no name
    /// </summary>
    private static WorldObjects.Player GetPlayerToMove(
        WorldObjects.Player admin,
        string[] parameters,
        int firstNameIndex
    )
    {
        if (parameters.Length <= firstNameIndex)
        {
            return admin;
        }

        var name = string.Join(" ", parameters.Skip(firstNameIndex));
        var player = PlayerManager.GetOnlinePlayer(name);

        if (player == null)
        {
            admin.SendMessage($"Player {name} was not found.", ChatMessageType.System);
        }

        return player;
    }

    private static void Audit(WorldObjects.Player admin, WorldObjects.Player player, string what)
    {
        if (player != admin)
        {
            PlayerManager.BroadcastToAuditChannel(admin, $"{admin.Name} sent {player.Name} {what}.");
        }
    }

    private static void Move(Session session, string[] parameters)
    {
        var admin = session.Player;

        if (parameters.Length < 2 || !uint.TryParse(parameters[1], out var id))
        {
            admin.SendMessage($"Usage: {Usage}", ChatMessageType.System);
            return;
        }

        WorldObjects.WorldObject wo;

        if (parameters.Length > 2)
        {
            wo = FindSubject(string.Join(" ", parameters.Skip(2)), out var problem);

            if (wo == null)
            {
                admin.SendMessage(problem, ChatMessageType.System);
                return;
            }
        }
        else if (admin.RequestedAppraisalTarget != null)
        {
            wo = CommandHandlerHelper.GetLastAppraisedObject(session);

            if (wo == null)
            {
                return;
            }
        }
        else
        {
            admin.SendMessage(
                "Select an object (appraise it), or give its guid, written like 0x80001234.",
                ChatMessageType.System
            );
            return;
        }

        if (id != Landblock.PersistentInstance)
        {
            var instance = InstanceManager.Get(id);

            if (instance == null || instance.IsClosing)
            {
                admin.SendMessage(
                    instance == null ? $"There is no instance {id}." : $"Instance {id} is shutting down.",
                    ChatMessageType.System
                );
                return;
            }
        }

        var previousInstance = wo.InstanceId;
        string reason = null;

        // where it is now, but in the other instance
        if (wo.Location == null || !InstanceManager.TryMoveObject(wo, id, wo.Location, out reason))
        {
            admin.SendMessage(
                $"{wo.Name} (0x{wo.Guid.Full:X8}) was not moved to instance {id}. {reason ?? "It has no place in the world."}",
                ChatMessageType.System
            );
            return;
        }

        admin.SendMessage(
            $"Moved {wo.Name} (0x{wo.Guid.Full:X8}) from instance {previousInstance} to instance {id}, to the place where it was.",
            ChatMessageType.System
        );

        PlayerManager.BroadcastToAuditChannel(
            admin,
            $"{admin.Name} moved {wo.Name} (0x{wo.Guid.Full:X8}) from instance {previousInstance} to instance {id}."
        );
    }

    private static void Close(WorldObjects.Player player, string[] parameters)
    {
        var instance = GetInstance(player, parameters);
        if (instance == null)
        {
            return;
        }

        InstanceManager.Close(instance.Id);

        player.SendMessage(
            $"Instance {instance.Id} is shutting down. It is deleted as soon as everyone in it has left.",
            ChatMessageType.System
        );
    }

    private static WorldInstance GetInstance(WorldObjects.Player player, string[] parameters)
    {
        if (parameters.Length < 2 || !uint.TryParse(parameters[1], out var id))
        {
            player.SendMessage($"Usage: {Usage}", ChatMessageType.System);
            return null;
        }

        var instance = InstanceManager.Get(id);
        if (instance == null)
        {
            player.SendMessage($"There is no instance {id}.", ChatMessageType.System);
        }

        return instance;
    }
}
