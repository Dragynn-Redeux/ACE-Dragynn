using System;
using ACE.Common.Extensions;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network.GameMessages.Messages;
using Serilog;

namespace ACE.Server.Network.GameAction.Actions;

public static class GameActionTalk
{
    private static readonly ILogger _log = Log.ForContext(typeof(GameActionTalk));

    [GameAction(GameActionType.Talk)]
    public static void Handle(ClientMessage clientMessage, Session session)
    {
        var message = clientMessage.Payload.ReadString16L();

        // Accept both '@' and '/' as command prefixes
        if (!string.IsNullOrEmpty(message) && (message.StartsWith("@") || message.StartsWith("/")))
        {
            var prefix = message[0]; // '@' or '/'
            var commandRaw = message.Remove(0, 1);
            var response = CommandHandlerResponse.InvalidCommand;
            CommandHandlerInfo commandHandler = null;
            string command = null;
            string[] parameters = null;

            try
            {
                CommandManager.ParseCommand(message.Remove(0, 1), out command, out parameters);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Exception while parsing command: {RawCommand}", commandRaw);
                return;
            }

            try
            {
                response = CommandManager.GetCommandHandler(session, command, parameters, out commandHandler);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Exception while getting command handler for: {RawCommand}", commandRaw);
            }

            if (response == CommandHandlerResponse.Ok)
            {
                try
                {
                    if (commandHandler?.Attribute.IncludeRaw ?? false)
                    {
                        parameters = CommandManager.StuffRawIntoParameters(message.Remove(0, 1), command, parameters);
                    }
                    ((CommandHandler)commandHandler.Handler).Invoke(session, parameters);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Exception while invoking command handler for: {RawCommand}", commandRaw);
                }
            }
            else if (response == CommandHandlerResponse.SudoOk)
            {
                try
                {
                    var sudoParameters = new string[parameters.Length - 1];
                    for (var i = 1; i < parameters.Length; i++)
                    {
                        sudoParameters[i - 1] = parameters[i];
                    }

                    if (commandHandler?.Attribute.IncludeRaw ?? false)
                    {
                        parameters = CommandManager.StuffRawIntoParameters(message.Remove(0, 1), command, parameters);
                    }
                    ((CommandHandler)commandHandler.Handler).Invoke(session, sudoParameters);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Exception while invoking sudo command handler for: {RawCommand}", commandRaw);
                }
            }
            else
            {
                // Use the same prefix the player typed when showing usage/errors
                var showUsagePrefix = prefix;

                switch (response)
                {
                    case CommandHandlerResponse.InvalidCommand:
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat($"Unknown command: {command}", ChatMessageType.Help)
                        );
                        break;
                    case CommandHandlerResponse.InvalidParameterCount:
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"Invalid parameter count, got {parameters?.Length ?? 0}, expected {commandHandler?.Attribute.ParameterCount}!",
                                ChatMessageType.Help
                            )
                        );
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"{showUsagePrefix}{commandHandler?.Attribute.Command} - {commandHandler?.Attribute.Description}",
                                ChatMessageType.Broadcast
                            )
                        );
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat(
                                $"Usage: {showUsagePrefix}{commandHandler?.Attribute.Command} {commandHandler?.Attribute.Usage}",
                                ChatMessageType.Broadcast
                            )
                        );
                        break;
                    case CommandHandlerResponse.NotAuthorized:
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat($"You are not authorized to use {showUsagePrefix}{command}.", ChatMessageType.Help)
                        );
                        break;
                    case CommandHandlerResponse.NotInWorld:
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat($"You must be in the world to use {showUsagePrefix}{command}.", ChatMessageType.Help)
                        );
                        break;
                    case CommandHandlerResponse.NoConsoleInvoke:
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat($"That command cannot be invoked from the console.", ChatMessageType.Help)
                        );
                        break;
                    default:
                        session.Network.EnqueueSend(
                            new GameMessageSystemChat($"Command failed: {response}", ChatMessageType.Help)
                        );
                        break;
                }

                _log.Information("Command {Command} returned {Response} for player {Player}", command, response, session?.Player?.Name ?? "Unknown");
            }
        }
        else
        {
            session.Player.HandleActionTalk(message);
        }
    }
}
