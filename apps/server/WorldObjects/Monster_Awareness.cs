using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACE.Common;
using ACE.Common.Extensions;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

/// <summary>
/// Determines when a monster wakes up from idle state
/// </summary>
partial class Creature
{
    /// <summary>
    /// Monsters wake up when players are in visual range
    /// </summary>
    public bool IsAwake = false;

    /// <summary>
    /// Transitions a monster from idle to awake state
    /// </summary>
    public void WakeUp(bool alertNearby = true)
    {
        MonsterState = State.Awake;
        IsAwake = true;
        //LastHeartbeatPosition = Location;
        LastAttackTime = Time.GetUnixTime();

        //DoAttackStance();
        EmoteManager.OnWakeUp(AttackTarget as Creature);
        EmoteManager.OnNewEnemy(AttackTarget as Creature);
        //SelectTargetingTactic();

        if (DeathTreasure != null)
        {
            var chance = ThreadSafeRandom.Next(1, 10);
            if (chance == 10)
            {
                var wo = WorldObjectFactory.CreateNewWorldObject(1020001);
                wo.Location = Location;
                wo.EnterWorld();
            }
        }

        if (alertNearby)
        {
            AlertFriendly();
        }
    }

    /// <summary>
    /// Transitions a monster from awake to idle state
    /// </summary>
    protected virtual void Sleep()
    {
        if (HasPatrol)
        {
            return;
        }

        if (DebugMove)
        {
            Console.WriteLine($"{Name} ({Guid}).Sleep()");
        }

        SetCombatMode(CombatMode.NonCombat);

        CurrentAttack = null;
        firstUpdate = true;
        AttackTarget = null;
        IsAwake = false;
        IsMoving = false;
        MonsterState = State.Idle;

        PhysicsObj.CachedVelocity = Vector3.Zero;

        ClearRetaliateTargets();
    }

