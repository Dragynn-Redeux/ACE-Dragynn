using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Commands.DeveloperCommands;

public class ForceLogoff
{
    [CommandHandler(
        "forcelogout",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        "Force log off of specified character or last appraised character"
    )]
    public static void HandleForceLogout(Session session, params string[] parameters)
    {
        HandleForceLogoff(session, parameters);
    }

     [CommandHandler(
        "forcelogoff",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        "Force log off of specified character or last appraised character"
    )]
    public static void HandleForceLogoff(Session session, params string[] parameters)
    {
        var playerName = "";
        if (parameters.Length > 0)
        {
            playerName = string.Join(" ", parameters);
        }

        WorldObject target = null;

        if (!string.IsNullOrEmpty(playerName))
        {
            var plr = PlayerManager.FindByName(playerName);
            if (plr != null)
            {
                target = PlayerManager.GetOnlinePlayer(plr.Guid);

                if (target == null)
                {
                    CommandHandlerHelper.WriteOutputInfo(
                        session,
                        $"Unable to force log off for {plr.Name}: Player is not online."
                    );
                    return;
                }
            }
            else
            {
                CommandHandlerHelper.WriteOutputInfo(
                    session,
                    $"Unable to force log off for {playerName}: Player not found in manager."
                );
                return;
            }
        }
        else
        {
            target = CommandHandlerHelper.GetLastAppraisedObject(session);
        }

        if (target != null && target is Player player)
        {
            //if (player.Session != null)
            //    player.Session.LogOffPlayer(true);
            //else
            //    player.LogOut();

            var msg = $"Player {player.Name} (0x{player.Guid}) found in PlayerManager.onlinePlayers.\n";
            msg +=
                $"------- Session: {(player.Session != null ? $"C2S: {player.Session.EndPointC2S} | S2C: {player.Session.EndPointS2C}" : "NULL")}\n";
            msg +=
                $"------- CurrentLandblock: {(player.CurrentLandblock != null ? $"0x{player.CurrentLandblock.Id:X4}" : "NULL")}\n";
            msg += $"------- Location: {(player.Location != null ? $"{player.Location.ToLOCString()}" : "NULL")}\n";
            msg += $"------- IsLoggingOut: {player.IsLoggingOut}\n";
            msg += $"------- IsInDeathProcess: {player.IsInDeathProcess}\n";
            var foundOnLandblock = false;
            if (player.CurrentLandblock != null)
            {
                // in the instance the player is in, which is not necessarily the persistent world's landblock with the same id
                foundOnLandblock =
                    LandblockManager
                        .TryGetLandblock(player.CurrentLandblock.Id, player.CurrentLandblock.Instance)
                        ?.GetObject(player.Guid) != null;
            }

            msg += $"------- FoundOnLandblock: {foundOnLandblock}\n";
            var playerForcedLogOffRequested = player.ForcedLogOffRequested;
            msg += $"------- ForcedLogOffRequested: {playerForcedLogOffRequested}\n";

            msg += "Log off path taken: ";
            if (playerForcedLogOffRequested)
            {
                player.Session?.Terminate(
                    Network.Enum.SessionTerminationReason.ForcedLogOffRequested,
                    new GameMessageBootAccount(" because the character was forced to log off by an admin")
                );
                player.ForceLogoff();
                msg += "player.Session?.Terminate() | player.ForceLogoff()";
            }
            else if (player.Session != null)
            {
                player.ForcedLogOffRequested = true;
                player.Session.Terminate(
                    Network.Enum.SessionTerminationReason.ForcedLogOffRequested,
                    new GameMessageBootAccount(" because the character was forced to log off by an admin")
                );
                msg += "player.ForcedLogOffRequested = true | player.Session.Terminate()";
            }
            else if (player.CurrentLandblock != null && foundOnLandblock)
            {
                player.ForcedLogOffRequested = true;
                player.LogOut();
                msg += "player.ForcedLogOffRequested = true | player.LogOut()";
            }
            else if (player.IsInDeathProcess)
            {
                player.ForcedLogOffRequested = true;
                player.IsInDeathProcess = false;
                player.LogOut_Inner(true);
                msg +=
                    "player.ForcedLogOffRequested = true | player.IsInDeathProcess = false | player.LogOut_Inner(true)";
            }
            else
            {
                player.ForcedLogOffRequested = true;
                msg += "player.ForcedLogOffRequested = true";
            }

            if (!playerForcedLogOffRequested)
            {
                msg += "\nUse this command again if this player does not properly log off within the next minute.";
            }
            else
            {
                msg += "\nPlease send the above report to ACEmulator development team via Discord.";
            }

            CommandHandlerHelper.WriteOutputInfo(session, msg);

            PlayerManager.BroadcastToAuditChannel(session?.Player, $"Forcing Log Off of {player.Name}...");
        }
        else
        {
            if (target != null)
            {
                CommandHandlerHelper.WriteOutputInfo(
                    session,
                    $"Unable to force log off for {target.Name}: Target is not a player."
                );
            }
            //else
            //    CommandHandlerHelper.WriteOutputInfo(session, $"Unable to force log off for {playerName}: Player not found in manager.");
        }
    }
}
