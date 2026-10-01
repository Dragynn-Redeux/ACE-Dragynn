using System;

namespace ACE.Server.WorldObjects;

public class SkillFormula
{
    // everything else: melee weapons (including finesse), thrown weapons, atlatls
    public const float DefaultMod = 0.011f;

    // bows and crossbows
    public const float BowMod = 0.011f;

    // magic
    public const float SpellMod = 1000.0f;

    // defenses
    public const float ArmorMod = 100.0f;
    public const float ShieldMod = 2000.0f;
    public const float WardMod = 100.0f;

    public static float GetAttributeMod(int currentAttribute, ACE.Entity.Enum.Skill skill = ACE.Entity.Enum.Skill.None)
    {
        var factor = skill == ACE.Entity.Enum.Skill.Bow ? BowMod : DefaultMod;

        return Math.Max(1.0f + (currentAttribute - 55) * factor, 1.0f);
    }

    /// <summary>
    /// Converts SpellMod from an additive linear value
    /// to a scaled damage multiplier
    /// </summary>
    public static float CalcSpellMod(int magicSkill)
    {
        return 1 / (SpellMod / (magicSkill + SpellMod));
    }

    /// <summary>
    /// Converts AL from an additive linear value
    /// to a scaled damage multiplier
    /// </summary>
    public static float CalcArmorMod(float armorLevel)
    {
        if (armorLevel > 0)
        {
            return ArmorMod / (armorLevel + ArmorMod);
        }
        else if (armorLevel < 0)
        {
            return 1.0f - armorLevel / ArmorMod;
        }
        else
        {
            return 1.0f;
        }
    }

    /// <summary>
    /// Converts Ward Level from an additive linear value
    /// to a scaled damage multiplier
    /// </summary>
    public static float CalcWardMod(float wardLevel)
    {
        if (wardLevel > 0)
        {
            return WardMod / (wardLevel + WardMod);
        }
        else if (wardLevel < 0)
        {
            return 1.0f - wardLevel / WardMod;
        }
        else
        {
            return 1.0f;
        }
    }

    private static readonly int[] avgEnemyAttackSkillPerTier = { 10, 50, 150, 200, 250, 300, 400, 500, 1000 };
    private static readonly int[] avgShieldLevelPerTier = { 45, 90, 180, 270, 360, 450, 540, 630, 720 };

    public static float CalcShieldMod(float shieldLevel, uint attackerSkill, uint attackerTier, int level)
    {
        var targetAttackSkill = GetTargetAttackSkill(attackerSkill, attackerTier, level);
        var skillCheck = (float)SkillCheck.GetSkillChance((int)shieldLevel, (int)targetAttackSkill, 0.005f);

        const float maxShieldMod = 0.25f;

        //Console.WriteLine($"atk: {attackerSkill}, shield: {shieldLevel}, targetAttack: {targetAttackSkill}, mod: {1 - (skillCheck * maxShieldMod)}");

        return 1 - (skillCheck * maxShieldMod);
    }

    private static double GetTargetAttackSkill(uint attackerSkill, uint attackerTier, int level)
    {
        var scalar = new double[9];
        for (var i = 0; i < scalar.Length; i++)
        {
            scalar[i] = (double)avgShieldLevelPerTier[i] / avgEnemyAttackSkillPerTier[i];
        }

        var weightedScalar = scalar[attackerTier] +
                             ((scalar[attackerTier + 1] - scalar[attackerTier]) * LevelWeight(attackerTier, level));

        var targetAttackSkill = attackerSkill * weightedScalar;
        return targetAttackSkill;
    }

    private static double LevelWeight(uint tier, int level)
    {
        switch (tier)
        {
            default:
                return (double)level / 9;
            case 1:
                return (double)(level - 10) / 10;
            case 2:
                return (double)(level - 20) / 10;
            case 3:
                return (double)(level - 30) / 10;
            case 4:
                return (double)(level - 40) / 10;
            case 5:
                return (double)(level - 50) / 25;
            case 6:
                return (double)(level - 75) / 25;
            case 7:
                return (double)(level - 100) / 25;
        }
    }
}
