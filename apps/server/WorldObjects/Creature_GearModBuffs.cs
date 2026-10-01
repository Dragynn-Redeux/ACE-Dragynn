using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    /// <summary>
    /// Gear mod totals below this are not shown as a bonus enchantment.
    /// They still apply, through GetGearSkillModNotInCurrent().
    /// </summary>
    private const double GearModBuffThreshold = 0.01;

    /// <summary>
    /// The enchantment that carries each skill's gear mod bonus on a player,
    /// so the character window shows the bonused skill
    /// </summary>
    public static readonly IReadOnlyDictionary<Skill, SpellId> GearSkillModBuffs = new Dictionary<Skill, SpellId>
    {
        { Skill.Run, SpellId.OntheRun },
        { Skill.PhysicalDefense, SpellId.PhysicalDefenseBonus },
        { Skill.MagicDefense, SpellId.MagicDefenseBonus },
        { Skill.DualWield, SpellId.DualWieldBonus },
        { Skill.TwoHandedCombat, SpellId.TwoHandedCombatBonus },
        { Skill.Thievery, SpellId.ThieveryBonus },
        { Skill.Shield, SpellId.ShieldBonus },
        { Skill.Perception, SpellId.PerceptionBonus },
        { Skill.Deception, SpellId.DeceptionBonus },
        { Skill.WarMagic, SpellId.WarMagicBonus },
        { Skill.LifeMagic, SpellId.LifeMagicBonus },
    };

    /// <summary>
    /// ArmorAttackMod raises whichever weapon skill the player attacks with,
    /// so each weapon skill has its own bonus enchantment.
    /// These only go on trained and specialized skills, to keep the enchantment list short.
    /// </summary>
    public static readonly IReadOnlyDictionary<Skill, SpellId> GearAttackModBuffs = new Dictionary<Skill, SpellId>
    {
        { Skill.MartialWeapons, SpellId.MartialWeaponsAttackBonus },
        { Skill.Staff, SpellId.StaffAttackBonus },
        { Skill.Dagger, SpellId.DaggerAttackBonus },
        { Skill.UnarmedCombat, SpellId.UnarmedCombatAttackBonus },
        { Skill.Bow, SpellId.BowAttackBonus },
        { Skill.ThrownWeapon, SpellId.ThrownWeaponAttackBonus },
    };

    /// <summary>
    /// Every enchantment that UpdateArmorModBuffs() manages
    /// </summary>
    public static readonly IReadOnlySet<uint> GearModBuffSpellIds = GearSkillModBuffs
        .Values.Concat(GearAttackModBuffs.Values)
        .Concat(new[] { SpellId.Ardence, SpellId.Vim, SpellId.Volition })
        .Select(spellId => (uint)spellId)
        .ToHashSet();

    /// <summary>
    /// Returns the total bonus that equipped gear mods give a skill, ie. 0.1 for +10%.
    /// Does not include ArmorAttackMod, which applies to whichever weapon skill is attacking.
    /// </summary>
    public double GetGearSkillMod(Skill skill)
    {
        return skill switch
        {
            Skill.Run => GetArmorRunMod() ?? 0,
            Skill.PhysicalDefense => GetArmorPhysicalDefMod() ?? 0,
            Skill.MagicDefense => GetArmorMagicDefMod() ?? 0,
            Skill.DualWield => GetArmorDualWieldMod() ?? 0,
            Skill.TwoHandedCombat => GetArmorTwohandedCombatMod() ?? 0,
            Skill.Thievery => GetArmorThieveryMod() ?? 0,
            Skill.Shield => GetArmorShieldMod() ?? 0,
            Skill.Perception => GetArmorPerceptionMod() ?? 0,
            Skill.Deception => GetArmorDeceptionMod() ?? 0,
            Skill.WarMagic => (GetArmorWarMagicMod() ?? 0) + (GetWeaponWarMagicMod() ?? 0),
            Skill.LifeMagic => (GetArmorLifeMagicMod() ?? 0) + (GetWeaponLifeMagicMod() ?? 0),
            _ => 0,
        };
    }

    /// <summary>
    /// Returns the part of GetGearSkillMod() that is not already in the skill's Current value.
    /// A player's gear mod bonus is an enchantment on the skill, so it is already in Current and must not be applied twice.
    /// </summary>
    public double GetGearSkillModNotInCurrent(Skill skill)
    {
        if (
            this is Player
            && GearSkillModBuffs.TryGetValue(skill, out var spellId)
            && EnchantmentManager.HasSpell((uint)spellId)
        )
        {
            return 0;
        }

        return GetGearSkillMod(skill);
    }

    /// <summary>
    /// Returns the ArmorAttackMod bonus for an attack with this skill that is not already in the skill's Current value
    /// </summary>
    public double GetGearAttackModNotInCurrent(Skill attackSkill)
    {
        if (
            this is Player
            && GearAttackModBuffs.TryGetValue(attackSkill, out var spellId)
            && EnchantmentManager.HasSpell((uint)spellId)
        )
        {
            return 0;
        }

        return GetArmorAttackMod() ?? 0;
    }

    /// <summary>
    /// Shows a player's gear mods as enchantments on the skills and vitals they raise,
    /// so the character window shows the bonused values. Call whenever equipped gear changes.
    /// </summary>
    public void UpdateArmorModBuffs()
    {
        if (this is not Player player)
        {
            return;
        }

        var enchantments = Biota.PropertiesEnchantmentRegistry.Clone(BiotaDatabaseLock);

        foreach (var (skill, spellId) in GearSkillModBuffs)
        {
            UpdateGearModBuff(player, enchantments, spellId, GetGearSkillMod(skill));
        }

        var attackMod = GetArmorAttackMod() ?? 0;

        foreach (var (skill, spellId) in GearAttackModBuffs)
        {
            var trained = GetCreatureSkill(skill, false)?.AdvancementClass >= SkillAdvancementClass.Trained;

            UpdateGearModBuff(player, enchantments, spellId, trained ? attackMod : 0);
        }

        UpdateGearModBuff(player, enchantments, SpellId.Ardence, GetArmorHealthMod() ?? 0);
        UpdateGearModBuff(player, enchantments, SpellId.Vim, GetArmorStaminaMod() ?? 0);
        UpdateGearModBuff(player, enchantments, SpellId.Volition, GetArmorManaMod() ?? 0);
    }

    /// <summary>
    /// Adds, updates or removes the gear bonus enchantment for spellId, to multiply its stat by 1 + mod
    /// </summary>
    private void UpdateGearModBuff(
        Player player,
        List<PropertiesEnchantmentRegistry> enchantments,
        SpellId spellId,
        double mod
    )
    {
        var existing = enchantments.Where(e => e.SpellId == (int)spellId).ToList();

        if (mod < GearModBuffThreshold && existing.Count == 0)
        {
            return;
        }

        var spell = new Spell(spellId);

        // until the spell is in both the dat and the world db, the mod is applied without an enchantment
        if (mod < GearModBuffThreshold || spell.NotFound)
        {
            foreach (var entry in existing)
            {
                EnchantmentManager.Dispel(entry);
            }

            if (existing.Count > 0)
            {
                player.HandleRunRateUpdate(spell);
            }

            return;
        }

        var statModValue = (float)(1 + mod);

        PropertiesEnchantmentRegistry enchantment;

        if (existing.Count == 1)
        {
            enchantment = existing[0];

            if (enchantment.StatModValue == statModValue)
            {
                return;
            }
        }
        else
        {
            // an older version of this method added a new layer on every gear change instead of updating in place
            foreach (var entry in existing)
            {
                EnchantmentManager.Dispel(entry);
            }

            enchantment = EnchantmentManager.Add(spell, null, null, true).Enchantment;
        }

        EnchantmentManager.SetStatModValue(enchantment, statModValue);

        player.Session.Network.EnqueueSend(
            new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(player, enchantment))
        );

        player.HandleMaxVitalUpdate(spell);
        player.HandleRunRateUpdate(spell);
    }
}
