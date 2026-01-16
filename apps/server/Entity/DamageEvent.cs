using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.DatLoader.Entity.AnimationHooks;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using ACE.Server.WorldObjects.Entity;
using Serilog;
using Time = ACE.Common.Time;

namespace ACE.Server.Entity;

public class DamageEvent
{
    private readonly ILogger _log = Log.ForContext<DamageEvent>();

    private float _accuracyMod;
    private float _ammoEffectMod;
    private List<WorldObject> _armor;
    private float _armorMod;
    private Creature _attacker;
    private AttackHeight _attackHeight;
    private float _attackHeightDamageBonus;
    private AttackHook _attackHook;
    private MotionCommand? _attackMotion;
    private KeyValuePair<CombatBodyPart, PropertiesBodyPart> _attackPart; // body part this monster is attacking with
    private CreatureSkill _attackSkill;
    private AttackType _attackType; // slash / thrust / punch / kick / offhand / multistrike
    private float _attributeMod;
    private float _backstabDamageMultiplier;
    private float _baseDamage;
    private BaseDamageMod _baseDamageMod;
    private float _combatAbilityFuryDamageBonus;
    private float _combatAbilityRelentlessDamagePenalty;
    private float _combatAbilityMultishotDamagePenalty;
    private float _combatAbilityProvokeDamageReduction;
    private Creature_BodyPart _creaturePart;
    private float _criticalChance;
    private float _criticalDamageMod;
    private float _criticalDamageRating;
    private float _criticalDamageResistanceRatingMod;
    private bool _criticalDefendedFromAug;
    private float _damageBeforeMitigation;
    private float _damageMitigated;
    private float _damageRatingMod;
    private float _damageResistanceRatingBaseMod;
    private float _damageResistanceRatingMod;
    private WorldObject _damageSource;
    private Creature _defender;
    private float _dualWieldDamageBonus;
    private uint _effectiveDefenseSkill;
    private float _evasionMod;
    private bool _generalFailure;
    private float _ignoreArmorMod;
    private bool _invulnerable;
    private float _levelScalingMod;
    private bool _overpower;
    private bool _pkBattle;
    private float _pkDamageMod;
    private float _pkDamageResistanceMod;
    private Player _playerAttacker;
    private Player _playerDefender;
    private float _powerMod;
    private KeyValuePair<CombatBodyPart, PropertiesBodyPart> _propertiesBodyPart;
    private Quadrant _quadrant;
    private float _ratingElementalDamageBonus;
    private float _ratingDamageTypeWard;
    private float _ratingRedFury;
    private float _ratingYellowFury;
    private float _ratingSelfHarm;
    private float _ratingPierceResistanceBonus;
    private float _recklessnessMod;
    private float _resistanceMod;
    private float _slayerMod;
    private float _specDefenseMod;
    private float _swarmedDamageReductionMod;
    private float _combatAbilitySteadyStrikeDamageBonus;
    private float _twohandedCombatDamageBonus;
    private float _weaponResistanceMod;

    private bool IgnoreMagicArmor =>
        (Weapon?.IgnoreMagicArmor ?? false) || (_attacker?.IgnoreMagicArmor ?? false); // ignores impen / banes

    private bool IgnoreMagicResist =>
        (Weapon?.IgnoreMagicResist ?? false) || (_attacker?.IgnoreMagicResist ?? false); // ignores life armor / prots

    public bool Blocked { get; private set; }
    public bool Parried { get; private set; }
    public float CriticalDamageBonusFromTrinket { get; set; }
    public bool CriticalOverridedByTrinket { get; set; }
    public bool Evaded { get; set; }
    public bool LifestoneProtection { get; private set; }
    public PartialEvasion PartialEvasion { get; set; }
    public uint EffectiveAttackSkill { get; private set; }
    public float SneakAttackMod { get; private set; }
    public bool IsCritical { get; private set; }
    public BodyPart BodyPart { get; private set; }
    public float ShieldMod { get; private set; }
    public float Damage { get; private set; }
    public CombatType CombatType { get; private set; }
    public DamageType DamageType { get; private set; }
    public WorldObject Weapon { get; private set; }
    public WorldObject DefenderWeapon { get; private set; }
    public WorldObject Offhand { get; private set; }

    public bool HasDamage => !Evaded && !Blocked && !Parried && !LifestoneProtection;

    public AttackConditions AttackConditions
    {
        get
        {
            var attackConditions = new AttackConditions();

            if (_criticalDefendedFromAug)
            {
                attackConditions |= AttackConditions.CriticalProtectionAugmentation;
            }

            if (_recklessnessMod > 1.0f)
            {
                attackConditions |= AttackConditions.Recklessness;
            }

            if (SneakAttackMod > 1.0f)
            {
                attackConditions |= AttackConditions.SneakAttack;
            }

            if (_overpower)
            {
                attackConditions |= AttackConditions.Overpower;
            }

            return attackConditions;
        }
    }

    public static DamageEvent CalculateDamage(
        Creature attacker,
        Creature defender,
        WorldObject damageSource,
        MotionCommand? attackMotion = null,
        AttackHook attackHook = null,
        bool cleaveHits = false
    )
    {
        var damageEvent = new DamageEvent { _attackMotion = attackMotion, _attackHook = attackHook };

        damageSource ??= attacker;

        damageEvent.DoCalculateDamage(attacker, defender, damageSource, cleaveHits);

        damageEvent.HandleLogging(attacker, defender);

        return damageEvent;
    }

    private float DoCalculateDamage(Creature attacker, Creature defender, WorldObject damageSource, bool cleaveHits = false)
    {
        if (PropertyManager.GetBool("debug_level_scaling_system").Item && (attacker is Player || defender is Player))
        {
            Console.WriteLine($"\n\n---- LEVEL SCALING - {attacker.Name} vs {defender.Name} ----");
        }

        if (defender.Name is "Placeholder")
        {
            return 0;
        }

        SetCombatSources(attacker, defender, damageSource);
        CheckForOnAttackEffects(cleaveHits);

        SetInvulnerable(defender);
        SetEvaded(attacker, defender);
        SetBlocked(attacker, defender);
        SetParry(attacker, defender);

        if (_invulnerable || Evaded || Blocked || Parried)
        {
            if (Blocked)
            {
                CheckForRatingThorns(attacker, defender, damageSource);
            }

            return 0.0f;
        }

        _damageBeforeMitigation = GetDamageBeforeMitigation(attacker, defender, damageSource);

        if (_generalFailure)
        {
            return 0.0f;
        }

        var mitigation = GetMitigation(attacker, defender);
        var cleaveMod = cleaveHits ? 0.5f : 1.0f;

        Damage = _damageBeforeMitigation * mitigation * cleaveMod;
        _damageMitigated = _damageBeforeMitigation - Damage;

        PostDamageMitigationEffects(attacker, defender, damageSource);

        //DpsLogging();

        return Damage;
    }

    private void CheckForOnAttackEffects(bool cleaveHits = false)
    {
        if (cleaveHits)
        {
            return;
        }

        if (Weapon is not null && _playerAttacker is { RelentlessStanceIsActive: true })
        {
            _playerAttacker.IncreaseRelentlessAdrenalineMeter(Weapon);
        }

        if (Weapon is not null && _playerAttacker is { FuryStanceIsActive: true })
        {
            _playerAttacker.IncreaseFuryAdrenalineMeter(Weapon);
        }
    }

    /// <summary>
    /// Sets PlayerAttacker, PlayerDefender, Attacker, Defender, PkBattle, AttackSkill, CombatType, DamageSource, Weapon, AttackType, AttackHeight
    /// </summary>
    private void SetCombatSources(Creature attacker, Creature defender, WorldObject damageSource)
    {
        _playerAttacker = attacker as Player;
        _playerDefender = defender as Player;

        _pkBattle = _playerAttacker != null && _playerDefender != null;

        _attacker = attacker;
        _defender = defender;

        _attackSkill = attacker.GetCreatureSkill(attacker.GetCurrentWeaponSkill());

        CombatType = damageSource.ProjectileSource == null ? CombatType.Melee : CombatType.Missile;

        _damageSource = damageSource;

        Weapon =
            damageSource.ProjectileSource == null
                ? attacker.GetEquippedMeleeWeapon()
                : (damageSource.ProjectileLauncher ?? damageSource.ProjectileAmmo);

        DefenderWeapon =
            defender.GetEquippedWeapon();

        Offhand = attacker.GetEquippedOffHand();

        _attackType = attacker.AttackType;
        _attackHeight = attacker.AttackHeight ?? AttackHeight.Medium;
    }

    private void SetInvulnerable(Creature defender)
    {
        var playerDefender = defender as Player;

        if (playerDefender is { UnderLifestoneProtection: true })
        {
            LifestoneProtection = true;
            playerDefender.HandleLifestoneProtection();
            {
                _invulnerable = true;
            }
        }

        if (defender.Invincible)
        {
            {
                _invulnerable = true;
            }
        }

        _invulnerable = false;
    }

    /// <summary>
    /// Checks for Overpower, Steady Strike, Fury, and Backstab auto-hits.
    /// If evade succeeded, determine if evade was full, partial, or none.
    /// Equal chance for each evasion type to occur.
    /// </summary>
    private void SetEvaded(Creature attacker, Creature defender)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        Evaded = false;
        _evasionMod = 1.0f;
        PartialEvasion = PartialEvasion.None;

        if (defender.CombatMode is CombatMode.NonCombat)
        {
            return;
        }

        // Check for guaranteed hits
        var isOverpower = CheckForOverpower(attacker, defender);
        var isFuryNoEvade = CheckForCombatAbilityEnrageNoEvade(playerAttacker);
        var isBackstabNoEvade = CheckForCombatAbilityBackstabStealthNoEvade(playerAttacker, defender);

        if (isOverpower || isFuryNoEvade || isBackstabNoEvade || attacker == defender)
        {
            return;
        }

        // Roll combat hit chance
        var attackRoll = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (attackRoll > GetEvadeChance(attacker, defender))
        {
            // If playerDefender has Phalanx active, 25-50% chance to convert a full hit into a partial hit, depending on shield size.
            if (playerDefender is { PhalanxIsActive: true } && (playerDefender.GetEquippedShield() is not null || playerDefender.GetEquippedWeapon() is { IsTwoHanded: true}))
            {
                var phalanxChance = 0.25;

                if (playerDefender.GetEquippedShield() is not null)
                {
                    phalanxChance = playerDefender.GetEquippedShield().ArmorStyle switch
                    {
                        (int)ArmorStyle.CovenantShield => 0.5f,
                        (int)ArmorStyle.TowerShield => 0.45f,
                        (int)ArmorStyle.LargeShield => 0.4f,
                        (int)ArmorStyle.StandardShield => 0.35f,
                        (int)ArmorStyle.SmallShield => 0.3f,
                        (int)ArmorStyle.Buckler => 0.3f,
                        _ => 0.25f
                    };
                }

                if (ThreadSafeRandom.Next(0.0f, 1.0f) < phalanxChance)
                {
                    _evasionMod = 0.5f;
                    PartialEvasion = PartialEvasion.Some;
                }
            }

            return;
        }

