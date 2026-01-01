using System;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.Commands.DeveloperCommands;

public class PlayEffect
{
    /// <summary>
    /// effect [Effect] (scale)
    /// </summary>
    [CommandHandler(
        "effect",
        AccessLevel.Developer,
        CommandHandlerFlag.RequiresWorld,
        1,
        "Plays an effect.",
        "effect (float)\n" + "Effect can be uint or enum name" + "float is scale level"
    )]
    public static void HandlePlayEffect(Session session, params string[] parameters)
    {
        try
        {
            if (parameters == null || parameters.Length == 0 || string.IsNullOrEmpty(parameters[0]))
            {
                var emptyMsg = new GameMessageSystemChat("Usage: /effect <EffectNameOrValue> (scale)", ChatMessageType.Broadcast);
                session.Network.EnqueueSend(emptyMsg);
                return;
            }

            var scale = 1f;
            if (parameters.Length > 1 && !string.IsNullOrEmpty(parameters[1]))
            {
                float.TryParse(parameters[1], out scale);
            }

            var message = $"Unable to find an effect called {parameters[0]} to play.";

            if (Enum.TryParse<ACE.Entity.Enum.PlayScript>(parameters[0], true, out var effect))
            {
                if (Enum.IsDefined(typeof(ACE.Entity.Enum.PlayScript), effect))
                {
                    message = $"Playing effect {Enum.GetName(typeof(ACE.Entity.Enum.PlayScript), effect)}";
                    session.Player.ApplyVisualEffects(effect);
                }
            }

            var sysChatMessage = new GameMessageSystemChat(message, ChatMessageType.Broadcast);
            session.Network.EnqueueSend(sysChatMessage);
        }
        catch (Exception)
        {
            // Do Nothing
        }
    }
}
