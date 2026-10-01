using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories.Tables;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    private static bool DebugArchetypeSystem = false;

    // Stat ranges by tier
    private static readonly int[] enemyHealth = { 10, 100, 200, 300, 500, 750, 1000, 1500, 2000 };
    private static readonly int[] enemyStaminaMana = { 20, 100, 150, 225, 325, 450, 650, 950, 1250 };
    private static readonly int[] enemyHealthRegen = { 1, 2, 5, 10, 20, 30, 40, 50, 100 };
    private static readonly int[] enemyStaminaManaRegen = { 1, 2, 5, 10, 15, 20, 25, 30, 50 };

    private static readonly int[] enemyArmorWard = { 10, 20, 45, 68, 101, 152, 228, 342, 513 };
    private static readonly int[] enemyAssessDeception = { 10, 60, 100, 150, 175, 200, 250, 350, 500 };
    private static readonly int[] enemyRun = { 10, 100, 150, 200, 250, 300, 400, 500, 600 };

    private static readonly float[] enemyDamage = { 1.0f, 2.0f, 3.0f, 4.5f, 6.0f, 7.0f, 8.0f, 9.0f, 10.0f }; // percentage of player health to be taken per second after all stats (of player and enemy) are considered

    private static readonly int[] avgPlayerHealth = { 75, 110, 170, 200, 230, 260, 300, 350, 400 };
    private static readonly float[] avgPlayerArmorReduction = { 0.6667f, 0.5000f, 0.3333f, 0.2500f, 0.2000f, 0.1667f, 0.1429f, 0.1250f, 0.1111f };
    private static readonly float[] avgPlayerLifeProtReduction = { 1.0f, 1.0f, 0.9f, 0.9f, 0.85f, 0.8f, 0.8f, 0.75f, 0.75f };
    private static readonly int[] avgPlayerPhysicalMagicDefense = { 10, 60, 90, 120, 150, 180, 225, 300, 500 };

    // Monster attack and defense are a flat EnemySkillGap above the average player's skill, which is the same for attack and
    // defense (avgPlayerPhysicalMagicDefense, and LevelScaling's AvgPlayerAttackSkillPerTier/AvgPlayerDefenseSkillPerTier).
    // Evade rolls (SkillCheck.GetSkillChance) depend on the difference between the two skills, not their ratio, so a flat
    // gap gives the same evade odds at every tier. At 50, monsters evade ~82% of the average player's attacks (50% at even
    // skill), which makes them ~27% slower to kill. Monster damage is tuned to how often it hits (GetNewBaseDamage), so the
    // gap on attack changes how often monsters hit the average player, not how much damage they deal to them.
    // Declared after avgPlayerPhysicalMagicDefense, since static fields are initialized in order.
    private const int EnemySkillGap = 50;
    private static readonly int[] enemyAttack = avgPlayerPhysicalMagicDefense.Select(skill => skill + EnemySkillGap).ToArray();
    private static readonly int[] enemyDefense = avgPlayerPhysicalMagicDefense.Select(skill => skill + EnemySkillGap).ToArray();

    private void SetSkills(
        int tier,
        float statWeight,
        double toughness,
        double physicality,
        double dexterity,
        double magic,
        double intelligence
    )
    {
        if (DebugArchetypeSystem)
        {
            Console.WriteLine(
                $"\n\n---- {Name} ----  \n\n-- SetSkills() for {Name} ({WeenieClassId}) (statWeight: {statWeight}) --"
            );
        }

        if ((OverrideArchetypeSkills ?? false) != true)
        {
            AdjustAttributesForArchetypeConstraints(tier, statWeight, physicality, dexterity, magic, intelligence);

            // Martial Weapons Skill
            {
                var newSkill = GetNewMeleeAttackSkill(tier, statWeight, physicality, dexterity);

                var skillType = Skill.MartialWeapons;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.MartialWeapons] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Unarmed Attack Skill
            {
                var newSkill = GetNewUnarmedCombatSkill(tier, statWeight, physicality, dexterity);

                var skillType = Skill.UnarmedCombat;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.UnarmedCombat] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Dagger Skill
            {
                var newSkill = GetNewDaggerSkill(tier, statWeight, physicality, dexterity);

                var skillType = Skill.Dagger;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.Dagger] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Staff Skill
            {
                var newSkill = GetNewStaffSkill(tier, statWeight, physicality, dexterity);

                var skillType = Skill.Staff;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.Staff] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Missile Attack Skill
            {
                var newSkill = GetNewMissileAttackSkill(tier, statWeight, dexterity);

                var skillType = Skill.Bow;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.Bow] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Thrown Weapons Skill
            {
                var newSkill = GetNewMissileAttackSkill(tier, statWeight, dexterity);

                var skillType = Skill.ThrownWeapon;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.ThrownWeapon] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // War Magic Skill
            {
                var newSkill = GetNewWarMagicSkill(tier, statWeight, magic);

                var skillType = Skill.WarMagic;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.WarMagic] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Life Magic Skill
            {
                var newSkill = GetNewLifeMagicSkill(tier, statWeight, magic);

                var skillType = Skill.LifeMagic;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.LifeMagic] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Physical Defense
            {
                var newSkill = GetNewPhysicalDefenseSkill(tier, statWeight, physicality, dexterity);

                var skillType = Skill.PhysicalDefense;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Specialized
                };

                Skills[Skill.PhysicalDefense] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Missile Defense
            //{
            //    var newSkill = GetNewMissileDefenseSkill(tier, statWeight, toughness, dexterity);

            //    var skillType = ACE.Entity.Enum.Skill.MissileDefense;

            //    var propertiesSkill = new PropertiesSkill() { InitLevel = newSkill, SAC = ACE.Entity.Enum.SkillAdvancementClass.Specialized };

            //    Skills[ACE.Entity.Enum.Skill.MissileDefense] = new CreatureSkill(this, skillType, propertiesSkill);
            //}

            // Magic Defense
            {
                var newSkill = GetNewMagicDefenseSkill(tier, statWeight, magic);

                var skillType = Skill.MagicDefense;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Specialized
                };

                Skills[Skill.MagicDefense] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Perception
            {
                var newSkill = GetNewPerceptionSkill(tier, statWeight, intelligence);

                var skillType = Skill.Perception;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.Perception] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Deception
            {
                var newSkill = GetNewDeceptionSkill(tier, statWeight, intelligence);

                var skillType = Skill.Deception;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.Deception] = new CreatureSkill(this, skillType, propertiesSkill);
            }

            // Run
            {
                var newSkill = GetNewRunSkill(tier, statWeight, dexterity);

                var skillType = Skill.Run;

                var propertiesSkill = new PropertiesSkill()
                {
                    InitLevel = newSkill,
                    SAC = SkillAdvancementClass.Trained
                };

                Skills[Skill.Run] = new CreatureSkill(this, skillType, propertiesSkill);
            }
        }
    }

    // Round value to the nearest multiple of 5, capped so the result never exceeds value.
    // Cases where the true nearest-5 would overshoot (value % 5 == 3 or 4) fall back to floor-to-5.
    private static int NearestFiveNotExceeding(int value)
    {
        var candidate = ((value + 2) / 5) * 5;
        return candidate > value ? candidate - 5 : candidate;
    }

    private void SetVitals(
        int tier,
        float statWeight,
        double toughness,
        double physicality,
        double dexterity,
        double magic
    )
    {
        if (DebugArchetypeSystem)
        {
            Console.WriteLine($"\n-- SetVitals() for {Name} ({WeenieClassId}) (statWeight: {statWeight}) --");
        }

        // Health
        {
            if ((OverrideArchetypeHealth ?? false) != true)
            {
                var newVital = GetNewHealthLevel(tier, statWeight, toughness, physicality);

                Vitals[PropertyAttribute2nd.MaxHealth].StartingValue = newVital;
                Health.Current = Health.MaxValue;
            }
        }

        // Health Regen
        {
            if ((OverrideArchetypeHealth ?? false) != true)
            {
                var newVital = GetNewHealthRegenLevel(tier, statWeight, toughness, physicality);

                HealthRate = newVital;
            }
        }

        // Stamina
        {
            if ((OverrideArchetypeStamina ?? false) != true)
            {
                var newVital = GetNewStaminaLevel(tier, statWeight, physicality, dexterity);

                Vitals[PropertyAttribute2nd.MaxStamina].StartingValue = newVital;
                Stamina.Current = Stamina.MaxValue;
            }
        }

        // Stamina Regen
        {
            if ((OverrideArchetypeStamina ?? false) != true)
            {
                var newVital = GetNewStaminaRegenLevel(tier, statWeight, physicality, dexterity);

                StaminaRate = newVital;
            }
        }

        // Mana
        {
            if ((OverrideArchetypeMana ?? false) != true)
            {
                var newVital = GetNewManaLevel(tier, statWeight, magic);

                Vitals[PropertyAttribute2nd.MaxMana].StartingValue = newVital;
                Mana.Current = Mana.MaxValue;
            }
        }

        // Mana Regen
        {
            if ((OverrideArchetypeMana ?? false) != true)
            {
                var newVital = GetNewManaRegenLevel(tier, statWeight, magic);

                ManaRate = newVital;
            }
        }
    }

    private void SetDamageArmorWard(
        int tier,
        float statWeight,
        double toughness,
        double physicality,
        double magic,
        double lethality
    )
    {
        if (DebugArchetypeSystem)
        {
            Console.WriteLine($"\n-- SetDamageArmorAegus() for {Name} ({WeenieClassId}) (statWeight: {statWeight}) --");
        }

        // Damage + Armor
        {
            // Armor Level
            var newArmorLevel = GetNewArmorLevel(tier, statWeight, toughness, physicality);

            // Damage Calculation
            var baseAvgDamage = GetNewBaseDamage(tier, statWeight, lethality);

            var damageHigh = Math.Max((int)(baseAvgDamage / (1.0f - (0.75f / 2))), 1);
            var damageMed = Math.Max((int)(baseAvgDamage / (1.0f - (0.5f / 2))), 1);
            var damageLow = Math.Max((int)(baseAvgDamage / (1.0f - (0.3f / 2))), 1);

            if (DebugArchetypeSystem)
            {
                Console.WriteLine(
                    $"Final: Damage(0.75): {damageHigh}, Damage(0.5): {damageMed}, Damage(0.3): {damageLow}\n"
                        + $"-Update Armor/Damage Body Values"
                );
            }

            // Set Base Damage (for use with weapons)
            Damage = Convert.ToInt32(baseAvgDamage);

            if (GetEquippedWeapon() is not null && Damage is not null)
            {
                MutateWeaponForArchetype(GetEquippedWeapon(), Damage.Value);
            }

            if (GetEquippedOffHand() is not null && Damage is not null)
            {
                MutateWeaponForArchetype(GetEquippedOffHand(), Damage.Value);
            }

            // Set Spell Damage Multiplier
            ArchetypeSpellDamageMultiplier = GetArchetypeSpellDamageMultiplier(tier, statWeight, lethality);

            // Set Body Parts
            if (Weenie != null)
            {
                if (Weenie.PropertiesBodyPart != null)
                {
                    foreach (var kvp in GetOwnBodyParts())
                    {
                        var bodyPart = kvp.Value;

                        // Armor
                        if (DebugArchetypeSystem)
                        {
                            Console.WriteLine($" Old/New Armor: {bodyPart.BaseArmor} -> {newArmorLevel}");
                        }

                        bodyPart.BaseArmor = newArmorLevel;

                        // Damage
                        if (bodyPart.DVal > 0)
                        {
                            var oldDamage = bodyPart.DVal;

                            if (bodyPart.DVar == 0.75 || bodyPart.DVar == 0.9)
                            {
                                bodyPart.DVal = damageHigh;
                            }
                            else if (bodyPart.DVar == 0.5)
                            {
                                bodyPart.DVal = damageMed;
                            }
                            else
                            {
                                bodyPart.DVal = damageLow;
                            }

                            if (DebugArchetypeSystem)
                            {
                                Console.WriteLine($" Old/New Damage: {oldDamage} -> {bodyPart.DVal}");
                            }
                        }
                    }
                }
                else
                {
                    _log.Warning(
                        "Creature_ArchetypeSystem.SetDamageArmorWard() - Unable to set damage/armor on bodyparts of {Name}. Body parts table may be missing from this wcid.",
                        Name
                    );
                }
            }
            else
            {
                _log.Warning(
                    "Creature.SetDamageArmorWard() - Weenie == null for {Name} ({WeenieClassId}). Cannot set Damage/Armor for this creature.",
                    Name,
                    WeenieClassId
                );
            }
        }

        // Ward
        {
            var tweakedWard = GetNewWardLevel(tier, statWeight, toughness, magic);

            WardLevel = tweakedWard;
        }
    }

    /// <summary>
    /// A creature made from a weenie shares the weenie's body part table with every other creature of its wcid
    /// (WeenieConverter.ConvertToBiota, referenceWeenieCollectionsForCommonProperties). The archetype system sets
    /// damage and armor per creature, so writing them into the shared table would let the last creature of a wcid
    /// to spawn anywhere decide the damage and armor of all of them - dungeon mods and frigid bonuses included.
    /// This gives the creature its own copy the first time it's needed.
    /// </summary>
    private IDictionary<CombatBodyPart, PropertiesBodyPart> GetOwnBodyParts()
    {
        if (ReferenceEquals(Biota.PropertiesBodyPart, Weenie?.PropertiesBodyPart))
        {
            Biota.PropertiesBodyPart = Weenie.PropertiesBodyPart.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());
        }

        return Biota.PropertiesBodyPart;
    }

    private void SetXp(double difficltyMod)
    {
        if ((OverrideArchetypeXp ?? false) != true)
        {
            var killXpMod = KillXpMod ?? 1.0f;

            KillXpMod = killXpMod * difficltyMod;

            if (DebugArchetypeSystem)
            {
                Console.Write(
                    $"\n-- SetXpMod() for {Name} ({WeenieClassId}) --\n"
                        + $" Old/New Xp mod: {killXpMod} -> {KillXpMod.Value}"
                );
            }
        }
    }

    // HELPER FUNCTIONS

    private void AdjustAttributesForArchetypeConstraints(int tier, float statWeight, double physicality, double dexterity, double magic, double intelligence)
    {
        // Each attribute is set so that (attribute / divisor) + minInitLevel == tweakedSkill.
        // Helper functions then compute InitLevel = tweakedSkill - skillFromAttributes, which equals minInitLevel
        // for the binding skill and is naturally higher for any less-constrained skill sharing the same attribute.
        var minInitLevel = (Level ?? 1) * 2;

        var attackTarget = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var defenseTarget = enemyDefense[tier] + (enemyDefense[tier + 1] - enemyDefense[tier]) * statWeight;
        var assDeceptTarget = enemyAssessDeception[tier] + (enemyAssessDeception[tier + 1] - enemyAssessDeception[tier]) * statWeight;
        var runTarget = enemyRun[tier] + (enemyRun[tier + 1] - enemyRun[tier]) * statWeight;

        // Strength: MeleeAttack uses Strength.Base / 2
        {
            var tweakedSkill = (int)(attackTarget * (physicality + dexterity) / 2);
            Strength.StartingValue = (uint)Math.Max(1, NearestFiveNotExceeding((tweakedSkill - minInitLevel) * 2));
        }

        // Coordination: binding constraint is min of melee (phys+dex)/2 and missile dexterity multipliers
        {
            var tweakedSkill = (int)(attackTarget * Math.Min((physicality + dexterity) / 2, dexterity));
            Coordination.StartingValue = (uint)Math.Max(1, NearestFiveNotExceeding((tweakedSkill - minInitLevel) * 2));
        }

        // Self: WarMagic and LifeMagic both use Self.Base / 2 with the same magic multiplier
        {
            var tweakedSkill = (int)(attackTarget * magic);
            Self.StartingValue = (uint)Math.Max(1, NearestFiveNotExceeding((tweakedSkill - minInitLevel) * 2));
        }

        // Focus: Perception and Deception both use Focus.Base / 2 with the same intelligence multiplier
        {
            var tweakedSkill = (int)(assDeceptTarget * intelligence);
            Focus.StartingValue = (uint)Math.Max(1, NearestFiveNotExceeding((tweakedSkill - minInitLevel) * 2));
        }

        // Quickness: Run uses Quickness.Base directly (divisor = 1)
        {
            var tweakedSkill = (int)(runTarget * dexterity);
            Quickness.StartingValue = (uint)Math.Max(1, NearestFiveNotExceeding(tweakedSkill - minInitLevel));
        }

        // Physical Defense compound: (Coordination.Base + Quickness.Base) / 4.
        // After individual assignments above the combined contribution may still overshoot; reduce both equally if so.
        {
            var tweakedSkill = (int)(defenseTarget * dexterity);
            var skillFromAttributes = ((int)Coordination.Base + (int)Quickness.Base) / 4;
            if (skillFromAttributes > tweakedSkill - minInitLevel)
            {
                var diff = tweakedSkill - minInitLevel - skillFromAttributes;
                var halfAdjustment = diff * 2;
                Coordination.StartingValue = (uint)Math.Max(1, (int)Coordination.StartingValue + halfAdjustment);
                Quickness.StartingValue = (uint)Math.Max(1, (int)Quickness.StartingValue + halfAdjustment);
                _log.Debug(
                    "Creature.AdjustAttributesForArchetypeConstraints() - Coordination and Quickness reduced by {Adjustment} each for {Name} ({WeenieClassId}) to satisfy PhysicalDefense constraint.",
                    halfAdjustment, Name, WeenieClassId
                );
            }
        }

        // Magic Defense compound: (Focus.Base + Self.Base) / 4.
        // Same overflow check as Physical Defense.
        {
            var tweakedSkill = (int)(defenseTarget * magic);
            var skillFromAttributes = ((int)Focus.Base + (int)Self.Base) / 4;
            if (skillFromAttributes > tweakedSkill - minInitLevel)
            {
                var diff = tweakedSkill - minInitLevel - skillFromAttributes;
                var halfAdjustment = diff * 2;
                Focus.StartingValue = (uint)Math.Max(1, (int)Focus.StartingValue + halfAdjustment);
                Self.StartingValue = (uint)Math.Max(1, (int)Self.StartingValue + halfAdjustment);
                _log.Debug(
                    "Creature.AdjustAttributesForArchetypeConstraints() - Focus and Self reduced by {Adjustment} each for {Name} ({WeenieClassId}) to satisfy MagicDefense constraint.",
                    halfAdjustment, Name, WeenieClassId
                );
            }
        }
    }

    private uint GetNewMeleeAttackSkill(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Strength.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"-Melee Attack\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewUnarmedCombatSkill(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Coordination.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"-Unarmed Combat\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewDaggerSkill(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Coordination.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"-Dagger\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewStaffSkill(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Coordination.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"-Staff\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewMissileAttackSkill(int tier, float statWeight, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = dexterity;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Coordination.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Missile Attack\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewThrownWeaponsSkill(int tier, float statWeight, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = dexterity;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Strength.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Thrown Weapons\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewWarMagicSkill(int tier, float statWeight, double magic)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = magic;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Self.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-War Magic\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewLifeMagicSkill(int tier, float statWeight, double magic)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = magic;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Self.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Life Magic\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewPhysicalDefenseSkill(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyDefense[tier] + (enemyDefense[tier + 1] - enemyDefense[tier]) * statWeight;
        var multiplier = dexterity;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 4;
        var skillFromAttributes = (Coordination.Base + Quickness.Base) / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Physical Defense\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewMagicDefenseSkill(int tier, float statWeight, double magic)
    {
        var target = enemyDefense[tier] + (enemyDefense[tier + 1] - enemyDefense[tier]) * statWeight;
        var multiplier = magic;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 4;
        var skillFromAttributes = (Focus.Base + Self.Base) / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Magic Defense\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewPerceptionSkill(int tier, float statWeight, double intelligence)
    {
        var target =
            enemyAssessDeception[tier] + (enemyAssessDeception[tier + 1] - enemyAssessDeception[tier]) * statWeight;
        var multiplier = intelligence;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Focus.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Perception\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewDeceptionSkill(int tier, float statWeight, double intelligence)
    {
        var target =
            enemyAssessDeception[tier] + (enemyAssessDeception[tier + 1] - enemyAssessDeception[tier]) * statWeight;
        var multiplier = intelligence;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Focus.Base / divisor;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - (uint)skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Deception\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private uint GetNewRunSkill(int tier, float statWeight, double dexterity)
    {
        var target = enemyRun[tier] + (enemyRun[tier + 1] - enemyRun[tier]) * statWeight;
        var multiplier = dexterity;
        var tweakedSkill = (uint)(target * multiplier);

        var skillFromAttributes = Quickness.Base;

        var newSkill = tweakedSkill < skillFromAttributes ? 0 : tweakedSkill - skillFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Run\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedSkill}\n"
                    + $" SkillFromAttributes: {skillFromAttributes}\n"
                    + $" NewSkill: {newSkill}"
            );
        }

        return newSkill;
    }

    private int GetNewArmorLevel(int tier, float statWeight, double toughness, double physicality)
    {
        var target = enemyArmorWard[tier] + (enemyArmorWard[tier + 1] - enemyArmorWard[tier]) * statWeight;
        var multiplier = physicality;
        var newArmor = (int)(target * multiplier);

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Armor\n" + $" Target: {target}\n" + $" Multiplier: {multiplier}\n" + $" TweakedSkill: {newArmor}"
            );
        }

        return newArmor;
    }

    private int GetNewWardLevel(int tier, float statWeight, double toughness, double magic)
    {
        var target = enemyArmorWard[tier] + (enemyArmorWard[tier + 1] - enemyArmorWard[tier]) * statWeight;
        var multiplier = magic;
        var newWard = (int)(target * multiplier);

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Ward\n" + $" Target: {target}\n" + $" Multiplier: {multiplier}\n" + $" TweakedSkill: {newWard}"
            );
        }

        return newWard;
    }

    private uint GetNewHealthLevel(int tier, float statWeight, double toughness, double physicality)
    {
        var target = enemyHealth[tier] + (enemyHealth[tier + 1] - enemyHealth[tier]) * statWeight;
        var multiplier = toughness;
        var tweakedVital = (uint)(target * multiplier);

        var divisor = 2;
        var vitalFromAttributes = Endurance.Base / divisor;

        if (vitalFromAttributes > tweakedVital)
        {
            var diff = (int)tweakedVital - vitalFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Health skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Endurance attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

        var newVital = tweakedVital - (uint)vitalFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Health\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedVital}\n"
                    + $" SkillFromAttributes: {vitalFromAttributes}\n"
                    + $" NewSkill: {newVital}"
            );
        }

        return newVital;
    }

    private double GetNewHealthRegenLevel(int tier, float statWeight, double toughness, double physicality)
    {
        var target = enemyHealthRegen[tier] + (enemyHealthRegen[tier + 1] - enemyHealthRegen[tier]) * statWeight;
        var multiplier = 1.0 + (toughness * 0.1);
        var newVital = (target * multiplier);

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Health Regen\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {newVital}"
            );
        }

        return newVital;
    }

    private uint GetNewStaminaLevel(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyStaminaMana[tier] + (enemyStaminaMana[tier + 1] - enemyStaminaMana[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var tweakedVital = (uint)(target * multiplier);

        var vitalFromAttributes = Endurance.Base;

        if (vitalFromAttributes > tweakedVital)
        {
            var diff = (int)tweakedVital - vitalFromAttributes;
            var attributeAdjustment = diff;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Stamina skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Endurance attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

        var newVital = tweakedVital - (uint)vitalFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Stamina\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedVital}\n"
                    + $" SkillFromAttributes: {vitalFromAttributes}\n"
                    + $" NewSkill: {newVital}"
            );
        }

        return newVital;
    }

    private double GetNewStaminaRegenLevel(int tier, float statWeight, double physicality, double dexterity)
    {
        var target =
            enemyStaminaManaRegen[tier] + (enemyStaminaManaRegen[tier + 1] - enemyStaminaManaRegen[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var newVital = (target * multiplier);

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Stamina Regen\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {newVital}"
            );
        }

        return newVital;
    }

    private uint GetNewManaLevel(int tier, float statWeight, double magic)
    {
        var target = enemyStaminaMana[tier] + (enemyStaminaMana[tier + 1] - enemyStaminaMana[tier]) * statWeight;
        var multiplier = magic;
        var tweakedVital = (uint)(target * multiplier);

        var vitalFromAttributes = Self.Base;

        if (vitalFromAttributes > tweakedVital)
        {
            var diff = (int)tweakedVital - vitalFromAttributes;
            var attributeAdjustment = diff;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Mana skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Self attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

        var newVital = tweakedVital - (uint)vitalFromAttributes;

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Mana\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {tweakedVital}\n"
                    + $" SkillFromAttributes: {vitalFromAttributes}\n"
                    + $" NewSkill: {newVital}"
            );
        }

        return newVital;
    }

    private double GetNewManaRegenLevel(int tier, float statWeight, double magic)
    {
        var target =
            enemyStaminaManaRegen[tier] + (enemyStaminaManaRegen[tier + 1] - enemyStaminaManaRegen[tier]) * statWeight;
        var multiplier = magic;
        var newVital = (target * multiplier);

        if (DebugArchetypeSystem)
        {
            Console.Write(
                $"\n-Mana Regen\n"
                    + $" Target: {target}\n"
                    + $" Multiplier: {multiplier}\n"
                    + $" TweakedSkill: {newVital}"
            );
        }

        return newVital;
    }

    private double GetNewBaseDamage(int tier, float statWeight, double lethality)
    {
        // target enemy damage per second. If enemy uses nearby player scaling, use highest tier enemy damage.
        var weightedEnemyDamage = (enemyDamage[tier] + (enemyDamage[tier + 1] - enemyDamage[tier]) * statWeight);
        if (CurrentLandblock is not null && CurrentLandblock.IsFellowshipRequired())
        {
            weightedEnemyDamage = enemyDamage[5]; // capstone dps setting
        }

        var weightedPlayerHealth = (
            avgPlayerHealth[tier] + (avgPlayerHealth[tier + 1] - avgPlayerHealth[tier]) * statWeight
        );
        var targetEnemyDamage = weightedEnemyDamage / 100 * weightedPlayerHealth;

        // player melee def
        var weightedPlayerMeleeDef =
            avgPlayerPhysicalMagicDefense[tier] + (avgPlayerPhysicalMagicDefense[tier + 1] - avgPlayerPhysicalMagicDefense[tier]) * statWeight;

        // enemy attack
        var weaponSkill = GetEquippedWeapon() is null ? Skill.UnarmedCombat : (Skill)(GetEquippedWeapon().WieldSkillType2 ?? (int)Skill.MartialWeapons);
        var archetypeSkillMulti = ((ArchetypePhysicality ?? 1.0) + (ArchetypeDexterity ?? 1.0)) / 2;
        var enemyAttackSkill = Math.Round(Skills[weaponSkill].Current / archetypeSkillMulti);

        // player evade
        var playerEvadeChance = 1 - SkillCheck.GetSkillChance((int)enemyAttackSkill, (int)weightedPlayerMeleeDef);
        var playerEvadeDamageReduction = 1 - (playerEvadeChance * 0.5f);

        // player armor
        var weightedPlayerArmorReduction =
            avgPlayerArmorReduction[tier]
            + (avgPlayerArmorReduction[tier + 1] - avgPlayerArmorReduction[tier]) * statWeight;

        //player life protection
        var playerLifeProtReduction = avgPlayerLifeProtReduction[tier];

        // enemy attack speed
        var animSpeed = GetAnimSpeed();
        var weapon = GetEquippedWeapon();
        var creatureAnimLength = MonsterAverageAnimationLength.GetValueMod(CreatureType);

        var enemyAvgAttackSpeed = 1 / (creatureAnimLength / animSpeed + (0.5f)); // 0.5f being average powerup time

        if (weapon is not null
            && (weapon.W_AttackType.HasFlag(AttackType.TripleStrike)
                || weapon.W_AttackType.HasFlag(AttackType.TripleSlash)
                || weapon.W_AttackType.HasFlag(AttackType.TripleThrust)
                || weapon.W_AttackType.HasFlag(AttackType.OffhandTripleSlash)
                || weapon.W_AttackType.HasFlag(AttackType.OffhandTripleThrust)))
        {
            enemyAvgAttackSpeed *= 3.0f;
        }
        else if (weapon is not null
                && (weapon.IsTwoHanded
                    || weapon.W_AttackType.HasFlag(AttackType.DoubleStrike)
                    || weapon.W_AttackType.HasFlag(AttackType.DoubleSlash)
                    || weapon.W_AttackType.HasFlag(AttackType.DoubleThrust)
                    || weapon.W_AttackType.HasFlag(AttackType.OffhandDoubleSlash)
                    || weapon.W_AttackType.HasFlag(AttackType.OffhandDoubleThrust)))
        {
            enemyAvgAttackSpeed *= 2.0f;
        }

        // enemy attribute mod
        var attributeAmount = weaponSkill is Skill.MartialWeapons or Skill.ThrownWeapon ? (int)Strength.Base : (int)Coordination.Base;
        var enemyAttributeMod = SkillFormula.GetAttributeMod(attributeAmount, weaponSkill);

        // final base damage
        var baseAvgDamage =
            targetEnemyDamage
            / weightedPlayerArmorReduction
            / playerLifeProtReduction
            / playerEvadeDamageReduction
            / enemyAvgAttackSpeed
            / enemyAttributeMod
            * lethality;

        if (DebugArchetypeSystem)
        {
            Console.WriteLine(
                $"\nDamage Calc for {Name} ({WeenieClassId})\n "
                    + $" BaseAvgDamge: {Math.Round(baseAvgDamage, 1)} = \n"
                    + $"  targetEnemyDamage ({Math.Round(targetEnemyDamage, 1)}) - weightedEnemyDamage ({Math.Round(weightedEnemyDamage, 1)}% per second), weightedPlayerHealth ({Math.Round(weightedPlayerHealth, 1)}) \n"
                    + $"  / armorReduction ({weightedPlayerArmorReduction})\n"
                    + $"  / protReduction ({playerLifeProtReduction})  \n"
                    + $"  / evadeReduction ({Math.Round(playerEvadeDamageReduction, 2)}) - weightedPlayerMeleeDef ({weightedPlayerMeleeDef}), enemyAttackSkill ({enemyAttackSkill}), playerEvadeChance ({Math.Round(playerEvadeChance, 2)})  \n"
                    + $"  / enemyAttacksPerSecond ({Math.Round(enemyAvgAttackSpeed, 2)})  \n"
                    + $"  / enemyAttributeMod ({enemyAttributeMod})\n"
                    + $"  * lethality ({lethality})\n"
            );
        }

        return baseAvgDamage;
    }

    private double GetArchetypeSpellDamageMultiplier(int tier, float statWeight, double lethality)
    {
        // target enemy damage per second
        var weightedEnemyDamage = (enemyDamage[tier] + (enemyDamage[tier + 1] - enemyDamage[tier]) * statWeight);
        if (CurrentLandblock is not null && CurrentLandblock.IsFellowshipRequired())
        {
            weightedEnemyDamage = enemyDamage[5]; // capstone dps setting
        }

        var weightedPlayerHealth = (avgPlayerHealth[tier] + (avgPlayerHealth[tier + 1] - avgPlayerHealth[tier]) * statWeight);
        var targetEnemyDamage = weightedEnemyDamage / 100 * weightedPlayerHealth;

        // player magic def
        var weightedPlayerMagicDef = avgPlayerPhysicalMagicDefense[tier] + (avgPlayerPhysicalMagicDefense[tier + 1] - avgPlayerPhysicalMagicDefense[tier]) * statWeight;
        var archetypeSkillMulti = ArchetypeMagic ?? 1.0;
        var enemyMagicSkill = Skills[Skill.WarMagic].Current > Skills[Skill.LifeMagic].Current ? Skills[Skill.WarMagic].Current : Skills[Skill.LifeMagic].Current;
        enemyMagicSkill = (uint)(enemyMagicSkill / archetypeSkillMulti);

        // player evade
        var playerResistChance = 1 - SkillCheck.GetSkillChance((int)enemyMagicSkill, (int)weightedPlayerMagicDef);
        var playerResistDamageReduction = 1 - (playerResistChance * 0.5f);

        // player armor
        var weightedPlayerWardReduction =
            avgPlayerArmorReduction[tier]
            + (avgPlayerArmorReduction[tier + 1] - avgPlayerArmorReduction[tier]) * statWeight;

        //player life protection
        var playerLifeProtReduction = avgPlayerLifeProtReduction[tier];

        // enemy cast speed
        var enemyAvgAttackSpeed = 1 / (WeaponAnimationLength.GetSpellCastAnimationLength(Server.Entity.ProjectileSpellType.Blast, 1) + 1.0); // 1.0 on the end for the default AiUseMagicDelay

        // enemy attribute mod
        var self = (int)Self.Base;
        var enemyAttributeMod = SkillFormula.GetAttributeMod(self, Skill.WarMagic);

        // final base damage
        var baseAvgDamage =
            targetEnemyDamage
            / weightedPlayerWardReduction
            / playerLifeProtReduction
            / playerResistDamageReduction
            / enemyAvgAttackSpeed
            / enemyAttributeMod
            * lethality;

        // base spell damage
        var baseSpellDamage = GetAvgSpellDamageAtTier(tier);


        if (DebugArchetypeSystem)
        {
            Console.WriteLine(
                $"\nSpell Damage Calc for {Name} ({WeenieClassId})\n "
                    + $" BaseAvgDamage: {Math.Round(baseAvgDamage, 1)} = \n"
                    + $"  targetEnemyDamage ({Math.Round(targetEnemyDamage, 1)}) - weightedEnemyDamage ({Math.Round(weightedEnemyDamage, 1)}% per second), weightedPlayerHealth ({Math.Round(weightedPlayerHealth, 1)}) \n"
                    + $"  / armorReduction ({weightedPlayerWardReduction})\n"
                    + $"  / protReduction ({playerLifeProtReduction})  \n"
                    + $"  / evadeReduction ({Math.Round(playerResistDamageReduction, 2)}) - weightedPlayerMeleeDef ({weightedPlayerMagicDef}), enemyAttackSkill ({enemyMagicSkill}), playerEvadeChance ({Math.Round(playerResistChance, 2)})  \n"
                    + $"  / enemyAttacksPerSecond ({Math.Round(enemyAvgAttackSpeed, 2)})  \n"
                    + $"  / enemyAttributeMod ({enemyAttributeMod})\n" +
                      $"  = BaseAvgDamage ({baseAvgDamage}) / AvgSpellDamage ({baseSpellDamage}) = SpellDamageMod ({baseAvgDamage / baseSpellDamage})"
            );
        }

        if (baseAvgDamage / baseSpellDamage is float.PositiveInfinity or float.NegativeInfinity)
        {
            if (Name is not "" or "Placeholder")
            {
                _log.Error("{Name}.GetArchetypeSpellDamageMultiplier(tier ({Tier}), statWeight ({StatWeight}), lethality ({Lethality})) - Value is infinity.",
                Name, tier, statWeight, lethality);
            }

            return 1.0f;
        }

        if (baseAvgDamage / baseSpellDamage < 0)
        {
            if (Name is not "" or "Placeholder")
            {
                _log.Error("{Name}.GetArchetypeSpellDamageMultiplier(tier ({Tier}), statWeight ({StatWeight}), lethality ({Lethality})) - Value is negative.",
                Name, tier, statWeight, lethality);
            }

            return 1.0f;
        }

        return baseAvgDamage / baseSpellDamage;
    }

    private int GetAvgSpellDamageAtTier(int tier)
    {
        return tier switch
        {
            1 => 9,
            2 => 18,
            3 => 27,
            4 => 36,
            5 => 45,
            6 => 54,
            7 => 160,
            _ => 9
        };
    }

    /// <summary>
    /// Adds the dungeon mods the fellowship leader chose for this creature's landblock (Landblock.SetLandblockMods)
    /// to its archetype values. Only the values passed in are changed, not the creature's own Archetype properties,
    /// so the mods can't be overwritten by a later recalculation or applied twice.
    /// </summary>
    private void ApplyDungeonMods(
        ref double toughness,
        ref double lethality,
        ref double healthMultiplier,
        ref double skillMultiplier
    )
    {
        var landblock = CurrentLandblock;

        // the mods make the dungeon's enemies stronger, not the fellowship's pets
        if (landblock?.LandblockMods is null || this is CombatPet)
        {
            return;
        }

        // Dungeon: Difficulty I-XX
        lethality *= 1.0 + landblock.GetLandblockLethalityMod();
        healthMultiplier *= 1.0 + landblock.GetLandblockHealthMod();
        skillMultiplier *= 1.0 + landblock.GetLandblockSkillMod();

        if (landblock.LandblockMods["Titans"].Active && MonsterRank >= 4)
        {
            toughness *= 2;
        }

        if (landblock.LandblockMods["Skilled"].Active)
        {
            skillMultiplier *= 1.1;
        }
    }

    /// <summary>
    /// Multiplies the creature's finished max health. Done on the result rather than through toughness, so it
    /// doesn't also raise armor, ward and regen, and so creatures with hand-set health (OverrideArchetypeHealth,
    /// which skips the archetype health calculation) get it too.
    /// </summary>
    private void ApplyHealthMultiplier(double healthMultiplier)
    {
        if (healthMultiplier == 1.0)
        {
            return;
        }

        var maxHealth = Vitals[PropertyAttribute2nd.MaxHealth];

        maxHealth.StartingValue += (uint)Math.Round(maxHealth.Base * (healthMultiplier - 1.0));
    }

    /// <summary>
    /// The skills the dungeon mods raise: the ones SetSkills() sets
    /// </summary>
    private static readonly Skill[] DungeonModSkills =
    {
        Skill.MartialWeapons,
        Skill.UnarmedCombat,
        Skill.Dagger,
        Skill.Staff,
        Skill.Bow,
        Skill.ThrownWeapon,
        Skill.WarMagic,
        Skill.LifeMagic,
        Skill.PhysicalDefense,
        Skill.MagicDefense,
        Skill.Perception,
        Skill.Deception,
        Skill.Run,
    };

    /// <summary>
    /// Multiplies the creature's finished skills. Done on the result, like health, so the part of a skill that comes from
    /// attributes is raised too (SetSkills only sets the rest, about half of it), and so creatures with hand-set skills
    /// (OverrideArchetypeSkills, which skips SetSkills) get it too.
    /// </summary>
    private void ApplySkillMultiplier(double skillMultiplier)
    {
        if (skillMultiplier == 1.0)
        {
            return;
        }

        foreach (var skill in DungeonModSkills)
        {
            if (Skills.TryGetValue(skill, out var creatureSkill))
            {
                creatureSkill.InitLevel += (uint)Math.Round(creatureSkill.Base * (skillMultiplier - 1.0));
            }
        }
    }

    /// <summary>
    /// The values ApplyArchetypeSystem() builds on without setting them itself: the kill xp mod, and the max health and
    /// skills of a creature that has them hand-set (OverrideArchetypeHealth, OverrideArchetypeSkills). Null until it first runs.
    /// </summary>
    private (double? KillXpMod, uint MaxHealth, Dictionary<Skill, uint> SkillLevels)? archetypeStartingValues;

    /// <summary>
    /// Saves the values ApplyArchetypeSystem() builds on the first time it runs, and puts them back each time after that,
    /// so running it again starts over instead of multiplying its xp, health and skill bonuses in twice.
    /// </summary>
    private void RestoreArchetypeStartingValues()
    {
        var maxHealth = Vitals[PropertyAttribute2nd.MaxHealth];

        if (archetypeStartingValues is not { } startingValues)
        {
            // SetSkills sets all of them for the others
            Dictionary<Skill, uint> skillLevels = null;

            if (OverrideArchetypeSkills ?? false)
            {
                skillLevels = new Dictionary<Skill, uint>();

                foreach (var skill in DungeonModSkills)
                {
                    if (Skills.TryGetValue(skill, out var creatureSkill))
                    {
                        skillLevels[skill] = creatureSkill.InitLevel;
                    }
                }
            }

            archetypeStartingValues = (KillXpMod, maxHealth.StartingValue, skillLevels);
            return;
        }

        KillXpMod = startingValues.KillXpMod;
        maxHealth.StartingValue = startingValues.MaxHealth;

        if (startingValues.SkillLevels != null)
        {
            foreach (var (skill, initLevel) in startingValues.SkillLevels)
            {
                GetCreatureSkill(skill).InitLevel = initLevel;
            }
        }

        // nearby player scaling works from the stats it found when it was first needed, which are about to change
        SkillsSet = false;
        LastNumberOfNearbyPlayers = 0;
    }
}
