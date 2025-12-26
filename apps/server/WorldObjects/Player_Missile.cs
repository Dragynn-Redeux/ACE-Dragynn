using System;
using System.Numerics;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics.Animation;
using MotionCommand = ACE.Entity.Enum.MotionCommand;

namespace ACE.Server.WorldObjects;

partial class Player
{
    private float _accuracyLevel;

    public float AccuracyLevel
    {
        get => IsExhausted ? 0.0f : _accuracyLevel;
        set => _accuracyLevel = value;
    }

    private Creature _missileTarget;

    private PowerAccuracy GetAccuracyRange()
    {
        switch (AccuracyLevel)
        {
            case < 0.33f:
                return PowerAccuracy.Low;
            case < 0.66f:
                return PowerAccuracy.Medium;
            default:
                return PowerAccuracy.High;
        }
    }

    /// <summary>
    /// Called by network packet handler 0xA - GameActionTargetedMissileAttack
    /// </summary>
    /// <param name="targetGuid">The target guid</param>
    /// <param name="attackHeight">The attack height 1-3</param>
    /// <param name="accuracyLevel">The 0-1 accuracy bar level</param>
    public void HandleActionTargetedMissileAttack(uint targetGuid, uint attackHeight, float accuracyLevel)
    {
        //log.Info($"-");

        if (CombatMode != CombatMode.Missile)
        {
            _log.Error(
                $"{Name}.HandleActionTargetedMissileAttack({targetGuid:X8}, {attackHeight}, {accuracyLevel}) - CombatMode mismatch {CombatMode}, LastCombatMode: {LastCombatMode}"
            );

            if (LastCombatMode == CombatMode.Missile)
            {
                CombatMode = CombatMode.Missile;
            }
            else
            {
                OnAttackDone();
                return;
            }
        }

        if (IsBusy || Teleporting || suicideInProgress)
        {
            SendWeenieError(WeenieError.YoureTooBusy);
            OnAttackDone();
            return;
        }

        if (IsJumping)
        {
            SendWeenieError(WeenieError.YouCantDoThatWhileInTheAir);
            OnAttackDone();
            return;
        }

        if (PKLogout)
        {
            SendWeenieError(WeenieError.YouHaveBeenInPKBattleTooRecently);
            OnAttackDone();
            return;
        }

        var weapon = GetEquippedMissileWeapon();
        var ammo = GetEquippedAmmo();

        // sanity check
        accuracyLevel = Math.Clamp(accuracyLevel, 0.0f, 1.0f);

        if (weapon == null || weapon.IsAmmoLauncher && ammo == null)
        {
            OnAttackDone();
            return;
        }

        AttackHeight = (AttackHeight)attackHeight;
        AttackQueue.Add(accuracyLevel);

        if (_missileTarget == null)
        {
            AccuracyLevel = accuracyLevel; // verify
        }

        // get world object of target guid
        var target = CurrentLandblock?.GetObject(targetGuid) as Creature;
        if (target == null || target.Teleporting)
        {
            //log.Warn($"{Name}.HandleActionTargetedMissileAttack({targetGuid:X8}, {AttackHeight}, {accuracyLevel}) - couldn't find creature target guid");
            OnAttackDone();
            return;
        }

        if (Attacking || _missileTarget != null && _missileTarget.IsAlive)
        {
            return;
        }

        if (!CanDamage(target))
        {
            SendTransientError($"You cannot attack {target.Name}");
            OnAttackDone();
            return;
        }

        //log.Info($"{Name}.HandleActionTargetedMissileAttack({targetGuid:X8}, {attackHeight}, {accuracyLevel})");

        AttackTarget = target;
        _missileTarget = target;

        var attackSequence = ++AttackSequence;

        // record stance here and pass it along
        // accounts for odd client behavior with swapping bows during repeat attacks
        var motionStance = CurrentMotionState.Stance;

        // turn if required
        var rotateTime = Rotate(target);
        var actionChain = new ActionChain();

        var delayTime = rotateTime;
        if (NextRefillTime > DateTime.UtcNow.AddSeconds(delayTime))
        {
            delayTime = (float)(NextRefillTime - DateTime.UtcNow).TotalSeconds;
        }

        actionChain.AddDelaySeconds(delayTime);

        // do missile attack
        actionChain.AddAction(this, () => LaunchMissile(target, attackSequence, motionStance));
        actionChain.EnqueueChain();
    }

