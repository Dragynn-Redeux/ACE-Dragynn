using System;
using System.Linq;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Factories.Tables;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;
using Spell = ACE.Server.Entity.Spell;
using Time = ACE.Common.Time;

namespace ACE.Server.WorldObjects;

partial class Player
{
    // Warrior
    public bool ProvokeIsActive => LastProvokeActivated > Time.GetUnixTime() - ProvokeActivatedDuration;
    private double LastProvokeActivated;
    private double ProvokeActivatedDuration = 10;

    public bool PhalanxIsActive;

    public bool RiposteIsActive => LastRiposteActivated > Time.GetUnixTime() - RiposteActivatedDuration;
    private double LastRiposteActivated;
    private double RiposteActivatedDuration = 10;

    // Blademaster
    public bool WeaponMasterSingleUseIsActive;

    public bool WeaponMasterIsActive => LastWeaponMasterActivated > Time.GetUnixTime() - WeaponMasterActivatedDuration;
    private double LastWeaponMasterActivated;
    private double WeaponMasterActivatedDuration = 10;


    public float AdrenalineMeter;
    public bool RelentlessTenacityIsActive => LastRelentlessActivated > Time.GetUnixTime() - RelentlessActivatedDuration;
    private double LastRelentlessActivated;
    private double RelentlessActivatedDuration = 10;
    public bool RelentlessStanceIsActive;
    public float TenacityLevel;

    public bool FuryEnrageIsActive => LastFuryEnrageActivated > Time.GetUnixTime() - FuryEnrageActivatedDuration;
    private double LastFuryEnrageActivated;
    private double FuryEnrageActivatedDuration = 10;
    public bool FuryStanceIsActive;
    public float EnrageLevel;

    // Archer
    public bool SteadyStrikeIsActive => LastSteadyStrikeActivated > Time.GetUnixTime() - SteadyStrikeActivatedDuration;
    private double LastSteadyStrikeActivated;
    private double SteadyStrikeActivatedDuration = 10;

    public bool MultiShotIsActive => LastMultishotActivated > Time.GetUnixTime() - MultishotActivatedDuration;
    private double LastMultishotActivated;
    private double MultishotActivatedDuration = 10;
    public int MultishotNumTargets = 1;

    public bool EvasiveStanceIsActive;

    // Vagabond
    public bool VanishIsActive => LastVanishActivated > Time.GetUnixTime() - VanishActivatedDuration;
    private double LastVanishActivated;
    private double VanishActivatedDuration = 5;

    public bool BackstabIsActive => LastBackstabActivated > Time.GetUnixTime() - BackstabActivatedDuration;
    private double LastBackstabActivated;
    private double BackstabActivatedDuration = 10;

    public bool SmokescreenIsActive => LastSmokescreenActivated > Time.GetUnixTime() - SmokescreenActivatedDuration;
    private double LastSmokescreenActivated;
    private double SmokescreenActivatedDuration = 10;

    // Sorcerer
    public bool OverloadDischargeIsActive => LastOverloadDischargeActivated > Time.GetUnixTime() - OverloadDischargeActivatedDuration;
    private double LastOverloadDischargeActivated;
    private double OverloadDischargeActivatedDuration = 10;
    public bool OverloadStanceIsActive;
    public float ManaChargeMeter = 0.0f;
    public float DischargeLevel;

    public bool BatteryDischargeIsActive => LastBatteryDischargeActivated > Time.GetUnixTime() - BatteryDischargeActivatedDuration;
    private double LastBatteryDischargeActivated;
    private double BatteryDischargeActivatedDuration = 10;
    public bool BatteryStanceIsActive;

    public bool ManaBarrierIsActive;

    // Spellsword
    public bool ReflectIsActive => LastReflectActivated > Time.GetUnixTime() - ReflectActivatedDuration;
    public bool ReflectFirstSpell = false;
    private double LastReflectActivated;
    private double ReflectActivatedDuration = 10;

    public bool AegisIsActive => LastAegisActivated > Time.GetUnixTime() - AegisActivatedDuration;
    private double LastAegisActivated;
    private double AegisActivatedDuration = 10;

    public bool EnchantedWeaponIsActive => LastEnchantedWeaponActivated > Time.GetUnixTime() - EnchantedWeaponActivatedDuration;
    private double LastEnchantedWeaponActivated;
    private double EnchantedWeaponActivatedDuration = 10;
    public Spell EnchantedBladeHighStoredSpell;
    public Spell EnchantedBladeMedStoredSpell;
    public Spell EnchantedBladeLowStoredSpell;

    public CombatAbility EquippedCombatAbility
    {
        get
        {
            var combatFocus = GetEquippedCombatFocus();
            if (combatFocus == null)
            {
                return CombatAbility.None;
            }

            var combatAbility = combatFocus.GetCombatAbility();
            return combatAbility;

        }
    }

