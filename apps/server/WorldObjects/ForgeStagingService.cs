using System;
using System.Collections.Concurrent;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;

namespace ACE.Server.WorldObjects;

public static class ForgeStagingService
{
    private const string ForgeTemplateName = "Resonance Forge";

    private static readonly ConcurrentDictionary<uint, ForgePromptState> PromptStateByPlayer = new();

    private sealed class ForgePromptState
    {
        public uint TargetGuid;
        public DateTime PromptedAtUtc;
    }

    public static bool IsForgeTarget(WorldObject target)
    {
        if (target == null)
        {
            return false;
        }

        if (target is ResonanceForge)
        {
            return true;
        }

        var template = target.GetProperty(PropertyString.Template);
        return string.Equals(template, ForgeTemplateName, StringComparison.OrdinalIgnoreCase);
    }

    public static void PromptAutoStageIfNeeded(Player player, Container forgeTarget)
    {
        if (player?.Session == null || forgeTarget == null || !IsForgeTarget(forgeTarget))
        {
            return;
        }

        var shouldPrompt = false;
        var now = DateTime.UtcNow;

        PromptStateByPlayer.AddOrUpdate(
            player.Guid.Full,
            _ =>
            {
                shouldPrompt = true;
                return new ForgePromptState
                {
                    TargetGuid = forgeTarget.Guid.Full,
                    PromptedAtUtc = now,
                };
            },
            (_, state) =>
            {
                if (state.TargetGuid != forgeTarget.Guid.Full || now - state.PromptedAtUtc > TimeSpan.FromMinutes(5))
                {
                    shouldPrompt = true;
                    state.TargetGuid = forgeTarget.Guid.Full;
                    state.PromptedAtUtc = now;
                }

                return state;
            }
        );

        if (!shouldPrompt)
        {
            return;
        }

        player.ConfirmationManager.EnqueueSend(
            new Confirmation_Custom(
                player.Guid,
                () =>
                {
                    var liveTarget = player.CurrentLandblock?.GetObject(forgeTarget.Guid) as Container;
                    if (liveTarget == null || !IsForgeTarget(liveTarget))
                    {
                        return;
                    }

                    player.TryStageAllUnstableItemsToForge(liveTarget);
                }
            ),
            "Place all unstable items in forge?"
        );
    }
}
