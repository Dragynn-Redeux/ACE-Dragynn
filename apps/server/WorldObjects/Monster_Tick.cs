using ACE.Entity.Enum;
using System;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    protected const double monsterTickInterval = 0.2;
    protected const double monsterThreatTickInterval = 1.0;
    protected const double monsterTargetScanInterval = 2.0;
    public double NextMonsterTickTime;
    public double NextMonsterThreatTickTime;
    public double NextMonsterTargetScanTime;
    private bool firstUpdate = true;
    private const bool DebugPatrolTargets = false;
    /// <summary>
    /// Primary dispatch for monster think
    /// </summary>
    public void Monster_Tick(double currentUnixTime)
    {
        if (HasPatrol)
        {
            IsAwake = true;

            if (MonsterState == State.Return)
            {
                MonsterState = State.Awake;
            }
        }

        if (IsChessPiece && this is GamePiece gamePiece)
        {
            // faster than vtable?
            gamePiece.Tick(currentUnixTime);
            return;
        }

        if (IsPassivePet && this is Pet pet)
        {
            pet.Tick(currentUnixTime);
            return;
        }

        NextMonsterTickTime = currentUnixTime + monsterTickInterval;

        if (!IsAwake)
        {
            if (MonsterState == State.Return)
            {
                MonsterState = State.Idle;
            }
            // Periodic scan for stationary players
            if (currentUnixTime > NextMonsterTargetScanTime)
            {
                PeriodicTargetScan();
                NextMonsterTargetScanTime = currentUnixTime + monsterTargetScanInterval;
            }

            if (IsFactionMob || HasFoeType)
            {
                FactionMob_CheckMonsters();
            }
            return;
        }

        if (IsDead)
        {
            return;
        }
        // If we're busy running an emote or finishing an animation, do not advance patrol.
        // This gives a small "refocus" window and prevents moving while emoting.
        if (EmoteManager.IsBusy)
        {
            if (HasPatrol && AttackTarget == null)
            {
                // If an emote (Use, etc.) begins while moving, hard stop so we don't "skate".
                if (IsMoving)
                {
                    CancelMoveToForEmote();
                    PatrolResetDestination();
                }
                // Busy means stop; patrol resumes after emote finishes.
            }
            return;
        }

        HandlePlayerCountScaling();
        HandleFindTarget();
        // If combat starts, drop any in-flight patrol destination so we don't stall after combat
        if (HasPatrol && AttackTarget != null)
        {
            PatrolResetDestination();
        }
        // Patrol: prevent getting stuck targeting a dead creature (required so patrol can resume)
        if (HasPatrol && AttackTarget is Creature at && at.IsDead)
        {
            if (DebugPatrolTargets && WeenieClassId == 2036553)
            {
                Console.WriteLine(
                    $"[PATROL][CLEAR_TARGET] {Name} {Guid} -> {at.Name} {at.Guid} IsDead={at.IsDead} Attackable={at.Attackable}"
                );
            }

            AttackTarget = null;
            CurrentAttack = null;
            firstUpdate = true;
            // Patrolling creatures bypass Sleep(); ensure we leave combat stance when combat ends.
            if (CombatMode != CombatMode.NonCombat)
            {
                SetCombatMode(CombatMode.NonCombat);
            }
            // Prevent re-acquiring the same dead target via threat/retaliate bookkeeping
            ClearRetaliateTargets();
            // If combat interrupted an in-flight patrol MoveTo, drop the stale destination
            PatrolResetDestination();
        }

        if (!HasPatrol)
        {
            CheckMissHome(); // tickrate?
        }

        if (currentUnixTime > NextMonsterThreatTickTime)
        {
            TickDownAllTargetThreatLevels();

            NextMonsterThreatTickTime = currentUnixTime + monsterThreatTickInterval;
        }

        if (AttackTarget == null)
        {
            if (HasPatrol)
            {
                if (MonsterState == State.Return)
                {
                    MonsterState = State.Awake;
                }

                PatrolUpdate(currentUnixTime);
                return;
            }

            if (MonsterState != State.Return)
            {
                Sleep();
                return;
            }
        }

        if (MonsterState == State.Return)
        {
            Movement();
            return;
        }

        var combatPet = this as CombatPet;
        var creatureTarget = AttackTarget as Creature;
        var playerTarget = AttackTarget as Player;

        if (playerTarget != null && playerTarget.IsStealthed)
        {
            if (IsDirectVisible(playerTarget))
            {
                playerTarget.TestStealth(this, $"{Name} detects you! You lose stealth.");
            }
        }

        if (
            creatureTarget != null && (creatureTarget.IsDead || (combatPet == null && !IsVisibleTarget(creatureTarget)))
            || (playerTarget != null && playerTarget.IsStealthed)
        )
        {
            FindNextTarget(false);
            return;
        }

        if (firstUpdate)
        {
            if (CurrentMotionState.Stance == MotionStance.NonCombat)
            {
                DoAttackStance();
            }

            if (IsAnimating)
            {
                //PhysicsObj.ShowPendingMotions();
                PhysicsObj.update_object();
                return;
            }

            firstUpdate = false;
        }

        // select a new weapon if missile launcher is out of ammo
        var weapon = GetEquippedWeapon();
        /*if (weapon != null && weapon.IsAmmoLauncher)
        {
            var ammo = GetEquippedAmmo();
            if (ammo == null)
                SwitchToMeleeAttack();
        }*/

        if (weapon == null && CurrentAttack != null && CurrentAttack == CombatType.Missile)
        {
            EquipInventoryItems(true, false, true, false);
            DoAttackStance();
            CurrentAttack = null;
        }

        // decide current type of attack
        if (CurrentAttack == null)
        {
            CurrentAttack = GetNextAttackType();
            MaxRange = GetMaxRange();

            //if (CurrentAttack == AttackType.Magic)
            //MaxRange = MaxMeleeRange;   // FIXME: server position sync
        }

        if (PhysicsObj.IsSticky)
        {
            UpdatePosition(false);
        }

        // get distance to target
        var targetDist = GetDistanceToTarget();
        //Console.WriteLine($"{Name} ({Guid}) - Dist: {targetDist}");

        if (CurrentAttack != CombatType.Missile)
        {
            if (targetDist > MaxRange || (!IsFacing(AttackTarget) && !IsSelfCast()))
            {
                // turn / move towards
                if (!IsTurning && !IsMoving)
                {
                    StartTurn();
                }
                else
                {
                    if (
                        CurrentAttack == CombatType.Melee
                        && targetDist > 20
                        && HasRangedWeapon
                        && !SwitchWeaponsPending
                        && LastWeaponSwitchTime + 5 < currentUnixTime
                    )
                    {
                        TrySwitchToMissileAttack();
                    }
                    else
                    {
                        Movement();
                    }
                }
            }
            else
            {
                // perform attack
                if (AttackReady())
                {
                    Attack();
                }
            }
        }
        else
        {
            if (IsTurning || IsMoving)
            {
                Movement();
                return;
            }

            if (!IsFacing(AttackTarget))
            {
                StartTurn();
            }
            else if (targetDist <= MaxRange)
            {
                // perform attack
                if (AttackReady())
                {
                    Attack();
                }
            }
            else
            {
                // monster switches to melee combat immediately,
                // if target is beyond max range?

                // should ranged mobs only get CurrentTargets within MaxRange?
                //Console.WriteLine($"{Name}.MissileAttack({AttackTarget.Name}): targetDist={targetDist}, MaxRange={MaxRange}, switching to melee");
                TrySwitchToMeleeAttack();
            }
        }

        // pets drawing aggro
        if (combatPet != null)
        {
            combatPet.PetCheckMonsters();
        }
    }
}
