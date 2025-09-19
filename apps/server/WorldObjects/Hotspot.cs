using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public enum MenhirManaHotspot
{
    Low = 0,
    Moderate = 1,
    High = 2,
    Lyceum = 3
}

public class Hotspot : WorldObject
{
    public Hotspot(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    public Hotspot(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    public Hotspot() { }

    private void SetEphemeralValues()
    {
        // If CycleTime is less than 1, player has a very bad time.
        if ((CycleTime ?? 0) < 1)
        {
            CycleTime = 1;
        }
    }

    private HashSet<ObjectGuid> Creatures = new HashSet<ObjectGuid>();

    private ActionChain ActionLoop = null;

    public Player P_HotspotOwner;

    public Player HotspotSummoner;

    public override void OnCollideObject(WorldObject wo)
    {
        if (!(wo is Creature creature))
        {
            return;
        }

        if (AffectsOnlyAis && (wo is Player))
        {
            return;
        }

        if (AffectsOnlyAis && creature is {ResetFromHotspot: true})
        {
            if (Time.GetUnixTime() < creature.LastHotspotVitalResetTime + 5.0)
            {
                return;
            }

            creature.RegainHalfOfMissingHealth();
            creature.MoveToHome();

            creature.EnqueueBroadcast(
                new GameMessageSystemChat(
                    $"{creature.Name} retreats to safety!",
                    ChatMessageType.Broadcast
                ),
                LocalBroadcastRange,
                ChatMessageType.Broadcast
            );
        }

        if (!Creatures.Contains(creature.Guid))
        {
            //Console.WriteLine($"{Name} ({Guid}).OnCollideObject({creature.Name})");
            Creatures.Add(creature.Guid);
        }

        if (ActionLoop == null)
        {
            ActionLoop = NextActionLoop;
            NextActionLoop.EnqueueChain();
        }
    }

    public override void OnCollideObjectEnd(WorldObject wo)
    {
        /*if (!(wo is Player player))
            return;

        if (Players.Contains(player.Guid))
            Players.Remove(player.Guid);*/
    }

    private ActionChain NextActionLoop
    {
        get
        {
            ActionLoop = new ActionChain();
            ActionLoop.AddDelaySeconds(CycleTimeNext);
            ActionLoop.AddAction(
                this,
                () =>
                {
                    if (Creatures.Any())
                    {
                        Activate();
                        NextActionLoop.EnqueueChain();
                    }
                    else
                    {
                        ActionLoop = null;
                    }
                }
            );
            return ActionLoop;
        }
    }

    private double CycleTimeNext
    {
        get
        {
            var max = CycleTime;
            var min = max * (1.0f - CycleTimeVariance ?? 0.0f);

            return ThreadSafeRandom.Next((float)min, (float)max);
        }
    }

    public double? CycleTime
    {
        get => GetProperty(PropertyFloat.HotspotCycleTime);
        set
        {
            if (value == null)
            {
                RemoveProperty(PropertyFloat.HotspotCycleTime);
            }
            else
            {
                SetProperty(PropertyFloat.HotspotCycleTime, (double)value);
            }
        }
    }

    public double? CycleTimeVariance
    {
        get => GetProperty(PropertyFloat.HotspotCycleTimeVariance) ?? 0;
        set
        {
            if (value == null)
            {
                RemoveProperty(PropertyFloat.HotspotCycleTimeVariance);
            }
            else
            {
                SetProperty(PropertyFloat.HotspotCycleTimeVariance, (double)value);
            }
        }
    }

    private float DamageNext
    {
        get
        {
            var r = GetBaseDamage();
            var p = (float)ThreadSafeRandom.Next(r.MinDamage, r.MaxDamage);
            return p;
        }
    }

    private int? _DamageType
    {
        get => GetProperty(PropertyInt.DamageType);
        set
        {
            if (value == null)
            {
                RemoveProperty(PropertyInt.DamageType);
            }
            else
            {
                SetProperty(PropertyInt.DamageType, (int)value);
            }
        }
    }

    public DamageType DamageType
    {
        get { return (DamageType)_DamageType; }
    }

    public bool IsHot
    {
        get => GetProperty(PropertyBool.IsHot) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.IsHot);
            }
            else
            {
                SetProperty(PropertyBool.IsHot, value);
            }
        }
    }

    public bool AffectsAis
    {
        get => GetProperty(PropertyBool.AffectsAis) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.AffectsAis);
            }
            else
            {
                SetProperty(PropertyBool.AffectsAis, value);
            }
        }
    }

    public bool AffectsOnlyAis
    {
        get => GetProperty(PropertyBool.AffectsOnlyAis) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.AffectsOnlyAis);
            }
            else
            {
                SetProperty(PropertyBool.AffectsOnlyAis, value);
            }
        }
    }

    private void Activate()
    {
        if (Creatures == null || CurrentLandblock == null)
        {
            return;
        }

        foreach (var creatureGuid in Creatures.ToList())
        {
            var creature = CurrentLandblock.GetObject(creatureGuid) as Creature;

            // verify current state of collision here
            if (creature == null || !creature.PhysicsObj.is_touching(PhysicsObj))
            {
                //Console.WriteLine($"{Name} ({Guid}).OnCollideObjectEnd({creature?.Name})");
                Creatures.Remove(creatureGuid);
                continue;
            }
            Activate(creature);
        }
    }

    private void Activate(Creature creature)
    {
        if (!IsHot)
        {
            return;
        }

        if (!(creature is Player))
        {
            if (!AffectsAis && !AffectsOnlyAis)
            {
                return;
            }
        }

        var amount = DamageNext;
        var iAmount = (int)Math.Round(amount);

        var player = creature as Player;

        var currentTime = Time.GetUnixTime();

        if (creature.HotspotImmunityTimestamp > currentTime)
        {
            return;
        }
        else
        {
            var immunityTime = (CycleTime ?? 0) * (1.0f - CycleTimeVariance ?? 0.0f) * 0.9f; // Multiplying the minimum possible CycleTime by 0.9 just to be extra sure that we wont be immune for the next tick.
            creature.HotspotImmunityTimestamp = currentTime + immunityTime;
        }

        CheckForMenhirScarabRecharge(player);

        if (player != null && CampfireHotspot == true)
        {
            var forwardCommand =
                player.CurrentMovementData.MovementType == MovementType.Invalid
                && player.CurrentMovementData.Invalid != null
                    ? player.CurrentMovementData.Invalid.State.ForwardCommand
                    : MotionCommand.Invalid;
            if (
                forwardCommand == MotionCommand.Sitting
                || forwardCommand == MotionCommand.Sleeping
                || forwardCommand == MotionCommand.Crouch
            )
            {
                if (player.CampfireTimer == 0)
                {
                    player.CampfireTimer = Time.GetUnixTime() + 10;
                }

                if (player.CampfireTimer < Time.GetUnixTime())
                {
                    if (player.WellRestedHotspot == null)
                    {
                        player.Session.Network.EnqueueSend(
                            new GameMessageSystemChat($"Gazing into the fire calms your soul.", ChatMessageType.Magic)
                        );
                    }

                    player.CampfireTimer = 0;
                    player.WellRestedHotspot = this;
                }
            }
            else
            {
                player.CampfireTimer = 0;
            }
        }

        switch (DamageType)
        {
            default:

                if (creature.Invincible)
                {
                    return;
                }

                amount *= creature.GetResistanceMod(DamageType, this, null);

                if (player != null)
                {
                    iAmount = player.TakeDamage(this, DamageType, amount, BodyPart.Foot, PartialEvasion.None);
                }
                else
                {
                    iAmount = (int)creature.TakeDamage(this, DamageType, amount);
                }

                if (creature.IsDead && Creatures.Contains(creature.Guid))
                {
                    Creatures.Remove(creature.Guid);
                }

                break;

            case DamageType.Mana:
                iAmount = creature.UpdateVitalDelta(creature.Mana, -iAmount);
                break;

            case DamageType.Stamina:
                iAmount = creature.UpdateVitalDelta(creature.Stamina, -iAmount);
                break;

            case DamageType.Health:
                iAmount = creature.UpdateVitalDelta(creature.Health, -iAmount);

                if (iAmount > 0)
                {
                    creature.DamageHistory.OnHeal((uint)iAmount);
                }
                else
                {
                    creature.DamageHistory.Add(this, DamageType.Health, (uint)-iAmount);
                }

                break;
        }

        if (!Visibility)
        {
            EnqueueBroadcast(new GameMessageSound(Guid, Sound.TriggerActivated, 1.0f));
        }

        if (player != null && !string.IsNullOrWhiteSpace(ActivationTalk) && iAmount != 0)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    ActivationTalk.Replace("%i", Math.Abs(iAmount).ToString()),
                    ChatMessageType.Broadcast
                )
            );
        }

        // perform activation emote
        if (ActivationResponse.HasFlag(ActivationResponse.Emote))
        {
            OnEmote(creature);
        }
    }

    private void CheckForMenhirScarabRecharge(Player player)
    {
        if (player == null || MenhirManaHotspot != true)
        {
            return;
        }

        var tier = Tier ?? 0;

        if (!player.HasRechargeableSigilTrinkets(tier) && player.HasMatchingMenhirBonusStat(SigilTrinketBonusStat ?? 0, SigilTrinketBonusStatAmount ?? 0))
        {
            switch ((MenhirManaHotspot)tier)
            {
                case WorldObjects.MenhirManaHotspot.Low:
                    player.SendTransientError("You don't have any blue sigil trinkets equipped that can be recharged or altered at this menhir ring.");
                    break;
                case WorldObjects.MenhirManaHotspot.Moderate:
                    player.SendTransientError("You don't have any yellow sigil trinkets equipped (or blue sigil trinkets held) that can be recharged or altered at this menhir ring.");
                    break;
                case WorldObjects.MenhirManaHotspot.High:
                    player.SendTransientError("You don't have any red sigil trinkets equipped (or blue/yellow sigil trinkets held) that can be recharged or altered at this menhir ring.");
                    break;
                case WorldObjects.MenhirManaHotspot.Lyceum:
                    player.SendTransientError("You don't have any blue/yellow/red sigil trinkets held that can be recharged at this menhir ring.");
                    break;
            }

            return;
        }

        var forwardCommand = player.CurrentMotionState.MotionState.ForwardCommand;
        if (forwardCommand != MotionCommand.MeditateState)
        {
            EmoteManager.OnAttack(player);
            return;
        }

        player.RechargeSigilTrinkets(this);
    }

    public static void TryGenHotspot(Player playerAttacker, Creature defender, int? tier, DamageType damageType)
    {
        uint wcid = 0;
        if (tier != null)
        {
            var modifiedTier = (int)tier - 1;
            modifiedTier = modifiedTier > 4 ? 4 : modifiedTier;

            int[] flameHotspots = [1053902, 1053903, 1053904, 1053905, 1053906];
            int[] frostHotspots = [1053909, 1053910, 1053911, 1053912, 1053913];
            int[] acidHotspots = [1053915, 1053916, 1053917, 1053918, 1053919];
            int[] lightningHotspots = [1053921, 1053922, 1053923, 1053924, 1053925];
            int[] healingHotspots = [1053928, 1053929, 1053930, 1053931, 1053932];

            switch (damageType)
            {
                case DamageType.Fire:
                    var chance = Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearFire, "", true);
                    if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
                    {
                        wcid = (uint)flameHotspots[modifiedTier];
                    }
                    break;
                case DamageType.Cold:
                    chance = Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearFrost, "", true);
                    if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
                    {
                        wcid = (uint)frostHotspots[modifiedTier];
                    }
                    break;
                case DamageType.Acid:
                    chance = Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearAcid, "", true);
                    if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
                    {
                        wcid = (uint)acidHotspots[modifiedTier];
                    }
                    break;
                case DamageType.Electric:
                    chance = Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearLightning, "", true);
                    if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
                    {
                        wcid = (uint)lightningHotspots[modifiedTier];
                    }
                    break;
                case DamageType.Health:
                case DamageType.Stamina:
                case DamageType.Mana:
                    chance = Jewel.GetJewelEffectMod(playerAttacker, PropertyInt.GearHealBubble, "", true);
                    if (chance > ThreadSafeRandom.Next(0.0f, 1.0f))
                    {
                        wcid = (uint)healingHotspots[modifiedTier];
                    }

                    break;
            }
        }

        if (wcid != 0)
        {
            CreateHotspot(playerAttacker, defender, wcid);
        }
    }

    private static void CreateHotspot(Player playerAttacker, Creature defender, uint wcid)
    {
        var wo = WorldObjectFactory.CreateNewWorldObject(wcid);

        if (wo is not Hotspot hotspot)
        {
            return;
        }

        var success = hotspot.Initialize(playerAttacker, defender, hotspot);

        var activator = WorldObjectFactory.CreateNewWorldObject(1053933);

        activator.Location = new Position(defender.Location)
        {
            LandblockId = new LandblockId(defender.Location.GetCell())
        };

        activator.Location.PositionZ += 0.05f;

        activator.EnterWorld();

        if (success != true)
        {
            wo.Destroy();
        }
    }

    protected virtual bool? Initialize(Player player, Creature defender, Hotspot hotspot)
    {
        Name = player.Name + "'s " + Name;

        HotspotOwner = player.Guid.Full;
        P_HotspotOwner = player;

        hotspot.Location = new Position(defender.Location)
        {
            LandblockId = new LandblockId(defender.Location.GetCell())
        };

        hotspot.Location.PositionZ += 0.05f;

        hotspot.EnterWorld();

        return true;
    }
}
