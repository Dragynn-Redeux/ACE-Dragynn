using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories.Tables;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects;

public enum CombatFocusType
{
    None,
    Warrior,
    Blademaster,
    Archer,
    Vagabond,
    Sorcerer,
    Spellsword
}

public enum CombatFocusPrestigeVersion
{
    Newcomer,
}

public class CombatFocus : WorldObject
{
    private List<SpellId> CurrentSpells = new List<SpellId>();

    public int? CombatFocusTypeId
    {
        get => GetProperty(PropertyInt.CombatFocusTypeId);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusTypeId);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusTypeId, value.Value);
            }
        }
    }

    public int? CombatFocusAttributeSpellRemoved
    {
        get => GetProperty(PropertyInt.CombatFocusAttributeSpellRemoved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusAttributeSpellRemoved);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusAttributeSpellRemoved, value.Value);
            }
        }
    }

    public int? CombatFocusAttributeSpellAdded
    {
        get => GetProperty(PropertyInt.CombatFocusAttributeSpellAdded);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusAttributeSpellAdded);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusAttributeSpellAdded, value.Value);
            }
        }
    }

    public int? CombatFocusSkillSpellRemoved
    {
        get => GetProperty(PropertyInt.CombatFocusSkillSpellRemoved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusSkillSpellRemoved);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusSkillSpellRemoved, value.Value);
            }
        }
    }

    public int? CombatFocusSkillSpellAdded
    {
        get => GetProperty(PropertyInt.CombatFocusSkillSpellAdded);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusSkillSpellAdded);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusSkillSpellAdded, value.Value);
            }
        }
    }

    public int? CombatFocusSkill2SpellRemoved
    {
        get => GetProperty(PropertyInt.CombatFocusSkill2SpellRemoved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusSkill2SpellRemoved);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusSkill2SpellRemoved, value.Value);
            }
        }
    }

    public int? CombatFocusSkill2SpellAdded
    {
        get => GetProperty(PropertyInt.CombatFocusSkill2SpellAdded);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusSkill2SpellAdded);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusSkill2SpellAdded, value.Value);
            }
        }
    }

    public int? CombatFocusSkill3SpellRemoved
    {
        get => GetProperty(PropertyInt.CombatFocusSkill3SpellRemoved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusSkill3SpellRemoved);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusSkill3SpellRemoved, value.Value);
            }
        }
    }

    public int? CombatFocusSkill3SpellAdded
    {
        get => GetProperty(PropertyInt.CombatFocusSkill3SpellAdded);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusSkill3SpellAdded);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusSkill3SpellAdded, value.Value);
            }
        }
    }

    public int? CombatFocusNumSkillsRemoved
    {
        get => GetProperty(PropertyInt.CombatFocusNumSkillsRemoved);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusNumSkillsRemoved);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusNumSkillsRemoved, value.Value);
            }
        }
    }

    public int? CombatFocusNumSkillsAdded
    {
        get => GetProperty(PropertyInt.CombatFocusNumSkillsAdded);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusNumSkillsAdded);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusNumSkillsAdded, value.Value);
            }
        }
    }

    public int? CombatFocusPrestigeVersionId
    {
        get => GetProperty(PropertyInt.CombatFocusPrestigeVersionId);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyInt.CombatFocusPrestigeVersionId);
            }
            else
            {
                SetProperty(PropertyInt.CombatFocusPrestigeVersionId, value.Value);
            }
        }
    }

    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public CombatFocus(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public CombatFocus(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues()
    {
        InitializeSpellList();
    }

    private static readonly List<SpellId> NewcomerSpells = new List<SpellId>()
    {
        SpellId.NewcomersFortitude,
        SpellId.NewcomersPersistence,
        SpellId.NewcomersClarity
    };

    private static readonly List<SpellId> WarriorSpells = new List<SpellId>()
    {
        SpellId.StrengthSelf1,
        SpellId.EnduranceSelf1,
        SpellId.HeavyWeaponsMasterySelf1, // Martial Weapons
        SpellId.ShieldMasterySelf1,
        SpellId.InvulnerabilitySelf1,
        SpellId.ThrownWeaponMasterySelf1,
        SpellId.HealingMasterySelf1,
        SpellId.MonsterAttunementSelf1 // Perception
    };

    private static readonly List<SpellId> BlademasterSpells = new List<SpellId>()
    {
        SpellId.EnduranceSelf1,
        SpellId.CoordinationSelf1,
        SpellId.HeavyWeaponsMasterySelf1, // Martial Weapons
        SpellId.DualWieldMasterySelf1,
        SpellId.InvulnerabilitySelf1,
        SpellId.ThrownWeaponMasterySelf1,
        SpellId.HealingMasterySelf1,
        SpellId.SprintSelf1
    };

    private static readonly List<SpellId> ArcherSpells = new List<SpellId>()
    {
        SpellId.CoordinationSelf1,
        SpellId.QuicknessSelf1,
        SpellId.MissileWeaponsMasterySelf1, // Bows
        SpellId.FinesseWeaponsMasterySelf1, // Dagger
        SpellId.StaffMasterySelf1,
        SpellId.UnarmedCombatMasterySelf1,
        SpellId.InvulnerabilitySelf1,
        SpellId.MonsterAttunementSelf1, // Perception
        SpellId.HealingMasterySelf1,
        SpellId.SprintSelf1,
    };

    private static readonly List<SpellId> VagabondSpells = new List<SpellId>()
    {
        SpellId.QuicknessSelf1,
        SpellId.FocusSelf1,
        SpellId.FinesseWeaponsMasterySelf1, // Dagger
        SpellId.StaffMasterySelf1,
        SpellId.UnarmedCombatMasterySelf1,
        SpellId.DualWieldMasterySelf1,
        SpellId.MagicResistanceSelf1,
        SpellId.MonsterAttunementSelf1, // Perception
        SpellId.DeceptionMasterySelf1,
        SpellId.LockpickMasterySelf1, // Thievery
    };

    private static readonly List<SpellId> SorcererSpells = new List<SpellId>()
    {
        SpellId.FocusSelf1,
        SpellId.WillpowerSelf1,
        SpellId.WarMagicMasterySelf1,
        SpellId.LifeMagicMasterySelf1,
        SpellId.ManaMasterySelf1,
        SpellId.ArcaneEnlightenmentSelf1,
        SpellId.DeceptionMasterySelf1,
        SpellId.MagicResistanceSelf1
    };

    private static readonly List<SpellId> SpellswordSpells = new List<SpellId>()
    {
        SpellId.WillpowerSelf1,
        SpellId.StrengthSelf1,
        SpellId.HeavyWeaponsMasterySelf1, // Martial Weapons
        SpellId.TwoHandedMasterySelf1,
        SpellId.ArcaneEnlightenmentSelf1,
        SpellId.WarMagicMasterySelf1,
        SpellId.LifeMagicMasterySelf1,
        SpellId.MagicResistanceSelf1
    };

    public void InitializeSpellList()
    {
        if (CombatFocusTypeId is null)
        {
            _log.Error("CombatFocus.InitializeSpellList() - Combat Focus ({Name}) is missing CombatFocusTypeId.", Name);
            return;
        }

        CurrentSpells.Clear();

        CombatFocusNumSkillsAdded = 0;
        CombatFocusNumSkillsRemoved = 0;

        var spellList = new List<SpellId>();
        switch ((CombatFocusType)CombatFocusTypeId)
        {
            case CombatFocusType.Warrior:
                spellList = WarriorSpells;
                break;
            case CombatFocusType.Blademaster:
                spellList = BlademasterSpells;
                break;
            case CombatFocusType.Archer:
                spellList = ArcherSpells;
                break;
            case CombatFocusType.Vagabond:
                spellList = VagabondSpells;
                break;
            case CombatFocusType.Sorcerer:
                spellList = SorcererSpells;
                break;
            case CombatFocusType.Spellsword:
                spellList = SpellswordSpells;
                break;
        }

        if ((CombatFocusPrestigeVersionId ?? 0) == (int)CombatFocusPrestigeVersion.Newcomer)
        {
            spellList.AddRange(NewcomerSpells);
        }

        var spellListCopy = new List<SpellId>(spellList);

        static bool IsValidSpellInt(int value)
        {
            return Enum.IsDefined(typeof(SpellId), (uint)value);
        }

        void AddIfNotPresent(int? value)
        {
            if (value is int v && IsValidSpellInt(v))
            {
                var s = (SpellId)v;
                if (!spellListCopy.Contains(s))
                {
                    spellListCopy.Add(s);
                }
            }
        }

        void RemoveHeritageOrSpell(int? value)
        {
            if (value is int v && IsValidSpellInt(v))
            {
                // heritage group: remove all three heritage skill spells
                var heritageIds = new[]
                {
                    SpellId.FinesseWeaponsMasterySelf1,
                    SpellId.StaffMasterySelf1,
                    SpellId.UnarmedCombatMasterySelf1
                };

                if (v == (int)heritageIds[0] || v == (int)heritageIds[1] || v == (int)heritageIds[2])
                {
                    foreach (var h in heritageIds)
                    {
                        spellListCopy.Remove(h);
                    }
                }
                else
                {
                    spellListCopy.Remove((SpellId)v);
                }
            }
        }

        // Attribute overrides
        AddIfNotPresent(CombatFocusAttributeSpellAdded);
        RemoveHeritageOrSpell(CombatFocusAttributeSpellRemoved);

        // Skill 1
        if (CombatFocusSkillSpellAdded != null)
        {
            AddIfNotPresent(CombatFocusSkillSpellAdded);
            CombatFocusNumSkillsAdded++;
        }

        if (CombatFocusSkillSpellRemoved != null)
        {
            RemoveHeritageOrSpell(CombatFocusSkillSpellRemoved);
            CombatFocusNumSkillsRemoved++;
        }

        // Skill 2
        if (CombatFocusSkill2SpellAdded != null)
        {
            AddIfNotPresent(CombatFocusSkill2SpellAdded);
            CombatFocusNumSkillsAdded++;
        }

        if (CombatFocusSkill2SpellRemoved != null)
        {
            RemoveHeritageOrSpell(CombatFocusSkill2SpellRemoved);
            CombatFocusNumSkillsRemoved++;
        }

        // Skill 3
        if (CombatFocusSkill3SpellAdded != null)
        {
            AddIfNotPresent(CombatFocusSkill3SpellAdded);
            CombatFocusNumSkillsAdded++;
        }

        if (CombatFocusSkill3SpellRemoved != null)
        {
            RemoveHeritageOrSpell(CombatFocusSkill3SpellRemoved);
            CombatFocusNumSkillsRemoved++;
        }

        // Apply to field
        CurrentSpells.AddRange(spellListCopy);

        UpdateDescriptionText();
    }

    public List<SpellId> GetCurrentSpellList()
    {
        return CurrentSpells;
    }

    public void OnEquip(Player player)
    {
        if (player == null)
        {
            return;
        }

        var combatFocusType = CombatFocusTypeId;
        if (combatFocusType == null || combatFocusType < 1)
        {
            return;
        }

        if (CurrentSpells.Count == 0)
        {
            _log.Warning("{Player} tried to equip pre-patch combat focus.", player.Name);
        }

        ActivateSpells(player, CurrentSpells);

        var particleEffect = GetFocusParticleEffect();
        player.PlayParticleEffect(particleEffect, player.Guid);

        TriggerCooldownsOfUsableAbilities(player, (CombatFocusType)combatFocusType);
    }

    private static void TriggerCooldownsOfUsableAbilities(Player player, CombatFocusType combatFocusType)
    {
        var phalanx = player.GetInventoryItemsOfWCID(1051123);
        var provoke = player.GetInventoryItemsOfWCID(1051118);
        var parry = player.GetInventoryItemsOfWCID(1051124);

        var weaponMaster = player.GetInventoryItemsOfWCID(1051125);
        var fury = player.GetInventoryItemsOfWCID(1051135);
        var relentless = player.GetInventoryItemsOfWCID(1051127);

        var multishot = player.GetInventoryItemsOfWCID(1051131);
        var steadyStrike = player.GetInventoryItemsOfWCID(1051130);
        var evasiveStance = player.GetInventoryItemsOfWCID(1051114);

        var vanish = player.GetInventoryItemsOfWCID(1051112);
        var backstab = player.GetInventoryItemsOfWCID(1051132);
        var smokescreen = player.GetInventoryItemsOfWCID(1051119);

        var overload = player.GetInventoryItemsOfWCID(1051133);
        var battery = player.GetInventoryItemsOfWCID(1051134);
        var manaBarrier = player.GetInventoryItemsOfWCID(1051110);

        var enchantedBlade = player.GetInventoryItemsOfWCID(1051115);
        var reflect = player.GetInventoryItemsOfWCID(1051129);
        var aegis = player.GetInventoryItemsOfWCID(1051128);

        // class-locked abilities
        switch (combatFocusType)
        {
            case CombatFocusType.Warrior:
                if (phalanx.Count > 0)
                {
                    player.EnchantmentManager.StartCooldown(phalanx[0]);
                }
                break;
            case CombatFocusType.Blademaster:
                if (weaponMaster.Count > 0)
                {
                    player.EnchantmentManager.StartCooldown(weaponMaster[0]);
                }
                break;
            case CombatFocusType.Archer:
                if (multishot.Count > 0)
                {
                    player.EnchantmentManager.StartCooldown(multishot[0]);
                }
                break;
            case CombatFocusType.Vagabond:
                if (vanish.Count > 0)
                {
                    player.EnchantmentManager.StartCooldown(vanish[0]);
                }
                break;
            case CombatFocusType.Sorcerer:
                if (overload.Count > 0)
                {
                    player.EnchantmentManager.StartCooldown(overload[0]);
                }
                break;
            case CombatFocusType.Spellsword:
                if (enchantedBlade.Count > 0)
                {
                    player.EnchantmentManager.StartCooldown(enchantedBlade[0]);
                }
                break;
        }

        // class-shared abilities
        if (combatFocusType is CombatFocusType.Warrior or CombatFocusType.Blademaster or CombatFocusType.Spellsword)
        {
            if (provoke.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(provoke[0]);
            }
            if (parry.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(parry[0]);
            }
        }

        if (combatFocusType is CombatFocusType.Blademaster or CombatFocusType.Archer or CombatFocusType.Warrior)
        {
            if (fury.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(fury[0]);
            }
            if (relentless.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(relentless[0]);
            }
        }

        if (combatFocusType is CombatFocusType.Archer or CombatFocusType.Blademaster or CombatFocusType.Vagabond)
        {
            if (steadyStrike.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(steadyStrike[0]);
            }
            if (evasiveStance.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(evasiveStance[0]);
            }
        }

        if (combatFocusType is CombatFocusType.Vagabond or CombatFocusType.Archer or CombatFocusType.Sorcerer)
        {
            if (backstab.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(backstab[0]);
            }
            if (smokescreen.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(smokescreen[0]);
            }
        }

        if (combatFocusType is CombatFocusType.Sorcerer or CombatFocusType.Vagabond or CombatFocusType.Spellsword)
        {
            if (battery.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(battery[0]);
            }
            if (manaBarrier.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(manaBarrier[0]);
            }
        }

        if (combatFocusType is CombatFocusType.Spellsword or CombatFocusType.Warrior or CombatFocusType.Sorcerer)
        {
            if (reflect.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(reflect[0]);
            }
            if (aegis.Count > 0)
            {
                player.EnchantmentManager.StartCooldown(aegis[0]);
            }
        }
    }

    public void OnDequip(Player player, bool onLevelUp = false, int? startingLevel = null)
    {
        if (player == null)
        {
            return;
        }

        var combatFocusType = CombatFocusTypeId;
        if (combatFocusType == null || combatFocusType < 1)
        {
            return;
        }

        DeactivateSpells(player, CurrentSpells, onLevelUp, startingLevel);

        if (!onLevelUp)
        {
            DisableActiveAbilities(player);
        }
    }

    private void ActivateSpells(Player player, List<SpellId> spellIds)
    {
        var spellLevel = Math.Clamp(player.GetPlayerTier(player.Level) - 1, 1, 7);

        foreach (var spellId in spellIds)
        {
            var leveledSpell = SpellLevelProgression.GetSpellAtLevel(spellId, spellLevel, true, true);
            if (leveledSpell == SpellId.Undef)
            {
                ActivateSpell(player, new Spell(spellId));
            }
            else
            {
                ActivateSpell(player, new Spell(leveledSpell));
            }
        }
    }

    private static void ActivateSpell(Player player, Spell spell)
    {
        var addResult = player.EnchantmentManager.Add(spell, null, null, true);
        //Console.WriteLine($"ActivateSpell: {spell.Name} Beneficial? {spell.IsBeneficial}");

        player.Session.Network.EnqueueSend(
            new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(player, addResult.Enchantment))
        );
        player.HandleSpellHooks(spell);
    }

    private void DeactivateSpells(Player player, List<SpellId> spellIds, bool onLevelUp, int? startingLevel = null)
    {
        var spellLevel = Math.Clamp(player.GetPlayerTier(player.Level) - 1, 1, 7);

        if (onLevelUp)
        {
            var startingSpellLevel = Math.Clamp(player.GetPlayerTier(startingLevel) - 1, 1, 7);

            foreach (var spellId in spellIds)
            {
                var leveledSpell = SpellLevelProgression.GetSpellAtLevel(spellId, spellLevel, true, true);
                if (leveledSpell == SpellId.Undef)
                {
                    DeactivateSpell(player, new Spell(spellId));
                }
                else
                {
                    DeactivateSpell(player, new Spell(leveledSpell));
                }
            }
        }
        else
        {
            foreach (var spellId in spellIds)
            {
                var leveledSpell = SpellLevelProgression.GetSpellAtLevel(spellId, spellLevel, true, true);
                if (leveledSpell == SpellId.Undef)
                {
                    DeactivateSpell(player, new Spell(spellId));
                }
                else
                {
                    DeactivateSpell(player, new Spell(leveledSpell));
                }
            }
        }
    }

    private void DeactivateSpell(Player player, Spell spell)
    {
        var enchantments = player
            .Biota.PropertiesEnchantmentRegistry.Clone(BiotaDatabaseLock)
            .Where(i => i.Duration == -1 && i.SpellId != (int)SpellId.Vitae)
            .ToList();

        //Console.WriteLine($"DeactivateSpell: {spell.Name} NumberOfEnchantments: {enchantments.Count}");
        foreach (var enchantment in enchantments)
        {
            if (enchantment.SpellId == spell.Id)
            {
                //Console.WriteLine($"DeactivateSpell: {spell.Name}");
                player.EnchantmentManager.Dispel(enchantment);
                player.HandleSpellHooks(spell);
            }
        }
    }

    private void DisableActiveAbilities(Player player)
    {
        if (player is {FuryStanceIsActive: true})
        {
            player.FuryStanceIsActive = false;

            player.AdrenalineMeter = 0.0f;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You calm down and your release adrenaline without effect.", ChatMessageType.Craft)
            );
        }

        if (player is { RelentlessStanceIsActive: true })
        {
            player.RelentlessStanceIsActive = false;

            player.AdrenalineMeter = 0.0f;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You calm down and release your adrenaline without effect.", ChatMessageType.Craft)
            );
        }


        if (player is { OverloadStanceIsActive: true })
        {
            player.OverloadStanceIsActive = false;

            player.ManaChargeMeter = 0.0f;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You release your charged mana to no effect.", ChatMessageType.Broadcast)
            );

            PlayParticleEffect(PlayScript.SkillDownBlue, Guid);
        }


        if (player is { BatteryStanceIsActive: true })
        {
            player.BatteryStanceIsActive = false;

            player.ManaChargeMeter = 0.0f;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You release your charged mana to no effect.", ChatMessageType.Broadcast)
            );

            PlayParticleEffect(PlayScript.SkillDownBlue, Guid);
        }


        if (player is { EvasiveStanceIsActive: true })
        {
            player.EvasiveStanceIsActive = false;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You move out of your evasive stance.", ChatMessageType.Broadcast)
            );
            PlayParticleEffect(PlayScript.DispelLife, Guid);
        }


        if (player is { ManaBarrierIsActive: true })
        {
            player.ManaBarrierIsActive = false;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You dispel your mana barrier.", ChatMessageType.Broadcast)
            );
            PlayParticleEffect(PlayScript.DispelLife, Guid);
        }


        if (player is { PhalanxIsActive: true })
        {
            player.PhalanxIsActive = false;

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat($"You lower your shield.", ChatMessageType.Broadcast)
            );
        }
    }

    public void OnLevelUp(Player player, int startingLevel)
    {
        OnDequip(player, true, startingLevel);
        OnEquip(player);
    }

    public CombatAbility GetCombatAbility()
    {
        var value = 0;

        if (CombatAbilityId != null && CombatAbilityId.HasValue)
        {
            value = (int)CombatAbilityId;
        }

        return (CombatAbility)value;
    }

    public void RemoveSpell(Player player, WorldObject source, SpellId spellId, bool isAttribute)
    {
        var spellName = GetSpellName(spellId) + " spell";

        // remove spell (if a heritage skill, remove all 3)
        if (
            spellId == SpellId.FinesseWeaponsMasterySelf1
            || spellId == SpellId.StaffMasterySelf1
            || spellId == SpellId.UnarmedCombatMasterySelf1
        )
        {
            CurrentSpells.Remove(SpellId.FinesseWeaponsMasterySelf1);
            CurrentSpells.Remove(SpellId.StaffMasterySelf1);
            CurrentSpells.Remove(SpellId.UnarmedCombatMasterySelf1);
            spellName = "Dagger, Staff, and Unarmed Combat spells";
        }
        else
        {
            CurrentSpells.Remove(spellId);
        }

        if (isAttribute)
        {
            if (IsBaseSpell(spellId))
            {
                CombatFocusAttributeSpellRemoved = (int)spellId;
            }
            else
            {
                CombatFocusAttributeSpellAdded = null;
            }
        }
        else
        {
            if (IsBaseSpell(spellId))
            {
                if (CombatFocusSkillSpellRemoved == null)
                {
                    CombatFocusSkillSpellRemoved = (int)spellId;
                    CombatFocusNumSkillsRemoved++;
                }
                else if (CombatFocusSkill2SpellRemoved == null)
                {
                    CombatFocusSkill2SpellRemoved = (int)spellId;
                    CombatFocusNumSkillsRemoved++;
                }
                else if (CombatFocusSkill3SpellRemoved == null)
                {
                    CombatFocusSkill3SpellRemoved = (int)spellId;
                    CombatFocusNumSkillsRemoved++;
                }
            }
            else
            {
                // Remove the specific added spell that matches
                if (CombatFocusSkillSpellAdded == (int)spellId)
                {
                    CombatFocusSkillSpellAdded = null;
                    CombatFocusNumSkillsAdded--;
                }
                else if (CombatFocusSkill2SpellAdded == (int)spellId)
                {
                    CombatFocusSkill2SpellAdded = null;
                    CombatFocusNumSkillsAdded--;
                }
                else if (CombatFocusSkill3SpellAdded == (int)spellId)
                {
                    CombatFocusSkill3SpellAdded = null;
                    CombatFocusNumSkillsAdded--;
                }
            }
        }


        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You remove the {spellName} from {Name}.", ChatMessageType.Craft)
        );
        player.TryConsumeFromInventoryWithNetworking(source);
    }

    public void AddSpell(Player player, WorldObject source, SpellId spellId, bool isAttribute)
    {
        // Add spell
        CurrentSpells.Add(spellId);

        if (isAttribute)
        {
            if (IsBaseSpell(spellId))
            {
                CombatFocusAttributeSpellRemoved = null;
            }
            else
            {
                CombatFocusAttributeSpellAdded = (int)spellId;
            }
        }
        else
        {
            if (IsBaseSpell(spellId))
            {
                if (CombatFocusSkillSpellRemoved != null)
                {
                    CombatFocusSkillSpellRemoved = null;
                    CombatFocusNumSkillsRemoved--;
                }
                else if (CombatFocusSkill2SpellRemoved != null)
                {
                    CombatFocusSkill2SpellRemoved = null;
                    CombatFocusNumSkillsRemoved--;
                }
                else if (CombatFocusSkill3SpellRemoved != null)
                {
                    CombatFocusSkill3SpellRemoved = null;
                    CombatFocusNumSkillsRemoved--;
                }
            }
            else
            {
                // Fill the first available slot, checking in order
                if (CombatFocusSkillSpellAdded == null)
                {
                    CombatFocusSkillSpellAdded = (int)spellId;
                    CombatFocusNumSkillsAdded++;
                }
                else if (CombatFocusSkill2SpellAdded == null)
                {
                    CombatFocusSkill2SpellAdded = (int)spellId;
                    CombatFocusNumSkillsAdded++;
                }
                else if (CombatFocusSkill3SpellAdded == null)
                {
                    CombatFocusSkill3SpellAdded = (int)spellId;
                    CombatFocusNumSkillsAdded++;
                }
            }
        }


        var spellName = GetSpellName(spellId);

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat($"You add the {spellName} spell to {Name}.", ChatMessageType.Craft)
        );
        player.TryConsumeFromInventoryWithNetworking(source);
    }

    public void UpdateDescriptionText()
    {
        // De-dupe while preserving order; interactions can sometimes add duplicates.
        var uniqueSpells = CurrentSpells.Distinct().ToList();

        var attributeSpellsText = string.Join(", ",
            uniqueSpells
                .Where(IsAttribute)
                .Select(GetSpellName)
                .Where(n => !string.IsNullOrWhiteSpace(n)));

        var skillSpellsText = string.Join(", ",
            uniqueSpells
                .Where(s => !IsAttribute(s) && s is not (SpellId.NewcomersFortitude or SpellId.NewcomersPersistence or SpellId.NewcomersClarity))
                .Select(GetSpellName)
                .Where(n => !string.IsNullOrWhiteSpace(n)));

        var description = "Use this focus to gain a boost towards the following attributes and skills:\n\n";
        description += "Attributes: " + (!string.IsNullOrWhiteSpace(attributeSpellsText) ? attributeSpellsText : "None") + ".\n\n";
        description += "Skills: " + (!string.IsNullOrWhiteSpace(skillSpellsText) ? skillSpellsText : "None") + ".\n\n";

        var alteredDescription = "";
        if (CombatFocusAttributeSpellAdded != null)
        {
            alteredDescription = "This combat focus' attribute spell has been altered.\n\n";
        }

        if (CombatFocusSkillSpellAdded != null)
        {
            alteredDescription = "This combat focus' skill spell has been altered.\n\n";
        }

        if (CombatFocusAttributeSpellAdded != null && CombatFocusSkillSpellAdded != null)
        {
            alteredDescription = "This combat focus' attribute and skill spells have been altered.\n\n";
        }

        var prestigeDescription = "";
        if ((CombatFocusPrestigeVersionId ?? 0) == (int)CombatFocusPrestigeVersion.Newcomer)
        {
            prestigeDescription =
                "This combat focus also grants +50 Health, +100 Stamina, and +100 Mana.\n\n";
        }

        LongDesc = description + alteredDescription + prestigeDescription;
    }

    private static bool IsAttribute(SpellId spellId)
    {
        SpellId[] attributeSpellIds =
        {
            SpellId.StrengthSelf1,
            SpellId.EnduranceSelf1,
            SpellId.CoordinationSelf1,
            SpellId.QuicknessSelf1,
            SpellId.FocusSelf1,
            SpellId.WillpowerSelf1
        };

        if (attributeSpellIds.Contains(spellId))
        {
            return true;
        }

        return false;
    }

    public bool IsBaseSpell(SpellId spellId)
    {
        switch (CombatFocusTypeId)
        {
            case 1:
                return WarriorSpells.Contains(spellId);
            case 2:
                return BlademasterSpells.Contains(spellId);
            case 3:
                return ArcherSpells.Contains(spellId);
            case 4:
                return VagabondSpells.Contains(spellId);
            case 5:
                return SorcererSpells.Contains(spellId);
            case 6:
                return SpellswordSpells.Contains(spellId);
        }

        return false;
    }

    private static string GetSpellName(SpellId spellId)
    {
        var name = "";

        switch (spellId)
        {
            case SpellId.StrengthSelf1:
                name = "Strength";
                break;
            case SpellId.EnduranceSelf1:
                name = "Endurance";
                break;
            case SpellId.CoordinationSelf1:
                name = "Coordination";
                break;
            case SpellId.QuicknessSelf1:
                name = "Quickness";
                break;
            case SpellId.FocusSelf1:
                name = "Focus";
                break;
            case SpellId.WillpowerSelf1:
                name = "Self";
                break;

            case SpellId.HeavyWeaponsMasterySelf1:
                name = "Martial Weapons";
                break;
            case SpellId.FinesseWeaponsMasterySelf1:
                name = "Dagger";
                break;
            case SpellId.StaffMasterySelf1:
                name = "Staff";
                break;
            case SpellId.UnarmedCombatMasterySelf1:
                name = "Unarmed Combat";
                break;
            case SpellId.MissileWeaponsMasterySelf1:
                name = "Bows";
                break;
            case SpellId.ThrownWeaponMasterySelf1:
                name = "Thrown Weapons";
                break;
            case SpellId.TwoHandedMasterySelf1:
                name = "Two-Handed Combat";
                break;
            case SpellId.DualWieldMasterySelf1:
                name = "Dual Wield";
                break;
            case SpellId.ShieldMasterySelf1:
                name = "Shield";
                break;
            case SpellId.WarMagicMasterySelf1:
                name = "War Magic";
                break;
            case SpellId.LifeMagicMasterySelf1:
                name = "Life Magic";
                break;
            case SpellId.InvulnerabilitySelf1:
                name = "Physical Defense";
                break;
            case SpellId.MagicResistanceSelf1:
                name = "Magic Defense";
                break;
            case SpellId.ArcaneEnlightenmentSelf1:
                name = "Arcane Lore";
                break;
            case SpellId.ManaMasterySelf1:
                name = "Mana Conversion";
                break;
            case SpellId.HealingMasterySelf1:
                name = "Healing";
                break;
            case SpellId.MonsterAttunementSelf1:
                name = "Perception";
                break;
            case SpellId.DeceptionMasterySelf1:
                name = "Deception";
                break;
            case SpellId.LockpickMasterySelf1:
                name = "Thievery";
                break;
            case SpellId.SprintSelf1:
                name = "Run";
                break;
            case SpellId.JumpingMasterySelf1:
                name = "Jump";
                break;

            case SpellId.NewcomersFortitude:
                name = "Fortitude";
                break;
            case SpellId.NewcomersPersistence:
                name = "Persistence";
                break;
            case SpellId.NewcomersClarity:
                name = "Clarity";
                break;
        }

        return name;
    }

    private PlayScript GetFocusParticleEffect()
    {
        switch (CombatFocusTypeId)
        {
            default:
            case 1:
                return PlayScript.SkillUpRed;
            case 2:
                return PlayScript.SkillUpOrange;
            case 3:
                return PlayScript.SkillUpYellow;
            case 4:
                return PlayScript.SkillUpGreen;
            case 5:
                return PlayScript.SkillUpBlue;
            case 6:
                return PlayScript.SkillUpPurple;
        }
    }

    /* ---------------------------------------------------------------------------------------------------------------------------------------
     * This section could alternatively make Combat Focuses activate without needing the Trinket Slot. (instead of the equip functions above)
     * It works by setting a UI Effect on the used Combat Focus and then disabling the UI Effect on all others. It also
     * manages the hotbar automatically when the UI Effect is changed on a Combat Focus (however this causes a VHS issue).
     *
     * In its current state, it works just fine, however Virindi Hotkey System throws an error whenever a Combat Focus
     * is used while any hotbar slot is filled. This would be the ideal solution for Combat Focuses if a solution can
     * be found for the VHS issue.
     *
     * ValidLocations must be disabled on the weenie to prevent it from being equipped.
     * ---------------------------------------------------------------------------------------------------------------------------------------

    public override void OnActivate(WorldObject activator)
    {
        if (ItemUseable == Usable.Contained && activator is Player player)
        {
            var containedItem = player.FindObject(Guid.Full, Player.SearchLocations.MyInventory | Player.SearchLocations.MyEquippedItems);
            if (containedItem != null) // item is contained by player
            {
                if (player.IsBusy || player.Teleporting || player.suicideInProgress)
                {
                    player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YoureTooBusy));
                    player.EnchantmentManager.StartCooldown(this);
                    return;
                }

                if (player.IsDead)
                {
                    player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.Dead));
                    player.EnchantmentManager.StartCooldown(this);
                    return;
                }
            }
            else
                return;
        }

        base.OnActivate(activator);
    }

    public override void ActOnUse(WorldObject activator)
    {
        ActOnUse(activator, false);
    }

    public void ActOnUse(WorldObject activator, bool confirmed, WorldObject target = null)
    {
        if (!(activator is Player player))
            return;

        if (player.IsBusy || player.Teleporting || player.suicideInProgress)
        {
            player.SendWeenieError(WeenieError.YoureTooBusy);
            return;
        }

        if (player.IsJumping)
        {
            player.SendWeenieError(WeenieError.YouCantDoThatWhileInTheAir);
            return;
        }

        if (UseUserAnimation != MotionCommand.Invalid)
        {
            // some gems have UseUserAnimation and UseSound, similar to food
            // eg. 7559 - Condensed Dispel Potion

            // the animation is also weird, and differs from food, in that it is the full animation
            // instead of stopping at the 'eat/drink' point... so we pass 0.5 here?

            var animMod = (UseUserAnimation == MotionCommand.MimeDrink || UseUserAnimation == MotionCommand.MimeEat) ? 0.5f : 1.0f;

            player.ApplyConsumable(UseUserAnimation, () => UseGem(player), animMod);
        }
        else
            UseGem(player);
    }

    public void UseGem(Player player)
    {
        Console.WriteLine("\n\n--------------------");
        if (player.IsDead) return;

        // verify item is still valid
        if (player.FindObject(Guid.Full, Player.SearchLocations.MyInventory) == null)
        {
            //player.SendWeenieError(WeenieError.ObjectGone);   // results in 'Unable to move object!' transient error
            player.SendTransientError($"Cannot find the {Name}");   // custom message
            return;
        }

        if (UseSound > 0)
            player.Session.Network.EnqueueSend(new GameMessageSound(player.Guid, UseSound));

        // Disable UI Effect from all other carried Combat Focuses
        var combatFocuses = player.GetInventoryItemsOfTypeWeenieType(WeenieType.CombatFocus);

        // Enable or disable this combat focus UI Effect
        if (UiEffects == ACE.Entity.Enum.UiEffects.Undef)
        {
            UiEffects = ACE.Entity.Enum.UiEffects.Magical;
            IconUnderlayId = 100691489;
        }
        else
        {
            UiEffects = ACE.Entity.Enum.UiEffects.Undef;
            IconUnderlayId = 100683040;
        }

        Console.WriteLine($"ShortcutCount: {player.Session.Player.GetShortcuts().Count}");
        var hasShortcuts = player.Session.Player.GetShortcuts().Count > 0;
        var unHotkeyedFocuses = new List<CombatFocus>();

        //int i = 0;
        foreach (CombatFocus focus in combatFocuses)
        {
            unHotkeyedFocuses.Add(focus);

            if (hasShortcuts)
            {
                foreach (var shortcut in player.Session.Player.GetShortcuts())
                {
                    if (focus.Biota.Id == shortcut.ObjectId)
                    {
                        var actionChain = new ActionChain();

                        actionChain.AddAction(player, () =>
                        {
                            Console.WriteLine($"\nRemoveShortcut @ index {shortcut.Index} for shortcut focus.");
                            player.Session.Player.HandleActionRemoveShortcut(shortcut.Index);
                            //player.Session.Network.EnqueueSend(new GameEventPlayerDescription(player.Session));
                            //player.EnqueueBroadcast(new GameEventPlayerDescription(player.Session));
                        });

                        actionChain.AddDelaySeconds(3);

                        actionChain.AddAction(player, () =>
                        {
                            if (focus.Biota.Id != Biota.Id)
                            {
                                Console.WriteLine("\nDisable UI Effect of untargeted hotkey focus.");
                                focus.UiEffects = ACE.Entity.Enum.UiEffects.Undef;
                                IconUnderlayId = 100683040;
                            }
                            player.EnqueueBroadcast(new GameMessageUpdateObject(focus));
                            //player.Session.Network.EnqueueSend(new GameEventPlayerDescription(player.Session));
                        });

                        actionChain.AddDelaySeconds(3);

                        actionChain.AddAction(player, () =>
                        {
                            Console.WriteLine($"\nAddShortcutBack @ index {shortcut.Index} for shortcut focus.");
                            player.Session.Player.HandleActionAddShortcut(shortcut);
                            player.Session.Network.EnqueueSend(new GameEventPlayerDescription(player.Session));
                            //player.EnqueueBroadcast(new GameEventPlayerDescription(player.Session));
                        });
                        actionChain.EnqueueChain();

                        unHotkeyedFocuses.Remove(focus);
                    }
                }
            }
        }

        Console.WriteLine($"Number of Unhotkeyed Focuses: {unHotkeyedFocuses.Count}");
        foreach (CombatFocus focus in unHotkeyedFocuses)
        {
            if (focus.Biota.Id != Biota.Id)
            {
                Console.WriteLine("Disabling UI Effect for: " + focus.Biota.Id);
                focus.UiEffects = ACE.Entity.Enum.UiEffects.Undef;
                IconUnderlayId = 100683040;
            }
            player.EnqueueBroadcast(new GameMessageUpdateObject(focus));
        }
    }
    */
}
