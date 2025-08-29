using System;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Entity;

public static class LevelScaling
{
    /* Level scaling is active when two main conditions are met:
     *  -A player has a Weakening Curse active from using a Stone of Shrouding.
     *  -The player is in combat with an enemy that is lower level than them.
     *
     * When level scaling is active, the following adjustments are made to the player and enemy:
     *  -During player attacks on the enemy:
     *   -Player damage is scaled down based on monster average max health.
     *   -Player attack skill is scaled down.
     *   -Player main attribute is scaled down for attribute mod calculations.
     *   -Monster armor is scaled up.
     *   -Monster ward is scaled up.
     *  -During enemy attacks on the player
     *   -Monster damage is scaled up based on player average max health.
     *   -Player defense skill is scaled down.
     *   -Player armor is scaled down.
     *   -Player ward is scaled down.
     *   -Player resistance is scaled down.
     *  -During player heals on other players:
     *   -Player heal amount is scaled down based on the target's level.
     *
     *   NOTE: May need an additional modifier to add more difficulty to higher level players if imbues and other crafting are too good.
     *   Handled previously with this formula: (float gapMod = 1f - (float)(levelGap / 10f) / 12.5f)
     */

    private static readonly ILogger _log = Log.ForContext(typeof(LevelScaling));

    private static readonly int[] AvgPlayerHealthPerTier = [50, 100, 130, 160, 190, 220, 250, 350];
    private static readonly int[] AvgPlayerArmorWardPerTier = [50, 100, 200, 300, 400, 500, 600, 700];
    private static readonly int[] AvgPlayerAttributePerTier = [125, 175, 200, 215, 230, 250, 270, 290];
    private static readonly int[] AvgPlayerAttackSkillPerTier = [75, 150, 175, 200, 225, 275, 350, 500];
    private static readonly int[] AvgPlayerDefenseSkillPerTier = [75, 150, 175, 200, 225, 275, 350, 500];
    private static readonly float[] AvgPlayerResistancePerTier = [1.0f, 0.9f, 0.9f, 0.85f, 0.85f, 0.8f, 0.8f, 0.75f, 0.75f];
    private static readonly int[] AvgPlayerBoostPerTier = [10, 15, 20, 25, 30, 35, 40, 40];

    private static readonly int[] AvgMonsterArmorWardPerTier = [20, 45, 68, 101, 152, 228, 342, 513];
    private static readonly int[] AvgMonsterHealthPerTier = [40, 100, 200, 300, 400, 500, 1000, 2000];

    private static readonly float[] AvgTimeToKillMonster = [9.0f, 10.7f, 12.9f, 16.1f, 17.2f, 17.4f, 24.6f, 44.1f];
    private static readonly float[] AvgEnemyDpsPerTier = [ 1.0f, 1.5f, 2.0f, 3.0f, 4.0f, 5.0f, 7.5f, 10.0f, 15.0f ];

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
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtPlayerLevel / statAtMonsterLevel}"
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

    public static float GetMonsterDamageTakenTtkScalar(Creature player, Creature monster)
    {
        return 1.0f; // TODO: reassess if this is needed

        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterDamageTakenTtkScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetMonsterDamageTakenTtkScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }


        var statAtPlayerLevel = GetTtkMonsterAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetTtkMonsterAtLevel(monster.Level.Value);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetMonsterDamageTakenTtkScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {(float)statAtMonsterLevel / statAtPlayerLevel}"
            );
        }

        return (float)statAtPlayerLevel / statAtMonsterLevel;
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
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerHealthPerTier[range + 1] - AvgPlayerHealthPerTier[range]) * statweight
            + AvgPlayerHealthPerTier[range];

        return (int)stat;
    }

    private static int GetPlayerArmorWardAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerArmorWardPerTier[range + 1] - AvgPlayerArmorWardPerTier[range]) * statweight
            + AvgPlayerArmorWardPerTier[range];

        return (int)stat;
    }

    private static int GetPlayerAttributeAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerAttributePerTier[range + 1] - AvgPlayerAttributePerTier[range]) * statweight
            + AvgPlayerAttributePerTier[range];

        return (int)stat;
    }

    private static int GetPlayerAttackSkillAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerAttackSkillPerTier[range + 1] - AvgPlayerAttackSkillPerTier[range]) * statweight
            + AvgPlayerAttackSkillPerTier[range];

        return (int)stat;
    }

    private static int GetPlayerDefenseSkillAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerDefenseSkillPerTier[range + 1] - AvgPlayerDefenseSkillPerTier[range]) * statweight
            + AvgPlayerDefenseSkillPerTier[range];

        return (int)stat;
    }

    private static float GetPlayerResistanceAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerResistancePerTier[range + 1] - AvgPlayerResistancePerTier[range]) * statweight
            + AvgPlayerResistancePerTier[range];

        return stat;
    }

    private static int GetPlayerBoostAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight, true);

        var stat =
            (AvgPlayerBoostPerTier[range + 1] - AvgPlayerBoostPerTier[range]) * statweight
            + AvgPlayerBoostPerTier[range];

        return (int)stat;
    }

    private static int GetMonsterHealthAtLevel(int level)
    {
        GetRangeAndStatWeight(level, out var range, out var statweight);

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
        GetRangeAndStatWeight(level, out var range, out var statweight);

        var stat =
            (AvgMonsterArmorWardPerTier[range + 1] - AvgMonsterArmorWardPerTier[range]) * statweight
            + AvgMonsterArmorWardPerTier[range];

        return (int)stat;
    }

    private static void GetRangeAndStatWeight(int level, out int range, out float statweight, bool player = false)
    {
        // Use playerScalar mod to account for the fact that player's receive a large jump in stats the moment
        // they equip a new tier of gear. Progression during a tier of gear won't get them close to the next tier's stats.
        // Perhaps up to 50% of the way.
        var playerScalar = player ? 0.5f : 1.0f;

        switch (level)
        {
            case < 20:
                range = 0;
                statweight = (level - 10.0f) / 10 * playerScalar;
                break;
            case < 30:
                range = 1;
                statweight = (level - 20.0f) / 10 * playerScalar;
                break;
            case < 40:
                range = 2;
                statweight = (level - 30.0f) / 10 * playerScalar;
                break;
            case < 50:
                range = 3;
                statweight = (level - 40.0f) / 10 * playerScalar;
                break;
            case < 75:
                range = 4;
                statweight = (level - 50.0f) / 25 * playerScalar;
                break;
            case < 100:
                range = 5;
                statweight = (level - 75.0f) / 25 * playerScalar;
                break;
            default:
                range = 6;
                statweight = (level - 100.0f) / 26 * playerScalar;
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

        if (!player.EnchantmentManager.HasSpell((uint)SpellId.CurseWeakness1))
        {
            return false;
        }

        if (player.Level.HasValue && monster.Level.HasValue && player.Level <= monster.Level)
        {
            return false;
        }

        if (monster.Level < 10)
        {
            return false;
        }

        return true;
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