        // Roll evade type (33% for each evade type)
        const float fullEvadeChance = 1.0f / 3.0f;
        const float partialEvadeChance = fullEvadeChance * 2;

        var partialEvadeRoll = ThreadSafeRandom.Next(0.0f, 1.0f);

        if (playerDefender is { EvasiveStanceIsActive: true })
        {
            var luckyRoll = ThreadSafeRandom.Next(0.0f, 1.0f);
            if (luckyRoll < partialEvadeRoll)
            {
                partialEvadeRoll = luckyRoll;
            }
        }

        switch (partialEvadeRoll)
        {
            case < fullEvadeChance:
                PartialEvasion = PartialEvasion.All;
                Evaded = true;
                break;
            case < partialEvadeChance:
                _evasionMod = 0.5f;
                PartialEvasion = PartialEvasion.Some; // glancing blow
                Evaded = false;
                break;
            default:
                // If playerDefender has Phalanx active, 50% chance to convert a full hit into a partial hit.
                if (playerDefender is { PhalanxIsActive: true } && (playerDefender.GetEquippedShield() is not null || playerDefender.GetEquippedWeapon() is { IsTwoHanded: true}))
                {
                    var phalanxChance = 0.25;

                    if (playerDefender.GetEquippedShield() is not null)
                    {
                        phalanxChance = playerDefender.GetEquippedShield().ArmorStyle switch
                        {
                            (int)ArmorStyle.CovenantShield => 0.5f,
                            (int)ArmorStyle.TowerShield => 0.45f,
                            (int)ArmorStyle.LargeShield => 0.4f,
                            (int)ArmorStyle.StandardShield => 0.35f,
                            (int)ArmorStyle.SmallShield => 0.3f,
                            (int)ArmorStyle.Buckler => 0.3f,
                            _ => 0.25f
                        };
                    }

                    if (ThreadSafeRandom.Next(0.0f, 1.0f) < phalanxChance)
                    {
                        _evasionMod = 0.5f;
                        PartialEvasion = PartialEvasion.Some;
                        Evaded = false;
                        break;
                    }
                }

                _evasionMod = 1.0f;
                PartialEvasion = PartialEvasion.None;
                Evaded = false;
                break;
        }

