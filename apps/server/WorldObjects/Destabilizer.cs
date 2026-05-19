using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class Destabilizer : Stackable
{
    private const string DirectDestabilizeBlockedMessage = "That item cannot be destabilized.";
    private const string QuestItemBlockedMessage = "Quest items cannot be destabilized.";
    private const string StableItemsMustUseForgeMessage = "Stable items must be taken to the forge for destabilization.";
    private const string RetainedMessage = "That item is retained and cannot be altered.";

    public Destabilizer(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    public Destabilizer(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private static void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target)
    {
        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (!RecipeManager.VerifyUse(player, source, target, true))
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (!TryValidateTarget(player, target))
        {
            player.SendUseDoneEvent();
            return;
        }

        var variancePercentOverride = GetVariancePercentOverride(source);

        if (!DestabilizedLootForge.TryFinalizeDirect(player, target, source, variancePercentOverride, out var failureMessage))
        {
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                player.SendTransientError(failureMessage);
            }

            player.SendUseDoneEvent();
            return;
        }

        player.SendUseDoneEvent();
    }

    private static bool TryValidateTarget(Player player, WorldObject target)
    {
        if (target == null)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "That item is unavailable.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (target.Retained)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    RetainedMessage,
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (IsQuestItem(target))
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    QuestItemBlockedMessage,
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var stage = ForgeStageDisplay.GetStage(target);
        if (stage == ForgeStage.Stable)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    StableItemsMustUseForgeMessage,
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (stage == ForgeStage.Destabilized)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "That item is already Destabilized.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (stage != ForgeStage.None)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    DirectDestabilizeBlockedMessage,
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (!DestabilizedLootEffects.CanDestabilize(target, out var reason))
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    reason ?? DirectDestabilizeBlockedMessage,
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        return true;
    }

    private static bool IsQuestItem(WorldObject target)
    {
        return target != null
            && (
                target.MutableQuestItem
                || target.UpgradeableQuestItem
                || !string.IsNullOrWhiteSpace(target.Quest)
                || !string.IsNullOrWhiteSpace(target.QuestRestriction)
            );
    }

    private static double? GetVariancePercentOverride(WorldObject source)
    {
        if (source == null)
        {
            return null;
        }

        var configuredPercent = source.GetProperty(PropertyFloat.DestabVarPercent);
        if (!configuredPercent.HasValue)
        {
            return null;
        }

        return Math.Max(0.0, configuredPercent.Value);
    }
}