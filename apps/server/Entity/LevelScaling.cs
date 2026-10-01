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
     *   -Player defense skill is reduced by the difference in average defense skill between the two levels.
     *   -Monster damage is scaled up based on player average max health.
     *   -Player armor/ward mitigation is scaled down.
     *   -Player resistance is scaled down.
     *  -During player heals on other players:
     *   -Player heal amount is scaled down based on the target's level.
     *
     * Player defense, armor/ward, and resistance are scaled so that however far above or below the average a
     * player's gear is at their own level, it's worth the same at every lower level too. See
     * GetPlayerArmorWardModScalar() and GetScaledPlayerDefenseSkill().
     */

    private static readonly ILogger _log = Log.ForContext(typeof(LevelScaling));

    private static readonly int[] AvgPlayerHealthPerTier = [75, 110, 170, 200, 230, 260, 300, 350, 400];
    private static readonly int[] AvgPlayerArmorWardPerTier = [50, 100, 200, 300, 400, 500, 600, 700, 800];
    private static readonly int[] AvgPlayerAttributePerTier = [100, 125, 175, 200, 215, 230, 250, 270, 290];
    // Monster attack/defense are set a flat amount above these (Creature_ArchetypeSystem's EnemySkillGap, built from its
    // avgPlayerPhysicalMagicDefense), so a Shrouded player scaled to a monster's level gets the same evade odds a native
    // does. Keep these matching avgPlayerPhysicalMagicDefense.
    private static readonly int[] AvgPlayerAttackSkillPerTier = [10, 60, 90, 120, 150, 180, 225, 300, 500];
    private static readonly int[] AvgPlayerDefenseSkillPerTier = [10, 60, 90, 120, 150, 180, 225, 300, 500];
    private static readonly float[] AvgPlayerResistancePerTier = [1.0f, 1.0f, 0.9f, 0.9f, 0.85f, 0.8f, 0.8f, 0.75f, 0.75f];
    private static readonly float[] AvgPlayerBoostPerTier = [5.0f, 7.5f, 12.5f, 17.5f, 22.5f, 27.5f, 32.5f, 37.5f, 42.5f];

    private static readonly int[] AvgMonsterArmorWardPerTier = [10, 20, 45, 68, 101, 152, 228, 342, 513];
    private static readonly int[] AvgMonsterHealthPerTier = [10, 100, 200, 300, 500, 750, 1000, 1500, 2000];

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

        var scalarMod = (float)statAtMonsterLevel / statAtPlayerLevel;

        // Fellowship-required dungeons: on top of the normal health-ratio scaling, additionally divide by a
        // per-dungeon-tier constant so a Shrouded player's damage-dealt TTK against this tier approaches
        // native level-50 TTK instead of the tier's own (much faster) native pace. Monster Armor/Ward is
        // deliberately NOT scaled up for these landblocks (see GetMonsterArmorWardScalar) -- this health-only
        // ratio is close enough on its own. Natives are unaffected: CanScalePlayer already excludes them.
        // See docs/fellowship-dungeon-scaling.md for the derivation, and GetFellowshipDealtTtkDivisor() below.
        if (monster.CurrentLandblock?.IsFellowshipRequired() == true)
        {
            scalarMod /= GetFellowshipDealtTtkDivisor(monster.Level.Value);
        }

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetMonsterDamageTakenHealthScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {scalarMod}"
            );
        }

        return scalarMod;
    }

    /// <summary>
    /// Per-dungeon-tier divisor applied in GetMonsterDamageTakenHealthScalar() for fellowship-required
    /// dungeons. Derived from native TTK = ArchetypeHealth / (real weapon DPS * real attribute mod * armor
    /// mitigation) at each tier -- using GetWeaponBaseDps, the real Str-by-tier progression, and
    /// Creature_ArchetypeSystem's enemyHealth/enemyArmorWard -- as the ratio of native level-50 TTK to each
    /// lower tier's own native TTK. Hand-computed, not derived at runtime: recompute if the reference level
    /// (currently native level 50) or any of the source tables above change. See
    /// docs/fellowship-dungeon-scaling.md for the full derivation and its known limits at high shrouded levels.
    /// </summary>
    private static float GetFellowshipDealtTtkDivisor(int monsterLevel)
    {
        switch (monsterLevel)
        {
            case 10:
                return 1.906f;
            case 20:
                return 1.551f;
            case 30:
                return 1.464f;
            case 40:
                return 1.171f;
            case 50:
                return 1.000f;
            default:
                return 1.0f;
        }
    }

    public static float GetMonsterArmorWardScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        // See the matching guard in GetMonsterDamageTakenHealthScalar() -- fellowship-required dungeons already
        // pin this monster's actual Armor/Ward to a fixed reference tier for every visitor, so scaling it again
        // here off the monster's stale Level property would double-count on top of an already-normalized value.
        if (monster.CurrentLandblock?.IsFellowshipRequired() == true)
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

    /// <summary>
    /// Scales a player's armor/ward LEVEL. Still used for shields (Creature.GetShieldMod) -- body armor and
    /// ward use GetPlayerArmorWardModScalar(), which scales the mitigation instead.
    /// </summary>
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

    /// <summary>
    /// Scales a player's armor/ward mitigation: multiply this into the armor/ward mod AFTER
    /// SkillFormula.CalcArmorMod()/CalcWardMod(), not into the armor/ward level before it.
    ///
    /// Multiplying the level instead (GetPlayerArmorWardScalar) flattens the 100 / (100 + AL) curve for a
    /// scaled-down player, which strips most of the value of above-average armor in much lower-level content
    /// (a well-geared level 50 took up to 1.7x more damage in a level-10 capstone than in a level-50 one).
    /// Scaling the mitigation keeps a player's armor exactly as valuable, relative to the average at their own
    /// level, at every lower level: monster damage is authored against CalcArmorMod(average AL at the monster's
    /// level), so the monster's level cancels out. A player with exactly average armor gets the same result
    /// either way. Works for ward too, since SkillFormula.ArmorMod and WardMod are both 100.
    /// </summary>
    public static float GetPlayerArmorWardModScalar(Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return 1.0f;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerArmorWardModScalar() - Player ({Player}) level is null. Scaling set to x1.0.", player.Name);
            return 1.0f;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetPlayerArmorWardModScalar() - Monster ({Monster}) level is null. Scaling set to x1.0.", monster.Name);
            return 1.0f;
        }

        var statAtPlayerLevel = GetPlayerArmorWardAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerArmorWardAtLevel(monster.Level.Value);

        var scalarMod = SkillFormula.CalcArmorMod(statAtMonsterLevel) / SkillFormula.CalcArmorMod(statAtPlayerLevel);

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetPlayerArmorWardModScalar(Player {player.Name}, Monster {monster.Name})"
                + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, scalarMod: {scalarMod}"
            );
        }

        return scalarMod;
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

    /// <summary>
    /// Returns a player's defense skill scaled down to the monster's level, by subtracting the difference in
    /// average defense skill between the two levels rather than multiplying by their ratio.
    ///
    /// Evade and resist chances only depend on the gap between attack and defense skill (SkillCheck), and the
    /// monster's attack skill isn't scaled. Multiplying shrank a player's above-average defense along with the
    /// rest of the skill -- a level 50 with 300 defense kept only a third of their 120-point advantage in a
    /// level-10 capstone. Subtracting keeps the whole advantage, so above-average defense is worth the same at
    /// every lower level. A player with exactly average defense gets the same result either way.
    /// </summary>
    public static uint GetScaledPlayerDefenseSkill(uint defenseSkill, Creature player, Creature monster)
    {
        if (!CanScalePlayer(player, monster))
        {
            return defenseSkill;
        }

        if (player.Level == null)
        {
            _log.Error("LevelScaling.GetScaledPlayerDefenseSkill() - Player ({Player}) level is null. Scaling skipped.", player.Name);
            return defenseSkill;
        }

        if (monster.Level == null)
        {
            _log.Error("LevelScaling.GetScaledPlayerDefenseSkill() - Monster ({Monster}) level is null. Scaling skipped.", monster.Name);
            return defenseSkill;
        }

        var statAtPlayerLevel = GetPlayerDefenseSkillAtLevel(player.Level.Value);
        var statAtMonsterLevel = GetPlayerDefenseSkillAtLevel(monster.Level.Value);

        var scaledSkill = (uint)Math.Max(0, (int)defenseSkill - (statAtPlayerLevel - statAtMonsterLevel));

        if (PropertyManager.GetBool("debug_level_scaling_system").Item)
        {
            Console.WriteLine(
                $"\nGetScaledPlayerDefenseSkill(Player {player.Name}, Monster {monster.Name})"
                    + $"\n  statAtPlayerLevel: {statAtPlayerLevel}, statAtMonsterLevel: {statAtMonsterLevel}, defenseSkill: {defenseSkill} -> {scaledSkill}"
            );
        }

        return scaledSkill;
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

        if (player.Level < monster.Level)
        {
            return false;
        }

        // Equal levels only scale in fellowship-required (level-scaled) dungeons -- these can require every
        // visitor to be Shrouded via Portal.RequiresShrouded, including natives fighting at-level enemies, so
        // they get the same TTK normalization as anyone else. Everywhere else, equal levels never scaled
        // before and still don't -- this only changes behavior inside fellowship-required landblocks.
        if (player.Level == monster.Level && monster.CurrentLandblock?.IsFellowshipRequired() != true)
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