        if (playerDefender is not null && PartialEvasion == PartialEvasion.Some)
        {
            playerDefender.CheckForSigilTrinketOnAttackEffects(playerAttacker, this, Skill.PhysicalDefense, SigilTrinketPhysicalDefenseEffect.Evasion);
        }
    }

    private bool CheckForOverpower(Creature attacker, Creature defender)
    {
        if (attacker.Overpower == null)
        {
            return false;
        }

        _overpower = Creature.GetOverpower(attacker, defender);
        return _overpower;
    }

    private bool CheckForCombatAbilityEnrageNoEvade(Player playerAttacker)
    {
        if (playerAttacker is not {FuryEnrageIsActive: true})
        {
            return false;
        }

        Evaded = false;
        PartialEvasion = PartialEvasion.None;
        _evasionMod = 1f;

        return true;
    }

    /// <summary>
    /// Attack cannot be evaded if Backstab ability activated when attacking from behind and stealthed
    /// </summary>
    private bool CheckForCombatAbilityBackstabStealthNoEvade(Player playerAttacker, Creature creatureTarget)
    {
        if (playerAttacker is {BackstabIsActive: true, IsAttackFromStealth: true} && playerAttacker.IsBehindTargetCreature(creatureTarget))
        {
            Evaded = false;
            PartialEvasion = PartialEvasion.None;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks attack angle and if defender has Spec Shield and/or Phalanx is active. If a block is possible,
    /// check for and combine bonuses from Spec Physical Defense, Phalanx Ability, and Block Rating.
    /// Roll and set Blocked accordingly.
    /// <list type="bullet">
    /// <item>Spec Bonus - Shield: Effective block angle is 270 degress instead of 180.</item>
    /// <item>Spec Bonus - Phys Def: Up to +50% increased block chance.</item>
    /// <item>Gear Block Rating: +X% increased block chance, equal to 10% + 0.5% per rating.</item>
    /// <item>Phalanx Ability: Effective block angle is 360 degrees and all glancing blows become blocks.</item>
    /// </list>
    /// </summary>
    private void SetBlocked(Creature attacker, Creature defender)
    {
        Blocked = false;

        var playerDefender = defender as Player;

        if (defender.CombatMode is CombatMode.NonCombat)
        {
            return;
        }

        var equippedShield = defender.GetEquippedShield();
        if (equippedShield is null)
        {
            return;
        }

        var effectiveAngle = 180.0f;
        effectiveAngle += GetSpecShieldEffectiveAngleBonus(playerDefender);

        var blockableAngle = Math.Abs(defender.GetAngle(attacker)) < effectiveAngle / 2.0f || playerDefender is {PhalanxIsActive: true};

        if (playerDefender is { PhalanxIsActive: false } && !blockableAngle)
        {
            return;
        }

        // If playerDefender has Phalanx active, up to 50% chance to convert partial hits into blocks/parries.
        if (playerDefender is { PhalanxIsActive: true }
            && PartialEvasion == PartialEvasion.Some)
        {
            var phalanxChance = playerDefender.GetEquippedShield().ArmorStyle switch
            {
                (int)ArmorStyle.CovenantShield => 0.5f,
                (int)ArmorStyle.TowerShield => 0.45f,
                (int)ArmorStyle.LargeShield => 0.4f,
                (int)ArmorStyle.StandardShield => 0.35f,
                (int)ArmorStyle.SmallShield => 0.3f,
                (int)ArmorStyle.Buckler => 0.3f,
                _ => 0.25f
            };

            if (ThreadSafeRandom.Next(0.0f, 1.0f) < phalanxChance)
            {
                Blocked = true;
                return;
            }
        }

        // base block/parry chance is 5%
        // Blocks (shields) can have up to +5% additional base chance, depending on Shield Level vs Attacker Skill
        // Parry base chance is always 5%
        const float minBlockChance = 0.05f;

        var blockChanceShieldBonus = GetBlockChanceShieldLevelBonus(defender, (int)defender.GetSkillModifiedShieldLevel((equippedShield.ArmorLevel ?? 1)));
        var baseBlockChance = minBlockChance * blockChanceShieldBonus;

        // other bonuses are additive then multiplied against base block chance
        // Spec Phys Def = up to 50%, Jewels = 10% + ratings, Riposte = 100%
        var specPhysicalDefenseBlockChanceBonus = GetSpecPhysicalDefenseBlockChanceBonus();
        var jewelBlockChanceBonus = Jewel.GetJewelEffectMod(playerDefender, PropertyInt.GearBlock);
        var riposteBlockChanceBonus = 0.0f;
        if (playerDefender is { RiposteIsActive: true })
        {
            riposteBlockChanceBonus = 1.0f;
        }

        var blockChance = baseBlockChance * (1.0f + specPhysicalDefenseBlockChanceBonus + jewelBlockChanceBonus + riposteBlockChanceBonus);

        if ((ThreadSafeRandom.Next(0f, 1f) > blockChance))
        {
            return;
        }

        Blocked = true;
    }

    /// <summary>
    /// </summary>
    private void SetParry(Creature attacker, Creature defender)
    {
        Parried = false;

        var playerDefender = defender as Player;

        if (defender.CombatMode is CombatMode.NonCombat)
        {
            return;
        }

        var equippedMainHand = defender.GetEquippedWeapon();
        var equippedOffHand = defender.GetEquippedOffHand();

        if (equippedMainHand is { IsTwoHanded: not true } && equippedOffHand is not {ItemType: ItemType.MeleeWeapon})
        {
            return;
        }

        const float effectiveAngle = 180.0f;
        var parryAngle = Math.Abs(defender.GetAngle(attacker)) < effectiveAngle / 2.0f;
        var twohandPhalanxActive = playerDefender is { PhalanxIsActive: true } && playerDefender.GetEquippedWeapon() is { IsTwoHanded: true };

        if (!parryAngle && !twohandPhalanxActive)
        {
            return;
        }

        // If playerDefender has Phalanx active, 50% chance to convert partial hits into blocks/parries.
        if (playerDefender is { PhalanxIsActive: true }
            && PartialEvasion == PartialEvasion.Some
            && ThreadSafeRandom.Next(0.0f, 1.0f) > 0.25f)
        {
            Parried = true;
            return;
        }

        var parrySkillUsed = equippedMainHand is { IsTwoHanded: true }
            ? defender.GetModdedTwohandedCombatSkill()
            : defender.GetModdedDualWieldSkill();

        var parryMod = SkillCheck.GetSkillChance((uint)(parrySkillUsed * 1.5), EffectiveAttackSkill);

        // parry chance is up to 10%, based on Two-hand or Dual-wield skill levels vs Attack Skill
        var maxBaseParryChance = 0.1f * parryMod;

        // other bonuses are additive then multiplied against base parry chance
        // Spec Phys Def = up to 50%, Riposte = 100%
        var specPhysicalDefenseParryChanceBonus = GetSpecPhysicalDefenseBlockChanceBonus();
        var riposteActivatedBonus = playerDefender is { RiposteIsActive: true } ? 1.0f : 0.0f;
        var parryChance = maxBaseParryChance * (1.0 + specPhysicalDefenseParryChanceBonus + riposteActivatedBonus);

        if ((ThreadSafeRandom.Next(0f, 1f) > parryChance))
        {
            return;
        }

        Parried = true;
    }

    private double GetBlockChanceShieldLevelBonus(Creature defender, int shieldLevel)
    {
        var effectiveShieldLevel = (uint)defender.GetSkillModifiedShieldLevel(shieldLevel);

        return 1.0 + SkillCheck.GetSkillChance(effectiveShieldLevel, EffectiveAttackSkill);
    }

    private float GetDamageBeforeMitigation(Creature attacker, Creature defender, WorldObject damageSource)
    {
        SetBaseDamage(attacker, defender, damageSource);
        SetDamageModifiers(attacker, defender);

        _criticalChance = GetCriticalChance(attacker, defender);

        var roll = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (roll > _criticalChance || GetCriticalDefendedFromAug(attacker, defender) || CheckForSpecPerceptionCriticalDefense(defender as Player))
        {
            _playerAttacker?.CheckForSigilTrinketOnAttackEffects(defender, this, Skill.TwoHandedCombat, SigilTrinketShieldTwohandedCombatEffect.Might);
            _playerAttacker?.CheckForSigilTrinketOnAttackEffects(defender, this, Skill.Shield, SigilTrinketShieldTwohandedCombatEffect.Might);

            if (!CriticalOverridedByTrinket)
            {
                return GetNonCriticalDamageBeforeMitigation();
            }
        }

        IsCritical = true;
        return GetCriticalDamageBeforeMitigation(attacker, defender);
    }

    private void SetBaseDamage(Creature attacker, Creature defender, WorldObject damageSource)
    {
        if (attacker is Player playerAttacker)
        {
            GetBaseDamage(playerAttacker);
        }
        else
        {
            GetBaseDamage(attacker, _attackMotion ?? MotionCommand.Invalid, _attackHook);
        }

        if (DamageType == DamageType.Undef)
        {
            if ((attacker?.Guid.IsPlayer() ?? false) || (damageSource?.Guid.IsPlayer() ?? false))
            {
                _log.Error(
                    $"DamageEvent.DoCalculateDamage({attacker?.Name} ({attacker?.Guid}), {defender?.Name} ({defender?.Guid}), {damageSource?.Name} ({damageSource?.Guid})) - DamageType == DamageType.Undef"
                );
                _generalFailure = true;
            }
        }
    }

    private void SetDamageModifiers(Creature attacker, Creature defender, float? powerMod = null)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        _powerMod = powerMod ?? attacker.GetPowerMod(Weapon);
        _attributeMod = attacker.GetAttributeMod(Weapon, false);
        _slayerMod = WorldObject.GetWeaponCreatureSlayerModifier(Weapon, attacker, defender);
        _damageRatingMod = Creature.GetPositiveRatingMod(attacker.GetDamageRating());
        _dualWieldDamageBonus = GetDualWieldDamageBonus(playerAttacker, defender);
        _twohandedCombatDamageBonus = GetTwohandedCombatDamageBonus(playerAttacker, defender);
        _combatAbilityMultishotDamagePenalty = GetCombatAbilityMultishotDamagePenalty(playerAttacker);
        _combatAbilityFuryDamageBonus = GetCombatAbilityFuryDamageBonus(playerAttacker, playerDefender);
        _combatAbilityRelentlessDamagePenalty = GetCombatAbilityRelentlessDamagePenalty(playerAttacker);
        _combatAbilitySteadyStrikeDamageBonus = GetCombatAbilitySteadySrikeDamageBonus(playerAttacker);
        _recklessnessMod = Creature.GetRecklessnessMod(attacker, defender);
        SneakAttackMod = attacker.GetSneakAttackMod(defender);
        _backstabDamageMultiplier = Creature.GetStealthBackstabDamageMultiplier(playerAttacker, defender);
        _attackHeightDamageBonus += GetHighAttackHeightBonus(playerAttacker);
        _ratingElementalDamageBonus = Jewel.HandleElementalBonuses(playerAttacker, DamageType);
        _ratingPierceResistanceBonus = GetRatingPierceResistanceBonus(defender, playerAttacker);
        _levelScalingMod = GetLevelScalingMod(attacker, defender, playerDefender);
        _ammoEffectMod = GetAmmoEffectMod(Weapon, playerAttacker);

        if (!_pkBattle)
        {
            return;
        }

        _pkDamageMod = Creature.GetPositiveRatingMod(attacker.GetPKDamageRating());
        _damageRatingMod = Creature.AdditiveCombine(_damageRatingMod, _pkDamageMod);
    }

    /// <summary>
    /// Dual Wield Damage Mod
    /// </summary>
    private float GetDualWieldDamageBonus(Player playerAttacker, Creature defender)
    {
        if (playerAttacker is not {IsDualWieldAttack: true} || defender is null)
        {
            return 1.0f;
        }

        var moddedDualWieldCombatSkill = (uint)(playerAttacker.GetModdedDualWieldSkill() * 1.5f);
        var defenderPhysicalDefense = defender.GetModdedPhysicalDefSkill();

        var damageMod = 0.5f * SkillCheck.GetSkillChance(moddedDualWieldCombatSkill, defenderPhysicalDefense);

        var finalDamageMod = 1.0f + (float)damageMod;

        return finalDamageMod;
    }

    /// <summary>
    /// Two-handed Combat Damage Mod
    /// </summary>
    private static float GetTwohandedCombatDamageBonus(Player playerAttacker, Creature defender)
    {
        if (playerAttacker?.GetEquippedWeapon() is null
            || playerAttacker.GetEquippedWeapon().W_WeaponType is not WeaponType.TwoHanded
            || defender is null)
        {
            return 1.0f;
        }

        var moddedTwohandedCombatSkill = (uint)(playerAttacker.GetModdedTwohandedCombatSkill() * 1.5f);
        var defenderPhysicalDefense = defender.GetModdedPhysicalDefSkill();

        var damageMod = 0.5f * SkillCheck.GetSkillChance(moddedTwohandedCombatSkill, defenderPhysicalDefense);

        var finalDamageMod = 1.0f + (float)damageMod;

        return finalDamageMod;
    }

    /// <summary>
    /// COMBAT ABILITY - Multishot: Damage reduced by 25% if 2 targets, by 33% if 3 targets.
    /// </summary>
    private float GetCombatAbilityMultishotDamagePenalty(Player playerAttacker)
    {
        return 1.0f; // TODO: Decide if this damage penalty is needed

        if (playerAttacker is not { MultiShotIsActive: true})
        {
            return 1.0f;
        }

        return playerAttacker.MultishotNumTargets switch
        {
            3 => 0.67f,
            2 => 0.75f,
            _ => 1.0f,
        };
    }

    /// <summary>
    /// COMBAT ABILITY - Provoke: Damage taken reduced by 15%.
    /// </summary>
    private float GetCombatAbilityProvokeDamageReduction(Player playerDefender)
    {
        return playerDefender is { ProvokeIsActive: true } ? 0.85f : 1.0f;
    }

    private void PostDamageMitigationEffects()
    {
        if (_attacker.IsMonster)
        {
            Damage *= 1.0f;
        }

        if (!_attacker.IsMonster)
        {
            Damage *= 1.0f;
        }
    }

    /// <summary>
    /// COMBAT ABILITY - Fury (Stance): Damage dealt and taken is increased by up to 25%. Attacking and
    /// taking damage builds up a Adrenaline meter. Adrenaline meter decreases over time.
    /// COMBAT ABILITY - Fury (Enrage): Damage increased by Adrenaline build up amount (%) for 10 seconds.
    /// </summary>
    private static float GetCombatAbilityFuryDamageBonus(Player playerAttacker, Player playerDefender)
    {
        var recklessMod = 1.0f;

        if (playerAttacker is {FuryStanceIsActive: true})
        {
            recklessMod += 0.25f * playerAttacker.AdrenalineMeter;
        }

        if (playerDefender is { FuryStanceIsActive: true } or { FuryEnrageIsActive: true })
        {
            recklessMod += 0.25f * playerDefender.AdrenalineMeter;
        }

        if (playerAttacker is {FuryEnrageIsActive: true})
        {
            recklessMod += playerAttacker.EnrageLevel;
        }

        return recklessMod;
    }

    /// <summary>
    /// COMBAT ABILITY - Relentless (Stance): Up to -10% damage, based on relentless adrenaline stacks.
    /// </summary>
    private static float GetCombatAbilityRelentlessDamagePenalty(Player playerAttacker)
    {
        if (playerAttacker is not { RelentlessStanceIsActive: true })
        {
            return 1.0f;
        }

        return 1.0f - 0.1f * playerAttacker.AdrenalineMeter;
    }

    /// <summary>
    /// COMBAT ABILITY - Steady Strike: +25% damage with melee/missile weapons.
    /// </summary>
    /// <param name="playerAttacker"></param>
    /// <returns></returns>
    private float GetCombatAbilitySteadySrikeDamageBonus(Player playerAttacker)
    {
        if (playerAttacker?.GetEquippedWeapon() is null)
        {
            return 1.0f;
        }

        if (playerAttacker.GetPowerAccuracyBar() < 0.5f)
        {
            return 1.0f;
        }

        return playerAttacker is {SteadyStrikeIsActive: true} ? 1.25f : 1.0f;
    }

    /// <summary>
    /// ATTACK HEIGHT BONUS - High: (10% increased damage, 20% if weapon is specialized)
    /// </summary>
    private float GetHighAttackHeightBonus(Player playerAttacker)
    {
        if (playerAttacker is { AttackHeight: AttackHeight.High })
        {
            return WeaponIsSpecialized(playerAttacker) ? 1.2f : 1.10f;
        }

        return 1.0f;
    }

    private static float GetLevelScalingMod(Creature attacker, Creature defender, Player playerDefender)
    {
        var monsterHealthScalingMod = playerDefender != null
            ? LevelScaling.GetMonsterDamageDealtHealthScalar(playerDefender, attacker)
            : LevelScaling.GetMonsterDamageTakenHealthScalar(attacker, defender);

        return monsterHealthScalingMod;
    }

    private float GetCriticalChance(Creature attacker, Creature defender)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        if (playerDefender != null && (playerDefender.IsLoggingOut || playerDefender.PKLogout || playerDefender.CombatMode is CombatMode.NonCombat))
        {
            return 1.0f;
        }

        if (CheckForRatingReprisal(playerAttacker))
        {
            playerAttacker.IsAttackFromStealth = false;
            return 1.0f;
        }

        var criticalChance = WorldObject.GetWeaponCriticalChance(Weapon, attacker, _attackSkill, defender);
        criticalChance += GetPlayerSpecSkillCriticalChanceBonus();

        return criticalChance;
    }

    private bool GetCriticalDefendedFromAug(Creature attacker, Creature defender)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        _criticalDefendedFromAug = CheckForAugmentationCriticalDefense(playerDefender, playerAttacker);

        return _criticalDefendedFromAug;
    }

    private float GetCriticalDamageBeforeMitigation(Creature attacker, Creature defender)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        CriticalDamageBonusFromTrinket = 1.0f;
        playerAttacker?.CheckForSigilTrinketOnAttackEffects(defender, this, Skill.Thievery, SigilTrinketThieveryEffect.Treachery, true);

        _criticalDamageMod = 1.0f + WorldObject.GetWeaponCritDamageMod(Weapon, attacker, _attackSkill, defender);
        _criticalDamageMod += GetMaceSpecCriticalDamageBonus(playerAttacker);
        _criticalDamageMod += GetStaffSpecCriticalDamageBonus(playerAttacker);
        _criticalDamageMod *= 1.0f + Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearBludgeon, "Bludgeon");
        _criticalDamageMod *= CriticalDamageBonusFromTrinket;

        CheckForRatingReprisalCriticalDefense(attacker, playerDefender);

        _criticalDamageRating = Creature.GetPositiveRatingMod(attacker.GetCritDamageRating());
        _damageRatingMod = Creature.AdditiveCombine(_damageRatingMod, _criticalDamageRating);

        if (_pkBattle)
        {
            _damageRatingMod = Creature.AdditiveCombine(_damageRatingMod, _pkDamageMod);
        }

        if (_baseDamageMod is null)
        {
            _log.Error("GetCriticalDamageBeforeMitigation({Attacker}, {Defender}) - _baseDamageMod is null", attacker.Name, defender.Name);
            return 0;
        }

        var baseDamage = playerAttacker != null ? _baseDamageMod.MaxDamage : _baseDamageMod.MedianDamage;

        return baseDamage
               * _attributeMod
               * _powerMod
               * _slayerMod
               * _damageRatingMod
               * _criticalDamageMod
               * _dualWieldDamageBonus
               * _twohandedCombatDamageBonus
               * _combatAbilityMultishotDamagePenalty
               * _combatAbilityFuryDamageBonus
               * _combatAbilityRelentlessDamagePenalty
               * _combatAbilitySteadyStrikeDamageBonus
               * _ratingElementalDamageBonus
               * _ratingPierceResistanceBonus
               * _recklessnessMod
               * SneakAttackMod
               * _backstabDamageMultiplier
               * _attackHeightDamageBonus
               * _ammoEffectMod
               * _levelScalingMod;
    }

    private float GetNonCriticalDamageBeforeMitigation()
    {
        return _baseDamage
               * _attributeMod
               * _powerMod
               * _slayerMod
               * _damageRatingMod
               * _recklessnessMod
               * SneakAttackMod
               * _backstabDamageMultiplier
               * _attackHeightDamageBonus
               * _ratingElementalDamageBonus
               * _ratingPierceResistanceBonus
               * _dualWieldDamageBonus
               * _twohandedCombatDamageBonus
               * _combatAbilityMultishotDamagePenalty
               * _combatAbilityFuryDamageBonus
               * _combatAbilityRelentlessDamagePenalty
               * _combatAbilitySteadyStrikeDamageBonus
               * _ammoEffectMod
               * _levelScalingMod;
    }

    private float GetMitigation(Creature attacker, Creature defender)
    {
        if (attacker is null || defender is null)
        {
            return 1.0f;
        }

        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        _ignoreArmorMod = GetIgnoreArmorMod(attacker, defender);
        _ignoreArmorMod -= GetSpearSpecIgnoreArmorBonus(attacker);

        _armorMod = GetArmorMod(attacker, defender);

        _weaponResistanceMod = WorldObject.GetWeaponResistanceModifier(
            Weapon,
            attacker,
            _attackSkill,
            DamageType,
            defender
        );

        _resistanceMod = GetResistanceMod(defender, playerDefender);

        if (DamageType is DamageType.Pierce)
        {
            _resistanceMod += Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearPierce, "Pierce");
        }

        _damageResistanceRatingMod = GetDamageResistRatingMod(defender, _pkBattle);
        _damageResistanceRatingMod *= 1.0f + Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearHardenedDefense, "Hardened Defense");

        _specDefenseMod = GetSpecDefenseMod(attacker, playerDefender);

        ShieldMod = _defender.GetShieldMod(attacker, DamageType, Weapon);

        _combatAbilityProvokeDamageReduction = GetCombatAbilityProvokeDamageReduction(playerDefender);

        _ratingSelfHarm = 1.0f + Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearSelfHarm);
        _ratingRedFury = 1.0f + Jewel.GetJewelRedFury(playerAttacker);
        _ratingYellowFury = 1.0f + Jewel.GetJewelYellowFury(playerAttacker);
        _ratingDamageTypeWard = DamageType switch
        {
            DamageType.Physical => Jewel.GetJewelEffectMod(playerDefender, PropertyInt.GearPhysicalWard),
            DamageType.Elemental => Jewel.GetJewelEffectMod(playerDefender, PropertyInt.GearElementalWard),
            _ => 1.0f
        };

        _swarmedDamageReductionMod = GetSwarmedMod(playerDefender);

        // if (playerAttacker is not null)
        // {
        //     Console.WriteLine(
        //         $"{_armorMod} {ShieldMod} {_resistanceMod} {_damageResistanceRatingMod} {_evasionMod} {_specDefenseMod} {_ratingDamageTypeWard} {_ratingSelfHarm} {_ratingRedFury} {_ratingYellowFury}");
        // }

        return _armorMod
               * ShieldMod
               * _resistanceMod
               * _damageResistanceRatingMod
               * _evasionMod
               * _specDefenseMod
               * _combatAbilityProvokeDamageReduction
               * _ratingDamageTypeWard
               * _ratingSelfHarm
               * _ratingRedFury
               * _ratingYellowFury
               * _swarmedDamageReductionMod;
    }

    /// <summary>
    /// Calculates a modifier that reduces damage taken for a defending player based on the number of
    /// nearby enemies.
    /// </summary>
    /// <remarks>The modifier is multiplicatively reduced by 10% for each nearby enemy beyond the first,
    /// within a radius of 3 units. This effect only applies if the player has a melee weapon equipped.</remarks>
    /// <param name="playerDefender">The player who is defending and whose damage reduction will be modified. Cannot be null.</param>
    /// <returns>A floating-point value representing the swarmed modifier. Returns 1.0 if the player or their equipped melee
    /// weapon is null, or if there is one or no nearby enemy; otherwise, returns a value less than 1.0 that decreases
    /// as the number of nearby enemies increases.</returns>
    private static float GetSwarmedMod(Player playerDefender)
    {
        var swarmedMod = 1.0f;

        if (playerDefender is null || playerDefender.GetEquippedMeleeWeapon() is null)
        {
            return swarmedMod;
        }

        var numNearbyEnemies = playerDefender.GetNearbyMonsters(3).Count;

        if (numNearbyEnemies <= 1)
        {
            return swarmedMod;
        }

        // start loop at 1 to only count mobs beyond the first
        for (var i = 1; i < numNearbyEnemies; i++)
        {
            swarmedMod *= 0.9f;
        }

        return swarmedMod;
    }

    private float GetIgnoreArmorMod(Creature attacker, Creature defender)
    {
        if (Weapon is null or { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
        {
            return 1.0f;
        }

        var playerAttacker = attacker as Player;

        var armorRendingMod = GetArmorRendingMod(defender, playerAttacker);
        var armorCleavingMod = attacker.GetArmorCleavingMod(Weapon);

        return armorCleavingMod - (1.0f - armorRendingMod);
    }

    private float GetArmorRendingMod(Creature defender, Player playerAttacker)
    {
        if (Weapon is null or { SpecialPropertiesRequireMana: true, ItemCurMana: 0 })
        {
            return 1.0f;
        }

        if (Weapon.HasImbuedEffect(ImbuedEffectType.ArmorRending))
        {
            return 1.0f - WorldObject.GetArmorRendingMod(_attackSkill, playerAttacker, defender);
        }

        return 1.0f;
    }

    /// <summary>
    /// SPEC BONUS - Martial Weapons (Spear): +10% armor penetration (additively)
    /// </summary>
    private float GetSpearSpecIgnoreArmorBonus(Creature attacker)
    {
        var playerAttacker = attacker as Player;

        if (playerAttacker?.GetEquippedWeapon() == null)
        {
            return 0.0f;
        }

        return IsWeaponSkillSpecialized(playerAttacker, Skill.Spear, Skill.MartialWeapons) ? 0.1f : 0.0f;
    }

    private float GetArmorMod(Creature attacker, Creature defender)
    {
        var playerDefender = defender as Player;

        if (Weapon != null && Weapon.HasImbuedEffect(ImbuedEffectType.IgnoreAllArmor))
        {
            return 1.0f;
        }

        if (playerDefender != null)
        {
            // select random body part @ current attack height
            GetBodyPart(_attackHeight);

            // get player armor pieces
            _armor = attacker.GetArmorLayers(playerDefender, BodyPart);

            // get armor modifiers
            return attacker.GetArmorMod(playerDefender, DamageType, _armor, Weapon, _ignoreArmorMod);
        }

        // determine height quadrant
        _quadrant = GetQuadrant(defender, attacker, _attackHeight, _damageSource);

        // select random body part @ current attack height
        GetBodyPart(defender, _quadrant);

        // Defensive check: GetBodyPart may have failed to populate _creaturePart when there's no body part table.
        if (_creaturePart == null)
        {
            _log.Error(
                "DamageEvent.GetArmorMod({Attacker} ({AttackerGuid}), {Defender} ({DefenderGuid})) - no creature body part available for wcid {DefenderWeenieClassId}; returning neutral armor mod.",
                attacker?.Name,
                attacker?.Guid,
                defender?.Name,
                defender?.Guid,
                defender.WeenieClassId
            );

            // Mark as evaded (GetBodyPart already sets Evaded when appropriate), but ensure we return a safe neutral modifier.
            Evaded = true;
            return 1.0f;
        }

        _armor = _creaturePart.GetArmorLayers(_propertiesBodyPart.Key);

        // get target armor
        return _creaturePart.GetArmorMod(DamageType, _armor, attacker, Weapon, _ignoreArmorMod);
    }

    private float GetResistanceMod(Creature defender, Player playerDefender)
    {
        if (playerDefender != null)
        {
            return playerDefender.GetResistanceMod(DamageType, _attacker, Weapon, _weaponResistanceMod);
        }

        var resistanceType = Creature.GetResistanceType(DamageType);

        return (float)
            Math.Max(0.0f, defender.GetResistanceMod(resistanceType, _attacker, Weapon, _weaponResistanceMod));
    }

    /// <summary>
    /// RATING - Pierce: Ramping Piercing Resistance Penetration.
    /// Up to +20% + 1% per rating (at max quest stamps).
    /// (JEWEL - Black Garnet)
    /// </summary>
    private float GetRatingPierceResistanceBonus(Creature defender, Player playerAttacker)
    {
        if (playerAttacker == null)
        {
            return 1.0f;
        }

        var rating = playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearPierce);

        if (rating <= 0 || DamageType != DamageType.Pierce)
        {
            return 1.0f;
        }

        var rampPercentage = (float)defender.QuestManager.GetCurrentSolves($"{playerAttacker.Name},Pierce") / 100;

        const float baseMod = 0.2f;
        const float bonusPerRating = 0.01f;

        return 1.0f + (rampPercentage * (baseMod + bonusPerRating * rating));
    }

    private void PostDamageMitigationEffects(Creature attacker, Creature defender, WorldObject damageSource)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        CheckForRatingPostDamageEffects(attacker, defender, damageSource, playerAttacker, playerDefender);
        CheckForCombatAbilityFuryBuildUpWhenDamaged(playerDefender);
        CheckForWeaponMasterEffects(playerAttacker, defender);
        CheckForEnchantedBlade(playerAttacker, defender, _attackHeight);

        if (_attacker.IsMonster)
        {
            Damage *= 1.0f;
        }

        if (!_attacker.IsMonster)
        {
            Damage *= 1.0f;
        }
    }

    private void CheckForWeaponMasterEffects(Player playerAttacker, Creature defender)
    {

        if (playerAttacker is not { WeaponMasterSingleUseIsActive: true } || Weapon is null)
        {
            return;
        }

        var powerLevel = playerAttacker.GetPowerAccuracyBar();

        if (powerLevel < 0.5)
        {
            return;
        }

        var weaponTier = Math.Clamp((Weapon.Tier ?? 1) - 1, 1, 7);

        switch (Weapon.WeaponSkill)
        {
            case Skill.Axe:
            case Skill.Dagger:

                WeaponMasterBleed(playerAttacker, defender, Weapon, powerLevel);

                break;
            case Skill.Mace:
            case Skill.Staff:

                WeaponMasterDaze(playerAttacker, defender, weaponTier, powerLevel);

                break;
            case Skill.UnarmedCombat:

                WeaponMasterOffBalance(playerAttacker, defender, weaponTier, powerLevel);

                break;
            case Skill.ThrownWeapon:

                if (Weapon.Name.Contains("Dagger") || Weapon.Name.Contains("Axe"))
                {
                    WeaponMasterBleed(playerAttacker, defender, Weapon, powerLevel);
                }
                else if (Weapon.Name.Contains("Club"))
                {
                    WeaponMasterDaze(playerAttacker, defender, weaponTier, powerLevel);
                }
                else if (Weapon.Name.Contains("Shouken"))
                {
                    WeaponMasterOffBalance(playerAttacker, defender, weaponTier, powerLevel);
                }

                break;
            // case Skill.Sword:
            //     Console.WriteLine("Sword");
            //     break;
            // case Skill.Spear:
            //     Console.WriteLine("Spear");
            //     break;
        }
    }

    private void CheckForEnchantedBlade(Player player, Creature target, AttackHeight attackHeight)
    {
        if (player is null)
        {
            return;
        }

        var spell = attackHeight switch
        {
            AttackHeight.High => player.EnchantedBladeHighStoredSpell,
            AttackHeight.Medium => player.EnchantedBladeMedStoredSpell,
            AttackHeight.Low => player.EnchantedBladeLowStoredSpell,
            _ => null
        };

        if (spell is null)
        {
            return;
        }

        var weapon = player.GetEquippedMeleeWeapon();

        if (weapon is null)
        {
            return;
        }

        var spellCraft = weapon.ItemSpellcraft ?? 1;

        player.TryCastSpell(spell, target, null, weapon, false, true, true, true, spellCraft);

        player.EnchantedBladeHighStoredSpell = null;
        player.EnchantedBladeMedStoredSpell = null;
        player.EnchantedBladeLowStoredSpell = null;
    }

    private void WeaponMasterOffBalance(Player playerAttacker, Creature defender, int weaponTier, float powerLevel)
    {
        float tierMod;
        tierMod = weaponTier switch
        {
            1 => 1.0f,
            2 => 3.0f,
            3 => 4.0f,
            4 => 5.0f,
            5 => 6.0f,
            6 => 8.0f,
            7 => 10.0f,
            _ => throw new ArgumentOutOfRangeException()
        };

        var defenseDebuffSpell = new Spell(SpellId.Unbalanced);

        if (defenseDebuffSpell.NotFound)
        {
            return;
        }

        defenseDebuffSpell.SpellStatModVal *= powerLevel * tierMod;

        defender.EnchantmentManager.Add(defenseDebuffSpell, playerAttacker, Weapon);

        defender.EnqueueBroadcast(new GameMessageScript(defender.Guid, PlayScript.DirtyFightingDefenseDebuff));

        playerAttacker.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"You put {defender.Name} off-balance, reducing their defense skill!",
                ChatMessageType.Broadcast
            )
        );

        playerAttacker.WeaponMasterSingleUseIsActive = false;
    }

    private void WeaponMasterDaze(Player playerAttacker, Creature defender, int weaponTier, float powerLevel)
    {
        float tierMod;
        tierMod = weaponTier switch
        {
            1 => 1.0f,
            2 => 3.0f,
            3 => 4.0f,
            4 => 5.0f,
            5 => 6.0f,
            6 => 8.0f,
            7 => 10.0f,
            _ => throw new ArgumentOutOfRangeException()
        };

        var attackDebuffSpell = new Spell(SpellId.Dazed);

        if (attackDebuffSpell.NotFound)
        {
            return;
        }

        attackDebuffSpell.SpellStatModVal *= powerLevel * tierMod;

        defender.EnchantmentManager.Add(attackDebuffSpell, playerAttacker, Weapon);

        defender.EnqueueBroadcast(new GameMessageScript(defender.Guid, PlayScript.DirtyFightingAttackDebuff));

        playerAttacker.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"You daze {defender.Name}, reducing their attack skill!",
                ChatMessageType.Broadcast
            )
        );

        playerAttacker.WeaponMasterSingleUseIsActive = false;
    }

    /// <summary>
    /// Triggers a bleed damage DoT on the target.
    /// Base damage of Bleed spell is 500. Damage is reduced depending on weapon damage roll
    /// and power bar level setting.
    /// </summary>
    private void WeaponMasterBleed(Player playerAttacker, Creature defender, WorldObject weapon, float powerLevel)
    {
        if (weapon.Damage is null)
        {
            return;
        }

        // todo: Add weapon subtypes to all lootgen weapons for higher accuracy
        // var weaponSubtype = weapon.WeaponSubtype.Value;
        // var weaponSubtypeMinDamage = weaponSubtype switch
        // {
        //     (int)LootTables.WeaponSubtype.AxeLarge => 5,
        //     (int)LootTables.WeaponSubtype.AxeMedium => 5,
        //     (int)LootTables.WeaponSubtype.AxeSmall => 4,
        //     (int)LootTables.WeaponSubtype.DaggerLarge => 5,
        //     (int)LootTables.WeaponSubtype.DaggerSmall => 4,
        //     (int)LootTables.WeaponSubtype.ThrownAxe => 11,
        //     (int)LootTables.WeaponSubtype.ThrownDagger => 10,
        //     (int)LootTables.WeaponSubtype.TwohandAxe => 5,
        //     _ => throw new ArgumentOutOfRangeException()
        // };
        // var weaponSubtypeMaxDamage = weaponSubtype switch
        // {
        //     (int)LootTables.WeaponSubtype.AxeLarge => 132,
        //     (int)LootTables.WeaponSubtype.AxeMedium => 120,
        //     (int)LootTables.WeaponSubtype.AxeSmall => 110,
        //     (int)LootTables.WeaponSubtype.DaggerLarge => 95,
        //     (int)LootTables.WeaponSubtype.DaggerSmall => 71,
        //     (int)LootTables.WeaponSubtype.ThrownAxe => 296,
        //     (int)LootTables.WeaponSubtype.ThrownDagger => 278,
        //     (int)LootTables.WeaponSubtype.TwohandAxe => 107,
        //     _ => throw new ArgumentOutOfRangeException()
        // };

        var weaponType = weapon.WeaponSkill;

        var weaponTypeMinDamage = weaponType switch
        {
            Skill.Axe => 9,
            Skill.Dagger => 4,
            Skill.ThrownWeapon => 10,
            Skill.TwoHandedCombat => 5,
            _ => throw new ArgumentOutOfRangeException()
        };

        var weaponTypeMaxDamage = weaponType switch
        {
            Skill.Axe => 132,
            Skill.Dagger => 95,
            Skill.ThrownWeapon => 296,
            Skill.TwoHandedCombat => 107,
            _ => throw new ArgumentOutOfRangeException()
        };

        var damageRange = weaponTypeMaxDamage - weaponTypeMinDamage;
        var weaponDamageRoll = weapon.Damage.Value - weaponTypeMinDamage;
        var weaponDamageRollPercentile = (float)weaponDamageRoll / damageRange;

        var spell = new Spell(SpellId.Bleed);

        if (spell.NotFound)
        {
            return;
        }

        spell.SpellStatModVal = powerLevel * weaponDamageRollPercentile;

        defender.EnchantmentManager.Add(spell, playerAttacker, Weapon);
        defender.EnqueueBroadcast(new GameMessageScript(defender.Guid, PlayScript.DirtyFightingDamageOverTime));

        playerAttacker.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"You cause {defender.Name} to bleed!",
                ChatMessageType.Broadcast
            )
        );

        playerAttacker.WeaponMasterSingleUseIsActive = false;
    }

    /// <summary>
    /// RATING (jewels) POST-DAMAGE STAMPS / PROCS / BONUSES
    /// </summary>
    private void CheckForRatingPostDamageEffects(
        Creature attacker,
        Creature defender,
        WorldObject damageSource,
        Player playerAttacker,
        Player playerDefender
    )
    {
        if (playerAttacker != null)
        {
            Jewel.HandlePlayerAttackerBonuses(playerAttacker, defender, Damage, DamageType);
            Jewel.HandleMeleeMissileAttackerRampingQuestStamps(playerAttacker, defender, DamageType);
        }

        if (playerDefender != null)
        {
            Jewel.HandlePlayerDefenderBonuses(playerDefender, attacker, Damage);
            Jewel.HandleMeleeMissileDefenderRampingQuestStamps(playerDefender);
        }
    }

    /// <summary>
    /// COMBAT ABILITY - Fury (build-up).
    /// </summary>
    private void CheckForCombatAbilityFuryBuildUpWhenDamaged(Player playerDefender)
    {
        if (playerDefender is { FuryStanceIsActive: true })
        {
            var furyGained = Damage / playerDefender.Health.MaxValue / 10;
            playerDefender.AdrenalineMeter += furyGained;

            if (playerDefender.AdrenalineMeter > 1.0f)
            {
                playerDefender.AdrenalineMeter = 1.0f;
            }
        }
    }

    /// <summary>
    /// SPEC BONUS: Physical Defense
    /// </summary>
    private static float GetSpecDefenseMod(Creature attacker, Player playerDefender)
    {
        if (
            playerDefender == null
            || playerDefender.GetCreatureSkill(Skill.PhysicalDefense).AdvancementClass != SkillAdvancementClass.Specialized
        )
        {
            return 1.0f;
        }

        var playerDefenderPhysicalDefense =
            playerDefender.GetModdedPhysicalDefSkill()
            * LevelScaling.GetPlayerDefenseSkillScalar(playerDefender, attacker);
        var bonusAmount = Math.Min(playerDefenderPhysicalDefense, 500) / 50;

        return 0.9f - bonusAmount * 0.01f;
    }

    /// <summary>
    /// RATING - Hardened Defense: Ramping Physical Damage Reduction.
    /// Up to +20% + 1% per rating (at max quest stamps).
    /// (JEWEL - Diamond)
    /// </summary>
    private static float GetRatingHardenedDefenseDamageResistanceBonus(Player playerDefender)
    {
        if (playerDefender == null)
        {
            return 0.0f;
        }

        var rating = playerDefender.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearHardenedDefense);

        if (rating <= 0)
        {
            return 0.0f;
        }

        var rampPercentage = (float)playerDefender.QuestManager.GetCurrentSolves($"{playerDefender.Name},Hardened Defense") / 100;

        const float baseMod = 0.2f;
        const float bonusPerRating = 0.01f;

        return rampPercentage * (baseMod + bonusPerRating * rating);
    }

    private float GetDamageResistRatingMod(Creature defender, bool pkBattle)
    {
        _damageResistanceRatingBaseMod = defender.GetDamageResistRatingMod(CombatType);

        if (IsCritical)
        {
            _criticalDamageResistanceRatingMod = Creature.GetNegativeRatingMod(defender.GetCritDamageResistRating());
            return Creature.AdditiveCombine(_damageResistanceRatingBaseMod, _criticalDamageResistanceRatingMod);
        }

        if (pkBattle)
        {
            _pkDamageResistanceMod = Creature.GetNegativeRatingMod(defender.GetPKDamageResistRating());
            return Creature.AdditiveCombine(_damageResistanceRatingBaseMod, _pkDamageResistanceMod);
        }

        return _damageResistanceRatingBaseMod;
    }

    /// <summary>
    /// RATING - Reprisal: Evade an Incoming Crit, auto crit in return
    /// (JEWEL - Black Opal)
    /// </summary>
    private void CheckForRatingReprisalCriticalDefense(Creature attacker, Player playerDefender)
    {
        if (playerDefender == null)
        {
            return;
        }

        var rating = playerDefender.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearReprisal);

        if (rating <= 0)
        {
            return;
        }

        var chance = Jewel.GetJewelEffectMod(playerDefender, PropertyInt.GearReprisal);

        if (ThreadSafeRandom.Next(0.0f, 1.0f) > chance)
        {
            return;
        }

        playerDefender.QuestManager.HandleReprisalQuest();
        playerDefender.QuestManager.Stamp($"{attacker.Guid}/Reprisal");
        Evaded = true;
        PartialEvasion = PartialEvasion.All;
        playerDefender.Reprisal = true;

        var msg = $"Reprisal! You evade the attack by {attacker.Name}";
        playerDefender.Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.CombatEnemy));
    }

    /// <summary>
    /// RATING - Bludgeon: Ramping Bludgeon Crit Damage Bonus.
    /// Up to +20% + 1% per rating (at max quest stamps).
    /// (JEWEL - White Sapphire)
    /// </summary>
    private static float GetRatingBludgeonCriticalDamageBonus(Creature defender, Player playerAttacker)
    {
        if (playerAttacker == null)
        {
            return 0.0f;
        }

        var rating = playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearBludgeon);
        if (rating <= 0)
        {
            return 0.0f;
        }

        var rampPercentage = (float)defender.QuestManager.GetCurrentSolves($"{playerAttacker.Name},Bludgeon") / 100;

        const float baseMod = 0.2f;
        const float bonusPerRating = 0.01f;

        return rampPercentage * (baseMod + bonusPerRating * rating);
    }

    /// <summary>
    /// SPEC BONUS - Staff: +50% crit damage (additively)
    /// </summary>
    private static float GetStaffSpecCriticalDamageBonus(Player playerAttacker)
    {
        if (playerAttacker?.GetEquippedWeapon() == null)
        {
            return 0.0f;
        }

        return IsWeaponSkillSpecialized(playerAttacker, Skill.Staff, Skill.Staff) ? 0.5f : 0.0f;
    }

    /// <summary>
    /// SPEC BONUS - Martial Weapons (Mace): +50% crit damage (additively)
    /// </summary>
    private static float GetMaceSpecCriticalDamageBonus(Player playerAttacker)
    {
        if (playerAttacker?.GetEquippedWeapon() == null)
        {
            return 0.0f;
        }

        return IsWeaponSkillSpecialized(playerAttacker, Skill.Mace, Skill.MartialWeapons) ? 0.5f : 0.0f;
    }

    /// <summary>
    /// SPEC BONUS - Perception - 50% chance to defend against a critical hit
    /// </summary>
    private bool CheckForSpecPerceptionCriticalDefense(Player playerDefender)
    {
        if (playerDefender == null)
        {
            return false;
        }

        var perception = playerDefender.GetCreatureSkill(Skill.Perception);
        if (perception.AdvancementClass != SkillAdvancementClass.Specialized)
        {
            return false;
        }

        var skillCheck = perception.Current / (float)_attackSkill.Current;
        var criticalDefenseChance = skillCheck > 1f ? 0.5f : skillCheck * 0.5f;

        if (!(criticalDefenseChance > ThreadSafeRandom.Next(0f, 1f)))
        {
            return false;
        }

        playerDefender.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Your perception skill allowed you to prevent a critical strike!",
                ChatMessageType.Broadcast
            )
        );

        return true;
    }

    private static bool CheckForAugmentationCriticalDefense(Player playerDefender, Player playerAttacker)
    {
        if (playerDefender == null || playerDefender.AugmentationCriticalDefense <= 0)
        {
            return false;
        }

        var criticalDefenseMod = playerAttacker != null ? 0.05f : 0.25f;
        var criticalDefenseChance = playerDefender.AugmentationCriticalDefense * criticalDefenseMod;

        return !(criticalDefenseChance < ThreadSafeRandom.Next(0.0f, 1.0f));
    }

    private bool CheckForRatingReprisal(Creature playerAttacker)
    {
        if (playerAttacker == null)
        {
            return false;
        }

        if (playerAttacker.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearReprisal) <= 0)
        {
            return false;
        }

        if (!playerAttacker.QuestManager.HasQuest($"{_defender.Guid}/Reprisal"))
        {
            return false;
        }

        playerAttacker.QuestManager.Erase($"{_defender.Guid}/Reprisal");
        return true;
    }

    /// <summary>
    /// SPEC BONUS - Axe/Dagger: +5% crit chance
    /// </summary>
    private float GetPlayerSpecSkillCriticalChanceBonus()
    {
        if (_playerAttacker?.GetEquippedWeapon() == null)
        {
            return 0.0f;
        }

        // SPEC BONUS - Martial Weapons (Axe): +5% crit chance (additively)
        if (IsWeaponSkillSpecialized(_playerAttacker, Skill.Axe, Skill.MartialWeapons))
        {
            return 0.05f;
        }

        // SPEC BONUS - Dagger: +5% crit chance (additively)
        if (IsWeaponSkillSpecialized(_playerAttacker, Skill.Dagger, Skill.Dagger))
        {
            return 0.05f;
        }

        return 0.0f;
    }

    private static bool IsWeaponSkillSpecialized(Player player, Skill weaponSkill, Skill creatureSkill)
    {
        return player.GetEquippedWeapon().WeaponSkill == weaponSkill
               && player.GetCreatureSkill(creatureSkill).AdvancementClass == SkillAdvancementClass.Specialized;
    }

    private static bool IsSkillSpecialized(Player player, Skill creatureSkill)
    {
        return player?.GetCreatureSkill(creatureSkill).AdvancementClass == SkillAdvancementClass.Specialized;
    }

    /// <summary>
    /// RATING - Thorns: Reflects damage on block
    /// (JEWEL - White Quartz)
    /// </summary>
    private void CheckForRatingThorns(Creature attacker, Creature defender, WorldObject damageSource)
    {
        var playerDefender = defender as Player;

        if (Blocked != true || playerDefender == null || !(attacker.GetDistance(playerDefender) < 10))
        {
            return;
        }

        if (playerDefender.GetEquippedAndActivatedItemRatingSum(PropertyInt.GearThorns) <= 0)
        {
            return;
        }

        if (damageSource is null)
        {
            return;
        }

        if (defender.GetEquippedWeapon() is null)
        {
            return;
        }

        SetCombatSources(attacker, defender, defender.GetEquippedWeapon());
        SetBaseDamage(attacker, defender, damageSource);
        SetDamageModifiers(attacker, defender);

        var damage = GetNonCriticalDamageBeforeMitigation();

        var thornsAmount = damage * Jewel.GetJewelEffectMod(playerDefender, PropertyInt.GearThorns);

        attacker.UpdateVitalDelta(attacker.Health, -(int)thornsAmount);
        attacker.DamageHistory.Add(playerDefender, DamageType.Health, (uint)thornsAmount);
        playerDefender.ShieldReprisal = (int)thornsAmount;

        var msg = $"You deflect {(int)thornsAmount} damage back to the attacker!";
        playerDefender.Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.CombatSelf));

        if (!attacker.IsDead)
        {
            return;
        }

        attacker.OnDeath(attacker.DamageHistory.LastDamager, DamageType.Health);
        attacker.Die();
    }

    public void CheckForRiposte(Creature attacker, Creature defender)
    {
        if ((!Parried && !Blocked) || defender is not Player playerDefender || !(attacker.GetDistance(playerDefender) < 10))
        {
            return;
        }

        if (!playerDefender.RiposteIsActive)
        {
            return;
        }

        if (!playerDefender.TwoHandedCombat && !playerDefender.IsDualWieldAttack && playerDefender.GetEquippedShield() is null)
        {
            return;
        }

        if (defender.GetEquippedWeapon() is null)
        {
            return;
        }

        SetCombatSources(defender, attacker, defender.GetEquippedWeapon());
        SetBaseDamage(defender, attacker, defender.GetEquippedWeapon());
        SetDamageModifiers(defender, attacker);

        _powerMod = 1.0f;
        var baseDamage = GetNonCriticalDamageBeforeMitigation();
        var mitigation = GetMitigation(defender, attacker);

        var damage = baseDamage * mitigation;

        if (damage is float.NaN or < int.MinValue or > int.MaxValue)
        {
            _log.Error("CheckForRiposte({Attacker}, {Defender}) - damage ({Damage}) could not be converted to Int.", attacker.Name, defender.Name, damage);
            return;
        }

        var intDamage = (int)damage;

        attacker.UpdateVitalDelta(attacker.Health, -intDamage);
        attacker.DamageHistory.Add(playerDefender, DamageType.Health, (uint)intDamage);

        var parryType = Parried ? "parry" : "block";

        var msg = $"You follow up your {parryType} with a quick riposte, dealing {intDamage} {_damageSource.W_DamageType} damage to {attacker.Name}!";
        playerDefender.Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.CombatSelf));

        if (!attacker.IsDead)
        {
            return;
        }

        attacker.OnDeath(attacker.DamageHistory.LastDamager, DamageType.Health);
        attacker.Die();
    }

    private bool IsAttackFromStealth()
    {
        if (_playerAttacker == null)
        {
            return false;
        }

        var isAttackFromStealth = _playerAttacker.IsAttackFromStealth;
        _playerAttacker.IsAttackFromStealth = false;

        return isAttackFromStealth;
    }

    private Quadrant GetQuadrant(
        Creature defender,
        Creature attacker,
        AttackHeight attackHeight,
        WorldObject damageSource
    )
    {
        var quadrant = attackHeight.ToQuadrant();

        var wo = damageSource.CurrentLandblock != null ? damageSource : attacker;

        quadrant |= wo.GetRelativeDir(defender);

        return quadrant;
    }

    /// <summary>
    /// Returns the chance for creature to avoid monster attack
    /// </summary>
    private float GetEvadeChance(Creature attacker, Creature defender)
    {
        var playerAttacker = attacker as Player;
        var playerDefender = defender as Player;

        _accuracyMod = attacker.GetAccuracySkillMod(Weapon);

        EffectiveAttackSkill = (uint)(attacker.GetEffectiveAttackSkill() * LevelScaling.GetPlayerAttackSkillScalar(playerAttacker, defender));

        EffectiveAttackSkill = Convert.ToUInt32(EffectiveAttackSkill * CheckForAttackHeightMediumAttackSkillBonus(playerAttacker));
        EffectiveAttackSkill = Convert.ToUInt32(EffectiveAttackSkill * CheckForCombatAbilitySteadyStrikeAttackSkillBonus(playerAttacker));
        EffectiveAttackSkill = Convert.ToUInt32(EffectiveAttackSkill * (1.0f + Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearBravado, "Bravado")));

        _effectiveDefenseSkill = (uint)(defender.GetEffectiveDefenseSkill(CombatType) * LevelScaling.GetPlayerDefenseSkillScalar(playerDefender, attacker)
        );

        _effectiveDefenseSkill = Convert.ToUInt32(_effectiveDefenseSkill * CheckForAttackHeightLowDefenseSkillBonus(playerDefender, playerAttacker));
        _effectiveDefenseSkill = Convert.ToUInt32(_effectiveDefenseSkill * (1.0f + Jewel.GetJewelEffectMod(playerDefender, PropertyInt.GearFamiliarity, "Familiarity")));

        var evadeChance = SkillCheck.GetSkillChance(_effectiveDefenseSkill, EffectiveAttackSkill);
        evadeChance = CheckForCombatAbilitySmokescreenEvadeChanceBonus(evadeChance, playerDefender);

        if (evadeChance < 0)
        {
            evadeChance = 0;
        }

        return (float)Math.Min(evadeChance, 1.0f);
    }

    /// <summary>
    /// COMBAT Ability - Smokescreen: 10% increased chance to evade attacks
    /// </summary>
    private static double CheckForCombatAbilitySmokescreenEvadeChanceBonus(double evadeChance, Player playerDefender)
    {
        if (playerDefender is not { SmokescreenIsActive: true })
        {
            return evadeChance;
        }

        var remainingChance = 1.0f - evadeChance;
        var bonus = remainingChance * 0.1f;

        return evadeChance + bonus;

    }

    /// <summary>
    /// COMBAT ABILITY - Steady Strike: Increased attack skill with melee/missile attacks by 25%.
    /// </summary>
    private float CheckForCombatAbilitySteadyStrikeAttackSkillBonus(Player playerAttacker)
    {
        if (playerAttacker?.GetEquippedWeapon() is null)
        {
            return 1.0f;
        }

        if (playerAttacker.GetPowerAccuracyBar() < 0.5f)
        {
            return 1.0f;
        }

        return playerAttacker is {SteadyStrikeIsActive: true} ? 1.25f : 1.0f;
    }

    /// <summary>
    /// ATTACK HEIGHT BONUS: Low (+10% physical defense skill, +20% if weapon specialized)
    /// </summary>
    /// <returns></returns>
    private float CheckForAttackHeightLowDefenseSkillBonus(Player playerDefender, Player playerAttacker)
    {
        if (playerDefender is { AttackHeight: AttackHeight.Low })
        {
            return WeaponIsSpecialized(playerAttacker) ? 1.2f : 1.1f;
        }

        return 1.0f;
    }

    /// <summary>
    /// ATTACK HEIGHT BONUS: Medium (+10% attack skill, +20% if weapon specialized)
    /// </summary>
    private float CheckForAttackHeightMediumAttackSkillBonus(Player playerAttacker)
    {
        if (playerAttacker is { AttackHeight: AttackHeight.Medium })
        {
            return WeaponIsSpecialized(playerAttacker) ? 1.2f : 1.1f;
        }

        return 1.0f;
    }

    /// <summary>
    /// Returns the base damage for a player attacker
    /// </summary>
    private void GetBaseDamage(Player attacker)
    {
        if (_damageSource.ItemType == ItemType.MissileWeapon)
        {
            DamageType = _damageSource.W_DamageType;

            // handle prismatic arrows
            if (DamageType == DamageType.Base)
            {
                if (Weapon != null && Weapon.W_DamageType != DamageType.Undef)
                {
                    DamageType = Weapon.W_DamageType;
                }
                else
                {
                    DamageType = DamageType.Pierce;
                }
            }
        }
        else
        {
            DamageType = attacker.GetDamageType(false, CombatType.Melee);
        }

        // TODO: combat maneuvers for player?
        _baseDamageMod = attacker.GetBaseDamageMod(_damageSource);

        // some quest bows can have built-in damage bonus
        if (Weapon?.WeenieType == WeenieType.MissileLauncher)
        {
            _baseDamageMod.DamageBonus += Weapon.Damage ?? 0;
        }

        if (_damageSource.ItemType == ItemType.MissileWeapon)
        {
            _baseDamageMod.ElementalBonus = WorldObject.GetMissileElementalDamageBonus(Weapon, attacker, DamageType);
            _baseDamageMod.DamageMod = WorldObject.GetMissileElementalDamageModifier(Weapon, DamageType);
        }

        _baseDamage = (float)ThreadSafeRandom.Next(_baseDamageMod.MinDamage, _baseDamageMod.MaxDamage);
    }

    /// <summary>
    /// Returns the base damage for a non-player attacker
    /// </summary>
    private void GetBaseDamage(Creature attacker, MotionCommand motionCommand, AttackHook attackHook)
    {
        _attackPart = attacker.GetAttackPart(motionCommand, attackHook);
        if (_attackPart.Value == null)
        {
            _generalFailure = true;
            return;
        }

        _baseDamageMod = attacker.GetBaseDamage(_attackPart.Value);
        _baseDamage = (float)ThreadSafeRandom.Next(_baseDamageMod.MinDamage, _baseDamageMod.MaxDamage);

        DamageType = attacker.GetDamageType(_attackPart.Value, CombatType);
    }

    /// <summary>
    /// Returns a body part for a player defender
    /// </summary>
    private void GetBodyPart(AttackHeight attackHeight)
    {
        // select random body part @ current attack height
        BodyPart = BodyParts.GetBodyPart(attackHeight);
    }

    /// <summary>
    /// Returns a body part for a creature defender
    /// </summary>
    private void GetBodyPart(Creature defender, Quadrant quadrant)
    {
        // get cached body parts table
        var bodyParts = Creature.GetBodyParts(defender.WeenieClassId);

        if (bodyParts == null)
        {
            _log.Debug(
                "DamageEvent.GetBodyPart({Defender} ({DefenderGuid}) ) - no body parts table for wcid {DefenderWeenieClassId}",
                defender.Name,
                defender.Guid,
                defender.WeenieClassId
            );
            Evaded = true;
            return;
        }

        // rng roll for body part
        var bodyPart = bodyParts.RollBodyPart(quadrant);

        if (bodyPart == CombatBodyPart.Undefined)
        {
            _log.Debug(
                "DamageEvent.GetBodyPart({Defender} ({DefenderGuid}) ) - couldn't find body part for wcid {DefenderWeenieClassId}, Quadrant {BodyPartQuadrant}",
                defender.Name,
                defender.Guid,
                defender.WeenieClassId,
                quadrant
            );
            Evaded = true;
            return;
        }

        //Console.WriteLine($"AttackHeight: {AttackHeight}, Quadrant: {quadrant & FrontBack}{quadrant & LeftRight}, AttackPart: {bodyPart}");

        defender.Biota.PropertiesBodyPart.TryGetValue(bodyPart, out var value);
        _propertiesBodyPart = new KeyValuePair<CombatBodyPart, PropertiesBodyPart>(bodyPart, value);

        // select random body part @ current attack height
        /*BiotaPropertiesBodyPart = BodyParts.GetBodyPart(defender, attackHeight);

        if (BiotaPropertiesBodyPart == null)
        {
            Evaded = true;
            return;
        }*/

        _creaturePart = new Creature_BodyPart(defender, _propertiesBodyPart);
    }

    /// <summary>
    /// SPEC BONUS - Shield: Increase shield effective angle by 45 degrees (to 225)
    /// </summary>
    private static float GetSpecShieldEffectiveAngleBonus(Player playerDefender)
    {
        if (playerDefender == null)
        {
            return 0.0f;
        }

        return IsSkillSpecialized(playerDefender, Skill.Shield) ? 45.0f : 0.0f;
    }

    /// <summary>
    /// SPEC BONUS - Physical Defense: Increase block/parry chance up to 50% (multiplicatively).
    /// Based on defender 'defense skill' and attacker 'attack skill'.
    /// </summary>
    private float GetSpecPhysicalDefenseBlockChanceBonus()
    {
        var blockChanceMod = SkillCheck.GetSkillChance(_effectiveDefenseSkill, EffectiveAttackSkill);

        return 0.5f * (float)blockChanceMod;
    }

    public static float GetAmmoEffectMod(WorldObject weapon, Player player)
    {
        if (weapon is {IsAmmoLauncher: not true} || player is null)
        {
            return 1.0f;
        }

        var ammo = player.GetEquippedAmmo() as Ammunition;

        if (ammo?.AmmoEffectUsesRemaining is null)
        {
            return 1.0f;
        }

        switch ((AmmoEffect)(ammo.AmmoEffect ?? 0))
        {
            case AmmoEffect.Sharpened: return 1.1f;
            default: return 1.0f;
        }
    }

    private bool WeaponIsSpecialized(Player playerAttacker)
    {
        if (playerAttacker == null)
        {
            return false;
        }

        if (Weapon != null)
        {
            switch (Weapon.WeaponSkill)
            {
                case Skill.Axe:
                    return playerAttacker.GetCreatureSkill(Skill.MartialWeapons).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Mace:
                    return playerAttacker.GetCreatureSkill(Skill.MartialWeapons).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Sword:
                    return playerAttacker.GetCreatureSkill(Skill.MartialWeapons).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Spear:
                    return playerAttacker.GetCreatureSkill(Skill.MartialWeapons).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Dagger:
                    return playerAttacker.GetCreatureSkill(Skill.Dagger).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Staff:
                    return playerAttacker.GetCreatureSkill(Skill.Staff).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.UnarmedCombat:
                    return playerAttacker.GetCreatureSkill(Skill.UnarmedCombat).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Bow:
                    return playerAttacker.GetCreatureSkill(Skill.Bow).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.Crossbow:
                    return playerAttacker.GetCreatureSkill(Skill.Bow).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                case Skill.ThrownWeapon:
                    return playerAttacker.GetCreatureSkill(Skill.ThrownWeapon).AdvancementClass
                           == SkillAdvancementClass.Specialized;
                default:
                    return false;
            }
        }

        return playerAttacker.GetCreatureSkill(Skill.UnarmedCombat).AdvancementClass == SkillAdvancementClass.Specialized;
    }

    private void DpsLogging()
    {
        if (_attacker == null || _defender == null)
        {
            return;
        }

        // if (_attacker.Name is not "")
        // {
        //     return;
        // }

        var currentTime = Time.GetUnixTime();
        var timeSinceLastAttack = currentTime - _attacker.LastAttackTime;
        if (_attacker as Player == null)
        {
            timeSinceLastAttack = MonsterAverageAnimationLength.GetValueMod(_attacker.CreatureType);
        }

        var damageSource = Weapon == null ? _attacker : Weapon;

        Console.WriteLine($"\n---- DAMAGE LOG ({damageSource.Name}) ----");
        Console.WriteLine(
            $"CurrentTime: {currentTime}, LastAttackTime: {_attacker.LastAttackTime} TimeBetweenAttacks: {timeSinceLastAttack}"
        );
        _attacker.LastAttackTime = currentTime;

        var critRate = _criticalChance;
        var nonCritRate = 1 - critRate;
        var critDamageMod = 1.0f + WorldObject.GetWeaponCritDamageMod(damageSource, _attacker, _attackSkill, _defender);

        var avgNonCritHit = (_baseDamageMod.MaxDamage + _baseDamageMod.MinDamage) / 2;
        var critHit = _baseDamageMod.MaxDamage * critDamageMod;

        var averageDamage = avgNonCritHit * nonCritRate + critHit * critRate;
        var baseDps = averageDamage / timeSinceLastAttack;

        var averageDamageBeforeMitigation =
            averageDamage
            * _powerMod
            * _attributeMod
            * _slayerMod
            * _damageRatingMod
            * _dualWieldDamageBonus
            * _twohandedCombatDamageBonus
            * _combatAbilitySteadyStrikeDamageBonus;
        var averageDpsBeforeMitigation = averageDamageBeforeMitigation / timeSinceLastAttack;

        var averageDamageAfterMitigation =
            averageDamageBeforeMitigation
            * _armorMod
            * ShieldMod
            * _resistanceMod
            * _damageResistanceRatingMod
            * _levelScalingMod;
        var averageDpsAfterMitigation = averageDamageAfterMitigation / timeSinceLastAttack;

        Console.WriteLine(
            $"TimeSinceLastAttack: {timeSinceLastAttack}"
            + $"\n\n-- Base --\n"
            + $"BaseDamageMod.MaxDamage: {_baseDamageMod.MaxDamage}, BaseDamageMod.MinDamage: {_baseDamageMod.MinDamage}, LiveBaseDamage: {_baseDamage}\n"
            + $"AverageDamageNonCrit: {avgNonCritHit}, AverageDamageCrit: {critHit}, AverageDamageHit: {averageDamage}\n"
            + $"DPS Base: {baseDps}\n\n"
            + $"-- Before Mitigation --\n"
            + $"PowerMod: {_powerMod}, AttributeMod: {_attributeMod}, SlayerMod: {_slayerMod}, DamageRatingMod: {_damageRatingMod}, DualWieldMod: {_dualWieldDamageBonus}, TwoHandMod: {_twohandedCombatDamageBonus}, SteadyStrikeMod: {_combatAbilitySteadyStrikeDamageBonus}\n"
            + $"AverageDamage Before Mitigation: {averageDamageBeforeMitigation}\n"
            + $"DPS Before Mitigation: {averageDpsBeforeMitigation}\n\n"
            + $"-- After Mitigation --\n"
            + $"DamageScalar(health): {_levelScalingMod}, ArmorMod: {_armorMod}, ShieldMod: {ShieldMod}, ResistanceMod: {_resistanceMod}, DamageResistanceRatingMod: {_damageResistanceRatingMod}\n"
            + $"AverageDamage After Mitigation: {averageDamageAfterMitigation}\n"
            + $"DPS After Mitigation: {averageDpsAfterMitigation}\n"
            + $"---- END DAMAGE LOG ({damageSource.Name}) ----"
        );
    }

    private void ShowInfo(Creature creature)
    {
        var targetInfo = PlayerManager.GetOnlinePlayer(creature.DebugDamageTarget);
        if (targetInfo == null)
        {
            creature.DebugDamage = Creature.DebugDamageType.None;
            return;
        }

        // setup
        var info = $"Attacker: {_attacker.Name} ({_attacker.Guid})\n";
        info += $"Defender: {_defender.Name} ({_defender.Guid})\n";

        info += $"CombatType: {CombatType}\n";

        info += $"DamageSource: {_damageSource.Name} ({_damageSource.Guid})\n";
        info += $"DamageType: {DamageType}\n";

        var weaponName = Weapon != null ? $"{Weapon.Name} ({Weapon.Guid})" : "None\n";
        info += $"Weapon: {weaponName}\n";

        info += $"AttackType: {_attackType}\n";
        info += $"AttackHeight: {_attackHeight}\n";

        // lifestone protection
        if (LifestoneProtection)
        {
            info += $"LifestoneProtection: {LifestoneProtection}\n";
        }

        // evade
        if (_accuracyMod != 0.0f && _accuracyMod != 1.0f)
        {
            info += $"AccuracyMod: {_accuracyMod}\n";
        }

        info += $"EffectiveAttackSkill: {EffectiveAttackSkill}\n";
        info += $"EffectiveDefenseSkill: {_effectiveDefenseSkill}\n";

        if (_attacker.Overpower != null)
        {
            info += $"Overpower: {_overpower} ({Creature.GetOverpowerChance(_attacker, _defender)})\n";
        }

        info += $"Evaded: {Evaded}\n";
        info += $"Blocked: {Blocked}\n";
        info += $"PartialEvaded: {PartialEvasion}\n";

        if (!(_attacker is Player))
        {
            if (_attackMotion != null)
            {
                info += $"AttackMotion: {_attackMotion}\n";
            }

            if (_attackPart.Value != null)
            {
                info += $"AttackPart: {_attackPart.Key}\n";
            }
        }

        // base damage
        if (_baseDamageMod != null)
        {
            info += $"BaseDamageRange: {_baseDamageMod.Range}\n";
        }

        info += $"BaseDamage: {_baseDamage}\n";

        // damage modifiers
        info += $"AttributeMod: {_attributeMod}\n";

        if (_powerMod != 0.0f && _powerMod != 1.0f)
        {
            info += $"PowerMod: {_powerMod}\n";
        }

        if (_slayerMod != 0.0f && _slayerMod != 1.0f)
        {
            info += $"SlayerMod: {_slayerMod}\n";
        }

        if (_baseDamageMod != null)
        {
            if (_baseDamageMod.DamageBonus != 0)
            {
                info += $"DamageBonus: {_baseDamageMod.DamageBonus}\n";
            }

            if (_baseDamageMod.DamageMod != 0.0f && _baseDamageMod.DamageMod != 1.0f)
            {
                info += $"DamageMod: {_baseDamageMod.DamageMod}\n";
            }

            if (_baseDamageMod.ElementalBonus != 0)
            {
                info += $"ElementalDamageBonus: {_baseDamageMod.ElementalBonus}\n";
            }
        }

        // critical hit
        info += $"CriticalChance: {_criticalChance}\n";
        info += $"CriticalHit: {IsCritical}\n";

        if (_criticalDefendedFromAug)
        {
            info += $"CriticalDefended: {_criticalDefendedFromAug}\n";
        }

        if (_criticalDamageMod != 0.0f && _criticalDamageMod != 1.0f)
        {
            info += $"CriticalDamageMod: {_criticalDamageMod}\n";
        }

        if (_criticalDamageRating != 0.0f && _criticalDamageRating != 1.0f)
        {
            info += $"CriticalDamageRatingMod: {_criticalDamageRating}\n";
        }

        // damage ratings
        if (_recklessnessMod != 0.0f && _recklessnessMod != 1.0f)
        {
            info += $"RecklessnessMod: {_recklessnessMod}\n";
        }

        if (SneakAttackMod != 0.0f && SneakAttackMod != 1.0f)
        {
            info += $"SneakAttackMod: {SneakAttackMod}\n";
        }

        if (_pkDamageMod != 0.0f && _pkDamageMod != 1.0f)
        {
            info += $"PkDamageMod: {_pkDamageMod}\n";
        }

        if (_damageRatingMod != 0.0f && _damageRatingMod != 1.0f)
        {
            info += $"DamageRatingMod: {_damageRatingMod}\n";
        }

        if (BodyPart != 0)
        {
            // player body part
            info += $"BodyPart: {BodyPart}\n";
        }

        if (_armor != null && _armor.Count > 0)
        {
            info += $"Armors: {string.Join(", ", _armor.Select(i => i.Name))}\n";
        }

        if (_creaturePart != null)
        {
            // creature body part
            info += $"BodyPart: {_propertiesBodyPart.Key}\n";
            info += $"BaseArmor: {_creaturePart.Biota.Value.BaseArmor}\n";
        }

        // damage mitigation
        if (_armorMod != 0.0f && _armorMod != 1.0f)
        {
            info += $"ArmorMod: {_armorMod}\n";
        }

        if (_resistanceMod != 0.0f && _resistanceMod != 1.0f)
        {
            info += $"ResistanceMod: {_resistanceMod}\n";
        }

        if (ShieldMod != 0.0f && ShieldMod != 1.0f)
        {
            info += $"ShieldMod: {ShieldMod}\n";
        }

        if (_weaponResistanceMod != 0.0f && _weaponResistanceMod != 1.0f)
        {
            info += $"WeaponResistanceMod: {_weaponResistanceMod}\n";
        }

        if (_damageResistanceRatingBaseMod != 0.0f && _damageResistanceRatingBaseMod != 1.0f)
        {
            info += $"DamageResistanceRatingBaseMod: {_damageResistanceRatingBaseMod}\n";
        }

        if (_criticalDamageResistanceRatingMod != 0.0f && _criticalDamageResistanceRatingMod != 1.0f)
        {
            info += $"CriticalDamageResistanceRatingMod: {_criticalDamageResistanceRatingMod}\n";
        }

        if (_pkDamageResistanceMod != 0.0f && _pkDamageResistanceMod != 1.0f)
        {
            info += $"PkDamageResistanceMod: {_pkDamageResistanceMod}\n";
        }

        if (_damageResistanceRatingMod != 0.0f && _damageResistanceRatingMod != 1.0f)
        {
            info += $"DamageResistanceRatingMod: {_damageResistanceRatingMod}\n";
        }

        if (IgnoreMagicArmor)
        {
            info += $"IgnoreMagicArmor: {IgnoreMagicArmor}\n";
        }

        if (IgnoreMagicResist)
        {
            info += $"IgnoreMagicResist: {IgnoreMagicResist}\n";
        }

        // final damage
        info += $"DamageBeforeMitigation: {_damageBeforeMitigation}\n";
        info += $"DamageMitigated: {_damageMitigated}\n";
        info += $"Damage: {Damage}\n";

        info += "----";

        targetInfo.Session.Network.EnqueueSend(new GameMessageSystemChat(info, ChatMessageType.Broadcast));
    }

    private void HandleLogging(Creature attacker, Creature defender)
    {
        if (attacker != null && (attacker.DebugDamage & Creature.DebugDamageType.Attacker) != 0)
        {
            ShowInfo(attacker);
        }

        if (defender != null && (defender.DebugDamage & Creature.DebugDamageType.Defender) != 0)
        {
            ShowInfo(defender);
        }
    }
}
