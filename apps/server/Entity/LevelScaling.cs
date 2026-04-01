using System;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Entity;

public static class LevelScaling
{
    /* Level scaling is active when two main conditions are met:
     *  -A player has Shrouded active from using a Stone of Shrouding.
     *  -The player is in combat with an enemy that is lower level than them.
     *
     * When level scaling is active, the following adjustments are made to the player and enemy:
     *  -During player attacks on the enemy:
     *   -Player attack skill is scaled down.
     *   -Player damage is scaled down based on monster average max health.
     *   -Monster armor/ward is scaled up.
     *  -During enemy attacks on the player
     *   -Player defense skill is scaled down.
     *   -Monster damage is scaled up based on player average max health.
     *   -Player armor/ward is scaled down.
     *   -Player resistance is scaled down.
     *  -During player heals on other players:
     *   -Player heal amount is scaled down based on the target's level.
     */

    private static readonly ILogger _log = Log.ForContext(typeof(LevelScaling));

    private static readonly int[] AvgPlayerHealthPerTier = [25, 60, 120, 150, 180, 210, 250, 300, 350];
    private static readonly int[] AvgPlayerArmorWardPerTier = [50, 100, 200, 300, 400, 500, 600, 700, 800];
    private static readonly int[] AvgPlayerAttributePerTier = [125, 175, 200, 215, 230, 250, 270, 290, 300];
    private static readonly int[] AvgPlayerAttackSkillPerTier = [10, 60, 90, 120, 150, 180, 225, 300, 500];
    private static readonly int[] AvgPlayerDefenseSkillPerTier = [10, 60, 90, 120, 150, 180, 225, 300, 500];
    private static readonly float[] AvgPlayerResistancePerTier = [1.0f, 1.0f, 0.9f, 0.9f, 0.85f, 0.8f, 0.8f, 0.75f, 0.75f];
    private static readonly float[] AvgPlayerBoostPerTier = [5.0f, 7.5f, 12.5f, 17.5f, 22.5f, 27.5f, 32.5f, 37.5f, 42.5f];

    private static readonly int[] AvgMonsterArmorWardPerTier = [20, 45, 68, 101, 152, 228, 342, 513, 600];
    private static readonly int[] AvgMonsterHealthPerTier = [10, 75, 150, 250, 400, 600, 800, 1000, 2000 ];

    private static readonly float[] AvgTimeToKillMonster = [9.0f, 10.7f, 12.9f, 16.1f, 17.2f, 17.4f, 24.6f, 44.1f];
    private static readonly float[] AvgEnemyDpsPerTier = [ 1.0f, 2.0f, 3.0f, 4.0f, 6.0f, 7.0f, 7.5f, 8.0f, 10.0f];

    public static float GetMonsterDamageDealtHealthScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterDamageDealtHealthScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterDamageDealtHealthScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetPlayerHealthAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerHealthAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetMonsterDamageDealtHealthScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel ({player.Level}): {statAtPlayerLevel}, statAtMonsterLevel ({monster.Level}): {statAtMonsterLevel}, scalarMod: {(float)statAtPlayerLevel / statAtMonsterLevel}"
            );
        }

        return (float)statAtPlayerLevel / statAtMonsterLevel;
    }

    public static float GetMonsterDamageTakenHealthScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterDamageTakenHealthScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterDamageTakenHealthScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetMonsterHealthAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetMonsterHealthAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetMonsterDamageTakenHealthScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return (float)statAtMonsterLevel / statAtPlayerLevel;
    }

    public static float GetMonsterArmorWardScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterArmorWardScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterArmorWardScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetMonsterArmorWardAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetMonsterArmorWardAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetMonsterArmorWardScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtPlayerLevel / statAtMonsterLevel}"
            );
        }

        return (float)statAtPlayerLevel / statAtMonsterLevel;
    }

    public static float GetPlayerArmorWardScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerArmorWardScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerArmorWardScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }

        var statAtPlayerLevel = GetPlayerArmorWardAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerArmorWardAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerArmorWardScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return (float)statAtMonsterLevel / statAtPlayerLevel;
    }

    public static float GetPlayerAttributeScalar(Creature player, Creature monster)
    {
        return 1.0f; // TODO: determine if this is needed

        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerAttributeScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerAttributeScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetPlayerAttributeAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerAttributeAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerAttributeScalar(Player {player.Name}, Monster {monster.Name})"
                    + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return (float)statAtMonsterLevel / statAtPlayerLevel;
    }

    public static float GetPlayerAttackSkillScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerAttackSkillScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerAttackSkillScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }

        var statAtPlayerLevel = GetPlayerAttackSkillAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerAttackSkillAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerAttackSkillScalar(Player {player.Name}, Monster {monster.Name})"
                    + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return (float)statAtMonsterLevel / statAtPlayerLevel;
    }

    public static float GetPlayerDefenseSkillScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerDefenseSkillScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerDefenseSkillScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetPlayerDefenseSkillAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerDefenseSkillAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerDefenseSkillScalar(Player {player.Name}, Monster {monster.Name})"
                    + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return (float)statAtMonsterLevel / statAtPlayerLevel;
    }

    public static float GetPlayerResistanceScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerResistanceScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerResistanceScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetPlayerResistanceAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerResistanceAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerResistanceScalar(Player {player.Name}, Monster {monster.Name})"
                    + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return statAtMonsterLevel / statAtPlayerLevel;
    }

    public static float GetPlayerBoostSpellScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerBoostSpellScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerBoostSpellScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetPlayerBoostAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerBoostAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerBoostSpellScalar(Player {player.Name}, Monster {monster.Name})"
                    + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtPlayerLevel / statAtMonsterLevel}"
            );
        }

        return (float)statAtMonsterLevel / statAtPlayerLevel;
    }

    // --- Private Get At-Level Helpers ---
    private static int GetPlayerHealthAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.5f);

        var stat =
            (AvgPlayerHealthPerTier[range + 1] - AvgPlayerHealthPerTier[range]) * statweight
            + AvgPlayerHealthPerTier[range];

        return (int)stat;
    }

    private static int GetPlayerArmorWardAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.0f);

        var stat =
            (AvgPlayerArmorWardPerTier[range + 1] - AvgPlayerArmorWardPerTier[range]) * statweight
            + AvgPlayerArmorWardPerTier[range];

        return (int)stat;
    }

    private static int GetPlayerAttributeAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 1.0f);

        var stat =
            (AvgPlayerAttributePerTier[range + 1] - AvgPlayerAttributePerTier[range]) * statweight
            + AvgPlayerAttributePerTier[range];

        return (int)stat;
    }

    private static int GetPlayerAttackSkillAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.5f);

        var stat =
            (AvgPlayerAttackSkillPerTier[range + 1] - AvgPlayerAttackSkillPerTier[range]) * statweight
            + AvgPlayerAttackSkillPerTier[range];

        return (int)stat;
    }

    private static int GetPlayerDefenseSkillAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.5f);

        var stat =
            (AvgPlayerDefenseSkillPerTier[range + 1] - AvgPlayerDefenseSkillPerTier[range]) * statweight
            + AvgPlayerDefenseSkillPerTier[range];

        return (int)stat;
    }

    private static float GetPlayerResistanceAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.0f);

        var stat =
            (AvgPlayerResistancePerTier[range + 1] - AvgPlayerResistancePerTier[range]) * statweight
            + AvgPlayerResistancePerTier[range];

        return stat;
    }

    private static float GetPlayerBoostAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.0f);

        var stat =
            (AvgPlayerBoostPerTier[range + 1] - AvgPlayerBoostPerTier[range]) * statweight
            + AvgPlayerBoostPerTier[range];

        return stat;
    }

    private static int GetMonsterHealthAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.5f);

        var stat =
            (AvgMonsterHealthPerTier[range + 1] - AvgMonsterHealthPerTier[range]) * statweight
            + AvgMonsterHealthPerTier[range];

        return (int)stat;
    }

    private static int GetTtkMonsterAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight);

        var stat =
            (AvgTimeToKillMonster[range + 1] - AvgTimeToKillMonster[range]) * statweight
            + AvgTimeToKillMonster[range];

        return (int)stat;
    }

    private static int GetMonsterDpsPerTierAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight);

        var stat =
            (AvgEnemyDpsPerTier[range + 1] - AvgEnemyDpsPerTier[range]) * statweight
            + AvgEnemyDpsPerTier[range];

        return (int)stat;
    }

    private static int GetMonsterArmorWardAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, 0.0f);

        var stat =
            (AvgMonsterArmorWardPerTier[range + 1] - AvgMonsterArmorWardPerTier[range]) * statweight
            + AvgMonsterArmorWardPerTier[range];

        return (int)stat;
    }

    private static void GetRangeAndStatWeight(int level, out int range, out float statweight, float scaleWeight = 1.0f)
    {
        // Use scaleWeight mod to account for the fact that player's receive a large jump in stats the moment
        // they equip a new tier of gear. Progression during a tier of gear won't get them close to the next tier's stats.

        switch (level)
        {
            case < 10:
                range = 0;
                statweight = (level - 10.0f) / 9 * scaleWeight;
                break;
            case < 20:
                range = 1;
                statweight = (level - 10.0f) / 10 * scaleWeight;
                break;
            case < 30:
                range = 2;
                statweight = (level - 20.0f) / 10 * scaleWeight;
                break;
            case < 40:
                range = 3;
                statweight = (level - 30.0f) / 10 * scaleWeight;
                break;
            case < 50:
                range = 4;
                statweight = (level - 40.0f) / 10 * scaleWeight;
                break;
            case < 75:
                range = 5;
                statweight = (level - 50.0f) / 25 * scaleWeight;
                break;
            case < 100:
                range = 6;
                statweight = (level - 75.0f) / 25 * scaleWeight;
                break;
            default:
                range = 7;
                statweight = (level - 100.0f) / 26 * scaleWeight;
                break;
        }

        statweight = Math.Min(statweight, 1.0f);
    }

    private static bool CanScalePlayer(Creature player, Creature monster)
    {
        if (player == null || monster == null)
        {
            return false;
        }

        if (player is not Player)
        {
            return false;
        }

        if (!player.EnchantmentManager.HasSpell((uint)SpellId.Shrouded))
        {
            return false;
        }

        if (!player.Level.HasValue || !monster.Level.HasValue)
        {
            return false;
        }

        if (player.Level <= monster.Level)
        {
            return false;
        }

        if (monster.Level.Value < 10)
        {
            return false;
        }

        return true;
    }

    public static float GetPlayerBoostHealScalarShroudedUpward(Player caster, Player target)
    {
        if (caster == null || target == null)
        {
            return 1.0f;
        }

        if (caster == target)
        {
            return 1.0f;
        }

        if (!caster.EnchantmentManager.HasSpell((uint)SpellId.Shrouded) || !target.EnchantmentManager.HasSpell((uint)SpellId.Shrouded))
        {
            return 1.0f;
        }

        if (!caster.Level.HasValue || !target.Level.HasValue)
        {
            return 1.0f;
        }

        if (caster.Level.Value >= target.Level.Value)
        {
            return 1.0f;
        }

        var statAtCasterLevel = GetPlayerBoostAtLevel(caster.Level.Value);
        var statAtTargetLevel = GetPlayerBoostAtLevel(target.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerBoostHealScalarShroudedUpward(Caster {caster.Name}, Target {target.Name})"
                    + $"\n  statAtCasterLevel ({caster.Level}): {statAtCasterLevel}, statAtTargetLevel ({target.Level}): {statAtTargetLevel}, scalarMod: {(float)statAtTargetLevel / statAtCasterLevel}"
            );
        }

        return (float)statAtTargetLevel / statAtCasterLevel;
    }

    public static int MaxExposeSpellLevel(Creature target)
    {
        var targetLevel = target.Level;

        switch (targetLevel)
        {
            case < 20: return 1;
            case < 30: return 2;
            case < 40: return 3;
            case < 50: return 4;
            case < 75: return 5;
            case < 100: return 6;
            default: return 7;
        }
    }
}
