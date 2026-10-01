using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class GearModBuffTests
{
    // these ids are baked into the client dat and the world db spell table, so they must never shift
    [TestMethod]
    public void GearModBonusSpellIds_AreStable()
    {
        Assert.AreEqual(6846u, (uint)SpellId.PhysicalDefenseBonus);
        Assert.AreEqual(6847u, (uint)SpellId.MagicDefenseBonus);
        Assert.AreEqual(6848u, (uint)SpellId.DualWieldBonus);
        Assert.AreEqual(6849u, (uint)SpellId.TwoHandedCombatBonus);
        Assert.AreEqual(6850u, (uint)SpellId.ThieveryBonus);
        Assert.AreEqual(6851u, (uint)SpellId.ShieldBonus);
        Assert.AreEqual(6852u, (uint)SpellId.PerceptionBonus);
        Assert.AreEqual(6853u, (uint)SpellId.DeceptionBonus);
        Assert.AreEqual(6854u, (uint)SpellId.WarMagicBonus);
        Assert.AreEqual(6855u, (uint)SpellId.LifeMagicBonus);
        Assert.AreEqual(6856u, (uint)SpellId.MartialWeaponsAttackBonus);
        Assert.AreEqual(6857u, (uint)SpellId.StaffAttackBonus);
        Assert.AreEqual(6858u, (uint)SpellId.DaggerAttackBonus);
        Assert.AreEqual(6859u, (uint)SpellId.UnarmedCombatAttackBonus);
        Assert.AreEqual(6860u, (uint)SpellId.BowAttackBonus);
        Assert.AreEqual(6861u, (uint)SpellId.ThrownWeaponAttackBonus);
    }

    [TestMethod]
    public void GearModBuffSpellIds_ContainsEveryManagedBuff()
    {
        var buffs = Creature.GearSkillModBuffs.Values.Concat(Creature.GearAttackModBuffs.Values).ToList();

        Assert.AreEqual(buffs.Count, buffs.Distinct().Count(), "a spell is used for more than one bonus");

        foreach (var spellId in buffs)
        {
            Assert.IsTrue(Creature.GearModBuffSpellIds.Contains((uint)spellId), $"{spellId} missing");
        }

        Assert.IsTrue(Creature.GearModBuffSpellIds.Contains((uint)SpellId.Ardence));
        Assert.IsTrue(Creature.GearModBuffSpellIds.Contains((uint)SpellId.Vim));
        Assert.IsTrue(Creature.GearModBuffSpellIds.Contains((uint)SpellId.Volition));
    }

    // GetGearAttackModNotInCurrent() looks up the attack bonus by skill, so a skill with its own gear bonus
    // must not also be an attack bonus skill, or its own bonus would be mistaken for the attack bonus
    [TestMethod]
    public void GearAttackModBuffs_DoNotOverlapSkillModBuffs()
    {
        foreach (var skill in Creature.GearAttackModBuffs.Keys)
        {
            Assert.IsFalse(Creature.GearSkillModBuffs.ContainsKey(skill), $"{skill} is in both tables");
        }
    }
}