    /// <summary>
    /// Launches a missile attack from player to target
    /// </summary>
    private void LaunchMissile(WorldObject target, int attackSequence, MotionStance motionStance, bool subsequent = false)
    {
        if (AttackSequence != attackSequence)
        {
            return;
        }

        var weapon = GetEquippedMissileWeapon();
        if (weapon == null || CombatMode == CombatMode.NonCombat)
        {
            OnAttackDone();
            return;
        }

        var ammo = weapon.IsAmmoLauncher ? GetEquippedAmmo() : weapon;
        if (ammo == null)
        {
            OnAttackDone();
            return;
        }

        var launcher = GetEquippedMissileLauncher();

        var creature = target as Creature;
        if (!IsAlive || IsBusy || _missileTarget == null || creature == null || !creature.IsAlive || suicideInProgress)
        {
            OnAttackDone();
            return;
        }

        if (!TargetInRange(target))
        {
            // this must also be sent to actually display the transient message
            SendWeenieError(WeenieError.MissileOutOfRange);

            // this prevents the accuracy bar from refilling when 'repeat attacks' is enabled
            OnAttackDone();

            return;
        }

        var actionChain = new ActionChain();

        if (subsequent && !IsFacing(target))
        {
            var rotateTime = Rotate(target);
            actionChain.AddDelaySeconds(rotateTime);
        }

        // launch animation
        // point of no return beyond this point -- cannot be cancelled
        actionChain.AddAction(this, () => Attacking = true);

        // if (subsequent)
        // {
        //     // client shows hourglass, until attack done is received
        //     // retail only did this for subsequent attacks w/ repeat attacks on
        //     Session.Network.EnqueueSend(new GameEventCombatCommenceAttack(Session));
        // }

        var projectileSpeed = GetProjectileSpeed();

        // get z-angle for aim motion
        var aimVelocity = GetAimVelocity(target, projectileSpeed);

        var aimLevel = GetAimLevel(aimVelocity);

        // calculate projectile spawn pos and velocity
        var localOrigin = GetProjectileSpawnOrigin(ammo.WeenieClassId, aimLevel);

        var velocity = CalculateProjectileVelocity(
            localOrigin,
            target,
            projectileSpeed,
            out var origin,
            out var orientation
        );

        //Console.WriteLine($"Velocity: {velocity}");

        if (velocity == Vector3.Zero)
        {
            // pre-check succeeded, but actual velocity calculation failed
            SendWeenieError(WeenieError.MissileOutOfRange);

            // this prevents the accuracy bar from refilling when 'repeat attacks' is enabled
            Attacking = false;
            OnAttackDone();
            return;
        }

        var launchTime = EnqueueMotionPersist(actionChain, aimLevel);

        // launch projectile
        actionChain.AddAction(
            this,
            () =>
            {
                // handle self-procs
                TryProcEquippedItems(this, this, true, weapon);

                var sound = GetLaunchMissileSound(weapon);
                EnqueueBroadcast(new GameMessageSound(Guid, sound, 1.0f));

                // stamina usage
                // TODO: ensure enough stamina for attack
                // TODO: verify formulas - double/triple cost for bow/xbow?


                var projectile = LaunchProjectile(launcher, ammo, target, origin, orientation, velocity);
                UpdateAmmoAfterLaunch(ammo);

                if (IsStealthed)
                {
                    EndStealth(null, true);
                }

                // Check for missile cleaves
                var numCleaves = 0;

                numCleaves += CheckForRatingSlashCleaveBonus(ammo);

                var cleaveTargets = creature.GetNearbyMonsters(10);

                if (MultiShotIsActive && GetPowerAccuracyBar() >= 0.5f && cleaveTargets.Count > 1)
                {
                    if (cleaveTargets.Count > 2)
                    {
                        MultishotNumTargets = 3;
                        numCleaves += 2;
                    }
                    else
                    {
                        MultishotNumTargets = 2;
                        numCleaves += 1;
                    }
                }

                var cleaveCount = 0;

                foreach (var cleave in cleaveTargets)
                {
                    if (cleaveCount == numCleaves)
                    {
                        break;
                    }

                    if (cleave.Translucency == 1 || cleave.Visibility)
                    {
                        continue;
                    }

                    var ammoCheck = weapon.IsAmmoLauncher ? GetEquippedAmmo() : weapon;
                    if (ammoCheck == null)
                    {
                        OnAttackDone();
                        return;
                    }

                    var angle = GetAngle(cleave);
                    var angleWrong = Math.Abs(angle) > CleaveAngle / 2.0f;

                    if (angleWrong && !MultiShotIsActive) // Multishot can hit targets behind you (if within the 10 range limit)
                    {
                        continue;
                    }

                    var newaimVelocity = GetAimVelocity(cleave, projectileSpeed);

                    var newaimLevel = GetAimLevel(newaimVelocity);

                    var newlocalOrigin = GetProjectileSpawnOrigin(ammo.WeenieClassId, newaimLevel);

                    var newvelocity = CalculateProjectileVelocity(
                        newlocalOrigin,
                        cleave,
                        projectileSpeed,
                        out var neworigin,
                        out var neworientation
                    );

                    var bonusProjectile = LaunchProjectile(
                        launcher,
                        ammo,
                        cleave,
                        neworigin,
                        neworientation,
                        newvelocity
                    );

                    UpdateAmmoAfterLaunch(ammo);

                    cleaveCount++;
                }
            }
        );

        // ammo remaining?
        if (!ammo.UnlimitedUse && (ammo.StackSize == null || ammo.StackSize <= 1))
        {
            actionChain.AddAction(
                this,
                () =>
                {
                    Session.Network.EnqueueSend(
                        new GameEventCommunicationTransientString(Session, "You are out of ammunition!")
                    );
                    SetCombatMode(CombatMode.NonCombat);
                    Attacking = false;
                    OnAttackDone();
                }
            );

            actionChain.EnqueueChain();
            return;
        }

        // reload animation
        var animSpeed = GetAnimSpeed(target as Creature);
        var reloadTime = EnqueueMotionPersist(actionChain, motionStance, MotionCommand.Reload, animSpeed);

        // reset for next projectile
        EnqueueMotionPersist(actionChain, motionStance, MotionCommand.Ready);
        var linkTime = MotionTable.GetAnimationLength(MotionTableId, motionStance, MotionCommand.Reload, MotionCommand.Ready);
        //var cycleTime = MotionTable.GetCycleLength(MotionTableId, CurrentMotionState.Stance, MotionCommand.Ready);

        LastAttackAnimationLength = (linkTime+launchTime+reloadTime) / animSpeed;
        //Console.WriteLine($"LaunchTime: {launchTime}, Reload: {reloadTime} (BaseReload: {reloadTime*animSpeed}), Link: {linkTime}, TOTAL: {LastAttackAnimationLength}");

        var staminaCost = GetAttackStamina((float)LastAttackAnimationLength, weapon);
        UpdateVitalDelta(Stamina, -staminaCost);

        if (Stamina.Current < 1 && EvasiveStanceIsActive)
        {
            EvasiveStanceIsActive = false;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"Your fall out of your evasive stance!!", ChatMessageType.Broadcast)
            );

