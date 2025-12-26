using System;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using Serilog;

namespace ACE.Server.Commands.AdminCommands.ServerStartStop;

public class StopNow
{
    private static readonly ILogger _log = Log.ForContext(typeof(StopNow));

    /// <summary>
    /// Immediately begins the shutdown process by setting the shutdown interval to 0 before executing the shutdown method
    /// </summary>
    [CommandHandler(
        "stop-now",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        -1,
        "Shuts the server down, immediately!",
        "\nThis command will attempt to safely logoff all players, before shutting down the server."
    )]
    public static void ShutdownServerNow(Session session, params string[] parameters)
    {
        var adminName = (session == null) ? "CONSOLE" : session.Player.Name;
        var msg = $"{adminName} has initiated an immediate server shut down.";
        //log.Info(msg);
        PlayerManager.BroadcastToAuditChannel(session?.Player, msg);

        ServerManager.SetShutdownInterval(0);
        ShutdownServer(session, parameters);
    }

    /// <summary>
    /// Function to shutdown the server from console or in-game.
    /// </summary>
    [CommandHandler(
        "shutdown",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        0,
        "Begins the server shutdown process. Optionally displays a shutdown message, if a string is passed.",
        "< Optional Shutdown Message >\n"
            + "\tUse @cancel-shutdown to abort an active shutdown!\n"
            + "\tSet the shutdown delay in seconds with @set-shutdown-interval < 0-99999 >"
    )]
    public static void ShutdownServer(Session session, params string[] parameters)
    {
        if (ServerManager.ShutdownInitiated)
        {
            CommandHandlerHelper.WriteOutputInfo(
                session,
                "Shutdown is already in progress.",
                ChatMessageType.Broadcast
            );
            return;
        }

        var adminText = "";
        if (parameters.Length > 0)
        {
            adminText = string.Join(" ", parameters);
        }

        var adminName = (session == null) ? "CONSOLE" : session.Player.Name;
        var hideName = string.IsNullOrEmpty(adminText);

        var timeTillShutdown = TimeSpan.FromSeconds(ServerManager.ShutdownInterval);
        var timeRemaining =
            timeTillShutdown.TotalSeconds > 120
                ? $"{(int)timeTillShutdown.TotalMinutes} minutes."
                : $"{timeTillShutdown.TotalSeconds} seconds.";

        _log.Information(
            "{AdminName} initiated a complete server shutdown @ {ShutdownInitiatedAt} ({ShutdownInitiatedAtUtc} UTC)",
            adminName,
            DateTime.Now,
            DateTime.UtcNow
        );
        _log.Information("The server will shut down in {ShutdownTimeRemaining}.", timeRemaining);
        PlayerManager.BroadcastToAuditChannel(
            session?.Player,
            $"{adminName} initiated a complete server shutdown @ {DateTime.Now} ({DateTime.UtcNow} UTC)"
        );

        if (adminText.Length > 0)
        {
            _log.Information("Admin message: {ShutdownMessage}", adminText);
            PlayerManager.BroadcastToAuditChannel(
                session?.Player,
                $"{adminName} sent the following message for the shutdown: {adminText}"
            );
        }

        var sdt = timeTillShutdown;
        var timeHrs =
            $"{(sdt.Hours >= 1 ? $"{sdt.ToString("%h")}" : "")}{(sdt.Hours >= 2 ? $" hours" : sdt.Hours == 1 ? " hour" : "")}";
        var timeMins =
            $"{(sdt.Minutes != 0 ? $"{sdt.ToString("%m")}" : "")}{(sdt.Minutes >= 2 ? $" minutes" : sdt.Minutes == 1 ? " minute" : "")}";
        var timeSecs =
            $"{(sdt.Seconds != 0 ? $"{sdt.ToString("%s")}" : "")}{(sdt.Seconds >= 2 ? $" seconds" : sdt.Seconds == 1 ? " second" : "")}";
        var time =
            $"{(timeHrs != "" ? timeHrs : "")}{(timeMins != "" ? $"{((timeHrs != "") ? ", " : "")}" + timeMins : "")}{(timeSecs != "" ? $"{((timeHrs != "" || timeMins != "") ? " and " : "")}" + timeSecs : "")}";

        if (adminName.Equals("CONSOLE"))
        {
            adminName = "System";
        }

        var genericMsgToPlayers =
            $"Broadcast from {(hideName ? "System" : $"{adminName}")}> {(timeTillShutdown.TotalMinutes > 1.5 ? "ATTENTION" : "WARNING")} - This Asheron's Call Server will be shutting down in {time}{(sdt.TotalMinutes <= 1 ? "!" : ".")}{(timeTillShutdown.TotalMinutes <= 3 ? $" Please log out{(sdt.TotalMinutes <= 1 ? "!" : ".")}" : "")}";

        if (sdt.TotalMilliseconds == 0)
        {
            genericMsgToPlayers =
                $"Broadcast from {(hideName ? "System" : $"{adminName}")}> ATTENTION - This Asheron's Call Server is shutting down NOW!!!!";
        }

        if (!hideName)
        {
            PlayerManager.BroadcastToAll(
                new GameMessageSystemChat(
                    $"Broadcast from {adminName}> {adminText}\n" + genericMsgToPlayers,
                    ChatMessageType.WorldBroadcast
                ),
                $"Broadcast from {adminName}> {adminText}\n" + genericMsgToPlayers,
                "System"
            );
        }
        else
        {
            PlayerManager.BroadcastToAll(
                new GameMessageSystemChat(genericMsgToPlayers, ChatMessageType.WorldBroadcast), genericMsgToPlayers, "System"
            );
        }

        ServerManager.BeginShutdown();
    }
}
