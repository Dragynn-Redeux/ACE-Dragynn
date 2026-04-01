using System;
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
    private static readonly int[] enemyAttack = { 10, 60, 100, 150, 175, 200, 250, 350, 500 };
    private static readonly int[] enemyDefense = { 10, 60, 100, 150, 175, 200, 250, 350, 500 };
    private static readonly int[] enemyAssessDeception = { 10, 60, 100, 150, 175, 200, 250, 350, 500 };
    private static readonly int[] enemyRun = { 10, 100, 150, 200, 250, 300, 400, 500, 600 };

    private static readonly float[] enemyDamage = { 1.0f, 2.0f, 3.0f, 4.5f, 6.0f, 7.0f, 8.0f, 9.0f, 10.0f }; // percentage of player health to be taken per second after all stats (of player and enemy) are considered

    private static readonly int[] avgPlayerHealth = { 75, 110, 170, 200, 230, 260, 300, 350, 400 };
    private static readonly float[] avgPlayerArmorReduction = { 0.75f, 0.57f, 0.40f, 0.31f, 0.25f, 0.21f, 0.18f, 0.16f, 0.1f };
    private static readonly float[] avgPlayerLifeProtReduction = { 1.0f, 1.0f, 0.9f, 0.9f, 0.85f, 0.8f, 0.8f, 0.75f, 0.75f };
    private static readonly int[] avgPlayerPhysicalMagicDefense = { 10, 60, 90, 120, 150, 180, 225, 300, 500 };

    private int _tier;
    private float _statWeight;

    private void SetSkills(
        int tier,
        float statWeight,
        double toughness,
        double physicality,
        double dexterity,
        double magic,
        double intelligence,
        double multiplier
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
            // Martial Weapons Skill
            {
                var newSkill = GetNewMeleeAttackSkill(tier, statWeight, physicality, dexterity);

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

                newSkill = ApplyMultiplier(multiplier, newSkill);

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

    private static uint ApplyMultiplier(double multiplier, uint newSkill)
    {
        var multipliedSkill = newSkill * multiplier;

        multipliedSkill = multipliedSkill switch
        {
            < uint.MinValue => uint.MinValue,
            > uint.MaxValue => uint.MaxValue,
            _ => Convert.ToUInt32(multipliedSkill)
        };

        return (uint)multipliedSkill;
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

        _tier = tier;
        _statWeight = statWeight;

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
                var bodyParts = GetBodyParts(this);
                if (bodyParts.Weenie.PropertiesBodyPart != null)
                {
                    foreach (var kvp in bodyParts.Weenie.PropertiesBodyPart)
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

    private uint GetNewMeleeAttackSkill(int tier, float statWeight, double physicality, double dexterity)
    {
        var target = enemyAttack[tier] + (enemyAttack[tier + 1] - enemyAttack[tier]) * statWeight;
        var multiplier = (physicality + dexterity) / 2;
        var tweakedSkill = (uint)(target * multiplier);

        var divisor = 2;
        var skillFromAttributes = Strength.Base / divisor;

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Martial Weapons skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Strength attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Unarmed Combat skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Dagger skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Staff skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Bow skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Thrown Weapons skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base War Magic skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Self attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base War Magic skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Self attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Physical Defense skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination and/or Quickness attributes should be lowered by a total of {AttributeAdjustment}.",
                tweakedSkill,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Magic Defense skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Coordination and/or Quickness attributes should be lowered by a total of {AttributeAdjustment}.",
                tweakedSkill,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Perception skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Focus attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff * divisor;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Deception skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Focus attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

        if (skillFromAttributes > tweakedSkill)
        {
            var diff = (int)tweakedSkill - skillFromAttributes;
            var attributeAdjustment = diff;
            _log.Warning(
                "Creature.SetSkills() - Archetype system is attempting to set the base Run skill to {TweakedSkill} for {Name} ({WeenieClassId}) (defaulting to 1). Quickness attribute should be lowered by {AttributeAdjustment}.",
                diff,
                Name,
                WeenieClassId,
                attributeAdjustment
            );
        }

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

    public void SetLethalityModFromDungeonMod()
    {
        if (CurrentLandblock is null)
        {
            return;
        }

        if (UseArchetypeSystem is not true)
        {
            return;
        }

        if (WeenieClassId == 1020001)
        {
            return;
        }

        var archetypeLethality = ArchetypeLethality ?? 1.0;

        var landblockLethalityMod = true switch
        {
            _ when CurrentLandblock.LandblockMods["Lethality 500%"].Active => 5.0,
            _ when CurrentLandblock.LandblockMods["Lethality 450%"].Active => 4.5,
            _ when CurrentLandblock.LandblockMods["Lethality 400%"].Active => 4.0,
            _ when CurrentLandblock.LandblockMods["Lethality 350%"].Active => 3.5,
            _ when CurrentLandblock.LandblockMods["Lethality 300%"].Active => 3.0,
            _ when CurrentLandblock.LandblockMods["Lethality 250%"].Active => 2.5,
            _ when CurrentLandblock.LandblockMods["Lethality 200%"].Active => 2.0,
            _ when CurrentLandblock.LandblockMods["Lethality 150%"].Active => 1.5,
            _ when CurrentLandblock.LandblockMods["Lethality 100%"].Active => 1.0,
            _ when CurrentLandblock.LandblockMods["Lethality 50%"].Active => 0.5,
            _ => 0.0
        };

        var adjustment = archetypeLethality * landblockLethalityMod;

        ArchetypeLethality = adjustment + archetypeLethality;

        SetSkills(_tier,
            _statWeight,
            ArchetypeToughness ?? 1.0,
            ArchetypePhysicality ?? 1.0,
            ArchetypeDexterity ?? 1.0,
            ArchetypeMagic ?? 1.0,
            ArchetypeIntelligence ?? 1.0,
            1.0);

        SetDamageArmorWard(_tier,
            _statWeight,
            ArchetypeToughness ?? 1.0,
            ArchetypePhysicality ?? 1.0,
            ArchetypeMagic ?? 1.0,
            ArchetypeLethality ?? 1.0);

        //Console.WriteLine($"{Name}: BaseLethality = {archetypeLethality}, LbLethality = {CurrentLandblock.LandblockLethalityMod}, Adjustment = {adjustment}, Total: {adjustment + archetypeLethality}");
    }

    public void SetHealthFromDungeonMod()
    {
        if (CurrentLandblock is null)
        {
            return;
        }

        if (UseArchetypeSystem is not true)
        {
            return;
        }

        if (!CurrentLandblock.LandblockMods["Titans"].Active)
        {
            return;
        }

        if (MonsterRank < 4)
        {
            return;
        }

        ArchetypeToughness *= 2;

    SetVitals(_tier,
            _statWeight,
            ArchetypeToughness ?? 1.0,
            ArchetypePhysicality ?? 1.0,
            ArchetypeDexterity ?? 1.0,
            ArchetypeMagic ?? 1.0);
    }

    public void SetSkillsFromDungeonMod()
    {
        if (CurrentLandblock is null)
        {
            return;
        }

        if (UseArchetypeSystem is not true)
        {
            return;
        }

        if (!CurrentLandblock.LandblockMods["Skilled"].Active)
        {
            return;
        }

        SetSkills(_tier,
            _statWeight,
            ArchetypeToughness ?? 1.0,
            ArchetypePhysicality ?? 1.0,
            ArchetypeDexterity ?? 1.0,
            ArchetypeMagic ?? 1.0,
            ArchetypeIntelligence ?? 1.0,
            1.1);
    }
}