    public bool TryUsePhalanx(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Phalanx))
        {
            return false;
        }

        if (GetEquippedShield() is null && GetEquippedWeapon() is not { IsTwoHanded: true})
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Phalanx requires an equipped shield or two-handed weapon.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (!PhalanxIsActive)
        {
            PhalanxIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You move into a defensive stance!",
                    ChatMessageType.Broadcast
                )
            );
            PlayParticleEffect(PlayScript.ShieldUpGrey, Guid);

            return false;
        }

        PhalanxIsActive = false;

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You lower your guard.", ChatMessageType.Broadcast)
        );
        PlayParticleEffect(PlayScript.DispelLife, Guid);

        return true;
    }

    public bool TryUseProvoke(WorldObject ability)
    {
        if (!VerifyCombatFocus(CombatAbility.Provoke))
        {
            return false;
        }
        LastProvokeActivated = Time.GetUnixTime();

        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Give them cause for provocation! For the next ten seconds, your attacks with at least 50% power generate double threat!.",
                ChatMessageType.Broadcast
            )
        );

        var nearbyMonsters = GetNearbyMonsters(10);

        foreach (var target in nearbyMonsters)
        {
            var skillCheck = SkillCheck.GetSkillChance(
                Strength.Current * 2,
                target.GetCreatureSkill(Skill.Perception).Current
            );

            if (ThreadSafeRandom.Next(0.0f, 1.0f) > skillCheck)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{target.Name} can see right through you. Your taunt failed.",
                        ChatMessageType.Broadcast
                    )
                );
                continue;
            }

            var areaThreatBonus = Convert.ToInt32((float)(Level ?? 1) / (target.Level ?? 1) * 100);

            target.IncreaseTargetThreatLevel(this, areaThreatBonus); // this amount is doubled from the Provoke threat bonus

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You successfully provoke {target.Name}, increasingly your threat level substantially.",
                    ChatMessageType.Broadcast
                )
            );

            PlayParticleEffect(PlayScript.VisionUpWhite, Guid);
            target.PlayParticleEffect(PlayScript.VisionDownBlack, target.Guid);
        }

        return true;
    }

    public bool TryUseRiposte(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Riposte))
        {
            return false;
        }

        if (RiposteIsActive)
        {
            return false;
        }

        LastRiposteActivated = Time.GetUnixTime();

        PlayParticleEffect(PlayScript.EnchantUpRed, Guid);

        return true;
    }

    public bool TryUseWeaponMaster(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.WeaponMaster))
        {
            return false;
        }

        if (WeaponMasterIsActive)
        {
            return false;
        }

        WeaponMasterSingleUseIsActive = true;
        LastWeaponMasterActivated = Time.GetUnixTime();

        PlayParticleEffect(PlayScript.EnchantUpOrange, Guid);

        return true;
    }

    public bool TryUseFury(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Fury))
        {
            return false;
        }

        if (!FuryStanceIsActive && !FuryEnrageIsActive)
        {
            if (RelentlessStanceIsActive)
            {
                var relentlessItem = GetInventoryItemsOfWCID(1051127);
                if (relentlessItem.Count > 0)
                {
                    EnchantmentManager.StartCooldown(relentlessItem[0]);
                }

                RelentlessStanceIsActive = false;
                AdrenalineMeter *= 0.5f;

                Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Relentless is disabled.", ChatMessageType.Broadcast)
                );
            }
            else
            {
                AdrenalineMeter = 0.0f;
            }

            FuryStanceIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You channel your inner fury, producing adrenaline each time you attack!",
                    ChatMessageType.Broadcast
                )
            );

            PlayParticleEffect(PlayScript.SkillUpOrange, Guid, AdrenalineMeter);

            return false;
        }

        if (FuryEnrageIsActive)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You cannot activate Fury while Enraged.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (RelentlessTenacityIsActive)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You cannot activate Fury while Tenacious.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (FuryStanceIsActive && AdrenalineMeter < 0.5f)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You calm down and your release adrenaline without effect.", ChatMessageType.Broadcast)
            );

            PlayParticleEffect(PlayScript.SkillDownOrange, Guid, AdrenalineMeter);

            FuryStanceIsActive = false;
            AdrenalineMeter = 0.0f;

            return true;
        }

        FuryStanceIsActive = false;
        EnrageLevel = AdrenalineMeter;
        AdrenalineMeter = 0.0f;
        LastFuryEnrageActivated = Time.GetUnixTime();

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You Enrage, converting your adrenaline into pure fury! ({Math.Round(EnrageLevel * 100)}%)", ChatMessageType.Broadcast)
        );
        PlayParticleEffect(PlayScript.EnchantUpOrange, Guid, EnrageLevel);

        return true;
    }

    public bool TryUseRelentless(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Relentless))
        {
            return false;
        }

        if (!RelentlessStanceIsActive && !RelentlessTenacityIsActive && !FuryEnrageIsActive)
        {
            if (FuryStanceIsActive)
            {
                var furyItem = GetInventoryItemsOfWCID(1051135);
                if (furyItem.Count > 0)
                {
                    EnchantmentManager.StartCooldown(furyItem[0]);
                }

                FuryStanceIsActive = false;
                AdrenalineMeter *= 0.5f;

                Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Fury is disabled.", ChatMessageType.Broadcast)
                );

                PlayParticleEffect(PlayScript.SkillDownOrange, Guid);
            }
            else
            {
                AdrenalineMeter = 0.0f;
            }

            RelentlessStanceIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You enter a state of relentless assault, producing adrenaline each time you attack!",
                    ChatMessageType.Broadcast
                )
            );

            PlayParticleEffect(PlayScript.RegenUpYellow, Guid, AdrenalineMeter);

            return false;
        }

        if (FuryEnrageIsActive)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You cannot activate Relentless while Enraged.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (RelentlessTenacityIsActive)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You cannot activate Relentless while Tenacious.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (RelentlessStanceIsActive && AdrenalineMeter < 0.5f)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You release your adrenaline to no effect.", ChatMessageType.Broadcast)
            );

            PlayParticleEffect(PlayScript.RegenDownYellow, Guid, AdrenalineMeter);

            RelentlessStanceIsActive = false;
            AdrenalineMeter = 0.0f;

            return true;
        }

        RelentlessStanceIsActive = false;
        TenacityLevel = AdrenalineMeter;

        AdrenalineMeter = 0.0f;
        LastRelentlessActivated = Time.GetUnixTime();

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You convert your adrenaline into Tenacity, reducing the cost of your attacks! ({Math.Round(TenacityLevel * 100)}%)", ChatMessageType.Broadcast)
        );

        PlayParticleEffect(PlayScript.EnchantUpOrange, Guid, TenacityLevel);

        return true;
    }

    public bool TryUseMultishot(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Multishot))
        {
            return false;
        }

        if (MultiShotIsActive)
        {
            return false;
        }

        LastMultishotActivated = Time.GetUnixTime();

        PlayParticleEffect(PlayScript.EnchantUpYellow, Guid);

        return true;
    }

    public bool TryUseSteadyStrike(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.SteadyStrike))
        {
            return false;
        }

        if (SteadyStrikeIsActive)
        {
            return false;
        }

        LastSteadyStrikeActivated = Time.GetUnixTime();

        PlayParticleEffect(PlayScript.SkillUpYellow, Guid);

        return true;
    }

    public bool TryUseBackstab(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Backstab))
        {
            return false;
        }

        if (BackstabIsActive)
        {
            return false;
        }

        LastBackstabActivated = Time.GetUnixTime();

        PlayParticleEffect(PlayScript.EnchantUpGreen, Guid);

        return true;
    }

    public bool TryUseSmokescreen(WorldObject ability)
    {
        if (!VerifyCombatFocus(CombatAbility.Smokescreen))
        {
            return false;
        }

        var nearbyMonsters = GetNearbyMonsters(15);

        foreach (var target in nearbyMonsters)
        {
            var skillCheck = SkillCheck.GetSkillChance(
                GetModdedDeceptionSkill(),
                target.GetCreatureSkill(Skill.Perception).Current
            );

            if (ThreadSafeRandom.Next(0.0f, 1.0f) > skillCheck)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{target.Name} can see right through you. Your attempt at reducing threat failed.",
                        ChatMessageType.Broadcast
                    )
                );
                continue;
            }

            target.IncreaseTargetThreatLevel(this, -100);
            LastSmokescreenActivated = Time.GetUnixTime();

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You successfully reduce your threat level towards {target.Name}.",
                    ChatMessageType.Broadcast
                )
            );

            PlayParticleEffect(PlayScript.VisionUpWhite, Guid);
            target.PlayParticleEffect(PlayScript.VisionDownBlack, target.Guid);
        }

        return true;
    }

    public bool TryUseVanish(WorldObject ability)
    {
        if (!VerifyCombatFocus(CombatAbility.Vanish))
        {
            return false;
        }

        if (IsStealthed)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You cannot use Vanish while stealthed.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var thieverySkill = GetCreatureSkill(Skill.Thievery);

        if (thieverySkill.AdvancementClass < SkillAdvancementClass.Trained)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Vanish requires trained Thievery.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var nearbyMonsters = GetNearbyMonsters(50);
        var attackingMonsters = nearbyMonsters.Where(m => m.AttackTarget == this).ToList();
        var smoke = WorldObjectFactory.CreateNewWorldObject(1051113);

        // Early exit if no monsters are attacking
        if (attackingMonsters.Count == 0)
        {
            if (smoke != null)
            {
                smoke.Location = Location;
                smoke.EnterWorld();
            }

            LastVanishActivated = Time.GetUnixTime();
            BeginStealth();
            return true;
        }

        // Calculate total stamina cost: average monster level + (1/10th of each monster level, rounded down)
        var totalMonsterLevels = 0;
        var tenthLevelSum = 0;

        foreach (var target in attackingMonsters)
        {
            var monsterLevel = target.Level ?? 1;
            totalMonsterLevels += monsterLevel;
            tenthLevelSum += monsterLevel / 10; // Integer division automatically rounds down
        }

        var averageLevel = totalMonsterLevels / attackingMonsters.Count; // Integer division rounds down
        var totalStaminaCost = averageLevel + tenthLevelSum;

        if (Stamina.Current < totalStaminaCost)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You do not have enough stamina to vanish from all attacking enemies. ({totalStaminaCost} stamina required)",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var fooledMonsters = 0;

        // Check each monster individually
        foreach (var target in attackingMonsters)
        {
            var playerThieverySkill = GetModdedThieverySkill();
            var monsterPerception = target.GetModdedPerceptionSkill();
            var skillCheck = SkillCheck.GetSkillChance(playerThieverySkill, monsterPerception);
            var roll = ThreadSafeRandom.Next(0.0f, 1.0f);

            if (roll > skillCheck)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{target.Name} was not fooled by your attempt to vanish!",
                        ChatMessageType.Broadcast
                    )
                );
            }
            else
            {
                fooledMonsters++;
                target.AddVanishedPlayer(this);
            }
        }

        // Apply stamina cost (you attempted the ability)
        UpdateVitalDelta(Stamina, -totalStaminaCost);

        // Always create smoke effect when at least one monster was rolled against
        if (smoke != null)
        {
            smoke.Location = Location;
            smoke.EnterWorld();
        }

        // Only enter stealth if all monsters were fooled
        if (fooledMonsters == attackingMonsters.Count)
        {
            LastVanishActivated = Time.GetUnixTime();
            BeginStealth();

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You vanish into the smoke!",
                    ChatMessageType.Broadcast
                )
            );
        }
        else if (fooledMonsters > 0)
        {
            LastVanishActivated = Time.GetUnixTime();

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You vanish into the smoke, though some enemies remain aware of your presence!",
                    ChatMessageType.Broadcast
                )
            );
        }
        else
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Your vanish attempt fails completely - no enemies were fooled!",
                    ChatMessageType.Broadcast
                )
            );
        }

        return true;
    }

    public bool TryUseOverload(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Overload))
        {
            return false;
        }

        if (!OverloadStanceIsActive && !OverloadDischargeIsActive && !BatteryDischargeIsActive)
        {
            if (BatteryStanceIsActive)
            {
                var batteryItem = GetInventoryItemsOfWCID(1051132);
                if (batteryItem.Count > 0)
                {
                    EnchantmentManager.StartCooldown(batteryItem[0]);
                }

                BatteryStanceIsActive = false;
                ManaChargeMeter *= 0.5f;

                Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Battery is disabled.", ChatMessageType.Broadcast)
                );
            }
            else
            {
                ManaChargeMeter = 0.0f;
            }

            OverloadStanceIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You begin to infuse your spells with extra mana, producing charge!",
                    ChatMessageType.Broadcast
                )
            );

            PlayParticleEffect(PlayScript.SkillUpBlue, Guid);

            return false;
        }

        if (OverloadDischargeIsActive)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You cannot activate Overload while discharging.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (OverloadStanceIsActive && ManaChargeMeter < 0.5f)
        {
            OverloadStanceIsActive = false;
            ManaChargeMeter = 0.0f;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You release your charged mana to no effect.", ChatMessageType.Broadcast)
            );

            PlayParticleEffect(PlayScript.SkillDownBlue, Guid);

            return true;
        }

        OverloadStanceIsActive = false;
        DischargeLevel = ManaChargeMeter;
        ManaChargeMeter = 0.0f;
        LastOverloadDischargeActivated = Time.GetUnixTime();

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You unleash your charged mana, increasing the effectiveness of your spells by {Math.Round(DischargeLevel * 100)}%!", ChatMessageType.Broadcast)
        );
        PlayParticleEffect(PlayScript.EnchantUpBlue, Guid);

        return true;
    }

    public bool TryUseBattery(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Battery))
        {
            return false;
        }

        if (!BatteryStanceIsActive && !BatteryDischargeIsActive && !OverloadDischargeIsActive)
        {
            if (OverloadStanceIsActive)
            {
                var overloadItem = GetInventoryItemsOfWCID(1051133);
                if (overloadItem.Count > 0)
                {
                    EnchantmentManager.StartCooldown(overloadItem[0]);
                }

                OverloadStanceIsActive = false;
                ManaChargeMeter *= 0.5f;

                Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"Overload is disabled.", ChatMessageType.Broadcast)
                );

                PlayParticleEffect(PlayScript.SkillDownBlue, Guid);
            }
            else
            {
                ManaChargeMeter = 0.0f;
            }

            BatteryStanceIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You begin to siphon mana from your spells, producing charge!",
                    ChatMessageType.Broadcast
                )
            );

            PlayParticleEffect(PlayScript.SkillUpBlue, Guid);

            return false;
        }

        if (OverloadDischargeIsActive || BatteryDischargeIsActive)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You cannot activate Battery while discharging.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (BatteryStanceIsActive && ManaChargeMeter < 0.5f)
        {
            BatteryStanceIsActive = false;
            ManaChargeMeter = 0.0f;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You release your charged mana to no effect.", ChatMessageType.Broadcast)
            );

            PlayParticleEffect(PlayScript.SkillDownBlue, Guid);

            return true;
        }

        BatteryStanceIsActive = false;
        DischargeLevel = ManaChargeMeter;
        ManaChargeMeter = 0.0f;
        LastBatteryDischargeActivated = Time.GetUnixTime();

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You unleash your charged mana, reducing the cost of your spells by {Math.Round(DischargeLevel * 100)}%!", ChatMessageType.Broadcast)
        );
        PlayParticleEffect(PlayScript.EnchantUpBlue, Guid);

        return true;
    }

    public bool TryUseEnchantedBlade(WorldObject ability)
    {
        if (!VerifyCombatFocus(CombatAbility.EnchantedBlade))
        {
            return false;
        }

        var equippedMeleeWeapon = GetEquippedMeleeWeapon();
        if (equippedMeleeWeapon is null)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{ability.Name} can only be used while a melee weapon is equipped.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (!equippedMeleeWeapon.ProcSpell.HasValue)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Equipped melee weapon must have a Cast-on-strike spell to use Enchanted Blade.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var weaponProcSpell = new Spell(equippedMeleeWeapon.ProcSpell.Value);
        var magicSchool = weaponProcSpell.School;

        if (magicSchool is MagicSchool.WarMagic
            && GetCreatureSkill(Skill.WarMagic).AdvancementClass < SkillAdvancementClass.Trained)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{ability.Name} with this weapon requires trained war magic.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        if (magicSchool is MagicSchool.LifeMagic
            && GetCreatureSkill(Skill.LifeMagic).AdvancementClass < SkillAdvancementClass.Trained)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{ability.Name} with this weapon requires trained life magic.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var weaponDamageType = equippedMeleeWeapon.W_DamageType;
        if (weaponDamageType is DamageType.SlashPierce)
        {
            weaponDamageType = SlashThrustToggle ? DamageType.Pierce : DamageType.Slash;
        }

        var baseSpellHighAttack = magicSchool is MagicSchool.WarMagic
            ? GetLevelOneBlastOfDamageType(weaponDamageType)
            : new Spell(SpellId.HealSelf1);

        var baseSpellMedAttack = magicSchool is MagicSchool.WarMagic
            ? GetLevelOneBoltOfDamageType(weaponDamageType)
            : new Spell(SpellId.RevitalizeSelf1);

        var baseSpellLowAttack = magicSchool is MagicSchool.WarMagic
            ? GetLevelOneVolleyOfDamageType(weaponDamageType)
            : new Spell(SpellId.ManaBoostSelf1);

        if (true) // TODO: If player has upgraded Enchanted Blade to use advanced spells, prevent this block from running
        {
            if (magicSchool is MagicSchool.WarMagic)
            {
                baseSpellHighAttack = GetLevelOneBoltOfDamageType(weaponDamageType);
                baseSpellMedAttack = GetLevelOneBoltOfDamageType(weaponDamageType);
                baseSpellLowAttack = GetLevelOneBoltOfDamageType(weaponDamageType);
            }
            else
            {
                baseSpellHighAttack = new Spell(SpellId.HealSelf1);
                baseSpellMedAttack = new Spell(SpellId.HealSelf1);
                baseSpellLowAttack = new Spell(SpellId.HealSelf1);
            }
        }

        if (baseSpellHighAttack is null || baseSpellMedAttack is null || baseSpellLowAttack is null)
        {
            _log.Error("TryUseEnchantedBlade() - baseSpell is null");
            return false;
        }

        var weaponSpellcraft = equippedMeleeWeapon.ItemSpellcraft ?? 50;
        weaponSpellcraft += (int)CheckForArcaneLoreSpecSpellcraftBonus(this);

        var magicSkill = magicSchool is MagicSchool.WarMagic ? GetModdedWarMagicSkill() : GetModdedLifeMagicSkill();
        magicSkill += (uint)(weaponSpellcraft * 0.1);

        var roll = Convert.ToInt32(ThreadSafeRandom.Next(magicSkill * 0.5f, magicSkill));
        int[] diff = [50, 100, 200, 300, 350, 400, 450];
        var closest = diff.MinBy(x => Math.Abs(x - roll));
        var level = Array.IndexOf(diff, closest);

        var finalHighSpellId = SpellLevelProgression.GetSpellAtLevel((SpellId)baseSpellHighAttack.Id, level + 1);
        var finalMedSpellId = SpellLevelProgression.GetSpellAtLevel((SpellId)baseSpellMedAttack.Id, level + 1);
        var finalLowSpellId = SpellLevelProgression.GetSpellAtLevel((SpellId)baseSpellLowAttack.Id, level + 1);

        EnchantedBladeHighStoredSpell = new Spell(finalHighSpellId);
        EnchantedBladeMedStoredSpell = new Spell(finalMedSpellId);
        EnchantedBladeLowStoredSpell = new Spell(finalLowSpellId);

        var manaCost = (int)EnchantedBladeHighStoredSpell.BaseMana;
        if (Mana.Current < manaCost)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You do not have enough mana.", ChatMessageType.Broadcast)
            );
            return false;
        }

        if (BatteryDischargeIsActive)
        {
            manaCost = 0;
        }
        else if (BatteryStanceIsActive)
        {
            var batteryMod = ManaChargeMeter * 0.5f;
            manaCost = (int)(manaCost * (1.0f - batteryMod));
        }

        UpdateVitalDelta(Mana, -manaCost);

        var particalIntensity = Math.Clamp((level - 1) * (1.0f / 6.0f), 0.0f, 1.0f);
        //var playScript = GetPlayScriptColor(weaponDamageType);

        PlayParticleEffect(PlayScript.EnchantUpPurple, Guid, particalIntensity);

        return true;
    }

    private PlayScript GetPlayScriptColor(DamageType weaponDamageType)
    {
        return weaponDamageType switch
        {
            DamageType.Slash => PlayScript.EnchantUpOrange,
            DamageType.Pierce => PlayScript.EnchantUpYellow,
            DamageType.Bludgeon => PlayScript.EnchantUpWhite,
            DamageType.Cold => PlayScript.EnchantUpBlue,
            DamageType.Fire => PlayScript.EnchantUpRed,
            DamageType.Acid => PlayScript.EnchantUpGreen,
            DamageType.Electric => PlayScript.EnchantUpPurple,
            _ => PlayScript.EnchantUpGrey
        };
    }

    private Spell GetLevelOneBoltOfDamageType(DamageType weaponDamageType)
    {
        return weaponDamageType switch
        {
            DamageType.Slash => new Spell(SpellId.WhirlingBlade1),
            DamageType.Pierce => new Spell(SpellId.ForceBolt1),
            DamageType.Bludgeon => new Spell(SpellId.ShockWave1),
            DamageType.Cold => new Spell(SpellId.FrostBolt1),
            DamageType.Fire => new Spell(SpellId.FlameBolt1),
            DamageType.Acid => new Spell(SpellId.AcidStream1),
            DamageType.Electric => new Spell(SpellId.LightningBolt1),
            _ => null
        };
    }

    private Spell GetLevelOneBlastOfDamageType(DamageType weaponDamageType)
    {
        return weaponDamageType switch
        {
            DamageType.Slash => new Spell(SpellId.BladeBlast1),
            DamageType.Pierce => new Spell(SpellId.ForceBlast1),
            DamageType.Bludgeon => new Spell(SpellId.ShockBlast1),
            DamageType.Cold => new Spell(SpellId.FrostBlast1),
            DamageType.Fire => new Spell(SpellId.FlameBlast1),
            DamageType.Acid => new Spell(SpellId.AcidBlast1),
            DamageType.Electric => new Spell(SpellId.LightningBlast1),
            _ => null
        };
    }

    private Spell GetLevelOneVolleyOfDamageType(DamageType weaponDamageType)
    {
        return weaponDamageType switch
        {
            DamageType.Slash => new Spell(SpellId.BladeVolley1),
            DamageType.Pierce => new Spell(SpellId.ForceVolley1),
            DamageType.Bludgeon => new Spell(SpellId.BludgeoningVolley1),
            DamageType.Cold => new Spell(SpellId.FrostVolley1),
            DamageType.Fire => new Spell(SpellId.FlameVolley1),
            DamageType.Acid => new Spell(SpellId.AcidVolley1),
            DamageType.Electric => new Spell(SpellId.LightningVolley1),
            _ => null
        };
    }

    public bool TryUseReflect(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Reflect))
        {
            return false;
        }

        if (ReflectIsActive)
        {
            return false;
        }

        LastReflectActivated = Time.GetUnixTime();

        if (GetCreatureSkill(Skill.MagicDefense).AdvancementClass is SkillAdvancementClass.Specialized)
        {
            ReflectFirstSpell = true;
        }

        PlayParticleEffect(PlayScript.SkillUpPurple, Guid);

        return true;
    }

    public bool TryUseAegis(Gem gem)
    {
        if (!VerifyCombatFocus(CombatAbility.Aegis))
        {
            return false;
        }

        if (AegisIsActive)
        {
            return false;
        }

        var baseSpell = LastHitReceivedDamageType switch
        {
            DamageType.Slash => new Spell(SpellId.BladeProtectionSelf1),
            DamageType.Pierce => new Spell(SpellId.PiercingProtectionSelf1),
            DamageType.Bludgeon => new Spell(SpellId.BludgeonProtectionSelf1),
            DamageType.Cold => new Spell(SpellId.ColdProtectionSelf1),
            DamageType.Fire => new Spell(SpellId.FireProtectionSelf1),
            DamageType.Acid => new Spell(SpellId.AcidProtectionSelf1),
            DamageType.Electric => new Spell(SpellId.LightningProtectionSelf1),
            _ => null
        };

        if (baseSpell is null)
        {
            _log.Error("TryUseAegis() - baseSpell is null");
            return false;
        }

        var equippedWeapon = GetEquippedWeapon();
        if (equippedWeapon is null)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Aegis can only be used while a weapon is equipped.",
                    ChatMessageType.Broadcast
                )
            );
            return false;
        }

        var weaponSpellcraft = equippedWeapon.ItemSpellcraft;
        if (weaponSpellcraft is null)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Aegis can only be used with a weapon that has spellcraft.",
                    ChatMessageType.Broadcast
                )
            );

            return false;
        }

        var roll = Convert.ToInt32(ThreadSafeRandom.Next(weaponSpellcraft.Value * 0.5f, weaponSpellcraft.Value));
        int[] diff = [50, 100, 200, 300, 350, 400, 450];
        var closest = diff.MinBy(x => Math.Abs(x - roll));
        var level = Array.IndexOf(diff, closest);

        var finalSpellId = SpellLevelProgression.GetSpellAtLevel((SpellId)baseSpell.Id, level + 1);

        TryCastSpell(new Spell(finalSpellId), this);

        LastAegisActivated = Time.GetUnixTime();

        return true;
    }

    public bool TryUseManaBarrier()
    {
        if (!VerifyCombatFocus(CombatAbility.ManaBarrier))
        {
            return false;
        }

        if (!ManaBarrierIsActive)
        {
            ManaBarrierIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You draw on your stored mana to form an enchanted shield around yourself!",
                    ChatMessageType.Broadcast
                )
            );
            PlayParticleEffect(PlayScript.ShieldUpBlue, Guid);

            return false;
        }

        ManaBarrierIsActive = false;

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You dispel your mana barrier.", ChatMessageType.Broadcast)
        );
        PlayParticleEffect(PlayScript.DispelLife, Guid);

        return false;
    }

    public bool TryUseEvasiveStance()
    {
        if (!VerifyCombatFocus(CombatAbility.EvasiveStance))
        {
            return false;
        }

        if (!EvasiveStanceIsActive)
        {
            EvasiveStanceIsActive = true;

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You move into an evasive stance!",
                    ChatMessageType.Broadcast
                )
            );
            PlayParticleEffect(PlayScript.ShieldUpYellow, Guid);

            return false;
        }

        EvasiveStanceIsActive = false;

        Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You move out of your evasive stance.", ChatMessageType.Broadcast)
        );
        PlayParticleEffect(PlayScript.DispelLife, Guid);

        return false;
    }

    public bool TryUseExposePhysicalWeakness(WorldObject ability)
    {
        var target = LastAttackedCreature;

        if (target is null || this == target || target is { IsDead: true })
        {
            return false;
        }

        var targetAsPlayer = target as Player;

        var attackerPerceptionSkill = GetModdedPerceptionSkill();
        var targetDeceptionSkill = target.GetModdedDeceptionSkill();

        var avoidChance = 1.0f - SkillCheck.GetSkillChance(attackerPerceptionSkill, targetDeceptionSkill);

        var roll = ThreadSafeRandom.Next(0.0f, 1.0f);
        if (avoidChance > roll)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{target.Name}'s deception prevents you from exposing a weakness!",
                    ChatMessageType.Broadcast
                )
            );

            if (targetAsPlayer != null)
            {
                Proficiency.OnSuccessUse(
                    targetAsPlayer,
                    target.GetCreatureSkill(Skill.Deception),
                    attackerPerceptionSkill
                );
                targetAsPlayer.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Your deception prevents {Name} from exposing a weakness!",
                        ChatMessageType.Broadcast
                    )
                );
            }
            return true;
        }

        Proficiency.OnSuccessUse(this, GetCreatureSkill(Skill.Perception), targetDeceptionSkill);

        var vulnerabilitySpellLevels = SpellLevelProgression.GetSpellLevels(SpellId.VulnerabilityOther1);
        var imperilSpellLevels = SpellLevelProgression.GetSpellLevels(SpellId.ImperilOther1);

        if (vulnerabilitySpellLevels.Count == 0 || imperilSpellLevels.Count == 0)
        {
            return false;
        }

        var overRoll = roll - avoidChance;

        var maxSpellLevel = (int)Math.Clamp(Math.Floor((double)attackerPerceptionSkill / 50), 1, 7);
        if (IsShrouded())
        {
            maxSpellLevel = Math.Min(maxSpellLevel, LevelScaling.MaxExposeSpellLevel(target));
        }

        var spellLevel = (int)Math.Clamp(Math.Floor(overRoll * 10), 1, maxSpellLevel);

        var vulnerabilitySpell = new Spell(vulnerabilitySpellLevels[spellLevel - 1]);
        var imperilSpellLevel = new Spell(imperilSpellLevels[spellLevel - 1]);

        string spellTypePrefix;
        switch (spellLevel)
        {
            default:
                spellTypePrefix = "a slight";
                break;
            case 2:
                spellTypePrefix = "a minor";
                break;
            case 3:
                spellTypePrefix = "a moderate";
                break;
            case 4:
                spellTypePrefix = "a major";
                break;
            case 5:
                spellTypePrefix = "a severe";
                break;
            case 6:
                spellTypePrefix = "a crippling";
                break;
            case 7:
                spellTypePrefix = "a tremendous";
                break;
        }

        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"Your perception allows you to expose {spellTypePrefix} physical weakness on {target.Name}!",
                ChatMessageType.Broadcast
            )
        );
        if (targetAsPlayer != null)
        {
            targetAsPlayer.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{Name}'s perception exposes {spellTypePrefix} physical weakness on you!",
                    ChatMessageType.Broadcast
                )
            );
        }

        if (vulnerabilitySpell.NonComponentTargetType == ItemType.None)
        {
            TryCastSpell(vulnerabilitySpell, null, this, null, false, false, false, false);
        }
        else
        {
            TryCastSpell(vulnerabilitySpell, target, this, null, false, false, false, false);
        }

        if (GetCreatureSkill(Skill.Perception).AdvancementClass == SkillAdvancementClass.Specialized)
        {
            if (imperilSpellLevel.NonComponentTargetType == ItemType.None)
            {
                TryCastSpell(imperilSpellLevel, null, this, null, false, false, false, false);
            }
            else
            {
                TryCastSpell(imperilSpellLevel, target, this, null, false, false, false, false);
            }
        }

        if (targetAsPlayer == null)
        {
            CheckForSigilTrinketOnCastEffects(target, vulnerabilitySpell, false, Skill.Perception, SigilTrinketPerceptionEffect.Exposure);
        }

        return true;
    }

    public bool TryUseExposeMagicalWeakness(WorldObject ability)
    {
        {
            var target = LastAttackedCreature;

            if (target is null || this == target || target is {IsDead: true })
            {
                return false;
            }

            var targetAsPlayer = target as Player;

            var attackerPerceptionSkill = GetModdedPerceptionSkill();
            var targetDeceptionSkill = target.GetModdedDeceptionSkill();

            var avoidChance = 1.0f - SkillCheck.GetSkillChance(attackerPerceptionSkill, targetDeceptionSkill);

            var roll = ThreadSafeRandom.Next(0.0f, 1.0f);
            if (avoidChance > roll)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{target.Name}'s deception prevents you from exposing a weakness!",
                        ChatMessageType.Broadcast
                    )
                );

                if (targetAsPlayer != null)
                {
                    Proficiency.OnSuccessUse(
                        targetAsPlayer,
                        target.GetCreatureSkill(Skill.Deception),
                        attackerPerceptionSkill
                    );
                    targetAsPlayer.Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Your deception prevents {Name} from exposing a weakness!",
                            ChatMessageType.Broadcast
                        )
                    );
                }
                return true;
            }

            Proficiency.OnSuccessUse(this, GetCreatureSkill(Skill.Perception), targetDeceptionSkill);

            var magicYieldSpellLevels = SpellLevelProgression.GetSpellLevels(SpellId.MagicYieldOther1);
            var succumbSpellLevels = SpellLevelProgression.GetSpellLevels(SpellId.ExposeWeakness1); // Succumb

            if (magicYieldSpellLevels.Count == 0 || succumbSpellLevels.Count == 0)
            {
                return false;
            }

            var overRoll = roll - avoidChance;

            var maxSpellLevel = (int)Math.Clamp(Math.Floor((double)attackerPerceptionSkill / 50), 1, 7);
            if (IsShrouded())
            {
                maxSpellLevel = Math.Min(maxSpellLevel, LevelScaling.MaxExposeSpellLevel(target));
            }

            var spellLevel = (int)Math.Clamp(Math.Floor(overRoll * 10), 1, maxSpellLevel);

            var magicYieldSpell = new Spell(magicYieldSpellLevels[spellLevel - 1]);
            var succumbSpellLevel = new Spell(succumbSpellLevels[spellLevel - 1]);

            string spellTypePrefix;
            switch (spellLevel)
            {
                default:
                    spellTypePrefix = "a slight";
                    break;
                case 2:
                    spellTypePrefix = "a minor";
                    break;
                case 3:
                    spellTypePrefix = "a moderate";
                    break;
                case 4:
                    spellTypePrefix = "a major";
                    break;
                case 5:
                    spellTypePrefix = "a severe";
                    break;
                case 6:
                    spellTypePrefix = "a crippling";
                    break;
                case 7:
                    spellTypePrefix = "a tremendous";
                    break;
            }

            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Your perception allows you to expose {spellTypePrefix} magical weakness on {target.Name}!",
                    ChatMessageType.Broadcast
                )
            );
            if (targetAsPlayer != null)
            {
                targetAsPlayer.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"{Name}'s perception exposes {spellTypePrefix} magical weakness on you!",
                        ChatMessageType.Broadcast
                    )
                );
            }

            if (magicYieldSpell.NonComponentTargetType == ItemType.None)
            {
                TryCastSpell(magicYieldSpell, null, this, null, false, false, false, false);
            }
            else
            {
                TryCastSpell(magicYieldSpell, target, this, null, false, false, false, false);
            }

            if (GetCreatureSkill(Skill.Perception).AdvancementClass == SkillAdvancementClass.Specialized)
            {
                if (succumbSpellLevel.NonComponentTargetType == ItemType.None)
                {
                    TryCastSpell(succumbSpellLevel, null, this, null, false, false, false, false);
                }
                else
                {
                    TryCastSpell(succumbSpellLevel, target, this, null, false, false, false, false);
                }
            }

            if (targetAsPlayer == null)
            {
                CheckForSigilTrinketOnCastEffects(target, magicYieldSpell, false, Skill.Perception, SigilTrinketPerceptionEffect.Exposure);
            }
        }

        return true;
    }

    public void TryUseStealth()
    {
        if (!IsStealthed)
        {
            var mostRecentAttackEventTime = LastAttackTime > LastAttackReceivedTime ? LastAttackTime : LastAttackReceivedTime;

            if (Time.GetUnixTime() - mostRecentAttackEventTime < 10.0)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You cannot use Stealth if you have attacked, or received an attack, within the last 10 seconds.",
                        ChatMessageType.Broadcast
                    )
                );

                return;
            }

            BeginStealth();
        }
        else
        {
            EndStealth();
        }
    }

    public void TryUseShroud()
    {
        if (EnchantmentManager.HasSpell((int)SpellId.Shrouded))
        {
            if (IsBusy)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You cannot dispel the Shroud while performing other actions.",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            if (Teleporting)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You cannot dispel the Shroud while teleporting.",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            if (LastSuccessCast_Time > Time.GetUnixTime() - 5.0)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You cannot dispel the Shroud if you have recently cast a spell.",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }
            if (CurrentLandblock != null && CurrentLandblock.IsDungeon)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You cannot dispel the Shroud while inside a dungeon.",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            if (Fellowship != null)
            {
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You must leave your Fellowship before you can dispel the Shroud.",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }

            var enchantment = EnchantmentManager.GetEnchantment((int)SpellId.Shrouded);
            if (enchantment != null)
            {
                EnchantmentManager.Dispel(enchantment);
                HandleSpellHooks(new Spell((int)SpellId.Shrouded));
                PlayParticleEffect(PlayScript.DispelCreature, Guid);
                Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You dispel the Shroud, and your innate strength returns.",
                        ChatMessageType.Broadcast
                    )
                );
            }
        }
        else
        {
            var spell = new Spell((int)SpellId.Shrouded);
            var addResult = EnchantmentManager.Add(spell, null, null, true);
            Session.Network.EnqueueSend(
                new GameEventMagicUpdateEnchantment(
                    Session,
                    new Enchantment(this, addResult.Enchantment)
                )
            );
            HandleSpellHooks(spell);
            PlayParticleEffect(PlayScript.SkillDownVoid, Guid);
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You activate the crystal, shrouding yourself and reducing your innate power.",
                    ChatMessageType.Broadcast
                )
            );
        }
    }

    private bool VerifyCombatFocus(CombatAbility combatAbility)
    {
        switch (combatAbility)
        {
            case CombatAbility.Phalanx:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId: (int)CombatFocusType.Warrior})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Phalanx can only be used with a Warrior Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Provoke:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Warrior
                        or (int)CombatFocusType.Spellsword
                        or (int)CombatFocusType.Blademaster})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Provoke can only be used with a Warrior Focus, Blademaster Focus, or Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Riposte:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Warrior
                        or (int)CombatFocusType.Spellsword
                        or (int)CombatFocusType.Blademaster})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Riposte can only be used with a Warrior Focus, Blademaster Focus, or Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.WeaponMaster:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId: (int)CombatFocusType.Blademaster})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Weapon Master can only be used with a Blademaster Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Fury:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Blademaster
                        or (int)CombatFocusType.Warrior
                        or (int)CombatFocusType.Archer})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Fury can only be used with a Blademaster Focus, Warrior Focus, or Archer Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Relentless:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Blademaster
                        or (int)CombatFocusType.Warrior
                        or (int)CombatFocusType.Archer})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Relentless can only be used with a Blademaster Focus, Warrior Focus, or Archer Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Multishot:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId: (int)CombatFocusType.Archer})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Multishot can only be used with an Archer Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.SteadyStrike:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Archer
                        or (int)CombatFocusType.Blademaster
                        or (int)CombatFocusType.Vagabond})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Steady Strike can only be used with an Archer Focus, Blademaster Focus, or Vagabond Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.ManaBarrier:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Sorcerer
                        or (int)CombatFocusType.Vagabond
                        or (int)CombatFocusType.Spellsword})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Mana Barrier can only be used with a Sorcerer Focus, Vagabond Focus, or Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.EvasiveStance:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Blademaster
                        or (int)CombatFocusType.Archer
                        or (int)CombatFocusType.Vagabond})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Evasive Stance can only be used with a Blademaster Focus, Archer Focus, or Vagabond Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Vanish:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId: (int)CombatFocusType.Vagabond})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Vanish can only be used with a Vagabond Focus.",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Smokescreen:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Vagabond
                        or (int)CombatFocusType.Archer
                        or (int)CombatFocusType.Sorcerer})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Smokescreen can only be used with a Vagabond Focus, Archer Focus, or Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Backstab:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Vagabond
                        or (int)CombatFocusType.Archer
                        or (int)CombatFocusType.Sorcerer})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Backstab can only be used with a Vagabond Focus, Archer Focus, or Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Overload:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId: (int)CombatFocusType.Sorcerer})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Overload can only be used with a Sorcerer Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Battery:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Sorcerer
                        or (int)CombatFocusType.Vagabond
                        or (int)CombatFocusType.Spellsword})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Battery can only be used with a Sorcerer Focus, Vagabond Focus, or Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.EnchantedBlade:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId: (int)CombatFocusType.Spellsword})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Enchanted Blade can only be used with a Spellsword Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Aegis:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Spellsword
                        or (int)CombatFocusType.Sorcerer
                        or (int)CombatFocusType.Warrior})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Aegis can only be used with a Spellsword Focus, Sorcerer Focus, or Warrior Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
            case CombatAbility.Reflect:
                if (GetEquippedCombatFocus() is not {CombatFocusTypeId:
                        (int)CombatFocusType.Spellsword
                        or (int)CombatFocusType.Sorcerer
                        or (int)CombatFocusType.Warrior})
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"Reflect can only be used with a Spellsword Focus, Sorcerer Focus, or Warrior Focus",
                            ChatMessageType.Broadcast
                        )
                    );
                    return false;
                }
                break;
        }

        return true;
    }

    public void IncreaseChargedMeter(Spell spell, bool fromProc = false)
    {
        if (fromProc)
        {
            ManaChargeMeter += 0.25f;
            if (ManaChargeMeter > 1.0f)
            {
                ManaChargeMeter = 1.0f;
            }
            return;
        }

        var animationLength = WeaponAnimationLength.GetSpellCastAnimationLength(ProjectileSpellType.Arc, spell.Level);

        ManaChargeMeter += 0.1f * animationLength;
        if (ManaChargeMeter > 1.0f)
        {
            ManaChargeMeter = 1.0f;
        }
    }

    public void IncreaseRelentlessAdrenalineMeter(WorldObject weapon)
    {
        var powerBarTime = GetPowerAccuracyBar();

        if (powerBarTime <= 0.5)
        {
            var weaponAnimTime = WeaponAnimationLength.GetWeaponAnimLength(weapon);

            if (weapon is { IsTwoHanded: true } or { W_AttackType: AttackType.DoubleStrike })
            {
                weaponAnimTime *= 0.5f;
            }

            if (weapon is { W_AttackType: AttackType.TripleStrike })
            {
                weaponAnimTime *= 0.33f;
            }

            AdrenalineMeter += weaponAnimTime * 0.05f;

            if (AdrenalineMeter > 1.0f)
            {
                AdrenalineMeter = 1.0f;
            }
        }
    }

    public void IncreaseFuryAdrenalineMeter(WorldObject weapon)
    {
        var powerBarTime = GetPowerAccuracyBar();

        if (powerBarTime >= 0.5)
        {
            var weaponAnimTime = WeaponAnimationLength.GetWeaponAnimLength(weapon);

            if (weapon is { IsTwoHanded: true } or { W_AttackType: AttackType.DoubleStrike })
            {
                weaponAnimTime *= 0.5f;
            }

            if (weapon is { W_AttackType: AttackType.TripleStrike})
            {
                weaponAnimTime *= 0.33f;
            }

            var powerBarMod = powerBarTime * 20 * powerBarTime;
            var weaponTimeMod = weaponAnimTime / 100;

            AdrenalineMeter += weaponTimeMod * powerBarMod;

            if (AdrenalineMeter > 1.0f)
            {
                AdrenalineMeter = 1.0f;
            }
        }
    }
}
