using System;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects;

partial class WorldObject
{
    public Skill WeaponSkill
    {
        get => (Skill)(GetProperty(PropertyInt.WeaponSkill) ?? 0);
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyInt.WeaponSkill);
            }
            else
            {
                SetProperty(PropertyInt.WeaponSkill, (int)value);
            }
        }
    }

    public DamageType W_DamageType
    {
        get => (DamageType)(GetProperty(PropertyInt.DamageType) ?? 0);
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyInt.DamageType);
            }
            else
            {
                SetProperty(PropertyInt.DamageType, (int)value);
            }
        }
    }

    public AttackType W_AttackType
    {
        get => (AttackType)(GetProperty(PropertyInt.AttackType) ?? 0);
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyInt.AttackType);
            }
            else
            {
                SetProperty(PropertyInt.AttackType, (int)value);
            }
        }
    }

    public WeaponType W_WeaponType
    {
        get => (WeaponType)(GetProperty(PropertyInt.WeaponType) ?? 0);
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyInt.WeaponType);
            }
            else
            {
                SetProperty(PropertyInt.WeaponType, (int)value);
            }
        }
    }

    public bool AutoWieldLeft
    {
        get => GetProperty(PropertyBool.AutowieldLeft) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.AutowieldLeft);
            }
            else
            {
                SetProperty(PropertyBool.AutowieldLeft, value);
            }
        }
    }

    /// <summary>
    /// Returns TRUE if this weapon cleaves
    /// </summary>
    public bool IsCleaving
    {
        get => GetProperty(PropertyInt.Cleaving) != null;
    }

    /// <summary>
    /// Returns the number of cleave targets for this weapon
    /// If cleaving weapon, this is PropertyInt.Cleaving - 1
    /// </summary>
    public int CleaveTargets
    {
        get
        {
            if (!IsCleaving)
            {
                return 0;
            }

            return GetProperty(PropertyInt.Cleaving).Value - 1;
        }
    }

    /// <summary>
    /// Returns the primary weapon equipped by a creature
    /// (melee, missile, or wand)
    /// </summary>
    private static WorldObject GetWeapon(Creature wielder, bool forceMainHand = false)
    {
        if (wielder == null)
        {
            return null;
        }

        var weapon = wielder.GetEquippedWeapon(forceMainHand);

        if (weapon == null)
        {
            weapon = wielder.GetEquippedWand();
        }

        return weapon;
    }

    private const float DefaultModifier = 1.0f;

    /// <summary>
    /// Returns the Melee Defense skill modifier for the current weapon
    /// </summary>
    public static float GetWeaponPhysicalDefenseModifier(Creature wielder)
    {
        // creatures only receive defense bonus in combat mode
        if (wielder == null || wielder.CombatMode == CombatMode.NonCombat)
        {
            return DefaultModifier;
        }

        var mainhand = GetWeapon(wielder, true);
        var offhand = wielder.GetDualWieldWeapon();

        if (offhand == null)
        {
            return GetWeaponPhysicalDefenseModifier(wielder, mainhand);
        }
        else
        {
            var mainhand_defenseMod = GetWeaponPhysicalDefenseModifier(wielder, mainhand);
            var offhand_defenseMod = GetWeaponPhysicalDefenseModifier(wielder, offhand);

            return Math.Max(mainhand_defenseMod, offhand_defenseMod);
        }
    }

    private static float GetWeaponPhysicalDefenseModifier(Creature wielder, WorldObject weapon)
    {
        if (weapon == null)
        {
            return DefaultModifier;
        }

        //var defenseMod = (float)(weapon.WeaponDefense ?? defaultModifier) + weapon.EnchantmentManager.GetDefenseMod();

        // TODO: Resolve this issue a better way?
        // Because of the way ACE handles default base values in recipe system (or rather the lack thereof)
        // we need to check the following weapon properties to see if they're below expected minimum and adjust accordingly
        // The issue is that the recipe system likely added 0.01 to 0 instead of 1, which is what *should* have happened.
        var baseWepDef = (float)(weapon.WeaponPhysicalDefense ?? DefaultModifier);
        if (
            weapon.WeaponPhysicalDefense > 0
            && weapon.WeaponPhysicalDefense < 1
            && ((weapon.GetProperty(PropertyInt.ImbueStackingBits) ?? 0) & 4) != 0
        )
        {
            baseWepDef += 1;
        }

        var defenseMod = baseWepDef + weapon.EnchantmentManager.GetAdditiveMod(PropertyFloat.WeaponAuraDefense);

        if (weapon.IsEnchantable)
        {
            defenseMod += wielder.EnchantmentManager.GetAdditiveMod(PropertyFloat.WeaponAuraDefense);
        }

        return defenseMod;
    }

    /// <summary>
    /// Returns the Magic Defense skill modifier for the current weapon
    /// </summary>
    public static float GetWeaponMagicDefenseModifier(Creature wielder)
    {
        var weapon = GetWeapon(wielder as Player);

        if (weapon == null || wielder.CombatMode == CombatMode.NonCombat)
        {
            return DefaultModifier;
        }

        //// no enchantments?
        //return (float)(weapon.WeaponMagicDefense ?? 1.0f);

        var baseWepDef = (float)(weapon.WeaponMagicalDefense ?? 1.0f);
        // TODO: Resolve this issue a better way?
        // Because of the way ACE handles default base values in recipe system (or rather the lack thereof)
        // we need to check the following weapon properties to see if they're below expected minimum and adjust accordingly
        // The issue is that the recipe system likely added 0.005 to 0 instead of 1, which is what *should* have happened.
        if (
            weapon.WeaponMagicalDefense > 0
            && weapon.WeaponMagicalDefense < 1
            && ((weapon.GetProperty(PropertyInt.ImbueStackingBits) ?? 0) & 1) == 1
        )
        {
            baseWepDef += 1;
        }

        var defenseMod = baseWepDef + weapon.EnchantmentManager.GetAdditiveMod(PropertyFloat.WeaponAuraDefense);

        if (weapon.IsEnchantable)
        {
            defenseMod += wielder.EnchantmentManager.GetAdditiveMod(PropertyFloat.WeaponAuraDefense);
        }

        // no enchantments?
        return defenseMod;
    }

    /// <summary>
    /// Returns the attack skill modifier for the current weapon
    /// </summary>
    public static float GetWeaponOffenseModifier(Creature wielder)
    {
        // creatures only receive offense bonus in combat mode
        if (wielder == null || wielder.CombatMode == CombatMode.NonCombat)
        {
            return DefaultModifier;
        }

        var mainhand = GetWeapon(wielder, true);
        var offhand = wielder.GetDualWieldWeapon();

        if (offhand == null)
        {
            return GetWeaponOffenseModifier(wielder, mainhand);
        }
        else
        {
            var mainhand_attackMod = GetWeaponOffenseModifier(wielder, mainhand);
            var offhand_attackMod = GetWeaponOffenseModifier(wielder, offhand);

            return Math.Max(mainhand_attackMod, offhand_attackMod);
        }
    }

    private static float GetWeaponOffenseModifier(Creature wielder, WorldObject weapon)
    {
        /* Excerpt from http://acpedia.org/wiki/Announcements_-_2002/07_-_Repercussions#Letter_to_the_Players
         The second issue will, in some ways, be both more troubling and more inconsequential for players. HeartSeeker does not affect missile launchers.
         It never has. Bows, crossbows, and atlatls get no benefit from the HeartSeeker spell or from innate attack bonuses (such as those found on the Singularity Bow).
         The only variables that determine whether a missile character hits their target is their bow/xbow/tw skill, the missile defense of the target, and where they set their accuracy meter while they are attacking.
         However, the Defender spell, as well as innate defensive bonuses, do work on missile launchers.
         The AC Live team has been aware of this for the last several months. Once we knew the situation, the question became what to do about it. Should we “fix” an issue that probably isn't broken?
         Almost no archer/atlatler complains about not being able to hit their target.
         They have a built in “HeartSeeker” all the time.
         If anything, most monsters' missile defense scores have historically been so low that many players regard archery as the fastest way to level a character up through the first 30-40 levels.
         We did not feel that “fixing” such a system would improve the game balance for anyone in Asheron's Call, archer or no.
         Ultimately, we decided to resolve the situation through our changes to the treasure system this month. From now on, missile launchers will have a chance of having an innate defensive bonus, but not an offensive one.
         While many old quest weapons still retain their (useless) attack bonus, we will not be putting any new ones into the system.
         */
        if (
            weapon == null /* see note above */
        )
        {
            return DefaultModifier;
        }

        var offenseMod = (float)(weapon.WeaponOffense ?? DefaultModifier) + weapon.EnchantmentManager.GetAttackMod();

        if (weapon.IsEnchantable)
        {
            offenseMod += wielder.EnchantmentManager.GetAttackMod();
        }

        return offenseMod;
    }

    /// <summary>
    /// Returns the Mana Conversion skill modifier for the current weapon
    /// </summary>
    public static float GetWeaponManaConversionModifier(Creature wielder)
    {
        var weapon = GetWeapon(wielder as Player);

        if (weapon == null)
        {
            return DefaultModifier;
        }

        if (wielder.CombatMode != CombatMode.NonCombat)
        {
            // hermetic link / void

            // base mod starts at 0
            var baseMod = (float)(weapon.ManaConversionMod ?? 0.0f);

            // enchantments are multiplicative, so they are only effective if there is a base mod
            var manaConvMod = weapon.EnchantmentManager.GetManaConvMod();

            var auraManaConvMod = 1.0f;

            if (weapon.IsEnchantable)
            {
                auraManaConvMod = wielder?.EnchantmentManager.GetManaConvMod() ?? 1.0f;
            }

            var enchantmentMod = manaConvMod * auraManaConvMod;

            return 1.0f + baseMod * enchantmentMod;
        }

        return DefaultModifier;
    }

    private const uint defaultSpeed = 40; // TODO: find default speed

    /// <summary>
    /// Returns the weapon speed, with enchantments factored in
    /// </summary>
    public static uint GetWeaponSpeed(Creature wielder)
    {
        var weapon = GetWeapon(wielder as Player);

        var baseSpeed = weapon?.WeaponTime ?? (int)defaultSpeed;

        var speedMod = weapon != null ? weapon.EnchantmentManager.GetWeaponSpeedMod() : 0;
        var auraSpeedMod = wielder != null ? wielder.EnchantmentManager.GetWeaponSpeedMod() : 0;

        return (uint)Math.Max(0, baseSpeed + speedMod + auraSpeedMod);
    }

    /// <summary>
    /// Biting Strike
    /// </summary>
    public double? CriticalFrequency
    {
        get => GetProperty(PropertyFloat.CriticalFrequency);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.CriticalFrequency);
            }
            else
            {
                SetProperty(PropertyFloat.CriticalFrequency, value.Value);
            }
        }
    }

    private const float DefaultPhysicalCritFrequency = 0.1f; // 10% base chance

    /// <summary>
    /// Returns the critical chance for the attack weapon
    /// </summary>
    public static float GetWeaponCriticalChance(
        WorldObject weapon,
        Creature wielder,
        CreatureSkill skill,
        Creature target
    )
    {
        var critRate = (float)(weapon?.CriticalFrequency ?? DefaultPhysicalCritFrequency);

        if (weapon != null && weapon.HasImbuedEffect(ImbuedEffectType.CriticalStrike))
        {
            var criticalStrikeBonus = DefaultPhysicalCritFrequency + GetCriticalStrikeMod(skill, wielder, target);

            if (weapon is { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
            {
                criticalStrikeBonus = DefaultPhysicalCritFrequency;
            }

            critRate = Math.Max(critRate, criticalStrikeBonus);
        }

        if (wielder != null)
        {
            critRate += wielder.GetCritRating() * 0.01f;
        }

        // mitigation
        var critResistRatingMod = Creature.GetNegativeRatingMod(target.GetCritResistRating());
        critRate *= critResistRatingMod;

        return critRate;
    }

    // http://acpedia.org/wiki/Announcements_-_2002/08_-_Atonement#Letter_to_the_Players - 2% originally

    // http://acpedia.org/wiki/Announcements_-_2002/11_-_The_Iron_Coast#Release_Notes
    // The chance for causing a critical hit with magic, both with and without a Critical Strike wand, has been increased.
    // what this was actually increased to for base, was never stated directly in the dev notes
    // speculation is that it was 5%, to align with the minimum that CS magic scales from

    private const float DefaultMagicCritFrequency = 0.1f;

    /// <summary>
    /// Returns the critical chance for the caster weapon
    /// </summary>
    public static float GetWeaponMagicCritFrequency(
        WorldObject weapon,
        Creature wielder,
        CreatureSkill skill,
        Creature target
    )
    {
        // TODO : merge with above function

        if (weapon == null)
        {
            return DefaultMagicCritFrequency;
        }

        var critRate = (float)(weapon.GetProperty(PropertyFloat.CriticalFrequency) ?? DefaultMagicCritFrequency);

        if (weapon.HasImbuedEffect(ImbuedEffectType.CriticalStrike))
        {
            var isPvP = wielder is Player && target is Player;

            var criticalStrikeMod = DefaultMagicCritFrequency + GetCriticalStrikeMod(skill, wielder, target, isPvP);

            if (weapon is { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
            {
                criticalStrikeMod = DefaultMagicCritFrequency;
            }

            critRate = Math.Max(critRate, criticalStrikeMod);
        }

        critRate += wielder.GetCritRating() * 0.01f;

        // mitigation
        var critResistRatingMod = Creature.GetNegativeRatingMod(target.GetCritResistRating());
        critRate *= critResistRatingMod;

        return critRate;
    }

    private const float DefaultCritDamageMultiplier = 1.0f;

    /// <summary>
    /// Returns the critical damage multiplier for the attack weapon
    /// </summary>
    public static float GetWeaponCritDamageMod(
        WorldObject weapon,
        Creature wielder,
        CreatureSkill skill,
        Creature target
    )
    {
        var critDamageMod = (float)(
            weapon?.GetProperty(PropertyFloat.CriticalMultiplier) ?? DefaultCritDamageMultiplier
        );

        if (weapon != null && weapon.HasImbuedEffect(ImbuedEffectType.CripplingBlow))
        {
            var cripplingBlowMod = DefaultCritDamageMultiplier + GetCripplingBlowMod(skill, wielder, target);

            if (weapon is { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
            {
                cripplingBlowMod = DefaultCritDamageMultiplier;
            }

            critDamageMod = Math.Max(critDamageMod, cripplingBlowMod);
        }

        return critDamageMod;
    }

    /// <summary>
    /// PvP damaged is halved, automatically displayed in the client
    /// </summary>
    public const float ElementalDamageBonusPvPReduction = 0.5f;

    /// <summary>
    /// Returns a multiplicative elemental damage modifier for the magic caster weapon type
    /// </summary>
    public static float GetCasterElementalDamageModifier(
        WorldObject weapon,
        Creature wielder,
        Creature target,
        DamageType damageType
    )
    {
        if (wielder == null || !(weapon is Caster) || weapon.W_DamageType != damageType)
        {
            return 1.0f;
        }

        var elementalDamageMod = weapon.ElementalDamageMod ?? 1.0f;

        if (damageType is DamageType.Health && weapon.WeaponRestorationSpellsMod is not null)
        {
            elementalDamageMod = weapon.WeaponRestorationSpellsMod.Value;
        }

        // multiplicative to base multiplier
        var wielderEnchantments = wielder.EnchantmentManager.GetElementalDamageMod();
        var weaponEnchantments = weapon.EnchantmentManager.GetElementalDamageMod();

        var enchantments = wielderEnchantments + weaponEnchantments;

        var modifier = (float)((elementalDamageMod - 1.0f) * (1.0f + enchantments) + 1.0f);

        if (modifier > 1.0f && target is Player)
        {
            modifier = 1.0f + (modifier - 1.0f) * ElementalDamageBonusPvPReduction;
        }

        return modifier;
    }

    /// <summary>
    /// Returns an additive elemental damage bonus for the missile launcher weapon type
    /// </summary>
    public static int GetMissileElementalDamageBonus(WorldObject weapon, Creature wielder, DamageType damageType)
    {
        if (weapon is MissileLauncher && weapon.ElementalDamageBonus != null)
        {
            var elementalDamageType = weapon.W_DamageType;

            if (elementalDamageType != DamageType.Undef && elementalDamageType == damageType)
            {
                return weapon.ElementalDamageBonus.Value;
            }
        }
        return 0;
    }

    /// <summary>
    /// Returns an additive elemental damage bonus for the missile launcher weapon type
    /// </summary>
    public static float GetMissileElementalDamageModifier(WorldObject weapon, DamageType damageType)
    {
        if (weapon is not MissileLauncher)
        {
            return 1.0f;
        }

        var elementalDamageType = weapon.W_DamageType;

        if (elementalDamageType is not DamageType.Undef && elementalDamageType != damageType)
        {
            return 1.0f;
        }

        if (weapon.DamageMod != null)
        {
            return (float)weapon.DamageMod.Value;
        }

        return 1.0f;
    }

    public CreatureType? SlayerCreatureType
    {
        get => (CreatureType?)GetProperty(PropertyInt.SlayerCreatureType);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.SlayerCreatureType);
            }
            else
            {
                SetProperty(PropertyInt.SlayerCreatureType, (int)value.Value);
            }
        }
    }

    public double? SlayerDamageBonus
    {
        get => GetProperty(PropertyFloat.SlayerDamageBonus);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.SlayerDamageBonus);
            }
            else
            {
                SetProperty(PropertyFloat.SlayerDamageBonus, value.Value);
            }
        }
    }

    /// <summary>
    /// Returns the slayer damage multiplier for the attack weapon
    /// against a particular creature type
    /// </summary>
    public static float GetWeaponCreatureSlayerModifier(WorldObject weapon, Creature wielder, Creature target)
    {
        if (
            weapon != null
            && weapon.SlayerCreatureType != null
            && weapon.SlayerDamageBonus != null
            && target != null
            && weapon.SlayerCreatureType == target.CreatureType
        )
        {
            // TODO: scale with base weapon skill?
            return (float)weapon.SlayerDamageBonus;
        }
        else
        {
            return DefaultModifier;
        }
    }

    public DamageType? ResistanceModifierType
    {
        get => (DamageType?)GetProperty(PropertyInt.ResistanceModifierType);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.ResistanceModifierType);
            }
            else
            {
                SetProperty(PropertyInt.ResistanceModifierType, (int)value.Value);
            }
        }
    }

    public double? ResistanceModifier
    {
        get => GetProperty(PropertyFloat.ResistanceModifier);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.ResistanceModifier);
            }
            else
            {
                SetProperty(PropertyFloat.ResistanceModifier, value.Value);
            }
        }
    }

    /// <summary>
    /// Returns the resistance modifier or rending modifier
    /// </summary>
    public static float GetWeaponResistanceModifier(
        WorldObject weapon,
        Creature wielder,
        CreatureSkill skill,
        DamageType damageType,
        Creature target = null
    )
    {
        var resistMod = DefaultModifier;

        if (wielder == null || weapon == null)
        {
            return DefaultModifier;
        }

        // handle quest weapon fixed resistance cleaving
        if (weapon.ResistanceModifierType != null && weapon.ResistanceModifierType == damageType)
        {
            resistMod = 1.0f + (float)(weapon.ResistanceModifier ?? DefaultModifier); // 1.0 in the data, equivalent to a level 5 vuln
        }

        // handle elemental resistance rending
        var rendDamageType = GetRendDamageType(damageType);

        if (rendDamageType == ImbuedEffectType.Undef)
        {
            _log.Debug(
                "{WielderName}.GetRendDamageType({DamageType}) unexpected damage type for {WeaponName} ({WeaponGuid})",
                wielder.Name,
                damageType,
                weapon.Name,
                weapon.Guid
            );
        }

        if (rendDamageType != ImbuedEffectType.Undef && weapon.HasImbuedEffect(rendDamageType) && skill != null)
        {
            var rendingMod = DefaultModifier + GetRendingMod(skill, wielder, target);

            if (weapon is { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
            {
                rendingMod = DefaultModifier;
            }

            resistMod = Math.Max(resistMod, rendingMod);
        }

        return resistMod;
    }

    public ImbuedEffectType GetImbuedEffects()
    {
        return (ImbuedEffectType)(
            (GetProperty(PropertyInt.ImbuedEffect) ?? 0)
            | (GetProperty(PropertyInt.ImbuedEffect2) ?? 0)
            | (GetProperty(PropertyInt.ImbuedEffect3) ?? 0)
            | (GetProperty(PropertyInt.ImbuedEffect4) ?? 0)
            | (GetProperty(PropertyInt.ImbuedEffect5) ?? 0)
        );
    }

    public bool HasImbuedEffect(ImbuedEffectType type)
    {
        return ImbuedEffect.HasFlag(type);
    }

    public static ImbuedEffectType GetRendDamageType(DamageType damageType)
    {
        switch (damageType)
        {
            case DamageType.Slash:
                return ImbuedEffectType.SlashRending;
            case DamageType.Pierce:
                return ImbuedEffectType.PierceRending;
            case DamageType.Bludgeon:
                return ImbuedEffectType.BludgeonRending;
            case DamageType.Fire:
                return ImbuedEffectType.FireRending;
            case DamageType.Cold:
                return ImbuedEffectType.ColdRending;
            case DamageType.Acid:
                return ImbuedEffectType.AcidRending;
            case DamageType.Electric:
                return ImbuedEffectType.ElectricRending;
            case DamageType.Nether:
                return ImbuedEffectType.NetherRending;
            default:
                //log.DebugFormat("GetRendDamageType({0}) unexpected damage type", damageType);
                return ImbuedEffectType.Undef;
        }
    }

    /// <summary>
    /// Returns TRUE if this item is enchantable, as per the client formula.
    /// </summary>
    public bool IsEnchantable => (ResistMagic ?? 0) < 9999;

    public int? ResistMagic
    {
        get => GetProperty(PropertyInt.ResistMagic);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.ResistMagic);
            }
            else
            {
                SetProperty(PropertyInt.ResistMagic, value.Value);
            }
        }
    }

    public bool IgnoreMagicArmor
    {
        get => GetProperty(PropertyBool.IgnoreMagicArmor) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.IgnoreMagicArmor);
            }
            else
            {
                SetProperty(PropertyBool.IgnoreMagicArmor, value);
            }
        }
    }

    public bool IgnoreMagicResist
    {
        get => GetProperty(PropertyBool.IgnoreMagicResist) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.IgnoreMagicResist);
            }
            else
            {
                SetProperty(PropertyBool.IgnoreMagicResist, value);
            }
        }
    }

    // -- CRITICAL STRIKE IMBUE --
    // Grants 5% crit chance, plus up to an additional 5% based on skill level
    // Bonus amount caps 500 skill level

    private static float MinCriticalStrikeMod = 0.05f;
    private static float MaxCriticalStrikeMod = 0.1f;

    public static float GetCriticalStrikeMod(CreatureSkill skill, Creature wielder, Creature target = null, bool isPvP = false)
    {
        var baseMod = MinCriticalStrikeMod;

        var skillType = GetImbuedSkillType(skill);
        var levelScalar = target != null ? LevelScaling.GetPlayerAttackSkillScalar(wielder, target) : 1.0f;
        var baseSkill = GetBaseSkillImbued(skill) * levelScalar;

        switch (skillType)
        {
            case ImbuedSkillType.Melee:
            case ImbuedSkillType.Missile:
            case ImbuedSkillType.Magic:

                var bonusMod = baseSkill / 500;
                baseMod += MinCriticalStrikeMod * bonusMod;
                break;

            default:
                return 0.0f;
        }

        return Math.Clamp(baseMod, MinCriticalStrikeMod, MaxCriticalStrikeMod);
    }

    // -- CRIPPLING BLOW IMBUE --
    // Grants +50% crit damage, plus up to an additional 50% based on skill level
    // Bonus amount caps 500 skill level

    private static float MinCripplingBlowMod = 0.5f;
    private static float MaxCripplingBlowMod = 1.0f;

    public static float GetCripplingBlowMod(CreatureSkill skill, Creature wielder, Creature target = null)
    {
        var skillType = GetImbuedSkillType(skill);
        var levelScalar = target != null ? LevelScaling.GetPlayerAttackSkillScalar(wielder, target) : 1.0f;
        var baseSkill = GetBaseSkillImbued(skill) * levelScalar;

        var baseMod = MinCripplingBlowMod;

        switch (skillType)
        {
            case ImbuedSkillType.Melee:
            case ImbuedSkillType.Missile:
            case ImbuedSkillType.Magic:

                var bonusMod = baseSkill / 500.0f;
                baseMod += MinCripplingBlowMod * bonusMod;
                break;

            default:
                return 0.0f;
        }

        return Math.Clamp(baseMod, MinCripplingBlowMod, MaxCripplingBlowMod);
    }

    // -- ELEMENTAL REND IMBUE --
    // Grants 15% increased damage, plus up to an additional 15% based on skill level
    // Bonus amount caps 500 skill level

    private static float MinRendingMod = 0.15f;
    private static float MaxRendingMod = 0.30f;

    public static float GetRendingMod(CreatureSkill skill, Creature wielder, Creature target = null)
    {
        var skillType = GetImbuedSkillType(skill);
        var levelScalar = target != null ? LevelScaling.GetPlayerAttackSkillScalar(wielder, target) : 1.0f;
        var baseSkill = GetBaseSkillImbued(skill) * levelScalar;

        var rendingMod = MinRendingMod;

        switch (skillType)
        {
            case ImbuedSkillType.Melee:
            case ImbuedSkillType.Missile:
            case ImbuedSkillType.Magic:

                var bonusMod = baseSkill / 500.0f;
                rendingMod += MinRendingMod * bonusMod;
                break;

            default:
                return 0.0f;
        }

        return Math.Clamp(rendingMod, MinRendingMod, MaxRendingMod);
    }

    // -- ARMOR REND IMBUE --
    // Grants Ignore 1% enemy armor, plus up to an additional 10% based on skill level
    // Bonus amount caps 500 skill level

    public static float MinArmorRendingMod = 0.1f;
    public static float MaxArmorRendingMod = 0.2f;

    public static float GetArmorRendingMod(CreatureSkill skill, Creature wielder, Creature target = null)
    {
        var skillType = GetImbuedSkillType(skill);
        var levelScalar = target != null ? LevelScaling.GetPlayerAttackSkillScalar(wielder, target) : 1.0f;
        var baseSkill = GetBaseSkillImbued(skill) * levelScalar;

        var armorRendingMod = MinArmorRendingMod;

        switch (skillType)
        {
            case ImbuedSkillType.Melee:
            case ImbuedSkillType.Missile:
            case ImbuedSkillType.Magic:

                var bonusMod = baseSkill / 500.0f;
                armorRendingMod += MinArmorRendingMod * bonusMod;
                break;

            default:
                return 0.0f;
        }

        return Math.Clamp(armorRendingMod, MinArmorRendingMod, MaxArmorRendingMod);
    }

    /// <summary>
    /// Armor Cleaving
    /// </summary>
    public double? IgnoreArmor
    {
        get => GetProperty(PropertyFloat.IgnoreArmor);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.IgnoreArmor);
            }
            else
            {
                SetProperty(PropertyFloat.IgnoreArmor, value.Value);
            }
        }
    }

    public float GetArmorCleavingMod(WorldObject weapon)
    {
        if (weapon is { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
        {
            return 1.0f;
        }

        // investigate: should this value be on creatures directly?
        var creatureMod = GetArmorCleavingMod();
        var weaponMod = weapon != null ? weapon.GetArmorCleavingMod() : 1.0f;

        return Math.Min(creatureMod, weaponMod);
    }

    public float GetArmorCleavingMod()
    {
        if (IgnoreArmor == null)
        {
            return 1.0f;
        }

        return (float)IgnoreArmor;
    }

    public double? IgnoreShield
    {
        get => GetProperty(PropertyFloat.IgnoreShield);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.IgnoreShield);
            }
            else
            {
                SetProperty(PropertyFloat.IgnoreShield, value.Value);
            }
        }
    }

    public float GetIgnoreShieldMod(WorldObject weapon)
    {
        var creatureMod = IgnoreShield ?? 0.0f;
        var weaponMod = weapon?.IgnoreShield ?? 0.0f;

        return 1.0f - (float)Math.Max(creatureMod, weaponMod);
    }

    // -- WARD REND IMBUE --
    // Grants Ignore 20% enemy ward, plus up to an additional 20% based on skill level
    // Bonus amount caps 500 skill level

    public static float MinWardRendingMod = 0.1f;
    public static float MaxWardRendingMod = 0.2f;

    public static float GetWardRendingMod(CreatureSkill skill)
    {
        var skillType = GetImbuedSkillType(skill);
        var baseSkill = GetBaseSkillImbued(skill);

        var wardRendingMod = MinWardRendingMod;

        switch (skillType)
        {
            case ImbuedSkillType.Melee:
            case ImbuedSkillType.Missile:
            case ImbuedSkillType.Magic:

                var bonusMod = baseSkill / 500.0f;
                wardRendingMod += MinWardRendingMod * bonusMod;
                break;

            default:
                return 0.0f;
        }

        return Math.Clamp(wardRendingMod, MinWardRendingMod, MaxWardRendingMod);
    }

    public double? IgnoreWard
    {
        get => GetProperty(PropertyFloat.IgnoreWard);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.IgnoreWard);
            }
            else
            {
                SetProperty(PropertyFloat.IgnoreWard, value.Value);
            }
        }
    }

    public float GetIgnoreWardMod(WorldObject weapon)
    {
        if (weapon == null)
        {
            return 1.0f;
        }

        if (weapon is { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
        {
            return 1.0f;
        }

        if (weapon.IgnoreWard == null)
        {
            return 1.0f;
        }

        var creatureMod = IgnoreWard ?? 0.0f;
        var weaponMod = weapon.IgnoreWard ?? 0.0f;

        var finalMod = (float)Math.Max(creatureMod, weaponMod);
        //Console.WriteLine($"FinalMod = {finalMod}");

        return finalMod;
    }

    public bool HasIgnoreWard()
    {
        return IgnoreWard != null ? true : false;
    }

    public static int GetBaseSkillImbued(CreatureSkill skill)
    {
        switch (GetImbuedSkillType(skill))
        {
            case ImbuedSkillType.Melee:
            case ImbuedSkillType.Missile:
            case ImbuedSkillType.Magic:
            default:
                return (int)Math.Min(skill.Base, 500);
        }
    }

    public enum ImbuedSkillType
    {
        Undef,
        Melee,
        Missile,
        Magic
    }

    public static ImbuedSkillType GetImbuedSkillType(CreatureSkill skill)
    {
        switch (skill?.Skill)
        {
            case Skill.LightWeapons:
            case Skill.MartialWeapons:
            case Skill.FinesseWeapons:
            case Skill.DualWield:
            case Skill.TwoHandedCombat:

            // legacy
            case Skill.Axe:
            case Skill.Dagger:
            case Skill.Mace:
            case Skill.Spear:
            case Skill.Staff:
            case Skill.Sword:
            case Skill.UnarmedCombat:

                return ImbuedSkillType.Melee;

            case Skill.MissileWeapons:

            // legacy
            case Skill.Bow:
            case Skill.Crossbow:
            case Skill.Sling:
            case Skill.ThrownWeapon:

                return ImbuedSkillType.Missile;

            case Skill.WarMagic:
            case Skill.VoidMagic:
            case Skill.LifeMagic: // Martyr's Hecatomb

                return ImbuedSkillType.Magic;

            default:
                _log.Debug("WorldObject_Weapon.GetImbuedSkillType({Skill}): unexpected skill", skill?.Skill);
                return ImbuedSkillType.Undef;
        }
    }

    /// <summary>
    /// Returns the base skill multiplier to the maximum bonus
    /// </summary>
    public static float GetImbuedInterval(CreatureSkill skill, bool useMin = true)
    {
        var skillType = GetImbuedSkillType(skill);

        var min = 0;
        if (useMin)
        {
            min = skillType == ImbuedSkillType.Melee ? 150 : 125;
        }

        var max = skillType == ImbuedSkillType.Melee ? 400 : 360;

        return GetInterval((int)skill.Base, min, max);
    }

    /// <summary>
    /// Returns an interval between 0-1
    /// </summary>
    public static float GetInterval(int num, int min, int max)
    {
        if (num <= min)
        {
            return 0.0f;
        }

        if (num >= max)
        {
            return 1.0f;
        }

        var range = max - min;

        return (float)(num - min) / range;
    }

    /// <summary>
    /// Projects a 0-1 interval between min and max
    /// </summary>
    public static float SetInterval(float interval, float min, float max)
    {
        var range = max - min;

        return interval * range + min;
    }

    /// Spell ID for 'Cast on Strike'
    /// </summary>
    public uint? ProcSpell
    {
        get => GetProperty(PropertyDataId.ProcSpell);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyDataId.ProcSpell);
            }
            else
            {
                SetProperty(PropertyDataId.ProcSpell, value.Value);
            }
        }
    }

    /// <summary>
    /// The chance for activating 'Cast on strike' spell
    /// </summary>
    public double? ProcSpellRate
    {
        get => GetProperty(PropertyFloat.ProcSpellRate);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.ProcSpellRate);
            }
            else
            {
                SetProperty(PropertyFloat.ProcSpellRate, value.Value);
            }
        }
    }

    /// <summary>
    /// If TRUE, 'Cast on strike' spell targets self
    /// instead of the target
    /// </summary>
    public bool ProcSpellSelfTargeted
    {
        get => GetProperty(PropertyBool.ProcSpellSelfTargeted) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.ProcSpellSelfTargeted);
            }
            else
            {
                SetProperty(PropertyBool.ProcSpellSelfTargeted, value);
            }
        }
    }

    /// <summary>
    /// Returns TRUE if this item has a proc / 'cast on strike' spell
    /// </summary>
    public bool HasProc => ProcSpell != null;

    /// <summary>
    /// Returns TRUE if this item has a proc spell
    /// that matches the input spell
    /// </summary>
    public bool HasProcSpell(uint spellID)
    {
        return HasProc && ProcSpell == spellID;
    }

    private double NextProcAttemptTime = 0;

    public void TryProcItem(WorldObject attacker, Creature target, bool selfTarget)
    {
        if (target.IsDead)
        {
            return; // Target is already dead, abort!
        }

        if (ProcSpell == null)
        {
            _log.Error("TryProcItem() - ProcSpell = null for {Weapon}", this);
            return;
        }

        var spell = new Spell(ProcSpell.Value);

        if (spell.NotFound)
        {
            if (attacker is Player player)
            {
                if (spell._spellBase == null)
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat($"SpellId {ProcSpell.Value} Invalid.", ChatMessageType.System)
                    );
                }
                else
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat($"{spell.Name} spell not implemented, yet!", ChatMessageType.System)
                    );
                }
            }
            return;
        }

        //Console.WriteLine($"TryProcItem: {Name}");

        var currentTime = Time.GetUnixTime();

        // roll for a chance of casting spell
        var chance = ProcSpellRate ?? 0.0f;

        var creatureAttacker = attacker as Creature;
        var playerAttacker = attacker as Player;

        if (playerAttacker != null && playerAttacker.GetCreatureSkill(Skill.ArcaneLore).Current < ItemDifficulty)
        {
            return;
        }

        if (creatureAttacker != null)
        {
            if (NextProcAttemptTime > currentTime)
            {
                return;
            }

            if (playerAttacker != null)
            {
                chance *= playerAttacker.ScaleWithPowerAccuracyBar((float)chance);
                chance *= GetMagicSkillProcChanceMod(playerAttacker, spell);
                chance *= GetSpecMagicSkillProcChanceMod(playerAttacker, spell);
            }
        }

        // special handling for aetheria
        if (Aetheria.IsAetheria(WeenieClassId) && creatureAttacker != null)
        {
            chance = Aetheria.CalcProcRate(this, creatureAttacker);
        }

        var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (rng >= chance)
        {
            return;
        }

        // not sure if this should go before or after the resist check
        // after would match Player_Magic, but would require changing the signature of TryCastSpell yet again
        // starting with the simpler check here
        if (!selfTarget && target != null && target.NonProjectileMagicImmune && !spell.IsProjectile)
        {
            if (attacker is Player player)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You fail to affect {target.Name} with {spell.Name}",
                        ChatMessageType.Magic
                    )
                );
            }

            return;
        }

        var itemCaster = this is Creature ? null : this;

        if (spell.NonComponentTargetType == ItemType.None)
        {
            attacker.TryCastSpell(spell, null, itemCaster, itemCaster, true, true);
        }
        else if (spell.NonComponentTargetType == ItemType.Vestements)
        {
            // TODO: spell.NonComponentTargetType should probably always go through TryCastSpell_WithItemRedirects,
            // however i don't feel like testing every possible known type of item procspell in the current db to ensure there are no regressions
            // current test case: 33990 Composite Bow casting Tattercoat
            attacker.TryCastSpell_WithRedirects(spell, target, itemCaster, itemCaster, true, true);
        }
        else
        {
            if (playerAttacker != null)
            {
                var baseCost = spell.BaseMana;

                baseCost = playerAttacker switch
                {
                    { OverloadDischargeIsActive: true } => Convert.ToUInt32(baseCost * (1.0f + playerAttacker.DischargeLevel)),
                    { OverloadStanceIsActive: true } => Convert.ToUInt32(baseCost * (1.0f + playerAttacker.ManaChargeMeter)),
                    { BatteryDischargeIsActive: true } => Convert.ToUInt32(baseCost * (1.0f - playerAttacker.DischargeLevel)),
                    { BatteryStanceIsActive: true } => Convert.ToUInt32(baseCost * (1.0f - playerAttacker.ManaChargeMeter * 0.5f)),
                    _ => baseCost
                };

                var scarabReduction = spell.School == MagicSchool.LifeMagic
                    ? playerAttacker.GetSigilTrinketManaReductionMod(spell, Skill.LifeMagic,
                        SigilTrinketLifeWarMagicEffect.Reduction)
                    : playerAttacker.GetSigilTrinketManaReductionMod(spell, Skill.WarMagic,
                        SigilTrinketLifeWarMagicEffect.Reduction);

                if (playerAttacker.Mana.Current < (uint)(baseCost * scarabReduction))
                {
                    return;
                }

                // SPEC BONUS - Arcane Lore: 25% chance to reduce the mana cost of a proc spell by up to half, scaled for skill.
                var loreScaler = 1f;
                var lore = playerAttacker.GetCreatureSkill(Skill.ArcaneLore);
                if (lore.AdvancementClass == SkillAdvancementClass.Specialized)
                {
                    var attackSkill = playerAttacker.GetCreatureSkill(playerAttacker.GetCurrentAttackSkill());
                    var skillCheck = (float)lore.Current / (float)attackSkill.Current;
                    var reductionChance = skillCheck > 1f ? 0.25f : skillCheck * 0.25f;

                    if (reductionChance >= ThreadSafeRandom.Next(0f, 1f))
                    {
                        loreScaler = 0.5f;
                        if (lore.Current < attackSkill.Current)
                        {
                            loreScaler = 1f - skillCheck;
                        }
                    }
                }
                playerAttacker.UpdateVitalDelta(
                    playerAttacker.Mana,
                    (int)(baseCost * (scarabReduction * -1) * loreScaler)
                );
            }

            attacker.TryCastSpell(spell, target, itemCaster, itemCaster, true, true);
        }
    }

    /// <summary>
    /// Up to double proc chance based on player effective magic skill and spell difficulty
    /// </summary>
    private static double GetMagicSkillProcChanceMod(Player playerAttacker, Spell spell)
    {
        var school = spell.School;
        var difficulty = spell.Power;
        var magicSkill = school == MagicSchool.WarMagic ? playerAttacker.GetModdedWarMagicSkill() : playerAttacker.GetModdedLifeMagicSkill();

        return 1.0 + SkillCheck.GetMagicSkillChance((int)magicSkill, (int)difficulty);
    }

    /// <summary>
    /// Up to double proc chance based on player effective magic skill and spell difficulty
    /// </summary>
    private static double GetSpecMagicSkillProcChanceMod(Player playerAttacker, Spell spell)
    {
        var school = spell.School;
        var magicSkill = school == MagicSchool.WarMagic
            ? playerAttacker.GetCreatureSkill(Skill.WarMagic)
            : playerAttacker.GetCreatureSkill(Skill.LifeMagic);

        return magicSkill.AdvancementClass == SkillAdvancementClass.Specialized ? 2.0 : 1.0;
    }

    private bool? isMasterable;

    public bool IsMasterable
    {
        get
        {
            // should be based on this, but a bunch of the weapon data probably needs to be updated...
            //return W_WeaponType != WeaponType.Undef;

            // cache this?
            if (isMasterable == null)
            {
                isMasterable =
                    LongDesc == null
                    || !LongDesc.Contains("This weapon seems tough to master.", StringComparison.OrdinalIgnoreCase);
            }

            return isMasterable.Value;
        }
    }

    // from the Dark Majesty strategy guide, page 150:

    // -   0 - 1/3 sec. Power-up Time = High Stab
    // - 1/3 - 2/3 sec. Power-up Time = High Backhand
    // -       2/3 sec+ Power-up Time = High Slash

    public const float ThrustThreshold = 0.33f;

    /// <summary>
    /// Returns TRUE if this is a thrust/slash weapon,
    /// or if this weapon uses 2 different attack types based on the ThrustThreshold
    /// </summary>
    public bool IsThrustSlash
    {
        get
        {
            return W_AttackType.HasFlag(AttackType.Slash | AttackType.Thrust)
                || W_AttackType.HasFlag(AttackType.DoubleSlash | AttackType.DoubleThrust)
                || W_AttackType.HasFlag(AttackType.TripleSlash | AttackType.TripleThrust)
                || W_AttackType.HasFlag(AttackType.DoubleSlash); // stiletto
        }
    }

    public AttackType GetAttackType(MotionStance stance, bool slashThrustToggle, bool offhand)
    {
        if (offhand)
        {
            return GetOffhandAttackType(stance, slashThrustToggle);
        }

        var attackType = W_AttackType;

        if ((attackType & AttackType.Offhand) != 0 && attackType != AttackType.DoubleStrike)
        {
            _log.Warning($"{Name} ({Guid}, {WeenieClassId}).GetAttackType(): {attackType}");
            attackType &= ~AttackType.Offhand;
        }

        if (stance == MotionStance.DualWieldCombat)
        {
            if (attackType.HasFlag(AttackType.TripleThrust | AttackType.TripleSlash))
            {
                if (!slashThrustToggle)
                {
                    attackType = AttackType.TripleSlash;
                }
                else
                {
                    attackType = AttackType.TripleThrust;
                }
            }
            else if (attackType.HasFlag(AttackType.DoubleThrust | AttackType.DoubleSlash))
            {
                if (!slashThrustToggle)
                {
                    attackType = AttackType.DoubleSlash;
                }
                else
                {
                    attackType = AttackType.DoubleThrust;
                }
            }
            // handle old bugged stilettos that only have DoubleThrust
            // handle old bugged rapiers w/ Thrust, DoubleThrust
            else if (attackType.HasFlag(AttackType.DoubleThrust))
            {
                if (!slashThrustToggle || !attackType.HasFlag(AttackType.Thrust))
                {
                    attackType = AttackType.DoubleThrust;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
            // handle old bugged poniards and newer tachis
            else if (attackType.HasFlag(AttackType.Thrust | AttackType.DoubleSlash))
            {
                if (!slashThrustToggle)
                {
                    attackType = AttackType.DoubleSlash;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
            // gaerlan sword / py16 (iasparailaun)
            else if (attackType.HasFlag(AttackType.Thrust | AttackType.TripleSlash))
            {
                if (slashThrustToggle)
                {
                    attackType = AttackType.TripleSlash;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
        }
        else if (stance == MotionStance.SwordShieldCombat)
        {
            // force thrust animation when using a shield with a multi-strike weapon
            if (attackType.HasFlag(AttackType.TripleThrust))
            {
                if (!slashThrustToggle || !attackType.HasFlag(AttackType.Thrust))
                {
                    attackType = AttackType.TripleThrust;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
            else if (attackType.HasFlag(AttackType.DoubleThrust))
            {
                if (!slashThrustToggle || !attackType.HasFlag(AttackType.Thrust))
                {
                    attackType = AttackType.DoubleThrust;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
            // handle old bugged poniards and newer tachis w/ Thrust, DoubleSlash
            // and gaerlan sword / py16 (iasparailaun) w/ Thrust, TripleSlash
            else if (
                attackType.HasFlag(AttackType.Thrust)
                && (attackType & (AttackType.DoubleSlash | AttackType.TripleSlash)) != 0
            )
            {
                attackType = AttackType.Thrust;
            }
        }
        else if (stance == MotionStance.SwordCombat)
        {
            // force slash animation when using no shield with a multi-strike weapon
            if (attackType.HasFlag(AttackType.TripleSlash))
            {
                if (!slashThrustToggle || !attackType.HasFlag(AttackType.Thrust))
                {
                    attackType = AttackType.TripleSlash;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
            else if (attackType.HasFlag(AttackType.DoubleSlash))
            {
                if (!slashThrustToggle || !attackType.HasFlag(AttackType.Thrust))
                {
                    attackType = AttackType.DoubleSlash;
                }
                else
                {
                    attackType = AttackType.Thrust;
                }
            }
            // handle old bugged stilettos that only have DoubleThrust
            else if (attackType.HasFlag(AttackType.DoubleThrust))
            {
                attackType = AttackType.Thrust;
            }
        }

        if (attackType.HasFlag(AttackType.Thrust | AttackType.Slash))
        {
            if (!slashThrustToggle)
            {
                attackType = AttackType.Slash;
            }
            else
            {
                attackType = AttackType.Thrust;
            }
        }

        return attackType;
    }

    public AttackType GetOffhandAttackType(MotionStance stance, bool slashThrustToggle)
    {
        var attackType = W_AttackType;

        if ((attackType & AttackType.Offhand) != 0 && attackType != AttackType.DoubleStrike)
        {
            _log.Warning($"{Name} ({Guid}, {WeenieClassId}).GetOffhandAttackType(): {attackType}");
            attackType &= ~AttackType.Offhand;
        }

        if (attackType.HasFlag(AttackType.TripleThrust | AttackType.TripleSlash))
        {
            if (!slashThrustToggle)
            {
                attackType = AttackType.OffhandTripleSlash;
            }
            else
            {
                attackType = AttackType.OffhandTripleThrust;
            }
        }
        else if (attackType.HasFlag(AttackType.DoubleThrust | AttackType.DoubleSlash))
        {
            if (!slashThrustToggle)
            {
                attackType = AttackType.OffhandDoubleSlash;
            }
            else
            {
                attackType = AttackType.OffhandDoubleThrust;
            }
        }
        // handle old bugged stilettos that only have DoubleThrust
        // handle old bugged rapiers w/ Thrust, DoubleThrust
        else if (attackType.HasFlag(AttackType.DoubleThrust))
        {
            if (!slashThrustToggle || !attackType.HasFlag(AttackType.Thrust))
            {
                attackType = AttackType.OffhandDoubleThrust;
            }
            else
            {
                attackType = AttackType.OffhandThrust;
            }
        }
        // handle old bugged poniards and newer tachis w/ Thrust, DoubleSlash
        else if (attackType.HasFlag(AttackType.Thrust | AttackType.DoubleSlash))
        {
            if (!slashThrustToggle)
            {
                attackType = AttackType.OffhandDoubleSlash;
            }
            else
            {
                attackType = AttackType.OffhandThrust;
            }
        }
        // gaerlan sword / py16 (iasparailaun) w/ Thrust, TripleSlash
        else if (attackType.HasFlag(AttackType.Thrust | AttackType.TripleSlash))
        {
            if (!slashThrustToggle)
            {
                attackType = AttackType.OffhandTripleSlash;
            }
            else
            {
                attackType = AttackType.OffhandThrust;
            }
        }
        else if (attackType.HasFlag(AttackType.Thrust | AttackType.Slash))
        {
            if (!slashThrustToggle)
            {
                attackType = AttackType.OffhandSlash;
            }
            else
            {
                attackType = AttackType.OffhandThrust;
            }
        }
        else
        {
            switch (attackType)
            {
                case AttackType.Thrust:
                    attackType = AttackType.OffhandThrust;
                    break;

                case AttackType.Slash:
                    attackType = AttackType.OffhandSlash;
                    break;

                case AttackType.Punch:
                    attackType = AttackType.OffhandPunch;
                    break;
            }
        }
        return attackType;
    }
}