            var evasiveStanceItem = GetInventoryItemsOfWCID(1051114);
            if (evasiveStanceItem.Count > 0)
            {
                EnchantmentManager.StartCooldown(evasiveStanceItem[0]);
            }
        }

        CheckForFurySelfDamage(staminaCost);

        actionChain.AddAction(
            this,
            () =>
            {
                if (CombatMode == CombatMode.Missile)
                {
                    EnqueueBroadcast(
                        new GameMessageParentEvent(
                            this,
                            ammo,
                            ACE.Entity.Enum.ParentLocation.RightHand,
                            ACE.Entity.Enum.Placement.RightHandCombat
                        )
                    );
                }
            }
        );

        actionChain.AddDelaySeconds(linkTime);

        actionChain.AddAction(
            this,
            () =>
            {
                Attacking = false;

                if (
                    creature.IsAlive
                    && GetCharacterOption(CharacterOption.AutoRepeatAttacks)
                    && !IsBusy
                    && !AttackCancelled
                )
                {
                    // client starts refilling accuracy bar
                    Session.Network.EnqueueSend(new GameEventAttackDone(Session));

                    AccuracyLevel = AttackQueue.Fetch();

                    // can be cancelled, but cannot be pre-empted with another attack
                    var nextAttack = new ActionChain();
                    var nextRefillTime = AccuracyLevel;

                    NextRefillTime = DateTime.UtcNow.AddSeconds(nextRefillTime);
                    nextAttack.AddDelaySeconds(nextRefillTime);

                    // perform next attack
                    nextAttack.AddAction(
                        this,
                        () =>
                        {
                            LaunchMissile(target, attackSequence, motionStance, true);
                        }
                    );
                    nextAttack.EnqueueChain();
                }
                else
                {
                    OnAttackDone();
                }
            }
        );

        actionChain.EnqueueChain();

        if (UnderLifestoneProtection)
        {
            LifestoneProtectionDispel();
        }
    }

    /// <summary>
    /// RATING - Slash: 1% chance per rating to gain +1 cleave target.
    /// (JEWEL - Imperial Topaz)
    /// </summary>
    private int CheckForRatingSlashCleaveBonus(WorldObject ammo)
    {
        if (ammo is not { W_DamageType: DamageType.Slash })
        {
            return 0;
        }

        var chance = Jewel.GetJewelEffectMod(this, PropertyInt.GearSlash);

        return ThreadSafeRandom.Next(0.0f, 1.0f) < chance ? 1 : 0;
    }

    // TODO: the damage pipeline currently uses the creature ammo instead of the projectile
    // for calculating damage. when the last arrow is launched, the player ammo will be null
    // give projectiles an owner, and have the damage pipeline take the actual damage source object
    // (ie. the arrow-in-flight, or a melee weapon)

    public override float GetAimHeight(WorldObject target)
    {
        if (AttackHeight == null)
        {
            return 2.0f;
        }

        switch (AttackHeight.Value)
        {
            case ACE.Entity.Enum.AttackHeight.High:
                return 1.0f;
            case ACE.Entity.Enum.AttackHeight.Medium:
                return 2.0f;
            //case AttackHeight.Low: return target.Height;
            case ACE.Entity.Enum.AttackHeight.Low:
                return 3.0f;
        }

        return 2.0f;
    }

    public override void UpdateAmmoAfterLaunch(WorldObject ammo)
    {
        //if (ammo.UnlimitedUse)
        //    return;

        // hide previously held ammo
        EnqueueBroadcast(new GameMessagePickupEvent(ammo));

        if (ammo is Ammunition {AmmoEffectUsesRemaining: not null} ammunition)
        {
            ammunition.AmmoEffectUsesRemaining -= 1;

            if (ammunition.AmmoEffectUsesRemaining < 1)
            {
                ammunition.AmmoEffectUsesRemaining = null;
            }
        }

        if (ammo.UnlimitedUse)
        {
            return;
        }

        if (ammo.StackSize == null || ammo.StackSize <= 1)
        {
            TryDequipObjectWithNetworking(ammo.Guid, out _, DequipObjectAction.ConsumeItem);
        }
        else
        {
            TryConsumeFromInventoryWithNetworking(ammo, 1);
        }
    }

    private bool TargetInRange(WorldObject target)
    {
        // 2d or 3d distance?
        var dist = Location.DistanceTo(target.Location);

        var maxRange = GetMaxMissileRange();

        return dist <= maxRange;
    }
}