    public Tolerance Tolerance
    {
        get => (Tolerance)(GetProperty(PropertyInt.Tolerance) ?? 0);
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyInt.Tolerance);
            }
            else
            {
                SetProperty(PropertyInt.Tolerance, (int)value);
            }
        }
    }

    /// <summary>
    /// This list of possible targeting tactics for this monster
    /// </summary>
    public TargetingTactic TargetingTactic
    {
        get => (TargetingTactic)(GetProperty(PropertyInt.TargetingTactic) ?? 0);
        set
        {
            if (value == 0)
            {
                RemoveProperty(PropertyInt.TargetingTactic);
            }
            else
            {
                // FIX: write the incoming value, not the property again
                SetProperty(PropertyInt.TargetingTactic, (int)value);
            }
        }
    }

    /// <summary>
    /// The current targeting tactic for this monster
    /// </summary>
    private TargetingTactic CurrentTargetingTactic;

    private void SelectTargetingTactic()
    {
        // monsters have multiple targeting tactics, ex. Focused | Random

        // when should this function be called?
        // when a monster spawns in, does it choose 1 TargetingTactic?

        // or do they randomly select a TargetingTactic from their list of possible tactics,
        // each time they go to find a new target?

        //Console.WriteLine($"{Name}.TargetingTactics: {TargetingTactic}");

        // if targeting tactic is none,
        // use the most common targeting tactic
        // TODO: ensure all monsters in the db have a targeting tactic
        var targetingTactic = TargetingTactic;
        if (targetingTactic == TargetingTactic.None)
        {
            targetingTactic = TargetingTactic.Random | TargetingTactic.TopDamager;
        }

        var possibleTactics = EnumHelper.GetFlags(targetingTactic);
        var rng = ThreadSafeRandom.Next(1, possibleTactics.Count - 1);

        CurrentTargetingTactic = (TargetingTactic)possibleTactics[rng];

        //Console.WriteLine($"{Name}.TargetingTactic: {CurrentTargetingTactic}");
    }

    private double NextFindTarget;

    protected virtual void HandleFindTarget()
    {
        // Only *non-combat* passive objectives never acquire targets (eg crates)
        if (GeneratesPassiveThreat && TargetingTactic == TargetingTactic.None)
        {
            return;
        }

        if (Timers.RunningTime < NextFindTarget)
        {
            return;
        }

        FindNextTarget(false);
    }


    private void SetNextTargetTime()
    {
        // Default monster cadence
        var next = 5.0;

        if (HasPatrol)
        {
            var patrolScan = GetProperty(PropertyFloat.PatrolScanInterval);
            if (patrolScan != null && patrolScan.Value > 0.05f)
            {
                next = patrolScan.Value;
            }
            else
            {
                next = 1.0;
            }
        }

        NextFindTarget = Timers.RunningTime + next;
    }

    private bool DebugThreatSystem
    {
        get => PropertyManager.GetBool("debug_threat_system").Item;
    }
    private readonly PassiveThreatController _passiveThreatController = new();
    private const int ThreatMinimum = 100;
    private double ThreatGainedSinceLastTick = 1;

    public bool GeneratesPassiveThreat =>
        GetProperty(PropertyBool.GeneratesPassiveThreat) ?? false;

    public int PassiveThreatThreshold =>
        GetProperty(PropertyInt.PassiveThreatThreshold) ?? 0;

    private Dictionary<Creature, int> ThreatLevel;
    public Dictionary<Creature, float> PositiveThreat;
    public Dictionary<Creature, float> NegativeThreat;

    private void EnsureThreatCollections()
    {
        ThreatLevel ??= new Dictionary<Creature, int>();
        PositiveThreat ??= new Dictionary<Creature, float>();
        NegativeThreat ??= new Dictionary<Creature, float>();
    }

    // FIX: avoid C# collection expression [] (not supported in many ACE builds)
    public List<Player> SkipThreatFromNextAttackTargets = new();
    public List<Player> DoubleThreatFromNextAttackTargets = new();
    public void IncreaseTargetThreatLevel(Creature targetCreature, int amount)
    {
        EnsureThreatCollections();

        var modifiedAmount = Convert.ToSingle(amount);

        if (targetCreature is Player targetPlayer)
        {
            // abilities
            if (targetPlayer.ProvokeIsActive && targetPlayer.GetPowerAccuracyBar() >= 0.5)
            {
                modifiedAmount *= 2.0f;
            }

            if (targetPlayer.SmokescreenIsActive && targetPlayer.GetPowerAccuracyBar() >= 0.5)
            {
                modifiedAmount *= 0.5f;
            }

            // sigils
            if (SkipThreatFromNextAttackTargets != null && SkipThreatFromNextAttackTargets.Contains(targetPlayer))
            {
                SkipThreatFromNextAttackTargets.Remove(targetPlayer);
                return;
            }

            if (DoubleThreatFromNextAttackTargets != null && DoubleThreatFromNextAttackTargets.Contains(targetPlayer))
            {
                DoubleThreatFromNextAttackTargets.Remove(targetPlayer);
                modifiedAmount *= 2.0f;
            }

            // jewels
            modifiedAmount *= 1.0f + Jewel.GetJewelEffectMod(targetPlayer, PropertyInt.GearThreatGain);
            modifiedAmount *= 1.0f - Jewel.GetJewelEffectMod(targetPlayer, PropertyInt.GearThreatReduction);
        }

        ThreatLevel.TryAdd(targetCreature, ThreatMinimum);

        amount = Convert.ToInt32(modifiedAmount);
        amount = amount < 2 ? 2 : amount;

        ThreatLevel[targetCreature] += amount;

        ThreatGainedSinceLastTick += amount;

        if (DebugThreatSystem)
        {
            Console.WriteLine($"{Name} threat increased towards {targetCreature.Name} by +{amount}");
        }
    }

    /// <summary>
    /// Every second, reduce threat levels towards each target by the total amount of threat gained since last tick divided by the number of targets.
    /// Minimum of amount of threat reduced is equal to 10% of total threat, above target minimums (100).
    /// </summary>
    private void TickDownAllTargetThreatLevels()
    {
        EnsureThreatCollections();
        PruneDeadThreatTargets();
        if (ThreatLevel.Count == 0)
        {
            return;
        }

        var totalThreat = 0;
        foreach (var kvp in ThreatLevel)
        {
            totalThreat += kvp.Value;
        }

        var minimumSubtraction = Math.Max((int)((totalThreat - ThreatMinimum * ThreatLevel.Count) * 0.1f), 1);

        var threatGained = (int)(ThreatGainedSinceLastTick / ThreatLevel.Count);

        threatGained = threatGained < minimumSubtraction ? minimumSubtraction : threatGained;

        foreach (var key in ThreatLevel.Keys)
        {
            if (ThreatLevel[key] > ThreatMinimum)
            {
                ThreatLevel[key] -= threatGained;
            }

            if (ThreatLevel[key] < ThreatMinimum)
            {
                ThreatLevel[key] = ThreatMinimum;
            }

            // Keep passive-threat targets at or above their baseline
            if (key.GeneratesPassiveThreat && key.PassiveThreatThreshold >= 2)
            {
                var passiveFloor = ThreatMinimum + key.PassiveThreatThreshold;
                if (ThreatLevel[key] < passiveFloor)
                {
                    ThreatLevel[key] = passiveFloor;
                }
            }
        }

        ThreatGainedSinceLastTick = 0;
    }
    private void PruneDeadThreatTargets()
    {
        EnsureThreatCollections();

        if (ThreatLevel.Count == 0)
        {
            return;
        }

        foreach (var kvp in ThreatLevel.ToList())
        {
            var target = kvp.Key;

            if (target == null || target.IsDead)
            {
                ThreatLevel.Remove(target);
                PositiveThreat?.Remove(target);
                NegativeThreat?.Remove(target);
            }
        }
    }


    public virtual bool FindNextTarget(bool onTakeDamage, Creature untargetablePlayer = null)
    {
        // stopwatch.Restart();
        try
        {
            if (HasPatrol)
            {
                SetNextTargetTime();
                return PatrolFindNextTarget();
            }

            SelectTargetingTactic();
            SetNextTargetTime();

            var visibleTargets = GetAttackTargets();

            if (visibleTargets.Count == 0)
            {
                if (MonsterState != State.Return)
                {
                    MoveToHome();
                }

                return false;
            }

            if (visibleTargets.Count > 1 && untargetablePlayer != null)
            {
                visibleTargets.Remove(untargetablePlayer);
            }

            if (untargetablePlayer is Player { VanishIsActive: true })
            {
                visibleTargets.Remove(untargetablePlayer);

                if (ThreatLevel != null && ThreatLevel.ContainsKey(untargetablePlayer))
                {
                    ThreatLevel.Remove(untargetablePlayer);
                }

                if (visibleTargets.Count == 0)
                {
                    MoveToHome();
                    return false;
                }
            }

            if (AttackTarget is Creature attackTargetCreature && GetDistance(AttackTarget) > VisualAwarenessRange)
            {
                ThreatLevel?.Remove(attackTargetCreature);
            }

            var prevAttackTarget = AttackTarget;

            var targetDistances = BuildTargetDistance(visibleTargets);

            // New Threat System
            if (!(UseLegacyThreatSystem ?? false))
            {
                EnsureThreatCollections();

                // Manage Threat Level list
                foreach (var targetCreature in visibleTargets)
                {
                    // skip targets that are already in list
                    if (ThreatLevel != null && ThreatLevel.ContainsKey(targetCreature))
                    {
                        continue;
                    }

                    // Add new visible targets to threat list
                    if (Name.Contains("Placeholder") || Name.Contains("Boss Watchdog"))
                    {
                        continue;
                    }

                    ThreatLevel?.Add(targetCreature, ThreatMinimum);
                }

                if (DebugThreatSystem)
                {
                    Console.WriteLine("--------------");
                    _log.Information("ThreatLevel list for {Name} ({WeenieClassId}):", Name, WeenieClassId);

                    if (ThreatLevel != null)
                    {
                        foreach (var targetCreature in ThreatLevel.Keys)
                        {
                            _log.Information("{Name}: {targetThreat}", targetCreature.Name,
                                ThreatLevel[targetCreature]);
                        }
                    }
                }

                // Apply passive threat sources (e.g., crates) so they influence selection math
                _passiveThreatController.ApplyPassiveThreatThreshold(
                    this,
                    visibleTargets,
                    ThreatLevel,
                    ThreatMinimum,
                    DebugThreatSystem,
                    _log
                );
                PruneDeadThreatTargets();

                if (ThreatLevel?.Count == 0)
                {
                    return false;
                }

                // Set potential threat value range based on 50% of highest player's threat
                var maxThreatValue = ThreatLevel?.Values.Max();

                if (maxThreatValue <= ThreatMinimum)
                {
                    AttackTarget = SelectWeightedDistance(targetDistances);
                }
                else
                {
                    if (maxThreatValue != null)
                    {
                        var minimumAggroRange = (int)(maxThreatValue * 0.5f);

                        // Add all player's witin the potential threat range to a new dictionary
                        var potentialTargetList = new Dictionary<Creature, int>();
                        var safeTargetList = new Dictionary<Creature, int>();

                        foreach (var targetCreature in ThreatLevel)
                        {
                            if (targetCreature.Value >= minimumAggroRange)
                            {
                                potentialTargetList.Add(targetCreature.Key, targetCreature.Value - minimumAggroRange);
                            }

                            if (targetCreature.Value < minimumAggroRange)
                            {
                                safeTargetList.Add(targetCreature.Key, targetCreature.Value);
                            }
                        }

                        if (DebugThreatSystem)
                        {
                            Console.WriteLine("\n");
                            _log.Information("Unsorted Potential Target List - {Name}:", Name);
                            foreach (var targetCreature in potentialTargetList.Keys)
                            {
                                _log.Information(
                                    "{Name}: {TargetThreat}",
                                    targetCreature.Name,
                                    potentialTargetList[targetCreature]
                                );
                            }
                        }

                        // Sort dictionary by threat value
                        var sortedPotentialTargetList = potentialTargetList
                            .OrderBy(x => x.Value)
                            .ToDictionary(x => x.Key, x => x.Value);
                        var sortedSafeTargetList = safeTargetList
                            .OrderBy(x => x.Value)
                            .ToDictionary(x => x.Key, x => x.Value);

                        if (DebugThreatSystem)
                        {
                            _log.Information("Sorted Potential Target List - {Name}:", Name);
                            foreach (var targetCreature in sortedPotentialTargetList.Keys)
                            {
                                _log.Information(
                                    "{Name}: {TargetThreat}",
                                    targetCreature.Name,
                                    sortedPotentialTargetList[targetCreature]
                                );
                            }
                        }

                        // Adjust values for each entry in the sorted list so that entry's value includes the sum of all previous values.
                        if (DebugThreatSystem)
                        {
                            _log.Information("Additive Threat Values - {Name}", Name);
                        }

                        var lastValue = 0;
                        foreach (var entry in sortedPotentialTargetList)
                        {
                            sortedPotentialTargetList[entry.Key] = entry.Value + lastValue;
                            lastValue = sortedPotentialTargetList[entry.Key];

                            if (DebugThreatSystem)
                            {
                                _log.Information(
                                    "{Name}: {Value}, Additive Value: {lastValue}",
                                    entry.Key.Name,
                                    entry.Value,
                                    lastValue
                                );
                            }
                        }

                        var sortedMaxValue = sortedPotentialTargetList.Values.Max();
                        var roll = ThreadSafeRandom.Next(1, sortedMaxValue);

                        if (DebugThreatSystem)
                        {
                            _log.Information(
                                "RollRange: {minimum} - {sortedMaxValue}, Roll: {roll}",
                                1,
                                sortedMaxValue,
                                roll
                            );
                        }

                        PositiveThreat.Clear();
                        var difference = 0;
                        foreach (var targetCreatureKey in sortedPotentialTargetList)
                        {
                            // Calculate chance to steal aggro, for Appraisal Threat Table
                            var creatureThreatValue = targetCreatureKey.Value - difference;
                            var chance = (float)(creatureThreatValue) / sortedMaxValue;
                            difference += creatureThreatValue;

                            PositiveThreat[targetCreatureKey.Key] = chance;

                            if (DebugThreatSystem)
                            {
                                _log.Information(
                                    "{Name} ThreatLevel: {creatureThreatValue}, ThreatRange: {Value}, Chance: {chance}",
                                    targetCreatureKey.Key.Name,
                                    creatureThreatValue,
                                    targetCreatureKey.Value,
                                    Math.Round(chance, 2)
                                );
                            }
                        }

                        foreach (var targetCreatureKey in sortedPotentialTargetList)
                        {
                            if (targetCreatureKey.Value > roll)
                            {
                                AttackTarget = targetCreatureKey.Key;
                                break;
                            }
                        }

                        NegativeThreat.Clear();
                        foreach (var targetCreatureKey in sortedSafeTargetList)
                        {
                            // Calculate percentile for Appraisal Threat Table
                            var percentile = targetCreatureKey.Value / minimumAggroRange;
                            NegativeThreat[targetCreatureKey.Key] = percentile - 1;
                        }
                    }
                    _passiveThreatController.UpdatePassiveLossStreaks(AttackTarget as Creature, ThreatLevel);
                    if (DebugThreatSystem)
                    {
                       _log.Information("SELECTED PLAYER: {Name}", AttackTarget.Name);
                    }
                }
            }
            else
            {
                if (onTakeDamage)
                {
                    return false;
                }

                switch (CurrentTargetingTactic)
                {
                    case TargetingTactic.None:
                        break;

                    case TargetingTactic.Random:
                        AttackTarget = SelectWeightedDistance(targetDistances);
                        break;

                    case TargetingTactic.Focused:
                        break;

                    case TargetingTactic.LastDamager:
                        var lastDamager = DamageHistory.LastDamager?.TryGetAttacker() as Creature;
                        if (lastDamager != null)
                        {
                            AttackTarget = lastDamager;
                        }
                        break;

                    case TargetingTactic.TopDamager:
                        var topDamager = DamageHistory.TopDamager?.TryGetAttacker() as Creature;
                        if (topDamager != null)
                        {
                            AttackTarget = topDamager;
                        }
                        break;

                    case TargetingTactic.Weakest:
                        var lowestLevel = visibleTargets.OrderBy(p => p.Level).FirstOrDefault();
                        AttackTarget = lowestLevel;
                        break;

                    case TargetingTactic.Strongest:
                        var highestLevel = visibleTargets.OrderByDescending(p => p.Level).FirstOrDefault();
                        AttackTarget = highestLevel;
                        break;

                    case TargetingTactic.Nearest:
                        var nearest = BuildTargetDistance(visibleTargets);
                        AttackTarget = nearest[0].Target;
                        break;
                }
            }

            var player = AttackTarget as Player;
            if (player != null && !Visibility && player.AddTrackedObject(this))
            {
                _log.Error(
                    $"Fixed invisible attacker on player {player.Name}. (Landblock:{CurrentLandblock.Id} - {Name} ({Guid})"
                );
            }

            if (visibleTargets.Count > 1 && player != null && player.IsAttemptingToDeceive)
            {
                var monsterPerception = GetCreatureSkill(Skill.Perception).Current;
                var playerDeception = player.GetCreatureSkill(Skill.Deception).Current;

                var skillCheck = SkillCheck.GetSkillChance(monsterPerception, playerDeception);
                var chanceToDeceive = skillCheck * 0.25f;

                if (
                    player.GetCreatureSkill(Skill.Deception).AdvancementClass == SkillAdvancementClass.Specialized
                    && visibleTargets.Count > 1
                )
                {
                    chanceToDeceive *= 2;
                }

                if (player is { SmokescreenIsActive: true })
                {
                    chanceToDeceive += 0.5f;
                }

                var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
                if (rng < chanceToDeceive)
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You successfully deceived {Name} into believing you aren't a threat! They don't attack you!",
                            ChatMessageType.Broadcast
                        )
                    );
                    FindNextTarget(false, player);
                }
                else
                {
                    player.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Your failed to deceive {Name} into believing you aren't a threat! They attack you!",
                            ChatMessageType.Broadcast
                        )
                    );
                }
            }

            if (AttackTarget != null && AttackTarget != prevAttackTarget)
            {
                EmoteManager.OnNewEnemy(AttackTarget);
            }

            return AttackTarget != null;
        }
        finally
        {
        }
    }

    /// <summary>
    /// Returns a list of attackable targets currently visible to this monster
    /// </summary>
    public List<Creature> GetAttackTargets()
    {
        var visibleTargets = new List<Creature>();

        // Start with the engine's normal "attack targets" list (players / pets / foes / etc.)
        var candidates = PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature().ToList();

        // Hard include: any visible creature that generates passive threat, even if it isn't in the normal
        // AttackTargets pipeline yet. These only become valid if they are already within attack range.
        foreach (var obj in PhysicsObj.ObjMaint.GetVisibleObjects(PhysicsObj.CurCell))
        {
            var c = obj.WeenieObj.WorldObject as Creature;
            if (c != null && c.GeneratesPassiveThreat && !candidates.Contains(c))
            {
                candidates.Add(c);
            }
        }

        foreach (var creature in candidates)
        {
            if (creature == null)
            {
                continue;
            }

            var allowPassiveThreat = creature.GeneratesPassiveThreat;

            // ensure attackable (unless passive threat target)
            if ((!allowPassiveThreat && !creature.Attackable && creature.TargetingTactic == TargetingTactic.None) || creature.Teleporting)
            {
                continue;
            }

            // check if player fooled this monster with vanish
            if (creature is Player player && IsPlayerVanished(player))
            {
                continue;
            }

            var distSq = PhysicsObj.get_distance_sq_to_object(creature.PhysicsObj, true);

            if (creature is Player targetPlayer && targetPlayer.TestStealth(this, distSq, $"{Name} sees you! You lose stealth."))
            {
                continue;
            }

            if (allowPassiveThreat)
            {
                // Passive-threat targets must be within "noticed" range to be considered.
                // Use VisualAwarenessRange so this can be tuned per-weenie via PropertyFloat.VisualAwarenessRange.
                if (distSq > VisualAwarenessRangeSq)
                {
                    continue;
                }
            }
            else
            {
                // Normal behavior: within detection/chase radius
                var chaseDistSq = creature == AttackTarget ? MaxChaseRangeSq : VisualAwarenessRangeSq;
                if (distSq > chaseDistSq)
                {
                    continue;
                }
            }

            // if this monster belongs to a faction,
            // ensure target does not belong to the same faction
            if (SameFaction(creature))
            {
                // unless they have been provoked
                if (!PhysicsObj.ObjMaint.RetaliateTargetsContainsKey(creature.Guid.Full))
                {
                    continue;
                }
            }

            // cannot switch AttackTargets with Tolerance.Target
            if (Tolerance.HasFlag(Tolerance.Target) && creature != AttackTarget)
            {
                continue;
            }

            // can only target other monsters with Tolerance.Monster -- cannot target players or combat pets
            if (Tolerance.HasFlag(Tolerance.Monster) && (creature is Player || creature is CombatPet))
            {
                continue;
            }

            visibleTargets.Add(creature);
        }

        return visibleTargets;
    }

    /// <summary>
    /// Returns the list of potential attack targets, sorted by closest distance
    /// </summary>
    protected List<TargetDistance> BuildTargetDistance(List<Creature> targets, bool distSq = false)
    {
        var targetDistance = new List<TargetDistance>();

        foreach (var target in targets)
        {
            targetDistance.Add(
                new TargetDistance(
                    target,
                    distSq
                        ? (float)PhysicsObj.get_distance_sq_to_object(target.PhysicsObj, true)
                        : (float)PhysicsObj.get_distance_to_object(target.PhysicsObj, true)
                )
            );
        }

        return targetDistance.OrderBy(i => i.Distance).ToList();
    }

    /// <summary>
    /// Uses weighted RNG selection by distance to select a target
    /// </summary>
    private Creature SelectWeightedDistance(List<TargetDistance> targetDistances)
    {
        if (targetDistances.Count == 1)
        {
            return targetDistances[0].Target;
        }

        var distSum = targetDistances.Select(i => i.Distance).Sum();

        var invRatioSum = targetDistances.Count - 1;

        var rng = ThreadSafeRandom.Next(0.0f, invRatioSum);

        var invRatio = 0.0f;
        foreach (var targetDistance in targetDistances)
        {
            invRatio += 1.0f - (targetDistance.Distance / distSum);

            if (rng < invRatio)
            {
                return targetDistance.Target;
            }
        }

        Console.WriteLine(
            $"{Name}.SelectWeightedDistance: couldn't find target: {string.Join(",", targetDistances.Select(i => i.Distance))}"
        );
        return targetDistances[0].Target;
    }

    private static readonly Tolerance ExcludeSpawnScan =
        Tolerance.NoAttack | Tolerance.Appraise | Tolerance.Provoke | Tolerance.Retaliate;

    public void CheckTargets()
    {
        if (!Attackable && TargetingTactic == TargetingTactic.None || (Tolerance & ExcludeSpawnScan) != 0)
        {
            return;
        }

        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(0.75f);
        actionChain.AddAction(this, CheckTargets_Inner);
        actionChain.EnqueueChain();
    }

    private void CheckTargets_Inner()
    {
        Creature closestTarget = null;
        var closestDistSq = float.MaxValue;

        foreach (var creature in PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature())
        {
            var player = creature as Player;
            if (player != null && (!player.Attackable || player.Teleporting || (player.Hidden ?? false)))
            {
                continue;
            }

            if (Tolerance.HasFlag(Tolerance.Monster) && (creature is Player || creature is CombatPet))
            {
                continue;
            }

            var distSq = PhysicsObj.get_distance_sq_to_object(creature.PhysicsObj, true);
            if (player != null && player.TestStealth(this, distSq, $"{creature.Name} sees you! You lose stealth."))
            {
                continue;
            }

            if (distSq < closestDistSq)
            {
                closestDistSq = (float)distSq;
                closestTarget = creature;
            }
        }

        if (closestTarget == null || closestDistSq > VisualAwarenessRangeSq)
        {
            return;
        }

        closestTarget.AlertMonster(this);
    }

    private const float VisualAwarenessRange_Default = 18.0f;
    public const float VisualAwarenessRange_Highest = 75.0f;

    public double? VisualAwarenessRange
    {
        get => GetProperty(PropertyFloat.VisualAwarenessRange);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.VisualAwarenessRange);
            }
            else
            {
                SetProperty(PropertyFloat.VisualAwarenessRange, value.Value);
            }
        }
    }

    public double? AuralAwarenessRange
    {
        get => GetProperty(PropertyFloat.AuralAwarenessRange);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyFloat.AuralAwarenessRange);
            }
            else
            {
                SetProperty(PropertyFloat.AuralAwarenessRange, value.Value);
            }
        }
    }

    private float? _visualAwarenessRangeSq;

    public float VisualAwarenessRangeSq
    {
        get
        {
            if (_visualAwarenessRangeSq == null)
            {
                var visualAwarenessRange = (float)(
                    (VisualAwarenessRange ?? VisualAwarenessRange_Default)
                    * PropertyManager.GetDouble("mob_awareness_range").Item
                );

                if (
                    !Location.Indoors && visualAwarenessRange < 45f && Level > 10 && !OverrideVisualRange.HasValue
                    || OverrideVisualRange == false
                )
                {
                    visualAwarenessRange = PropertyManager.GetLong("monster_visual_awareness_range").Item;
                }

                _visualAwarenessRangeSq = visualAwarenessRange * visualAwarenessRange;
            }

            return _visualAwarenessRangeSq.Value;
        }
    }

    private float? _auralAwarenessRangeSq;

    private float AuralAwarenessRangeSq
    {
        get
        {
            if (_auralAwarenessRangeSq == null)
            {
                var auralAwarenessRange = (float)(
                    (AuralAwarenessRange ?? VisualAwarenessRange ?? VisualAwarenessRange_Default)
                    * PropertyManager.GetDouble("mob_awareness_range").Item
                );

                _auralAwarenessRangeSq = auralAwarenessRange * auralAwarenessRange;
            }

            return _auralAwarenessRangeSq.Value;
        }
    }

    private static readonly TimeSpan AlertThreshold = TimeSpan.FromMinutes(2);

    private Dictionary<uint, DateTime> Alerted;

    private void AlertFriendly()
    {
        if (
            Alerted != null
            && Alerted.TryGetValue(AttackTarget.Guid.Full, out var lastAlertTime)
            && DateTime.UtcNow - lastAlertTime < AlertThreshold
        )
        {
            return;
        }

        var visibleObjs = PhysicsObj.ObjMaint.GetVisibleObjects(PhysicsObj.CurCell);

        var targetCreature = AttackTarget as Creature;

        var alerted = false;

        foreach (var obj in visibleObjs)
        {
            var nearbyCreature = obj.WeenieObj.WorldObject as Creature;
            if (
                nearbyCreature == null
                || nearbyCreature.IsAwake
                || !nearbyCreature.Attackable && nearbyCreature.TargetingTactic == TargetingTactic.None
            )
            {
                continue;
            }

            if ((nearbyCreature.Tolerance & AlertExclude) != 0)
            {
                continue;
            }

            if (
                CreatureType != null && CreatureType == nearbyCreature.CreatureType
                || FriendType != null && FriendType == nearbyCreature.CreatureType
            )
            {
                var distSq = PhysicsObj.get_distance_sq_to_object(nearbyCreature.PhysicsObj, true);
                if (distSq > nearbyCreature.AuralAwarenessRangeSq)
                {
                    continue;
                }

                if (nearbyCreature == AttackTarget)
                {
                    continue;
                }

                if (nearbyCreature.SameFaction(targetCreature))
                {
                    nearbyCreature.AddRetaliateTarget(AttackTarget);
                }

                if (PotentialFoe(targetCreature))
                {
                    if (nearbyCreature.PotentialFoe(targetCreature))
                    {
                        nearbyCreature.AddRetaliateTarget(AttackTarget);
                    }
                    else
                    {
                        continue;
                    }
                }

                alerted = true;

                nearbyCreature.AttackTarget = AttackTarget;
                nearbyCreature.WakeUp(false);
            }
        }

        if (alerted)
        {
            if (Alerted == null)
            {
                Alerted = new Dictionary<uint, DateTime>();
            }

            Alerted[AttackTarget.Guid.Full] = DateTime.UtcNow;
        }
    }

    private void FactionMob_CheckMonsters()
    {
        if (MonsterState != State.Idle)
        {
            return;
        }

        var creatures = PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature();

        foreach (var creature in creatures)
        {
            if (creature is Player || creature is CombatPet)
            {
                continue;
            }

            if (
                creature.IsDead
                || !creature.Attackable && creature.TargetingTactic == TargetingTactic.None
                || creature.Teleporting
            )
            {
                continue;
            }

            if (SameFaction(creature) && !PotentialFoe(creature))
            {
                continue;
            }

            if (PhysicsObj.get_distance_sq_to_object(creature.PhysicsObj, true) > VisualAwarenessRangeSq)
            {
                continue;
            }

            creature.AlertMonster(this);
            break;
        }
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

    private Dictionary<uint, double> VanishedPlayers;
    private HashSet<uint> FooledByVanishPlayers;

    public void AddVanishedPlayer(Player player)
    {
        if (FooledByVanishPlayers == null)
        {
            FooledByVanishPlayers = new HashSet<uint>();
        }

        FooledByVanishPlayers.Add(player.Guid.Full);
    }

    public bool IsPlayerVanished(Player player)
    {
        if (FooledByVanishPlayers == null || !FooledByVanishPlayers.Contains(player.Guid.Full))
        {
            return false;
        }

        if (!player.VanishIsActive)
        {
            FooledByVanishPlayers.Remove(player.Guid.Full);
            return false;
        }

        return true;
    }

    public void PeriodicTargetScan()
    {
        if (MonsterState != State.Idle || (!Attackable && TargetingTactic == TargetingTactic.None))
        {
            return;
        }

        if ((Tolerance & ExcludeSpawnScan) != 0)
        {
            return;
        }

        var creatures = PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature();

        foreach (var creature in creatures)
        {
            var player = creature as Player;
            if (player != null && (!player.Attackable || player.Teleporting || (player.Hidden ?? false)))
            {
                continue;
            }

            if (Tolerance.HasFlag(Tolerance.Monster) && (creature is Player || creature is CombatPet))
            {
                continue;
            }

            var distSq = PhysicsObj.get_distance_sq_to_object(creature.PhysicsObj, true);

            if (distSq > VisualAwarenessRangeSq)
            {
                continue;
            }

            if (player != null && player.TestStealth(this, distSq, $"{Name} sees you! You lose stealth."))
            {
                continue;
            }

            if (player != null && IsPlayerVanished(player))
            {
                continue;
            }

            creature.AlertMonster(this);
            break;
        }
    }
}
