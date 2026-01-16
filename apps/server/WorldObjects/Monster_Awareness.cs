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
                SetProperty(PropertyInt.TargetingTactic, (int)TargetingTactic);
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
        if (Timers.RunningTime < NextFindTarget)
        {
            return;
        }

        FindNextTarget(false);
    }

    private void SetNextTargetTime()
    {
        // use rng?

        //var rng = ThreadSafeRandom.Next(5.0f, 10.0f);
        var rng = 5.0f;

        NextFindTarget = Timers.RunningTime + rng;
    }

    private bool DebugThreatSystem
    {
        get => PropertyManager.GetBool("debug_threat_system").Item;
    }
    private const int ThreatMinimum = 100;
    private double ThreatGainedSinceLastTick = 1;

    private Dictionary<Creature, int> ThreatLevel;
    public Dictionary<Creature, float> PositiveThreat;
    public Dictionary<Creature, float> NegativeThreat;

    public List<Player> SkipThreatFromNextAttackTargets = [];
    public List<Player> DoubleThreatFromNextAttackTargets = [];

    public void IncreaseTargetThreatLevel(Creature targetCreature, int amount)
    {
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
        }

        //if (DebugThreatSystem)
        //    _log.Information("TickDownAllTargetThreatLevels() - {Name} - {Threat}", Name, threatGained);

        ThreatGainedSinceLastTick = 0;
    }

    public virtual bool FindNextTarget(bool onTakeDamage, Creature untargetablePlayer = null)
    {
        // stopwatch.Restart();
        try
        {
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

            // Generally, a creature chooses whom to attack based on:
            //  - who it was last attacking,
            //  - who attacked it last,
            //  - or who caused it damage last.

            // When players first enter the creature's detection radius, however, none of these things are useful yet,
            // so the creature chooses a target randomly, weighted by distance.

            // Players within the creature's detection sphere are weighted by how close they are to the creature --
            // the closer you are, the more chance you have to be selected to be attacked.

            var prevAttackTarget = AttackTarget;

            var targetDistances = BuildTargetDistance(visibleTargets);

            // New Threat System
            if (!(UseLegacyThreatSystem ?? false))
            {
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
                        // i.e. KVPs of <1,30>, <2,38>, <3,45>
                        // would become <1,30>, <2,68>, <3,113>
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

                    if (DebugThreatSystem)
                    {
                        _log.Information("SELECTED PLAYER: {Name}", AttackTarget.Name);
                    }
                }
            }
            else
            {
                //var currentTactic = CurrentTargetingTactic;
                if (onTakeDamage)
                {
                    return false;
                }

                switch (CurrentTargetingTactic)
                {
                    case TargetingTactic.None:

                        //Console.WriteLine($"{Name}.FindNextTarget(): TargetingTactic.None");
                        break; // same as focused?

                    case TargetingTactic.Random:

                        // this is a very common tactic with monsters,
                        // although it is not truly random, it is weighted by distance
                        //var targetDistances = BuildTargetDistance(visibleTargets);
                        AttackTarget = SelectWeightedDistance(targetDistances);
                        break;

                    case TargetingTactic.Focused:

                        break; // always stick with original target?

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

                    // these below don't seem to be used in PY16 yet...

                    case TargetingTactic.Weakest:

                        // should probably shuffle the list beforehand,
                        // in case a bunch of levels of same level are in a group,
                        // so the same player isn't always selected
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
            //Console.WriteLine($"{Name}.FindNextTarget = {AttackTarget.Name}");

            var player = AttackTarget as Player;
            if (player != null && !Visibility && player.AddTrackedObject(this))
            {
                _log.Error(
                    $"Fixed invisible attacker on player {player.Name}. (Landblock:{CurrentLandblock.Id} - {Name} ({Guid})"
                );
            }

            // If multiple player targets are nearby, check to see if the current target can force this monster to look for a new target
            // Base chance to avoid monster aggro can be up to 25%, depending on monster perception and player deception.
            // With Specialized Deception and the Smokescreen combat ability, it's possible for a player to receive 100% chance to avoid aggro.
            if (visibleTargets.Count > 1 && player != null && player.IsAttemptingToDeceive)
            {
                var monsterPerception = GetCreatureSkill(Skill.Perception).Current;
                var playerDeception = player.GetCreatureSkill(Skill.Deception).Current;

                var skillCheck = SkillCheck.GetSkillChance(monsterPerception, playerDeception);
                var chanceToDeceive = skillCheck * 0.25f;

                // SPEC BONUS - Deception (chanceToDeceive value doubled)
                if (
                    player.GetCreatureSkill(Skill.Deception).AdvancementClass == SkillAdvancementClass.Specialized
                    && visibleTargets.Count > 1
                )
                {
                    chanceToDeceive *= 2;
                }

                // COMBAT ABILITY - Smokescreen (+50% to chanceToDeceive value, additively)
                if (player is {SmokescreenIsActive: true})
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
            // ServerPerformanceMonitor.AddToCumulativeEvent(
            //     ServerPerformanceMonitor.CumulativeEventHistoryType.Monster_Awareness_FindNextTarget,
            //     stopwatch.Elapsed.TotalSeconds
            // );
        }
    }

    /// <summary>
    /// Returns a list of attackable targets currently visible to this monster
    /// </summary>
    public List<Creature> GetAttackTargets()
    {
        var visibleTargets = new List<Creature>();

        foreach (var creature in PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature())
        {
            // ensure attackable
            if (!creature.Attackable && creature.TargetingTactic == TargetingTactic.None || creature.Teleporting)
            {
                continue;
            }

            // check if player fooled this monster with vanish
            if (creature is Player player && IsPlayerVanished(player))
            {
                continue;
            }

            // ensure within 'detection radius' ?
            var chaseDistSq = creature == AttackTarget ? MaxChaseRangeSq : VisualAwarenessRangeSq;

            /*if (Location.SquaredDistanceTo(creature.Location) > chaseDistSq)
                continue;*/

            var distSq = PhysicsObj.get_distance_sq_to_object(creature.PhysicsObj, true);

            if (creature is Player targetPlayer && targetPlayer.TestStealth(this, distSq, $"{Name} sees you! You lose stealth."))
            {
                continue;
            }

            if (distSq > chaseDistSq)
            {
                continue;
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
            //targetDistance.Add(new TargetDistance(target, distSq ? Location.SquaredDistanceTo(target.Location) : Location.DistanceTo(target.Location)));
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

        // http://asheron.wikia.com/wiki/Wi_Flag

        var distSum = targetDistances.Select(i => i.Distance).Sum();

        // get the sum of the inverted ratios
        var invRatioSum = targetDistances.Count - 1;

        // roll between 0 - invRatioSum here,
        // instead of 0-1 (the source of the original wi bug)
        var rng = ThreadSafeRandom.Next(0.0f, invRatioSum);

        // walk the list
        var invRatio = 0.0f;
        foreach (var targetDistance in targetDistances)
        {
            invRatio += 1.0f - (targetDistance.Distance / distSum);

            if (rng < invRatio)
            {
                return targetDistance.Target;
            }
        }
        // precision error?
        Console.WriteLine(
            $"{Name}.SelectWeightedDistance: couldn't find target: {string.Join(",", targetDistances.Select(i => i.Distance))}"
        );
        return targetDistances[0].Target;
    }

    /// <summary>
    /// If one of these fields is set, monster scanning for targets when it first spawns in
    /// is terminated immediately
    /// </summary>
    private static readonly Tolerance ExcludeSpawnScan =
        Tolerance.NoAttack | Tolerance.Appraise | Tolerance.Provoke | Tolerance.Retaliate;

    /// <summary>
    /// Called when a monster is first spawning in
    /// </summary>
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

            //var distSq = Location.SquaredDistanceTo(creature.Location);
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

    /// <summary>
    /// The most common value from retail
    /// Some other common values are in the range of 12-25
    /// </summary>
    private const float VisualAwarenessRange_Default = 18.0f;

    /// <summary>
    /// The highest value found in the current database
    /// </summary>
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

    /// <summary>
    /// A monster can only alert friendly mobs to the presence of each attack target
    /// once every AlertThreshold
    /// </summary>
    private static readonly TimeSpan AlertThreshold = TimeSpan.FromMinutes(2);

    /// <summary>
    /// AttackTarget => last alerted time
    /// </summary>
    private Dictionary<uint, DateTime> Alerted;

    private void AlertFriendly()
    {
        // if current attacker has already alerted this monster recently,
        // don't re-alert friendlies
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
                //var distSq = Location.SquaredDistanceTo(nearbyCreature.Location);
                var distSq = PhysicsObj.get_distance_sq_to_object(nearbyCreature.PhysicsObj, true);
                if (distSq > nearbyCreature.AuralAwarenessRangeSq)
                {
                    continue;
                }

                // scenario: spawn a faction mob, and then spawn a non-faction mob next to it, of the same CreatureType
                // the spawning mob will become alerted by the faction mob, and will then go to alert its friendly types
                // the faction mob happens to be a friendly type, so it in effect becomes alerted to itself
                // this is to prevent the faction mob from adding itself to its retaliate targets / visible targets,
                // and setting itself to its AttackTarget
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
        // only set alerted if monsters were actually alerted
        if (alerted)
        {
            if (Alerted == null)
            {
                Alerted = new Dictionary<uint, DateTime>();
            }

            Alerted[AttackTarget.Guid.Full] = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Wakes up a faction monster from any non-faction monsters wandering within range
    /// </summary>
    private void FactionMob_CheckMonsters()
    {
        if (MonsterState != State.Idle)
        {
            return;
        }

        var creatures = PhysicsObj.ObjMaint.GetVisibleTargetsValuesOfTypeCreature();

        foreach (var creature in creatures)
        {
            // ensure type isn't already handled elsewhere
            if (creature is Player || creature is CombatPet)
            {
                continue;
            }

            // ensure attackable
            if (
                creature.IsDead
                || !creature.Attackable && creature.TargetingTactic == TargetingTactic.None
                || creature.Teleporting
            )
            {
                continue;
            }

            // ensure another faction
            if (SameFaction(creature) && !PotentialFoe(creature))
            {
                continue;
            }

            // ensure within detection range
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

    /// <summary>
    /// Tracks players who have vanished from this monster's sight
    /// Key: Player GUID, Value: Expiration time (unix timestamp)
    /// </summary>
    private Dictionary<uint, double> VanishedPlayers;

    /// <summary>
    /// Tracks player GUIDs who successfully fooled this monster with Vanish
    /// These players cannot be targeted while their VanishIsActive is true
    /// </summary>
    private HashSet<uint> FooledByVanishPlayers;

    /// <summary>
    /// Marks a player as having successfully fooled this monster with Vanish
    /// </summary>
    /// <param name="player">The player who fooled the monster</param>
    public void AddVanishedPlayer(Player player)
    {
        if (FooledByVanishPlayers == null)
        {
            FooledByVanishPlayers = new HashSet<uint>();
        }

        FooledByVanishPlayers.Add(player.Guid.Full);
    }

    /// <summary>
    /// Checks if a player is currently vanished from this monster's perspective
    /// </summary>
    /// <param name="player">The player to check</param>
    /// <returns>True if the player fooled this monster and their vanish is still active</returns>
    public bool IsPlayerVanished(Player player)
    {
        if (FooledByVanishPlayers == null || !FooledByVanishPlayers.Contains(player.Guid.Full))
        {
            return false;
        }

        // Check if the player's vanish is still active
        if (!player.VanishIsActive)
        {
            // Vanish expired, remove from tracking
            FooledByVanishPlayers.Remove(player.Guid.Full);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Called periodically for idle monsters to check for stationary players
    /// </summary>
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

        // Scan for all creatures in range, including stationary ones
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

            // Check if player fooled this monster with vanish
            if (player != null && IsPlayerVanished(player))
            {
                continue;
            }

            // Found a valid target - wake up!
            creature.AlertMonster(this);
            break;
        }
    }
}
