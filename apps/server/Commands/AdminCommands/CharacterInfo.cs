using System;
using System.Collections.Generic;
using Serilog;
using ACE.Entity.Enum;
using ACE.Server.Commands.Handlers;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.Commands.AdminCommands;

public class CharacterInfo
{
    private static readonly ILogger _log = Log.ForContext(typeof(CharacterInfo));

    /// <summary>
    /// Debug command to print out all info about a character
    /// </summary>
    [CommandHandler(
        "characterinfo",
        AccessLevel.Admin,
        CommandHandlerFlag.None,
        0,
        "Displays information about a character."
    )]
    public static void HandleCharacterInfo(Session session, params string[] parameters)
    {
        var player = PlayerManager.GetOnlinePlayer(parameters[0]);

        if (parameters == null || parameters.Length == 0)
        {
            if (session != null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Usage: @characterinfo <playerName>", ChatMessageType.System));
            }
            else
            {
                CommandHandlerHelper.WriteOutputInfo(null, "Usage: characterinfo <playerName>", ChatMessageType.System);
            }
            return;
        }

        try
        {
            // Support multi-word character names by joining parameters.
            var playerName = string.Join(" ", parameters).Trim();

            player = PlayerManager.GetOnlinePlayer(playerName);

            if (player == null)
            {
                if (session != null)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("No player found with that name.", ChatMessageType.System));
                }
                else
                {
                    CommandHandlerHelper.WriteOutputInfo(null, "No player found with that name.", ChatMessageType.System);
                }
                return;
            }

            var message = string.Empty;

            message += $"Character information for: {player.Name}";


            message += $"\n\nAttributes: ({player.Strength.StartingValue}/{player.Endurance.StartingValue}/{player.Coordination.StartingValue}/{player.Quickness.StartingValue}/{player.Focus.StartingValue}/{player.Self.StartingValue})";
            message += $"\n -Strength = {player.Strength.Current} ({player.Strength.Base})";
            message += $"\n -Endurance = {player.Endurance.Current} ({player.Endurance.Base})";
            message += $"\n -Coordination = {player.Coordination.Current} ({player.Coordination.Base})";
            message += $"\n -Quickness = {player.Quickness.Current} ({player.Quickness.Base})";
            message += $"\n -Focus = {player.Focus.Current} ({player.Focus.Base})";
            message += $"\n -Self = {player.Self.Current} ({player.Self.Base})";
            message += $"\n\n -Health = {player.Health.Current} ({player.Health.Base})";
            message += $"\n -Stamina = {player.Stamina.Current} ({player.Stamina.Base})";
            message += $"\n -Mana = {player.Mana.Current} ({player.Mana.Base})";

            var specializedSkills = new Dictionary<Skill, CreatureSkill>();
            var trainedSkills = new Dictionary<Skill, CreatureSkill>();

            foreach (var skill in player.Skills)
            {
                if (!SkillHelper.ValidSkills.Contains(skill.Key))
                {
                    continue;
                }    

                if (skill.Value.AdvancementClass is SkillAdvancementClass.Specialized)
                {
                    specializedSkills.Add(skill.Key, skill.Value);
                }

                if (skill.Value.AdvancementClass is SkillAdvancementClass.Trained)
                {
                    trainedSkills.Add(skill.Key, skill.Value);
                }
            }

            if (specializedSkills.Count > 0)
            {
                message += "\n\nSpecialized:";

                foreach (var skill in specializedSkills)
                {
                    message += $"\n -{skill.Key} = {skill.Value.Current} ({skill.Value.Base})";
                }
            }

            if (trainedSkills.Count > 0)
            {
                message += "\n\nTrained:";

                foreach (var skill in trainedSkills)
                {
                    message += $"\n -{skill.Key} = {skill.Value.Current} ({skill.Value.Base})";
                }
            }

            // Send message regardless of session state. CommandHandlerHelper.WriteOutputInfo
            // only sends to session when State == WorldConnected which can silently drop responses.
            if (session != null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat(message, ChatMessageType.System));
            }
            else
            {
                CommandHandlerHelper.WriteOutputInfo(null, message, ChatMessageType.System);
            }

            _log.Information(
                "CharacterInfo message delivered to clientId {ClientId} for player {PlayerName}",
                session?.Network?.ClientId ?? -1,
                playerName
            );
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Unhandled error in HandleCharacterInfo");
            if (session != null)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Error showing character info: {ex.Message}", ChatMessageType.System));
            }
            else
            {
                CommandHandlerHelper.WriteOutputInfo(null, $"Error showing character info: {ex.Message}", ChatMessageType.System);
            }
        }
    }
}
