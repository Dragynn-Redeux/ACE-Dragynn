using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class UpgradeKit : Stackable
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public UpgradeKit(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public UpgradeKit(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private static void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
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

        if (!target.UpgradeableQuestItem)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat("Only certain quest items can be upgraded.", ChatMessageType.Craft)
            );
            player.SendUseDoneEvent();
            return;
        }

        var requiredUpgradeKits = GetRequiredUpgradeKits(player, target);
        var maxRequirementForPlayer = GetMaxRequirementForPlayer(player, target);

        if (source.StackSize < requiredUpgradeKits)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Upgrading {target.Name} to match your current tier requires {requiredUpgradeKits} Upgrade Kits.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if ((target.WieldDifficulty ?? 0) >= maxRequirementForPlayer)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{target.Name} already matches your current tier.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (!confirmed)
        {
            if (
                !player.ConfirmationManager.EnqueueSend(
                    new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                    $"This will upgrade {target.Name} to match your current tier.\n\n" +
                    $"{requiredUpgradeKits} Upgrade Kits will be consumed."
                )
            )
            {
                player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
            }
            else
            {
                player.SendUseDoneEvent();
            }

            return;
        }

        var actionChain = new ActionChain();

        var animTime = 0.0f;

        player.IsBusy = true;

        if (player.CombatMode != CombatMode.NonCombat)
        {
            var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
            actionChain.AddDelaySeconds(stanceTime);

            animTime += stanceTime;
        }

        animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

        actionChain.AddAction(
            player,
            () =>
            {
                if (!UpgradeItem(player, target))
                {
                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            "The crafting attempt encountered an error. Please report.",
                            ChatMessageType.Broadcast
                        )
                    );
                }
                else
                {
                    player.EnqueueBroadcast(new GameMessageUpdateObject(target));
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You upgrade {target.Name} to a more powerful version.",
                            ChatMessageType.Broadcast
                        )
                    );
                    player.TryConsumeFromInventoryWithNetworking(source, requiredUpgradeKits);
                }
            }
        );

        player.EnqueueMotion(actionChain, MotionCommand.Ready);

        actionChain.AddAction(
            player,
            () =>
            {
                player.IsBusy = false;
            }
        );

        actionChain.EnqueueChain();

        player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
    }

    public static bool UpgradeItem(Player player, WorldObject target, int forcedNewWieldDifficulty = 0)
    {
        var usesRequiredLevelPath = UsesRequiredLevelTiering(target);
        var currentRequirement = target.WieldDifficulty ?? (usesRequiredLevelPath ? 1 : 50);
        var newRequirement = forcedNewWieldDifficulty > 0 && target.ItemType != ItemType.Jewelry
            ? forcedNewWieldDifficulty
            : GetMaxRequirementForPlayer(player, target);

        var currentTier = GetTierIndexFromRequirement(target, currentRequirement);
        var newTier = GetTierIndexFromRequirement(target, newRequirement);

        if (!LootGenerationFactory.ApplyUpgradeKitTierUpgrades(target, currentTier, newTier))
        {
            return false;
        }

        target.SetProperty(PropertyInt.WieldDifficulty, newRequirement);
        LootGenerationFactory.ApplyUpgradeKitPostTierUpgrades(target, currentTier, newTier);

        return true;
    }

    private static bool UsesRequiredLevelTiering(WorldObject target)
    {
        return target.ItemType == ItemType.Jewelry
            || (target.WeenieType == WeenieType.Clothing && target.WieldRequirements == WieldRequirement.Level);
    }

    private static int GetMaxRequirementForPlayer(Player player, WorldObject target)
    {
        return UsesRequiredLevelTiering(target)
            ? GetRequiredLevelFromPlayerTier(player)
            : GetHighestWieldDifficultyForPlayer(player, target);
    }

    private static int GetTierIndexFromRequirement(WorldObject target, int requirementValue)
    {
        var tier = UsesRequiredLevelTiering(target)
            ? LootGenerationFactory.GetTierFromRequiredLevel(requirementValue)
            : LootGenerationFactory.GetTierFromWieldDifficulty(requirementValue);

        return Math.Clamp(tier - 1, 0, 7);
    }

    private static int GetHighestWieldDifficultyForPlayer(Player player, WorldObject target)
    {
        if (target.WieldSkillType == null)
        {
            return 0;
        }

        if (target.WieldRequirements == WieldRequirement.RawAttrib)
        {
            var targetWieldAttribute = (PropertyAttribute)target.WieldSkillType;
            var playerBaseAttributeLevel = player.Attributes[targetWieldAttribute].Base;

            return playerBaseAttributeLevel switch
            {
                >= 270 => 270,
                >= 250 => 250,
                >= 230 => 230,
                >= 215 => 215,
                >= 200 => 200,
                >= 175 => 175,
                >= 125 => 125,
                _ => 50
            };
        }

        return player.Level switch
        {
            >= 100 => 100,
            >= 75 => 75,
            >= 50 => 50,
            >= 40 => 40,
            >= 30 => 30,
            >= 20 => 20,
            >= 10 => 10,
            _ => 1
        };
    }

    private static int GetRequiredLevelFromPlayerTier(Player player)
    {
        var playerTier = player.GetPlayerTier(player.Level ?? 1);

        return LootGenerationFactory.GetRequiredLevelPerTier(playerTier);
    }

    private static int GetRequiredUpgradeKits(Player player, WorldObject target)
    {
        var newWieldReq = GetMaxRequirementForPlayer(player, target);

        if (target.WieldRequirements == WieldRequirement.RawAttrib)
        {
            switch (newWieldReq)
            {
                case 125: return 1;
                case 175: return 4;
                case 200: return 9;
                case 215: return 16;
                case 230: return 15;
                case 250: return 36;
                case 270: return 49;
            }
        }

        switch (newWieldReq)
        {
            case 10: return 1;
            case 20: return 4;
            case 30: return 9;
            case 40: return 16;
            case 50: return 25;
            case 75: return 36;
            case 100: return 49;
        }

        return 1;
    }
}
