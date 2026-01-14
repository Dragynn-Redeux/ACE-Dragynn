using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.Enum;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
using Time = ACE.Common.Time;

namespace ACE.Server.WorldObjects;

public enum CombatType
{
    Melee,
    Missile,
    Magic
};

/// <summary>
/// Handles combat with a Player as the attacker
/// generalized methods for melee / missile
/// </summary>
partial class Player
{
    public int AttackSequence;
    public bool Attacking;
    public bool AttackCancelled;

    public double LastAttackReceivedTime;

    public double NextHealingKitUseTime;

    public DateTime NextRefillTime;

    private DamageType LastHitReceivedDamageType;

    public double LastPkAttackTimestamp
    {
        get => GetProperty(PropertyFloat.LastPkAttackTimestamp) ?? 0;
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyFloat.LastPkAttackTimestamp);
            }
            else
            {
                SetProperty(PropertyFloat.LastPkAttackTimestamp, value);
            }
        }
    }

    public double PkTimestamp
    {
        get => GetProperty(PropertyFloat.PkTimestamp) ?? 0;
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyFloat.PkTimestamp);
            }
            else
            {
                SetProperty(PropertyFloat.PkTimestamp, value);
            }
        }
    }

    /// <summary>
    /// Returns the current attack skill for the player
    /// </summary>
    public override Skill GetCurrentAttackSkill()
    {
        if (CombatMode == CombatMode.Magic)
        {
            return GetCurrentMagicSkill();
        }
        else
        {
            return GetCurrentWeaponSkill();
        }
    }

    /// <summary>
    /// Returns the current weapon skill for the player
    /// </summary>
    public override Skill GetCurrentWeaponSkill()
    {
        var weapon = GetEquippedWeapon();

        if (weapon?.WeaponSkill == null)
        {
            return GetHighestMeleeSkill();
        }

        var skill = ConvertToMoASkill(weapon.WeaponSkill);

        return skill;
    }

    /// <summary>
    /// Called when a player receives an attack, evaded or not
    /// </summary>
    public override void OnAttackReceived(
        WorldObject attacker,
        CombatType attackType,
        bool critical,
        bool avoided,
        int spellLevel = 1
    )
    {
        base.OnAttackReceived(attacker, attackType, critical, avoided);
    }

    public override CombatType GetCombatType()
    {
        // this is an unsafe function, move away from this
        var weapon = GetEquippedWeapon();

        if (weapon == null || weapon.CurrentWieldedLocation != EquipMask.MissileWeapon)
        {
            return CombatType.Melee;
        }
        else
        {
            return CombatType.Missile;
        }
    }

    public DamageEvent DamageTarget(Creature target, WorldObject damageSource, bool cleaveHits = false)
    {
        if (target.Health.Current <= 0)
        {
            return null;
        }

        var targetPlayer = target as Player;

        // check PK status
        var pkError = CheckPKStatusVsTarget(target, null);
        if (pkError != null)
        {
            Session.Network.EnqueueSend(new GameEventWeenieErrorWithString(Session, pkError[0], target.Name));
            if (targetPlayer != null)
            {
                targetPlayer.Session.Network.EnqueueSend(
                    new GameEventWeenieErrorWithString(targetPlayer.Session, pkError[1], Name)
                );
            }

            return null;
        }

        var damageEvent = DamageEvent.CalculateDamage(this, target, damageSource, null, null, cleaveHits);

        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.TwoHandedCombat, SigilTrinketShieldTwohandedCombatEffect.Aggression);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.Shield, SigilTrinketShieldTwohandedCombatEffect.Aggression);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.DualWield, SigilTrinketDualWieldMissileEffect.Assailment, damageEvent.IsCritical);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.Bow, SigilTrinketDualWieldMissileEffect.Assailment, damageEvent.IsCritical);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.DualWield, SigilTrinketDualWieldMissileEffect.SwiftKiller);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.Bow, SigilTrinketDualWieldMissileEffect.SwiftKiller);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.Thievery, SigilTrinketThieveryEffect.Treachery, damageEvent.IsCritical);
        CheckForSigilTrinketOnAttackEffects(target, damageEvent, Skill.Deception, SigilTrinketDeceptionEffect.Avoidance);

        target.OnAttackReceived(
            this,
            (damageSource == null || damageSource.ProjectileSource == null) ? CombatType.Melee : CombatType.Missile,
            damageEvent.IsCritical,
            damageEvent.Evaded || damageEvent.Blocked || damageEvent.PartialEvasion != PartialEvasion.None
        );

        if (target.IsMonster)
        {
            var damage = damageEvent.Damage;
            var targetAvgHealth = target.GetCreatureAvgTierHealth();
            var percentDamageDealt = damage / targetAvgHealth;

            var threat = percentDamageDealt * 1000;

            target.IncreaseTargetThreatLevel(this, (int)threat);

            LastAttackedCreature = target;
            LastAttackTime = Time.GetUnixTime();
        }

        var crit = damageEvent.IsCritical;
        var critMessage = crit == true ? "Critical Hit! " : "";

        if (damageEvent.HasDamage)
        {
            if (damageEvent.Blocked || damageEvent.Parried)
            {
                return null;
            }

            OnDamageTarget(target, damageEvent.CombatType, damageEvent.IsCritical);

            if (targetPlayer != null)
            {
                targetPlayer.TakeDamage(this, damageEvent);
            }
            else
            {
                target.TakeDamage(this, damageEvent.DamageType, damageEvent.Damage, damageEvent.IsCritical);
            }
        }
        else
        {
            if (damageEvent.LifestoneProtection)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The Lifestone's magic protects {target.Name} from the attack!",
                        ChatMessageType.Magic
                    )
                );
            }
            else if (this != target && damageEvent.Blocked)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"{target.Name} blocked your attack!", ChatMessageType.CombatEnemy)
                );
            }
            else if (this != target && damageEvent.Parried)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"{target.Name} parried your attack!", ChatMessageType.CombatEnemy)
                );
            }
            else if (!SquelchManager.Squelches.Contains(target, ChatMessageType.CombatSelf))
            {
                Session.Network.EnqueueSend(new GameEventEvasionAttackerNotification(Session, target.Name));
            }

            if (targetPlayer != null)
            {
                targetPlayer.OnEvade(this, damageEvent.CombatType);
            }
        }

        if (damageEvent.HasDamage && target.IsAlive && target.SilentCombat is not true)
        {
            // notify attacker
            var intDamage = (uint)Math.Round(damageEvent.Damage);

            var pointsText = intDamage == 1 ? "point" : "points";

            var damageTypeText = "";
            switch (damageEvent.DamageType)
            {
                case DamageType.Acid:
                    damageTypeText = "acid";
                    break;
                case DamageType.Bludgeon:
                    damageTypeText = "bludgeoning";
                    break;
                case DamageType.Cold:
                    damageTypeText = "cold";
                    break;
                case DamageType.Electric:
                    damageTypeText = "electric";
                    break;
                case DamageType.Fire:
                    damageTypeText = "fire";
                    break;
                case DamageType.Pierce:
                    damageTypeText = "piercing";
                    break;
                case DamageType.Slash:
                    damageTypeText = "slashing";
                    break;
            }

            if (!SquelchManager.Squelches.Contains(this, ChatMessageType.CombatSelf))
            {
                var furyMsg = "";
                var furyPercent = 0;
                var critMsg = damageEvent.IsCritical ? "Critical Hit! " : "";
                var sneakMsg = damageEvent.SneakAttackMod > 1.0f ? "Sneak Attack! " : "";

                var percent = intDamage / target.Health.MaxValue;
                string verb = null,
                    single = null;
                Strings.GetAttackVerb(damageEvent.DamageType, percent, ref verb, ref single);

                if (this != target && RelentlessStanceIsActive || FuryStanceIsActive)
                {
                    furyPercent = (int)(AdrenalineMeter * 100);

                    if (RelentlessStanceIsActive)
                    {
                        furyMsg = $"{furyPercent}% Relentless Adrenaline! ";
                    }

                    if (FuryStanceIsActive)
                    {
                        furyMsg = $"{furyPercent}% Furious Adrenaline! ";
                    }
                }

                if (FuryEnrageIsActive)
                {
                    furyMsg = $"Enrage! ";
                }

                if (RelentlessTenacityIsActive)
                {
                    furyMsg = $"Tenacity! ";
                }

                if (this != target && damageEvent.PartialEvasion == PartialEvasion.Some)
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"{furyMsg}{sneakMsg}Glancing Blow! You {verb} {target.Name} for {intDamage} {pointsText} of {damageTypeText} damage.",
                            ChatMessageType.CombatSelf
                        )
                    );
                }
                else if (this != target && furyMsg != "")
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"{furyMsg}{critMsg}{sneakMsg}You {verb} {target.Name} for {intDamage} {pointsText} of {damageTypeText} damage.",
                            ChatMessageType.CombatSelf
                        )
                    );
                }
                else if (this != target)
                {
                    Session.Network.EnqueueSend(
                        new GameEventAttackerNotification(
                            Session,
                            target.Name,
                            damageEvent.DamageType,
                            (float)intDamage / target.Health.MaxValue,
                            intDamage,
                            damageEvent.IsCritical,
                            damageEvent.AttackConditions
                        )
                    );
                }
                else
                {
                    Session.Network.EnqueueSend(
                        new GameEventAttackerNotification(
                            Session,
                            "yourself",
                            damageEvent.DamageType,
                            (float)intDamage / target.Health.MaxValue,
                            intDamage,
                            damageEvent.IsCritical,
                            damageEvent.AttackConditions
                        )
                    );
                }
            }

            // splatter effects
            if (targetPlayer == null)
            {
                Session.Network.EnqueueSend(new GameMessageSound(target.Guid, Sound.HitFlesh1, 0.5f));
                if (damageEvent.Damage >= target.Health.MaxValue * 0.25f)
                {
                    var painSound = (Sound)Enum.Parse(typeof(Sound), "Wound" + ThreadSafeRandom.Next(1, 3), true);
                    Session.Network.EnqueueSend(new GameMessageSound(target.Guid, painSound, 1.0f));
                }

                var splatter = (PlayScript)
                    Enum.Parse(typeof(PlayScript), "Splatter" + GetSplatterHeight() + GetSplatterDir(target));
                Session.Network.EnqueueSend(new GameMessageScript(target.Guid, splatter));
            }

            // handle Dirty Fighting
            if (GetCreatureSkill(Skill.DirtyFighting).AdvancementClass >= SkillAdvancementClass.Trained)
            {
                FightDirty(target, damageEvent.Weapon);
            }

            target.EmoteManager.OnDamage(this);

            if (damageEvent.IsCritical)
            {
                target.EmoteManager.OnReceiveCritical(this);
            }
        }


        if (targetPlayer == null)
        {
            OnAttackMonster(target);
        }

        return damageEvent;
    }

    /// <summary>
    /// Sets the creature that last attacked a player
    /// This is called when the player takes damage, evades, or resists a spell from a creature
    /// If the CurrentAttacker has changed, sends a network message to the player's client
    /// This enables the 'last attacker' functionality in the client, which is bound to the 'home' key by default
    /// </summary>
    public void SetCurrentAttacker(Creature currentAttacker)
    {
        if (currentAttacker == this || CurrentAttacker == currentAttacker.Guid.Full)
        {
            return;
        }

        CurrentAttacker = currentAttacker.Guid.Full;

        Session.Network.EnqueueSend(
            new GameMessagePrivateUpdateInstanceID(this, PropertyInstanceId.CurrentAttacker, currentAttacker.Guid.Full)
        );
    }

    /// <summary>
    /// Called when a player hits a target
    /// </summary>
    public override void OnDamageTarget(WorldObject target, CombatType attackType, bool critical)
    {
        var attackSkill = GetCreatureSkill(GetCurrentWeaponSkill());
        var difficulty = GetTargetEffectiveDefenseSkill(target);

        Proficiency.OnSuccessUse(this, attackSkill, difficulty);
    }

    public override uint GetEffectiveAttackSkill()
    {
        var weapon = GetEquippedWeapon();
        var attackSkill = GetCreatureSkill(GetCurrentWeaponSkill()).Current;
        double? offenseMod = 1.0;

        offenseMod = GetWeaponOffenseModifier(this) + GetArmorAttackMod();

        var accuracyMod = GetAccuracySkillMod(weapon);

        attackSkill = (uint)
            Math.Round(
                attackSkill * accuracyMod * ((float)offenseMod + GetSecondaryAttributeMod(GetCurrentAttackSkill()))
            );

        //if (IsExhausted)
        //attackSkill = GetExhaustedSkill(attackSkill);

        return attackSkill;
    }

    public uint GetTargetEffectiveDefenseSkill(WorldObject target)
    {
        var creature = target as Creature;
        if (creature == null)
        {
            return 0;
        }

        var attackType = GetCombatType();
        var defenseSkill = attackType == CombatType.Missile ? Skill.MissileDefense : Skill.PhysicalDefense;
        var defenseMod = defenseSkill == Skill.PhysicalDefense ? GetWeaponPhysicalDefenseModifier(creature) : 1.0f;
        var effectiveDefense = (uint)Math.Round(creature.GetCreatureSkill(defenseSkill).Current * defenseMod);

        if (creature.IsExhausted)
        {
            effectiveDefense = 0;
        }

        //var baseStr = defenseMod != 1.0f ? $" (base: {creature.GetCreatureSkill(defenseSkill).Current})" : "";
        //Console.WriteLine("Defense skill: " + effectiveDefense + baseStr);

        return effectiveDefense;
    }

    /// <summary>
    /// Returns a modifier to the player's defense skill, based on current motion state
    /// </summary>
    /// <returns></returns>
    public float GetDefenseStanceMod()
    {
        if (IsJumping)
        {
            return 0.5f;
        }

        if (IsLoggingOut)
        {
            return 0.8f;
        }

        if (CombatMode != CombatMode.NonCombat)
        {
            return 1.0f;
        }

        var forwardCommand =
            CurrentMovementData.MovementType == MovementType.Invalid && CurrentMovementData.Invalid != null
                ? CurrentMovementData.Invalid.State.ForwardCommand
                : MotionCommand.Invalid;

        switch (forwardCommand)
        {
            // TODO: verify multipliers
            case MotionCommand.Crouch:
                return 0.4f;
            case MotionCommand.Sitting:
                return 0.3f;
            case MotionCommand.Sleeping:
                return 0.2f;
            default:
                return 1.0f;
        }
    }

    public bool Reprisal = false;

    /// <summary>
    /// Called when player successfully evades an attack
    /// </summary>
    public override void OnEvade(WorldObject attacker, CombatType attackType)
    {
        var creatureAttacker = attacker as Creature;

        if (creatureAttacker != null)
        {
            SetCurrentAttacker(creatureAttacker);
        }

        if (UnderLifestoneProtection)
        {
            return;
        }

        // http://asheron.wikia.com/wiki/Attributes

        // Endurance will also make it less likely that you use a point of stamina to successfully evade a missile or melee attack.
        // A player is required to have Melee Defense for melee attacks or Missile Defense for missile attacks trained or specialized
        // in order for this specific ability to work. This benefit is tied to Endurance only, and it caps out at around a 75% chance
        // to avoid losing a point of stamina per successful evasion.

        var defenseSkill = GetCreatureSkill(Skill.PhysicalDefense);

        if (CombatMode != CombatMode.NonCombat)
        {
            if (defenseSkill.AdvancementClass >= SkillAdvancementClass.Trained)
            {
                var enduranceBase = (int)Endurance.Base;

                // TODO: find exact formula / where it caps out at 75%

                // more literal / linear formula
                //var noStaminaUseChance = (enduranceBase - 50) / 320.0f;

                // gdle curve-based formula, caps at 300 instead of 290
                var noStaminaUseChance =
                    (enduranceBase * enduranceBase * 0.000005f) + (enduranceBase * 0.00124f) - 0.07f;

                noStaminaUseChance = Math.Clamp(noStaminaUseChance, 0.0f, 0.75f);

                //Console.WriteLine($"NoStaminaUseChance: {noStaminaUseChance}");

                if (noStaminaUseChance <= ThreadSafeRandom.Next(0.0f, 1.0f))
                {
                    UpdateVitalDelta(Stamina, -1);
                }
            }
            else
            {
                UpdateVitalDelta(Stamina, -1);
            }
        }
        else
        {
            // if the player is in non-combat mode, no stamina is consumed on evade
            // reference: https://youtu.be/uFoQVgmSggo?t=145
            // from the dm guide, page 147: "if you are not in Combat mode, you lose no Stamina when an attack is thrown at you"

            //UpdateVitalDelta(Stamina, -1);
        }
        if (Reprisal)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Reprisal! You evaded {attacker.Name}'s critical strike!",
                    ChatMessageType.CombatEnemy
                )
            );
            Reprisal = false;
        }
        else if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
        {
            Session.Network.EnqueueSend(new GameEventEvasionDefenderNotification(Session, attacker.Name));
        }

        if (creatureAttacker == null)
        {
            return;
        }

        var difficulty = creatureAttacker.GetCreatureSkill(creatureAttacker.GetCurrentWeaponSkill()).Current;
        // attackMod?
        Proficiency.OnSuccessUse(this, defenseSkill, difficulty);
    }

    public int? ShieldReprisal = null;

    /// <summary>
    /// Called when player successfully blocks an attack
    /// </summary>
    public override void OnBlock(WorldObject attacker, CombatType attackType, DamageEvent damageEvent)
    {
        var creatureAttacker = attacker as Creature;

        if (creatureAttacker != null)
        {
            SetCurrentAttacker(creatureAttacker);
        }

        if (UnderLifestoneProtection)
        {
            return;
        }

        UpdateVitalDelta(Stamina, -1);

        if (GetEquippedShield() != null)
        {
            if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
            {
                if (ShieldReprisal.HasValue)
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You blocked {attacker.Name}'s attack, deflecting {ShieldReprisal} damage back at them!",
                            ChatMessageType.CombatEnemy
                        )
                    );
                    ShieldReprisal = null;
                }
                else
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat($"You blocked {attacker.Name}'s attack!", ChatMessageType.CombatEnemy)
                    );
                }
            }
        }

        damageEvent.CheckForRiposte(creatureAttacker, this);
    }

    /// <summary>
    /// Called when player successfully parries an attack
    /// </summary>
    public override void OnParry(WorldObject attacker, CombatType attackType, DamageEvent damageEvent)
    {
        var creatureAttacker = attacker as Creature;

        if (creatureAttacker != null)
        {
            SetCurrentAttacker(creatureAttacker);
        }

        if (UnderLifestoneProtection)
        {
            return;
        }

        UpdateVitalDelta(Stamina, -1);

        if (!SquelchManager.Squelches.Contains(attacker, ChatMessageType.CombatEnemy))
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You parried {attacker.Name}'s attack!", ChatMessageType.CombatEnemy)
            );
        }

        damageEvent.CheckForRiposte(creatureAttacker, this);
    }

    public BaseDamageMod GetBaseDamageMod(WorldObject damageSource)
    {
        if (damageSource == this)
        {
            BaseDamageMod baseDamageMod;

            if (AttackType == AttackType.Punch)
            {
                damageSource = HandArmor;
            }
            else if (AttackType == AttackType.Kick)
            {
                damageSource = FootArmor;
            }

            // no weapon, no hand or foot armor
            if (damageSource == null)
            {
                var baseDamage = new BaseDamage(1, 0.75f);
                baseDamageMod = new BaseDamageMod(baseDamage);
            }
            else
            {
                baseDamageMod = damageSource.GetDamageMod(this, damageSource);
                baseDamageMod.BaseDamage.MaxDamage += 1;
            }

            return baseDamageMod;
        }
        return damageSource.GetDamageMod(this);
    }

    /// <summary>
    /// Scales from -50% modifier to +100% (Retail was -50% to +50%)
    /// </summary>
    public override float GetPowerMod(WorldObject weapon)
    {
        if (weapon == null)
        {
            return 1.0f;
        }

        var currentAnimLength = LastAttackAnimationLength;
        var multistrike = 1;

        if (
            weapon.IsTwoHanded
            || weapon.W_AttackType == AttackType.DoubleStrike
            || weapon.W_AttackType == AttackType.DoubleSlash
            || weapon.W_AttackType == AttackType.DoubleThrust
            || weapon.W_AttackType == AttackType.OffhandDoubleSlash
            || weapon.W_AttackType == AttackType.OffhandDoubleThrust
        )
        {
            currentAnimLength /= 2;
            multistrike = 2;
        }
        if (
            weapon.W_AttackType == AttackType.MultiStrike
            || weapon.W_AttackType == AttackType.TripleStrike
            || weapon.W_AttackType == AttackType.TripleSlash
            || weapon.W_AttackType == AttackType.TripleThrust
            || weapon.W_AttackType == AttackType.OffhandTripleSlash
            || weapon.W_AttackType == AttackType.OffhandTripleThrust
        )
        {
            currentAnimLength /= 3;
            multistrike = 3;
        }

        var animMod = (float)((currentAnimLength + GetPowerAccuracyBar() / multistrike) / currentAnimLength);

        //Console.WriteLine($"\n--------- {weapon.Name} {Math.Round(GetPowerAccuracyBar() * 100, 0)}% ---------\n" +
        //    $"CurrentAnimLength: {currentAnimLength}\n" +
        //    $"AnimMod: {animMod}");

        if (weapon.IsRanged)
        {
            return (float)(Math.Pow(GetPowerAccuracyBar() / 2, 2) + 0.5) * animMod;
        }
        else
        {
            return (float)(Math.Pow(GetPowerAccuracyBar() / 2, 2) + 0.5) * animMod;
        }
    }

    /// <summary>
    /// Accuracy Skill Mod ranges from 1 to 1.1 (+0% to +10%)
    /// (the accuracy bar now also grants crit mods)
    /// </summary>
    public override float GetAccuracySkillMod(WorldObject weapon)
    {
        if (weapon != null && weapon.IsRanged)
        {
            var accuracyMod = 1 + (AccuracyLevel / 10);

            //Console.WriteLine($"GetAccuracyMod - AccuracyLevel: {AccuracyLevel}, AccuracyMod: {accuracyMod}");

            return accuracyMod;
        }
        else
        {
            return 1.0f;
        }
    }

    /// <summary>
    /// Accuracy Crit Chance Mod ranges from 0 to 0.1. This is added to DamageEvent.CriticalChance
    /// </summary>
    public float GetAccuracyCritChanceMod(WorldObject weapon)
    {
        if (weapon != null && weapon.IsRanged)
        {
            var critChanceMod = AccuracyLevel <= 0.5 ? AccuracyLevel / 15 : AccuracyLevel / 10;

            //Console.WriteLine($"GetAccuracyCritChanceMod - AccuracyLevel: {AccuracyLevel}, CritChanceMod: {critChanceMod}");

            return critChanceMod;
        }
        else
        {
            return 1.0f;
        }
    }

    /// <summary>
    /// Accuracy Crit Damage Mod ranges from 0 to 0.25. This is added to DamageEvent.CriticalDamageMod
    /// </summary>
    public float GetAccuracyCritDamageMod(WorldObject weapon)
    {
        if (weapon != null && weapon.IsRanged)
        {
            var critDamageMod = AccuracyLevel / 4;

            //Console.WriteLine($"GetAccuracyCritDamageMod - AccuracyLevel: {AccuracyLevel}, CritDamageMod: {critDamageMod}");

            return critDamageMod;
        }
        else
        {
            return 0.0f;
        }
    }

    public float GetPowerAccuracyBar()
    {
        return GetCombatType() == CombatType.Missile ? AccuracyLevel : PowerLevel;
    }

    /// <summary>
    /// Up to double proc chance based on power/accuracy bar amount
    /// </summary>
    public float ScaleWithPowerAccuracyBar(float value)
    {
        return 1.0f + GetPowerAccuracyBar();
    }

    public Sound GetHitSound(WorldObject source, BodyPart bodyPart)
    {
        /*var creature = source as Creature;
        var armors = creature.GetArmor(bodyPart);

        foreach (var armor in armors)
        {
            var material = armor.GetProperty(PropertyInt.MaterialType) ?? 0;
            //Console.WriteLine("Name: " + armor.Name + " | Material: " + material);
        }*/
        return Sound.HitFlesh1;
    }

    /// <summary>
    /// Simplified player take damage function, only called for DoTs currently
    /// </summary>
    public override void TakeDamageOverTime(float _amount, DamageType damageType)
    {
        if (Invincible || IsDead)
        {
            return;
        }

        // check lifestone protection
        if (UnderLifestoneProtection)
        {
            HandleLifestoneProtection();
            return;
        }

        var amount = (uint)Math.Round(_amount);
        var percent = (float)amount / Health.MaxValue;

        // update health
        var damageTaken = (uint)-UpdateVitalDelta(Health, (int)-amount);

        // update stamina
        //UpdateVitalDelta(Stamina, -1);

        //if (Fellowship != null)
        //Fellowship.OnVitalUpdate(this);

        // send damage text message
        //if (PropertyManager.GetBool("show_dot_messages").Item)
        //{
        var nether = damageType == DamageType.Nether ? "nether " : "";
        var chatMessageType = damageType == DamageType.Nether ? ChatMessageType.Magic : ChatMessageType.Combat;
        var text = $"You receive {amount} points of periodic {nether}damage.";
        SendMessage(text, chatMessageType);
        //}

        // splatter effects
        //var splatter = new GameMessageScript(Guid, (PlayScript)Enum.Parse(typeof(PlayScript), "Splatter" + creature.GetSplatterHeight() + creature.GetSplatterDir(this)));  // not sent in retail, but great visual indicator?
        var splatter = new GameMessageScript(
            Guid,
            damageType == DamageType.Nether ? PlayScript.HealthDownVoid : PlayScript.DirtyFightingDamageOverTime
        );
        EnqueueBroadcast(splatter);

        if (Health.Current <= 0 && !IsInDeathProcess)
        {
            IsInDeathProcess = true;
            // since damage over time is possibly combined from multiple sources,
            // sending a message to the last damager here could be tricky..

            // TODO: get last damager from dot stack instead?
            OnDeath(DamageHistory.LastDamager, damageType, false);
            Die();

            return;
        }

        if (percent >= 0.1f)
        {
            EnqueueBroadcast(new GameMessageSound(Guid, Sound.Wound1, 1.0f));
        }
    }

    public int TakeDamage(WorldObject source, DamageEvent damageEvent)
    {
        return TakeDamage(
            source,
            damageEvent.DamageType,
            damageEvent.Damage,
            damageEvent.BodyPart,
            damageEvent.PartialEvasion,
            damageEvent.IsCritical,
            damageEvent.AttackConditions
        );
    }

    /// <summary>
    /// Applies damages to a player from a physical damage source
    /// </summary>
    public int TakeDamage(
        WorldObject source,
        DamageType damageType,
        float _amount,
        BodyPart bodyPart,
        PartialEvasion partialEvasion,
        bool crit = false,
        AttackConditions attackConditions = AttackConditions.None
    )
    {
        if (Invincible || IsDead || IsInDeathProcess)
        {
            return 0;
        }

        if (source is Creature creatureAttacker)
        {
            SetCurrentAttacker(creatureAttacker);

            // Combat Focus - Smokescreen (50% to force attacker to search for a new target when hit, if more than one player is available)
            var playerCombatFocus = GetPlayerCombatAbility(this);
            if (playerCombatFocus == CombatAbility.Smokescreen)
            {
                var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
                if (rng > 0.5f)
                {
                    creatureAttacker.FindNextTarget(false);
                }
            }
        }
        // check lifestone protection
        if (UnderLifestoneProtection)
        {
            HandleLifestoneProtection();
            return 0;
        }

        if (_amount < 0)
        {
            _log.Error(
                $"{Name}.TakeDamage({source?.Name} ({source?.Guid}), {damageType}, {_amount}) - negative damage, this shouldn't happen"
            );
            return 0;
        }

        var amount = (uint)Math.Round(_amount);
        var percent = (float)amount / Health.MaxValue;

        var equippedCloak = EquippedCloak;

        if (equippedCloak != null && Cloak.HasDamageProc(equippedCloak) && Cloak.RollProc(equippedCloak, percent))
        {
            var reducedAmount = Cloak.GetReducedAmount(source, amount);

            Cloak.ShowMessage(this, source, amount, reducedAmount);

            amount = reducedAmount;
            percent = (float)amount / Health.MaxValue;
        }

        uint damageTaken;

        if (ManaBarrierIsActive)
        {
            damageTaken = CombatAbilityManaBarrier(this, amount, source, damageType);
        }
        else
        {
            // update health
            damageTaken = (uint)-UpdateVitalDelta(Health, (int)-amount);
            DamageHistory.Add(source, damageType, damageTaken);
        }

        // update stamina
        if (CombatMode != CombatMode.NonCombat)
        {
            // if the player is in non-combat mode, no stamina is consumed on evade
            // reference: https://youtu.be/uFoQVgmSggo?t=145
            // from the dm guide, page 147: "if you are not in Combat mode, you lose no Stamina when an attack is thrown at you"

            UpdateVitalDelta(Stamina, -1);
        }

        //if (Fellowship != null)
        //Fellowship.OnVitalUpdate(this);

        if (Health.Current <= 0 && !IsInDeathProcess)
        {
            IsInDeathProcess = true;
            OnDeath(new DamageHistoryInfo(source), damageType, crit);
            Die();
            return (int)damageTaken;
        }

        LastHitReceivedDamageType = damageType;

        if (!BodyParts.Indices.TryGetValue(bodyPart, out var iDamageLocation))
        {
            _log.Error(
                $"{Name}.TakeDamage({source.Name}, {damageType}, {amount}, {bodyPart}, {crit}): avoided crash for bad damage location"
            );
            return 0;
        }
        var damageLocation = (DamageLocation)iDamageLocation;

        var pointsText = damageTaken == 1 ? "point" : "points";

        var damageTypeText = "";
        switch (damageType)
        {
            case DamageType.Acid:
                damageTypeText = "acid";
                break;
            case DamageType.Bludgeon:
                damageTypeText = "bludgeoning";
                break;
            case DamageType.Cold:
                damageTypeText = "cold";
                break;
            case DamageType.Electric:
                damageTypeText = "electric";
                break;
            case DamageType.Fire:
                damageTypeText = "fire";
                break;
            case DamageType.Pierce:
                damageTypeText = "piercing";
                break;
            case DamageType.Slash:
                damageTypeText = "slashing";
                break;
        }

        // send network messages
        if (source is Creature creature)
        {
            var critMessage = crit == true ? "Critical Hit! " : "";

            var sneakAttackMod = creature.GetSneakAttackMod(this);
            var sneakMsg = sneakAttackMod > 1.0f ? "Sneak Attack! " : "";

            var percentHp = damageTaken / Health.MaxValue;
            string verb = null,
                plural = null;
            Strings.GetAttackVerb(damageType, percentHp, ref verb, ref plural);

            if (
                !SquelchManager.Squelches.Contains(source, ChatMessageType.CombatEnemy)
                && this != creature
                && partialEvasion == PartialEvasion.Some
            )
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{sneakMsg}Glancing Blow! {creature.Name} {plural} you for {damageTaken} {pointsText} of {damageTypeText} damage.",
                        ChatMessageType.CombatEnemy
                    )
                );
            }
            else if (!SquelchManager.Squelches.Contains(source, ChatMessageType.CombatEnemy) && this != creature)
            {
                Session.Network.EnqueueSend(
                    new GameEventDefenderNotification(
                        Session,
                        creature.Name,
                        damageType,
                        percent,
                        damageTaken,
                        damageLocation,
                        crit,
                        attackConditions
                    )
                );
            }

            var hitSound = new GameMessageSound(Guid, GetHitSound(source, bodyPart), 1.0f);
            var splatter = new GameMessageScript(
                Guid,
                (PlayScript)
                    Enum.Parse(
                        typeof(PlayScript),
                        "Splatter" + creature.GetSplatterHeight() + creature.GetSplatterDir(this)
                    )
            );
            EnqueueBroadcast(hitSound, splatter);
        }

        if (percent >= 0.1f)
        {
            // Wound1 - Aahhh!    - elemental attacks above some threshold
            // Wound2 - Deep Ugh! - bludgeoning attacks above some threshold
            // Wound3 - Ooh!      - slashing / piercing / undef attacks above some threshold

            var woundSound = Sound.Wound3;

            if (damageType == DamageType.Bludgeon)
            {
                woundSound = Sound.Wound2;
            }
            else if ((damageType & DamageType.Elemental) != 0)
            {
                woundSound = Sound.Wound1;
            }

            EnqueueBroadcast(new GameMessageSound(Guid, woundSound, 1.0f));
        }

        if (equippedCloak != null && Cloak.HasProcSpell(equippedCloak))
        {
            Cloak.TryProcSpell(this, source, equippedCloak, percent);
        }

        // if player attacker, update PK timer
        if (source is Player attacker)
        {
            UpdatePKTimers(attacker, this);
        }

        return (int)damageTaken;
    }

    public static uint CombatAbilityManaBarrier(Player player, uint amount, WorldObject source, DamageType damageType)
    {
        if (player is null || source is null)
        {
            return amount;
        }

        var finalAmount = amount;

        var skill = player.GetCreatureSkill(Skill.ManaConversion);

        if (player.Level == null)
        {
            return finalAmount;
        }

        const double manaBarrierDamageReduction = 0.25;

        var expectedSkill = (float)(player.Level * 5);
        var currentSkill = (float)skill.Current;

        // Create a scaling mod. if expected skill is much higher than currentSkill, you will be multiplying the amount
        // of mana damage singificantly, so low skill players will not get much benefit before bubble bursts.
        // Capped at 1f so high skill gets the proper ratio of health-to-mana, but no better than that.

        var skillPenalty = Math.Max(expectedSkill / currentSkill, 1.0f);

        // 25% of damage taken as mana instead, x2 for trained, x1.5 for specialized
        var specMod = skill.AdvancementClass == SkillAdvancementClass.Specialized ? 1.5 : 2;

        var manaDamage = (amount * manaBarrierDamageReduction) * specMod * skillPenalty;

        if (player.Mana.Current >= manaDamage)
        {
            finalAmount = (uint)(amount * (1 - manaBarrierDamageReduction));
            player.PlayParticleEffect(PlayScript.RestrictionEffectBlue, player.Guid);
            player.UpdateVitalDelta(player.Mana, (int)-Math.Round(manaDamage));
            player.UpdateVitalDelta(player.Health, (int)-finalAmount);
            player.DamageHistory.Add(source, damageType, (uint)-finalAmount);
        }
        // if not enough mana, barrier falls and player takes remainder of damage as health
        else
        {
            player.ManaBarrierIsActive = false;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"Your mana barrier fails and collapses!", ChatMessageType.Broadcast)
            );

            var manaBarrierItem = player.GetInventoryItemsOfWCID(1051110);
            if (manaBarrierItem.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(manaBarrierItem[0]);
            }

            player.PlayParticleEffect(PlayScript.HealthDownBlue, player.Guid);

            // find mana damage overage and reconvert to HP damage
            var manaRemainder = (manaDamage - player.Mana.Current) / skillPenalty / specMod;

            finalAmount = (uint)((amount * (1 - manaBarrierDamageReduction)) + manaRemainder);
            player.UpdateVitalDelta(player.Mana, (int)-(player.Mana.Current - 1));
            player.UpdateVitalDelta(player.Health, (int)-(finalAmount));
            player.DamageHistory.Add(source, damageType, (uint)-finalAmount);
        }

        return finalAmount;
    }

    public string GetArmorType(BodyPart bodyPart)
    {
        // Flesh, Leather, Chain, Plate
        // for hit sounds
        return null;
    }

    /// <summary>
    /// Returns the total burden of items held in both hands
    /// (main hand and offhand)
    /// </summary>
    public int GetHeldItemBurden()
    {
        var mainhand = GetEquippedMainHand();
        var offhand = GetEquippedOffHand();

        var mainhandBurden = mainhand?.EncumbranceVal ?? 0;
        var offhandBurden = offhand?.EncumbranceVal ?? 0;

        return mainhandBurden + offhandBurden;
    }

    /// <summary>
    /// Returns weapon tier of a mainhand weapon
    /// </summary>
    public int GetMainHandWeaponTier()
    {
        var weapon = GetEquippedMainHand();

        int weaponTier;

        if (weapon == null || (!weapon.WieldDifficulty.HasValue && !weapon.Tier.HasValue))
        {
            return 1;
        }

        if (weapon.Tier.HasValue)
        {
            weaponTier = weapon.Tier.Value;
        }
        else if (weapon.WieldDifficulty.Value == 300)
        {
            weaponTier = 8;
        }
        else if (weapon.WieldDifficulty.Value == 275)
        {
            weaponTier = 7;
        }
        else if (weapon.WieldDifficulty.Value == 250)
        {
            weaponTier = 6;
        }
        else if (weapon.WieldDifficulty.Value == 225)
        {
            weaponTier = 5;
        }
        else if (weapon.WieldDifficulty.Value == 200)
        {
            weaponTier = 4;
        }
        else if (weapon.WieldDifficulty.Value == 150)
        {
            weaponTier = 3;
        }
        else if (weapon.WieldDifficulty.Value == 100)
        {
            weaponTier = 2;
        }
        else
        {
            weaponTier = 1;
        }

        return weaponTier;
    }

    /// <summary>
    /// Returns weapon tier of a mainhand weapon
    /// </summary>
    public int GetOffHandWeaponTier()
    {
        var weapon = GetEquippedOffHand();

        int weaponTier;

        if (weapon == null || (!weapon.WieldDifficulty.HasValue && !weapon.Tier.HasValue))
        {
            return 1;
        }

        if (weapon.Tier.HasValue)
        {
            weaponTier = weapon.Tier.Value;
        }
        else if (weapon.WieldDifficulty.Value == 300)
        {
            weaponTier = 8;
        }
        else if (weapon.WieldDifficulty.Value == 275)
        {
            weaponTier = 7;
        }
        else if (weapon.WieldDifficulty.Value == 250)
        {
            weaponTier = 6;
        }
        else if (weapon.WieldDifficulty.Value == 225)
        {
            weaponTier = 5;
        }
        else if (weapon.WieldDifficulty.Value == 200)
        {
            weaponTier = 4;
        }
        else if (weapon.WieldDifficulty.Value == 150)
        {
            weaponTier = 3;
        }
        else if (weapon.WieldDifficulty.Value == 100)
        {
            weaponTier = 2;
        }
        else
        {
            weaponTier = 1;
        }

        return weaponTier;
    }

    //private double TimeSinceLastStaminaUse = 0;

    /// <summary>
    /// Calculates the amount of stamina required to perform this attack
    /// </summary>
    private int GetAttackStamina(float attackAnimLength, WorldObject weapon)
    {
        var weaponTier = Math.Max(GetMainHandWeaponTier(), GetOffHandWeaponTier());
        var powerAccuracyLevel = GetEquippedMissileWeapon() != null ? AccuracyLevel : PowerLevel;
        var weightClassPenalty = (float)(1 + (GetArmorResourcePenalty() ?? 0));
        var baseCost = StaminaTable.GetStaminaCost(weaponTier, attackAnimLength, powerAccuracyLevel, weightClassPenalty);

        var staminaCostReductionMod = GetStaminaReductionMod(weapon);

        // ability penalty mods are additive with each other
        var evasiveStancePenaltyMod = GetEvasiveStanceStaminaPenalty();
        var phalanxPenaltyMod = PhalanxIsActive ? 0.25f : 0.0f;
        var provokePenaltyMod = ProvokeIsActive ? 0.25f : 0.0f;
        var ripostePenaltyMod = RiposteIsActive ? 0.25f : 0.0f;
        var furyPenaltyMod = FuryEnrageIsActive ? 0.25f : 0.0f;
        var multiShotPenaltyMod = MultiShotIsActive ? 0.25f : 0.0f;
        var steadyStrikePenaltyMod = SteadyStrikeIsActive ? 0.25f : 0.0f;
        var smokescreenPenaltyMod = SmokescreenIsActive ? 0.25f : 0.0f;
        var backstabPenaltyMod = BackstabIsActive ? 0.25f : 0.0f;
        var abilityPenaltyMod = 1.0f
                                + evasiveStancePenaltyMod
                                + phalanxPenaltyMod
                                + provokePenaltyMod
                                + ripostePenaltyMod
                                + furyPenaltyMod
                                + multiShotPenaltyMod
                                + steadyStrikePenaltyMod
                                + smokescreenPenaltyMod
                                + backstabPenaltyMod;

        baseCost *= staminaCostReductionMod * abilityPenaltyMod;

        var staminaCost = Math.Max(baseCost, 1);

        if (GetEquippedCombatFocus() is { CombatFocusTypeId: (int)CombatFocusType.Spellsword})
        {
            staminaCost = (int)Math.Round(staminaCost * 0.5f);
            var manaCost = staminaCost;

            if (BatteryDischargeIsActive)
            {
                manaCost = 0;
            }
            else if (BatteryStanceIsActive)
            {
                var batteryMod = ManaChargeMeter * 0.5f;
                manaCost = (int)Math.Round(manaCost * (1.0f - batteryMod));
            }

            UpdateVitalDelta(Mana, (int)-manaCost);

            return (int)Math.Round(staminaCost);
        }

        return (int)Math.Round(staminaCost);
    }

    private float GetEvasiveStanceStaminaPenalty()
    {
        if (EvasiveStanceIsActive is not true)
        {
            return 0.0f;
        }

        var skillRun = GetCreatureSkill(Skill.Run);
        var skillJump = GetCreatureSkill(Skill.Jump);

        var currentSkillRun = (float)skillRun.Current;
        var currentSkillJump = (float)skillJump.Current;

        if (Level is null)
        {
            return 0.0f;
        }

        // to account for the combat run debuff, if run is not spec we use half the expected skill
        var expectedRunSkill = skillRun.AdvancementClass is SkillAdvancementClass.Specialized ? (float)(Level * 5) : (float)(Level * 2.5);
        var expectedJumpSkill = (float)(Level * 5);

        var baseRunMod = skillRun.AdvancementClass is SkillAdvancementClass.Specialized ? 0.25f : 0.5f;
        var baseJumpMod = skillJump.AdvancementClass is SkillAdvancementClass.Specialized ? 0.25f : 0.5f;

        var skillModifierRun = Math.Max(expectedRunSkill / currentSkillRun * baseRunMod, baseRunMod);
        var skillModifierJump = Math.Max(expectedJumpSkill / currentSkillJump * baseJumpMod, baseJumpMod);

        // Use whichever skill prevents more stamina loss
        return Math.Min(skillModifierRun, skillModifierJump);

    }

    private float GetStaminaReductionMod(WorldObject weapon)
    {
        var staminaCostReductionMod = 1.0f;

        // Sword/UA implicit rolled bonuses
        if (weapon?.StaminaCostReductionMod != null)
        {
            staminaCostReductionMod *= (1.0f - (float)weapon.StaminaCostReductionMod);
        }

        // SPEC BONUS - UA: Stamina costs for melee attacks reduced by 10%
        if (
            GetCurrentWeaponSkill() == Skill.UnarmedCombat
            && GetCreatureSkill(Skill.UnarmedCombat).AdvancementClass == SkillAdvancementClass.Specialized
        )
        {
            staminaCostReductionMod *= 0.9f;
        }

        // SPEC BONUS - Martial Weapons (Sword): Stamina costs for melee attacks reduced by 10%
        if (
            GetCurrentWeaponSkill() == Skill.Sword
            && GetCreatureSkill(Skill.MartialWeapons).AdvancementClass == SkillAdvancementClass.Specialized
        )
        {
            staminaCostReductionMod *= 0.9f;
        }

        if (RelentlessTenacityIsActive && TenacityLevel > 0.0f)
        {
            staminaCostReductionMod *= (1.0f - TenacityLevel);
        }

        return staminaCostReductionMod;
    }

    /// <summary>
    /// Returns the damage rating modifier for an applicable Recklessness attack
    /// </summary>
    /// <param name="powerAccuracyBar">The 0.0 - 1.0 power/accurary bar</param>
    public float GetRecklessnessMod( /*float powerAccuracyBar*/
    )
    {
        return 1.0f; // Change to enable EoR Recklessness

        //// ensure melee or missile combat mode
        //if (CombatMode != CombatMode.Melee && CombatMode != CombatMode.Missile)
        //    return 1.0f;

        //var skill = GetCreatureSkill(Skill.Recklessness);

        //// recklessness skill must be either trained or specialized to use
        //if (skill.AdvancementClass < SkillAdvancementClass.Trained)
        //    return 1.0f;

        //// recklessness is active when attack bar is between 20% and 80% (according to wiki)
        //// client attack bar range seems to indicate this might have been updated, between 10% and 90%?
        //var powerAccuracyBar = GetPowerAccuracyBar();
        ////if (powerAccuracyBar < 0.2f || powerAccuracyBar > 0.8f)
        //if (powerAccuracyBar < 0.1f || powerAccuracyBar > 0.9f)
        //    return 1.0f;

        //// recklessness only applies to non-critical hits,
        //// which is handled outside of this method.

        //// damage rating is increased by 20 for specialized, and 10 for trained.
        //// incoming non-critical damage from all sources is increased by the same.
        //var damageRating = skill.AdvancementClass == SkillAdvancementClass.Specialized ? 20 : 10;

        //// if recklessness skill is lower than current attack skill (as determined by your equipped weapon)
        //// then the damage rating is reduced proportionately. The damage rating caps at 10 for trained
        //// and 20 for specialized, so there is no reason to raise the skill above your attack skill.
        //var attackSkill = GetCreatureSkill(GetCurrentAttackSkill());

        //if (skill.Current < attackSkill.Current)
        //{
        //    var scale = (float)skill.Current / attackSkill.Current;
        //    damageRating = (int)Math.Round(damageRating * scale);
        //}

        //// The damage rating adjustment for incoming damage is also adjusted proportinally if your Recklessness skill
        //// is lower than your active attack skill

        //var recklessnessMod = GetDamageRating(damageRating);    // trained DR 1.10 = 10% additional damage
        //                                                        // specialized DR 1.20 = 20% additional damage
        //return recklessnessMod;
    }

    /// <summary>
    /// Returns TRUE if this player is PK and died to another player
    /// </summary>
    public bool IsPKDeath(DamageHistoryInfo topDamager)
    {
        return IsPKDeath(topDamager?.Guid.Full);
    }

    public bool IsPKDeath(uint? killerGuid)
    {
        return PlayerKillerStatus.HasFlag(PlayerKillerStatus.PK)
            && new ObjectGuid(killerGuid ?? 0).IsPlayer()
            && killerGuid != Guid.Full;
    }

    /// <summary>
    /// Returns TRUE if this player is PKLite and died to another player
    /// </summary>
    public bool IsPKLiteDeath(DamageHistoryInfo topDamager)
    {
        return IsPKLiteDeath(topDamager?.Guid.Full);
    }

    public bool IsPKLiteDeath(uint? killerGuid)
    {
        return PlayerKillerStatus.HasFlag(PlayerKillerStatus.PKLite)
            && new ObjectGuid(killerGuid ?? 0).IsPlayer()
            && killerGuid != Guid.Full;
    }

    public CombatMode LastCombatMode;

    public const float UseTimeEpsilon = 0.05f;

    /// <summary>
    /// This method processes the Game Action (F7B1) Change Combat Mode (0x0053)
    /// </summary>
    public void HandleActionChangeCombatMode(
        CombatMode newCombatMode,
        bool forceHandCombat = false,
        Action callback = null
    )
    {
        //log.Info($"{Name}.HandleActionChangeCombatMode({newCombatMode})");

        // Make sure the player doesn't have an invalid weapon setup (e.g. sword + wand)
        if (!CheckWeaponCollision(null, null, newCombatMode))
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ActionCancelled)); // "Action cancelled!"

            // Go back to non-Combat mode
            float animTime = 0.0f,
                queueTime = 0.0f;
            animTime = SetCombatMode(newCombatMode, out queueTime, false, true);

            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(animTime);
            actionChain.AddAction(
                this,
                () =>
                {
                    SetCombatMode(CombatMode.NonCombat);
                }
            );
            actionChain.EnqueueChain();

            NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
            return;
        }

        if (DateTime.UtcNow >= NextUseTime.AddSeconds(UseTimeEpsilon))
        {
            HandleActionChangeCombatMode_Inner(newCombatMode, forceHandCombat, callback);
        }
        else
        {
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds((NextUseTime - DateTime.UtcNow).TotalSeconds + UseTimeEpsilon);
            actionChain.AddAction(
                this,
                () => HandleActionChangeCombatMode_Inner(newCombatMode, forceHandCombat, callback)
            );
            actionChain.EnqueueChain();
        }

        LastCombatMode = newCombatMode;

        if (IsAfk)
        {
            HandleActionSetAFKMode(false);
        }
    }

    public void HandleActionChangeCombatMode_Inner(
        CombatMode newCombatMode,
        bool forceHandCombat = false,
        Action callback = null
    )
    {
        //log.Info($"{Name}.HandleActionChangeCombatMode_Inner({newCombatMode})");

        var currentCombatStance = GetCombatStance();

        var missileWeapon = GetEquippedMissileWeapon();
        var caster = GetEquippedWand();

        if (CombatMode == CombatMode.Magic && MagicState.IsCasting)
        {
            FailCast();
        }

        HandleActionCancelAttack();

        float animTime = 0.0f,
            queueTime = 0.0f;

        switch (newCombatMode)
        {
            case CombatMode.NonCombat:
            {
                if (
                    currentCombatStance
                    is MotionStance.AtlatlCombat
                        or MotionStance.CrossbowCombat
                        or MotionStance.AtlatlCombat
                )
                {
                    var equippedAmmo = GetEquippedAmmo();
                    if (equippedAmmo != null)
                    {
                        ClearChild(equippedAmmo); // We must clear the placement/parent when going back to peace
                    }
                }

                break;
            }
            case CombatMode.Melee:

                // todo expand checks
                if (!forceHandCombat && (missileWeapon != null || caster != null))
                {
                    // client has already independently brought the melee bar up by this point, revert and sync everything back up
                    SetCombatMode(CombatMode.NonCombat);
                    return;
                }

                break;

            case CombatMode.Missile:
            {
                if (missileWeapon == null)
                {
                    // client has already independently switched to missile mode by this point,
                    // so instead of simply returning here, we need to deny the request by reverting to either the current server combat state, or switching to NonCombat to maintain client sync
                    // this is especially important for missile, because the client is unable to break out of this bugged state for this mode specifically
                    // see: ClientCombatSystem::PlayerInReadyPosition

                    SetCombatMode(CombatMode.NonCombat);
                    return;
                }

                if (
                    currentCombatStance
                    is MotionStance.AtlatlCombat
                        or MotionStance.CrossbowCombat
                        or MotionStance.AtlatlCombat
                )
                {
                    var equippedAmmo = GetEquippedAmmo();
                    if (equippedAmmo == null)
                    {
                        animTime = SetCombatMode(newCombatMode, out queueTime);

                        var actionChain = new ActionChain();
                        actionChain.AddDelaySeconds(animTime);
                        actionChain.AddAction(
                            this,
                            () =>
                            {
                                Session.Network.EnqueueSend(
                                    new GameEventCommunicationTransientString(Session, "You are out of ammunition!")
                                );
                                SetCombatMode(CombatMode.NonCombat);
                            }
                        );
                        actionChain.EnqueueChain();

                        NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
                        return;
                    }

                    // We must set the placement/parent when going into combat
                    equippedAmmo.Placement = ACE.Entity.Enum.Placement.RightHandCombat;
                    equippedAmmo.ParentLocation = ACE.Entity.Enum.ParentLocation.RightHand;
                }
                break;
            }

            case CombatMode.Magic:

                // todo expand checks
                if (caster == null)
                {
                    // client has already independently brought the magic bar up by this point, revert and sync everything back up
                    SetCombatMode(CombatMode.NonCombat);
                    return;
                }

                break;
        }

        if (WasInPeaceModePreviously())
        {
            ApplyCombatModeRunPenalty();
        }

        if (IsEnteringPeaceMode(newCombatMode))
        {
            RemoveCombatModeRunPenalty();
        }

        // animTime already includes queueTime
        animTime = SetCombatMode(newCombatMode, out queueTime, forceHandCombat);
        //log.Info($"{Name}.HandleActionChangeCombatMode_Inner({newCombatMode}) - animTime: {animTime}, queueTime: {queueTime}");

        NextUseTime = DateTime.UtcNow.AddSeconds(animTime);

        if (MagicState.IsCasting && RecordCast.Enabled)
        {
            RecordCast.OnSetCombatMode(newCombatMode);
        }

        if (callback != null)
        {
            var callbackChain = new ActionChain();
            callbackChain.AddDelaySeconds(animTime);
            callbackChain.AddAction(this, callback);
            callbackChain.EnqueueChain();
        }
    }

    private static bool IsEnteringPeaceMode(CombatMode newCombatMode)
    {
        return newCombatMode == CombatMode.NonCombat;
    }

    private bool WasInPeaceModePreviously()
    {
        return LastCombatMode is CombatMode.Undef or CombatMode.NonCombat;
    }

    private void ApplyCombatModeRunPenalty()
    {
        // SPEC BONUS - Run: Ignore combat mode movement penalty
        var skill = GetCreatureSkill(Skill.Run);
        if (skill.AdvancementClass == SkillAdvancementClass.Specialized)
        {
            return;
        }

        var spell = new Spell(SpellId.CombatModeRunDebuff);
        var addResult = EnchantmentManager.Add(spell, null, null, true);

        Session.Network.EnqueueSend(
            new GameEventMagicUpdateEnchantment(Session, new Enchantment(this, addResult.Enchantment))
        );
        HandleRunRateUpdate(spell);
    }

    private void RemoveCombatModeRunPenalty()
    {
        const uint combatModeRunDebuff = (uint)SpellId.CombatModeRunDebuff;

        while (EnchantmentManager.HasSpell(combatModeRunDebuff))
        {
            var propertiesEnchantmentRegistry = EnchantmentManager.GetEnchantment(combatModeRunDebuff);
            if (propertiesEnchantmentRegistry is null)
            {
                return;
            }

            EnchantmentManager.Dispel(propertiesEnchantmentRegistry);
            if (!Teleporting)
            {
                HandleRunRateUpdate(new Spell(propertiesEnchantmentRegistry.SpellId));
            }
        }
    }

    public override bool CanDamage(Creature target)
    {
        return target.Attackable && !target.Teleporting && target is not CombatPet;
    }

    // http://acpedia.org/wiki/Announcements_-_2002/04_-_Betrayal

    // Some combination of strength and endurance (the two are roughly of equivalent importance) now allows one to have a level of "natural resistances" to the 7 damage types,
    // and to partially resist drain health and harm attacks.

    // This caps out at a 50% resistance (the equivalent to level 5 life prots) to these damage types.

    // This resistance is not additive to life protections: higher level life protections will overwrite these natural resistances,
    // although life vulns will take these natural resistances into account, if the player does not have a higher level life protection cast upon them.

    // For example, a player will not get a free protective bonus from natural resistances if they have both Prot 7 and Vuln 7 cast upon them.
    // The Prot and Vuln will cancel each other out, and since the Prot has overwritten the natural resistances, there will be no resistance bonus.

    // The natural resistances, drain resistances, and regeneration rate info are now visible on the Character Information Panel, in what was once the Burden panel.

    // The 5 categories for the endurance benefits are, in order from lowest benefit to highest: Poor, Mediocre, Hardy, Resilient, and Indomitable,
    // with each range of benefits divided up equally amongst the 5 (e.g. Poor describes having anywhere from 1-10% resistance against drain health attacks, etc.).

    // A few other important notes:

    // - The abilities that Endurance or Endurance/Strength conveys are not increased by Strength or Endurance buffs.
    //   It is the raw Strength and/or Endurance scores that determine the various bonuses.
    // - For April, natural resistances will offer some protection versus hollow type damage, whether it is from a Hollow Minion or a Hollow weapon. This will be changed in May.
    // - These abilities are player-only, creatures with high endurance will not benefit from any of these changes.
    // - Come May, you can type @help endurance for a summary of the April changes to Endurance.

    /// <summary>
    /// Daralet version - Now acts as a multiplier, reducing damage by up to an additional 20%.
    /// Uses "Current" Str+End attribute amounts so that those buffs are desired for damage reduction.
    /// Starts at 200 Str+End and caps at 600. Effects all damage types equally.
    /// </summary>
    /// <returns></returns>
    public override float GetNaturalResistance(DamageType damageType)
    {
        var strAndEnd = Strength.Current + Endurance.Current;
        var amountForScaling = (int)strAndEnd - 200;

        if (amountForScaling <= 0)
        {
            return 1.0f;
        }

        var naturalResistance = 1.0f -  ((float)(amountForScaling) / 600 * 0.2f);
        naturalResistance = Math.Clamp(naturalResistance, 0.8f, 1.0f);

        return naturalResistance;
    }

    public string GetNaturalResistanceString(ResistanceType resistanceType)
    {
        var strAndEnd = Strength.Base + Endurance.Base;

        if (strAndEnd > 440)
        {
            return "Indomitable";
        }
        else if (strAndEnd > 380)
        {
            return "Resilient";
        }
        else if (strAndEnd > 320)
        {
            return "Hardy";
        }
        else if (strAndEnd > 260)
        {
            return "Mediocre";
        }
        else if (strAndEnd > 200)
        {
            return "Poor";
        }
        else
        {
            return "None";
        }
    }

    public string GetRegenBonusString()
    {
        var strAndEnd = Strength.Base + 2 * Endurance.Base;

        if (strAndEnd > 690)
        {
            return "Indomitable";
        }
        else if (strAndEnd > 580)
        {
            return "Resilient";
        }
        else if (strAndEnd > 470)
        {
            return "Hardy";
        }
        else if (strAndEnd > 346)
        {
            return "Mediocre";
        }
        else if (strAndEnd > 200)
        {
            return "Poor";
        }
        else
        {
            return "None";
        }
    }

    /// <summary>
    /// If a player has been involved in a PK battle this recently,
    /// logging off leaves their character in a frozen state for 20 seconds
    /// </summary>
    public static TimeSpan PKLogoffTimer = TimeSpan.FromMinutes(2);

    public void UpdatePKTimer()
    {
        //log.Info($"Updating PK timer for {Name}");

        LastPkAttackTimestamp = Time.GetUnixTime();
    }

    /// <summary>
    /// Called when a successful attack is landed in PVP
    /// The timestamp for both PKs are updated
    ///
    /// If a physical attack is evaded, or a magic spell is resisted,
    /// this function should NOT be called.
    /// </summary>
    public static void UpdatePKTimers(Player attacker, Player defender)
    {
        if (attacker == defender)
        {
            return;
        }

        if (
            attacker.PlayerKillerStatus == PlayerKillerStatus.Free
            || defender.PlayerKillerStatus == PlayerKillerStatus.Free
        )
        {
            return;
        }

        attacker.UpdatePKTimer();
        defender.UpdatePKTimer();
    }

    public bool PKTimerActive =>
        IsPKType && Time.GetUnixTime() - LastPkAttackTimestamp < PropertyManager.GetLong("pk_timer").Item;

    public bool PKLogoutActive => IsPKType && Time.GetUnixTime() - LastPkAttackTimestamp < PKLogoffTimer.TotalSeconds;

    public bool IsPKType =>
        PlayerKillerStatus == PlayerKillerStatus.PK || PlayerKillerStatus == PlayerKillerStatus.PKLite;

    public bool IsPK => PlayerKillerStatus == PlayerKillerStatus.PK;

    public bool IsPKL => PlayerKillerStatus == PlayerKillerStatus.PKLite;

    public bool IsNPK => PlayerKillerStatus == PlayerKillerStatus.NPK;

    public bool CheckHouseRestrictions(Player player)
    {
        if (Location.Cell == player.Location.Cell)
        {
            return true;
        }

        // dealing with outdoor cell equivalents at this point, if applicable
        var cell = (CurrentLandblock?.IsDungeon ?? false) ? Location.Cell : Location.GetOutdoorCell();
        var playerCell =
            (player.CurrentLandblock?.IsDungeon ?? false) ? player.Location.Cell : player.Location.GetOutdoorCell();

        if (cell == playerCell)
        {
            return true;
        }

        HouseCell.HouseCells.TryGetValue(cell, out var houseGuid);
        HouseCell.HouseCells.TryGetValue(playerCell, out var playerHouseGuid);

        // pass if both of these players aren't in a house cell
        if (houseGuid == 0 && playerHouseGuid == 0)
        {
            return true;
        }

        var houses = new HashSet<House>();
        CheckHouseRestrictions_GetHouse(houseGuid, houses);
        player.CheckHouseRestrictions_GetHouse(playerHouseGuid, houses);

        foreach (var house in houses)
        {
            if (!house.HasPermission(this) || !house.HasPermission(player))
            {
                return false;
            }
        }
        return true;
    }

    public void CheckHouseRestrictions_GetHouse(uint houseGuid, HashSet<House> houses)
    {
        if (houseGuid == 0)
        {
            return;
        }

        var house = CurrentLandblock.GetObject(houseGuid) as House;
        if (house != null)
        {
            var rootHouse = house.LinkedHouses.Count > 0 ? house.LinkedHouses[0] : house;

            if (rootHouse.HouseOwner == null || rootHouse.OpenStatus || houses.Contains(rootHouse))
            {
                return;
            }

            //Console.WriteLine($"{Name}.CheckHouseRestrictions_GetHouse({houseGuid:X8}): found root house {house.Name} ({house.HouseId})");
            houses.Add(rootHouse);
        }
        else
        {
            _log.Error(
                $"{Name}.CheckHouseRestrictions_GetHouse({houseGuid:X8}): couldn't find house from {CurrentLandblock.Id.Raw:X8}"
            );
        }
    }

    /// <summary>
    /// Returns the damage type for the currently equipped weapon / ammo
    /// </summary>
    /// <param name="multiple">If true, returns all of the damage types for the weapon</param>
    public override DamageType GetDamageType(bool multiple = false, CombatType? combatType = null)
    {
        // player override
        if (combatType == null)
        {
            combatType = GetCombatType();
        }

        var weapon = GetEquippedWeapon();
        var ammo = GetEquippedAmmo();

        if (weapon == null && combatType == CombatType.Melee)
        {
            // handle gauntlets/ boots
            if (AttackType == AttackType.Punch)
            {
                weapon = HandArmor;
            }
            else if (AttackType == AttackType.Kick)
            {
                weapon = FootArmor;
            }
            else
            {
                _log.Warning($"{Name}.GetDamageType(): no weapon, AttackType={AttackType}");
                return DamageType.Undef;
            }

            if (weapon != null && weapon.W_DamageType == DamageType.Undef)
            {
                return DamageType.Bludgeon;
            }
        }

        if (weapon == null)
        {
            return DamageType.Bludgeon;
        }

        var damageSource = combatType == CombatType.Melee || ammo == null || !weapon.IsAmmoLauncher ? weapon : ammo;

        var damageType = damageSource.W_DamageType;

        if (damageType == DamageType.Undef)
        {
            _log.Warning(
                $"{Name}.GetDamageType(): {damageSource} ({damageSource.Guid}, {damageSource.WeenieClassId}): no DamageType"
            );
            return DamageType.Bludgeon;
        }

        // return multiple damage types
        if (multiple || !damageType.IsMultiDamage())
        {
            return damageType;
        }

        switch (damageType)
        {
            // get single damage type
            case DamageType.Pierce | DamageType.Slash when (AttackType & AttackType.Punches) != 0:
            {
                return SlashThrustToggle ? DamageType.Pierce : DamageType.Slash;
            }
            case DamageType.Pierce | DamageType.Slash when (AttackType & AttackType.Thrusts) != 0:
                return DamageType.Pierce;
            case DamageType.Pierce | DamageType.Slash:
                return DamageType.Slash;
            case DamageType.Slash | DamageType.Fire:
                return SlashThrustToggle ? DamageType.Slash : DamageType.Fire;
            default:
            {
                var powerLevel = combatType == CombatType.Melee ? (float?)PowerLevel : null;

                return damageType.SelectDamageType(powerLevel);
            }
        }
    }

    public WorldObject HandArmor =>
        EquippedObjects.Values.FirstOrDefault(i => (i.ClothingPriority & CoverageMask.Hands) > 0);

    public WorldObject FootArmor =>
        EquippedObjects.Values.FirstOrDefault(i => (i.ClothingPriority & CoverageMask.Feet) > 0);

    /// <summary>
    /// Determines if player can damage a target via PlayerKillerStatus
    /// </summary>
    /// <returns>null if no errors, else pk error list</returns>
    public override List<WeenieErrorWithString> CheckPKStatusVsTarget(WorldObject target, Spell spell)
    {
        if (target == null || target == this)
        {
            return null;
        }

        var targetCreature = target as Creature;
        if (targetCreature == null && target.WielderId != null)
        {
            // handle casting item spells
            targetCreature = CurrentLandblock.GetObject(target.WielderId.Value) as Creature;
        }
        if (targetCreature == null)
        {
            return null;
        }

        if (
            PlayerKillerStatus == PlayerKillerStatus.Free
            || targetCreature.PlayerKillerStatus == PlayerKillerStatus.Free
        )
        {
            return null;
        }

        var targetPlayer = target as Player;

        if (targetPlayer != null)
        {
            if (spell == null || spell.IsHarmful)
            {
                // Ensure that a non-PK cannot cast harmful spells on another player
                if (PlayerKillerStatus == PlayerKillerStatus.NPK)
                {
                    return new List<WeenieErrorWithString>()
                    {
                        WeenieErrorWithString.YouFailToAffect_YouAreNotPK,
                        WeenieErrorWithString._FailsToAffectYou_TheyAreNotPK
                    };
                }

                if (targetPlayer.PlayerKillerStatus == PlayerKillerStatus.NPK)
                {
                    return new List<WeenieErrorWithString>()
                    {
                        WeenieErrorWithString.YouFailToAffect_TheyAreNotPK,
                        WeenieErrorWithString._FailsToAffectYou_YouAreNotPK
                    };
                }

                // Ensure not attacking across housing boundary
                if (!CheckHouseRestrictions(targetPlayer))
                {
                    return new List<WeenieErrorWithString>()
                    {
                        WeenieErrorWithString.YouFailToAffect_AcrossHouseBoundary,
                        WeenieErrorWithString._FailsToAffectYouAcrossHouseBoundary
                    };
                }
            }

            // additional checks for different PKTypes
            if (PlayerKillerStatus != targetPlayer.PlayerKillerStatus)
            {
                // require same pk status, unless beneficial spell being cast on NPK
                // https://asheron.fandom.com/wiki/Player_Killer
                // https://asheron.fandom.com/wiki/Player_Killer_Lite

                if (spell == null || spell.IsHarmful || targetPlayer.PlayerKillerStatus != PlayerKillerStatus.NPK)
                {
                    return new List<WeenieErrorWithString>()
                    {
                        WeenieErrorWithString.YouFailToAffect_NotSamePKType,
                        WeenieErrorWithString._FailsToAffectYou_NotSamePKType
                    };
                }
            }
        }
        else
        {
            // if monster has a non-default pk status, ensure pk types match up
            if (
                targetCreature.PlayerKillerStatus != PlayerKillerStatus.NPK
                && PlayerKillerStatus != targetCreature.PlayerKillerStatus
            )
            {
                return new List<WeenieErrorWithString>()
                {
                    WeenieErrorWithString.YouFailToAffect_NotSamePKType,
                    WeenieErrorWithString._FailsToAffectYou_NotSamePKType
                };
            }
        }
        return null;
    }

    private CombatAbility GetPlayerCombatAbility(Player player)
    {
        var playerCombatAbility = CombatAbility.None;

        var playerCombatFocus = player.GetEquippedCombatFocus();
        if (playerCombatFocus != null)
        {
            playerCombatAbility = playerCombatFocus.GetCombatAbility();
        }

        return playerCombatAbility;
    }

    public bool IsBehindTargetCreature(Creature targetCreature)
    {
        if (targetCreature is null)
        {
            return false;
        }

        var angle = Math.Abs(targetCreature.GetAngle(this));

        if (angle > 90)
        {
            return true;
        }

        return false;
    }
}
