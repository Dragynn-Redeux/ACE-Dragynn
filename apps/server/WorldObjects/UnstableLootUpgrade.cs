using System;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;

namespace ACE.Server.WorldObjects;

public static class UnstableLootUpgrade
{
    public readonly record struct StabilizationTierAnalysis(int FromTier, int ToTier, string DriverName, int DriverValue);

    public static bool UpgradeItem(Player player, WorldObject target, int forcedNewWieldDifficulty = 0)
    {
        if (target.ItemType != ItemType.Jewelry)
        {
            var usesRequiredLevelPath = UsesRequiredLevelTiering(target);
            var currentRequirement = target.WieldDifficulty ?? (usesRequiredLevelPath ? 1 : 50);
            var newRequirement = forcedNewWieldDifficulty > 0
                ? forcedNewWieldDifficulty
                : GetMaxRequirementForPlayer(player, target);

            newRequirement = Math.Max(currentRequirement, newRequirement);

            var currentTier = GetTierIndexFromRequirement(target, currentRequirement);
            var newTier = GetTierIndexFromRequirement(target, newRequirement);

            if (!LootGenerationFactory.ApplyUnstableStabilizationTierUpgrades(target, currentTier, newTier))
            {
                return false;
            }

            LootGenerationFactory.ApplyUnstableStabilizationParity(target, currentTier, newTier);

            target.SetProperty(PropertyInt.WieldDifficulty, newRequirement);
            target.Tier = newTier + 1;

            LootGenerationFactory.ApplyUnstableStabilizationPostTierUpgrades(target, currentTier, newTier);
            LootGenerationFactory.RecalculateWorkmanshipFromCurrentStats(target, newTier);
        }
        else
        {
            var currentRequiredLevel = target.WieldDifficulty ?? 1;
            var newRequiredLevel = Math.Max(currentRequiredLevel, GetRequiredLevelFromPlayerTier(player));

            var currentTier = Math.Clamp(LootGenerationFactory.GetTierFromRequiredLevel(currentRequiredLevel) - 1, 0, 7);
            var newTier = Math.Clamp(LootGenerationFactory.GetTierFromRequiredLevel(newRequiredLevel) - 1, 0, 7);

            if (!LootGenerationFactory.ApplyUnstableStabilizationTierUpgrades(target, currentTier, newTier))
            {
                return false;
            }

            LootGenerationFactory.ApplyUnstableStabilizationParity(target, currentTier, newTier);

            target.SetProperty(PropertyInt.WieldDifficulty, newRequiredLevel);
            target.Tier = newTier + 1;

            LootGenerationFactory.ApplyUnstableStabilizationPostTierUpgrades(target, currentTier, newTier);
            LootGenerationFactory.RecalculateWorkmanshipFromCurrentStats(target, newTier);
        }

        return true;
    }

    public static StabilizationTierAnalysis AnalyzeTarget(Player player, WorldObject target)
    {
        if (player == null || target == null)
        {
            return default;
        }

        if (target.ItemType == ItemType.Jewelry)
        {
            var currentRequiredLevel = target.WieldDifficulty ?? 1;
            var newRequiredLevel = Math.Max(currentRequiredLevel, GetRequiredLevelFromPlayerTier(player));
            var currentRequiredTier = Math.Clamp(LootGenerationFactory.GetTierFromRequiredLevel(currentRequiredLevel) - 1, 0, 7);
            var newRequiredTier = Math.Clamp(LootGenerationFactory.GetTierFromRequiredLevel(newRequiredLevel) - 1, 0, 7);

            return new StabilizationTierAnalysis(currentRequiredTier, newRequiredTier, "Level", player.Level ?? 1);
        }

        var usesRequiredLevelPath = UsesRequiredLevelTiering(target);
        var currentRequirement = target.WieldDifficulty ?? (usesRequiredLevelPath ? 1 : 50);
        var newRequirement = Math.Max(currentRequirement, GetMaxRequirementForPlayer(player, target));
        var currentTier = GetTierIndexFromRequirement(target, currentRequirement);
        var newTier = GetTierIndexFromRequirement(target, newRequirement);

        if (usesRequiredLevelPath || target.WieldRequirements != WieldRequirement.RawAttrib || target.WieldSkillType == null)
        {
            return new StabilizationTierAnalysis(currentTier, newTier, "Level", player.Level ?? 1);
        }

        var attribute = (PropertyAttribute)target.WieldSkillType;
        var attributeValue = (int)player.Attributes[attribute].Base;
        return new StabilizationTierAnalysis(currentTier, newTier, attribute.ToString(), attributeValue);
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
}
