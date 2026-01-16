using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects;

partial class Player
{
    private static uint EnterStealthDifficulty = 50;

    public bool IsStealthed = false;
    public bool IsAttackFromStealth = false;

    // Track consecutive failed stealth checks per creature for stamina cost scaling
    private Dictionary<ObjectGuid, ConsecutiveFailureTracker> StealthFailureTrackers = new Dictionary<ObjectGuid, ConsecutiveFailureTracker>();
    private const double FailureTrackerResetDelay = 30.0; // Reset after 30 seconds of no failures

    private class ConsecutiveFailureTracker
    {
        public int FailureCount { get; set; }
        public double LastFailureTime { get; set; }

        public ConsecutiveFailureTracker(double initialTime)
        {
            FailureCount = 0;
            LastFailureTime = initialTime;
        }

        public void IncrementFailure(double currentTime)
        {
            FailureCount++;
            LastFailureTime = currentTime;
        }

        public void Reset()
        {
            FailureCount = 0;
        }

        public bool ShouldReset(double currentTime, double resetDelay)
        {
            return currentTime - LastFailureTime > resetDelay;
        }
    }

    public void BeginStealth()
    {
        if (IsStealthed)
        {
            return;
        }

        var result = TestStealthInternal(EnterStealthDifficulty);
        switch (result)
        {
            case StealthTestResult.Success:
            {
                IsStealthed = true;
                if (Time.GetUnixTime() < LastVanishActivated + 5)
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            "You vanish in a cloud of smoke, and you cannot be detected for the next 5 seconds!",
                            ChatMessageType.Broadcast
                        )
                    );
                }
                else
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat("You enter stealth.", ChatMessageType.Broadcast)
                    );
                }

                EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.StealthBegin));

                ApplyStealthRunPenalty();

                break;
            }
            case StealthTestResult.Failure:
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat("You fail on your attempt to enter stealth.", ChatMessageType.Broadcast)
                );
                break;
            case StealthTestResult.Untrained:
            default:
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat("You are not trained in thievery!", ChatMessageType.Broadcast)
                );
                break;
        }
    }

    public void EndStealth(string message = null, bool isAttackFromStealth = false)
    {
        if (!IsStealthed)
        {
            return;
        }

        IsStealthed = false;
        IsAttackFromStealth = isAttackFromStealth;

        Session.Network.EnqueueSend(
            new GameMessageSystemChat(message ?? "You lose stealth.", ChatMessageType.Broadcast)
        );
        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(0.25f);
        if (!Teleporting)
        {
            actionChain.AddAction(
                this,
                () =>
                {
                    EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.StealthEnd));
                }
            );
        }
        actionChain.AddAction(
            this,
            () =>
            {
                RemoveStealthRunPenalty();
            }
        );

        RadarColor = null;
        EnqueueBroadcast(true, new GameMessagePublicUpdatePropertyInt(this, PropertyInt.RadarBlipColor, 0));

        actionChain.EnqueueChain();
    }

    private void ApplyStealthRunPenalty()
    {
        var spell = new Spell(SpellId.StealthRunDebuff);
        var addResult = EnchantmentManager.Add(spell, null, null, true);

        Session.Network.EnqueueSend(
            new GameEventMagicUpdateEnchantment(Session, new Enchantment(this, addResult.Enchantment))
        );
        HandleRunRateUpdate(spell);
    }

    private void RemoveStealthRunPenalty()
    {
        const uint stealthRunDebuff = (uint)SpellId.StealthRunDebuff;

        while (EnchantmentManager.HasSpell(stealthRunDebuff))
        {
            var propertiesEnchantmentRegistry = EnchantmentManager.GetEnchantment(stealthRunDebuff);
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

    private const double CreatureRetestDelay = 3.0;
    private Dictionary<ObjectGuid, double> RecentStealthTests = new Dictionary<ObjectGuid, double>();

    public bool TestStealth(Creature creature, double distance, string failureMessage)
    {
        if (!IsStealthed)
        {
            return false;
        }

        if (Time.GetUnixTime() < LastVanishActivated + 5)
        {
            return true;
        }

        if (
            creature == null
            || creature.PlayerKillerStatus == PlayerKillerStatus.RubberGlue
            || creature.PlayerKillerStatus == PlayerKillerStatus.Protected
            || distance > creature.VisualAwarenessRangeSq
            || !creature.IsDirectVisible(this)
        )
        {
            return true;
        }

        if (creature.CannotBreakStealth == true || creature.Translucency == 1.0 || creature.Visibility == true) // watchers
        {
            return true;
        }

        foreach (var kvp in RecentStealthTests)
        {
            if (creature.Guid == kvp.Key)
            {
                if (Time.GetUnixTime() < kvp.Value + CreatureRetestDelay)
                {
                    return true;
                }
                else
                {
                    RecentStealthTests.Remove(kvp.Key);
                }
            }
        }

        RecentStealthTests.Add(creature.Guid, Time.GetUnixTime());

        var maxDistance = creature.VisualAwarenessRangeSq;
        var monsterDistanceBonus = Math.Min(2.0f, (float)(maxDistance / distance));

        var angle = Math.Abs(creature.GetAngle(this));

        var angleMod = 2.0f - angle / 90.0f; // mod ranges from 0.0 (180 angle) to 2.0 (0 angle)

        var difficulty = (uint)(creature.GetModdedPerceptionSkill() * monsterDistanceBonus * angleMod);

        //Console.WriteLine($"\nCreature: {creature.Name} {creature.WeenieClassId} - distance: {distance}, distanceBonus: {monsterDistanceBonus}, angle: {angle}, angleBonus: {angleMod}");

        return TestStealth(difficulty, failureMessage, creature);
    }

    public bool TestStealth(Creature creature, string failureMessage)
    {
        if (!IsStealthed)
        {
            return false;
        }

        if (creature == null)
        {
            return true;
        }

        return TestStealth(creature, PhysicsObj.get_distance_sq_to_object(creature.PhysicsObj, true), failureMessage);
    }

    public bool TestStealth(uint difficulty, string failureMessage, Creature creature = null)
    {
        var thieverySkill = GetCreatureSkill(Skill.Thievery);
        var isSpecialized = thieverySkill.AdvancementClass == SkillAdvancementClass.Specialized;

        var result = TestStealthInternal(difficulty);

        if (result != StealthTestResult.Success)
        {
            if (creature != null && TryPreserveStealthWithStamina(creature, isSpecialized))
            {
                return true;
            }

            EndStealth(failureMessage);
            return false;
        }
        else
        {
            // Reset failure tracker on success
            if (creature != null && StealthFailureTrackers.ContainsKey(creature.Guid))
            {
                StealthFailureTrackers[creature.Guid].Reset();
            }
        }

        return true;
    }

    private bool TryPreserveStealthWithStamina(Creature creature, bool isSpecialized)
    {
        var currentTime = Time.GetUnixTime();

        // Check if player has more than 50% stamina
        var staminaPercentage = Stamina.Current / (float)Stamina.MaxValue;
        if (staminaPercentage <= 0.5f)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "You are too exhausted to remain hidden! (Requires more than 50% stamina)",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        // Get or create failure tracker for this creature
        if (!StealthFailureTrackers.TryGetValue(creature.Guid, out var tracker))
        {
            tracker = new ConsecutiveFailureTracker(currentTime);
            StealthFailureTrackers[creature.Guid] = tracker;
        }
        else if (tracker.ShouldReset(currentTime, FailureTrackerResetDelay))
        {
            tracker.Reset();
        }

        // Calculate stamina cost: creature level * (1 + failureCount)
        // This scales linearly: base cost, 2x base cost, 3x base cost, etc.
        var creatureLevel = creature.Level ?? 1;
        var divisor = isSpecialized ? 2 : 1;
        var baseCost = Math.Max(1, creatureLevel / divisor);
        var staminaCost = baseCost * (1 + tracker.FailureCount);

        // Check if player has enough stamina for the cost
        if (Stamina.Current < staminaCost)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You don't have enough stamina to remain hidden! (Required: {staminaCost})",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        // Deduct stamina and track the failure
        UpdateVitalDelta(Stamina, -staminaCost);
        tracker.IncrementFailure(currentTime);

        // Send feedback to player
        var failureIndicator = tracker.FailureCount > 1 ? $" (x{tracker.FailureCount})" : "";
        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"You strain to remain hidden, expending {staminaCost} stamina. ({creature.Name}){failureIndicator}",
                ChatMessageType.Broadcast
            )
        );

        return true;
    }

    private enum StealthTestResult
    {
        Untrained,
        Failure,
        Success
    }

    private StealthTestResult TestStealthInternal(uint difficulty)
    {
        var thieverySkill = GetCreatureSkill(Skill.Thievery); // Thievery
        if (thieverySkill.AdvancementClass < SkillAdvancementClass.Trained)
        {
            return StealthTestResult.Untrained;
        }

        var moddedThieverySkill = GetModdedThieverySkill();

        var chance = SkillCheck.GetSkillChance(moddedThieverySkill, difficulty);

        var roll = ThreadSafeRandom.Next(0.0f, 1.0f);

        //Console.WriteLine($"playerEffectiveSkill: {moddedThieverySkill}, difficulty: {difficulty}, chance: {chance}, roll: {roll}");

        if (chance > roll)
        {
            Proficiency.OnSuccessUse(this, thieverySkill, difficulty);
            return StealthTestResult.Success;
        }
        else if (Time.GetUnixTime() < LastVanishActivated + 5)
        {
            return StealthTestResult.Success;
        }
        else
        {
            return StealthTestResult.Failure;
        }
    }

    public bool IsAware(WorldObject wo)
    {
        return wo.IsAware(this);
    }

    public bool TestPerception(WorldObject wo)
    {
        var perceptionSkill = GetCreatureSkill(Skill.Perception); // Perception
        var chance = SkillCheck.GetSkillChance(perceptionSkill.Current, (uint)(wo.ResistPerception ?? 0));
        if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
        {
            Proficiency.OnSuccessUse(this, perceptionSkill, (uint)(wo.ResistPerception ?? 0));
            return true;
        }
        return false;
    }

    private double NextThreatTableTime = 0.0;

    public void GetMonsterThreatTable(Creature monster)
    {
        if (monster != null && IsAttemptingToPerceiveThreats && NextThreatTableTime < Time.GetUnixTime())
        {
            var playerPerception = GetModdedPerceptionSkill();
            var targetDeceptionHalved = Convert.ToUInt32(monster.GetCreatureSkill(Skill.Deception).Current * 0.5);

            var skillCheck = SkillCheck.GetSkillChance(playerPerception, targetDeceptionHalved);

            if (ThreadSafeRandom.Next(0.0f, 1.0f) > skillCheck)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You fail to perceive {monster.Name}'s current threats.",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }
            else
            {
                if (monster.PositiveThreat.Count > 0)
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat($"{monster.Name}'s Threat Table:", ChatMessageType.Broadcast)
                    );

                    var sortedDict = from entry in monster.PositiveThreat orderby entry.Value descending select entry;

                    foreach (var entry in sortedDict)
                    {
                        string threatLevel;

                        switch (entry.Value)
                        {
                            case > 0.5f:
                                threatLevel = $"Major threat";
                                break;
                            case > 0.25f:
                                threatLevel = $"Moderate threat";
                                break;
                            case > 0.0f:
                            default:
                                threatLevel = $"Minor threat";
                                break;
                        }
                        Session.Network.EnqueueSend(
                            new GameMessageSystemChat($"   {entry.Key.Name}: {threatLevel}", ChatMessageType.Broadcast)
                        );
                    }
                }

                if (monster.NegativeThreat.Count > 0)
                {
                    //Session.Network.EnqueueSend(new GameMessageSystemChat($"  ----------------", ChatMessageType.Broadcast));

                    var sortedDict = from entry in monster.NegativeThreat orderby entry.Value descending select entry;

                    foreach (var entry in sortedDict)
                    {
                        string threatLevel;
                        switch (entry.Value)
                        {
                            case > -0.25f:
                                threatLevel = $"Potential threat";
                                break;
                            case > -0.5f:
                                threatLevel = $"Unthreatening";
                                break;
                            case > -1.0f:
                            default:
                                threatLevel = $"Harmless";
                                break;
                        }
                        Session.Network.EnqueueSend(
                            new GameMessageSystemChat($"   {entry.Key.Name}: {threatLevel}", ChatMessageType.Broadcast)
                        );
                    }
                }
            }

            NextThreatTableTime = Time.GetUnixTime() + 10;
        }
    }
}
