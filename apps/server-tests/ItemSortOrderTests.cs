using System.Linq;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Factories.Tables.Wcids;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests;

[TestClass]
public class ItemSortOrderTests
{
    [TestMethod]
    public void WeightClassRank_GoesClothLightHeavyThenNone()
    {
        var order = new ArmorWeightClass?[] { null, ArmorWeightClass.Heavy, ArmorWeightClass.None, ArmorWeightClass.Cloth, ArmorWeightClass.Light }
            .OrderBy(ItemSortOrder.WeightClassRank)
            .ToArray();

        Assert.AreEqual(ArmorWeightClass.Cloth, order[0]);
        Assert.AreEqual(ArmorWeightClass.Light, order[1]);
        Assert.AreEqual(ArmorWeightClass.Heavy, order[2]);
        Assert.AreEqual(ItemSortOrder.WeightClassRank(null), ItemSortOrder.WeightClassRank(ArmorWeightClass.None));
    }

    [TestMethod]
    public void SlotRank_GoesHeadToToeThenClothesShieldsAndCloaks()
    {
        int Rank(EquipMask mask) => ItemSortOrder.SlotRank(mask);

        Assert.IsTrue(Rank(EquipMask.HeadWear) < Rank(EquipMask.ChestArmor));
        Assert.IsTrue(Rank(EquipMask.ChestArmor) < Rank(EquipMask.HandWear));
        Assert.IsTrue(Rank(EquipMask.HandWear) < Rank(EquipMask.UpperLegArmor));
        Assert.IsTrue(Rank(EquipMask.LowerLegArmor) < Rank(EquipMask.FootWear));

        // shirts, then pants, after all armor
        var shirt = EquipMask.ChestWear | EquipMask.UpperArmWear;
        var pants = EquipMask.AbdomenWear | EquipMask.UpperLegWear | EquipMask.LowerLegWear;
        Assert.IsTrue(Rank(EquipMask.FootWear) < Rank(shirt));
        Assert.IsTrue(Rank(shirt) < Rank(pants));
        Assert.IsTrue(Rank(pants) < Rank(EquipMask.Shield));
        Assert.IsTrue(Rank(EquipMask.Shield) < Rank(EquipMask.Cloak));

        // A piece covering several slots sorts by the highest one it covers: a coat is a chest piece.
        var coat = EquipMask.ChestArmor | EquipMask.AbdomenArmor | EquipMask.UpperArmArmor | EquipMask.LowerArmArmor;
        Assert.AreEqual(Rank(EquipMask.ChestArmor), Rank(coat));

        Assert.IsTrue(Rank(EquipMask.Cloak) < ItemSortOrder.SlotRank(null));
        Assert.IsTrue(Rank(EquipMask.Cloak) < Rank(EquipMask.None));
    }

    [TestMethod]
    public void StyleRank_FollowsTheArmorStyleListWithNoneLast()
    {
        Assert.IsTrue(ItemSortOrder.StyleRank(ArmorStyle.Leather) < ItemSortOrder.StyleRank(ArmorStyle.Chainmail));
        Assert.IsTrue(ItemSortOrder.StyleRank(ArmorStyle.Chainmail) < ItemSortOrder.StyleRank(ArmorStyle.Platemail));
        Assert.IsTrue(ItemSortOrder.StyleRank(ArmorStyle.Buckler) < ItemSortOrder.StyleRank(ArmorStyle.TowerShield));
        Assert.IsTrue(ItemSortOrder.StyleRank(ArmorStyle.CovenantShield) < ItemSortOrder.StyleRank(ArmorStyle.None));
        Assert.AreEqual(ItemSortOrder.StyleRank(null), ItemSortOrder.StyleRank(ArmorStyle.None));
    }

    [TestMethod]
    public void SkillRank_GoesMeleeThenMissileThenMagicWithNoSkillLast()
    {
        var order = new[]
        {
            Skill.None, Skill.LifeMagic, Skill.Bow, Skill.TwoHandedCombat, Skill.Staff, Skill.MartialWeapons,
            Skill.ThrownWeapon, Skill.WarMagic, Skill.Dagger, Skill.UnarmedCombat, Skill.Healing,
        }
            .OrderBy(ItemSortOrder.SkillRank)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                Skill.MartialWeapons, Skill.Dagger, Skill.Staff, Skill.UnarmedCombat, Skill.TwoHandedCombat,
                Skill.Bow, Skill.ThrownWeapon, Skill.WarMagic, Skill.LifeMagic, Skill.Healing, Skill.None,
            },
            order
        );
    }

    [TestMethod]
    public void SubtypeRank_FollowsTheWeaponSubtypeListWithNoneLast()
    {
        Assert.IsTrue(ItemSortOrder.SubtypeRank(LootTables.WeaponSubtype.AxeLarge) < ItemSortOrder.SubtypeRank(LootTables.WeaponSubtype.AxeSmall));
        Assert.IsTrue(ItemSortOrder.SubtypeRank(LootTables.WeaponSubtype.MaceSmall) < ItemSortOrder.SubtypeRank(LootTables.WeaponSubtype.SwordLarge));
        Assert.IsTrue(ItemSortOrder.SubtypeRank(LootTables.WeaponSubtype.ThrownShuriken) < ItemSortOrder.SubtypeRank(null));
        Assert.AreEqual(ItemSortOrder.SubtypeRank(null), ItemSortOrder.SubtypeRank(LootTables.WeaponSubtype.Undef));
    }

    [TestMethod]
    public void JewelrySlotRank_GoesNecklaceBraceletRing()
    {
        Assert.IsTrue(ItemSortOrder.JewelrySlotRank(EquipMask.NeckWear) < ItemSortOrder.JewelrySlotRank(EquipMask.WristWearLeft));
        Assert.AreEqual(ItemSortOrder.JewelrySlotRank(EquipMask.WristWearLeft), ItemSortOrder.JewelrySlotRank(EquipMask.WristWear));
        Assert.IsTrue(ItemSortOrder.JewelrySlotRank(EquipMask.WristWear) < ItemSortOrder.JewelrySlotRank(EquipMask.FingerWear));
        Assert.AreEqual(ItemSortOrder.JewelrySlotRank(EquipMask.FingerWearRight), ItemSortOrder.JewelrySlotRank(EquipMask.FingerWear));
        Assert.IsTrue(ItemSortOrder.JewelrySlotRank(EquipMask.FingerWear) < ItemSortOrder.JewelrySlotRank(null));
    }

    [TestMethod]
    public void AmmoRanks_GoByTypeThenElementWithNoneLast()
    {
        Assert.IsTrue(ItemSortOrder.AmmoTypeRank(AmmoType.Arrow) < ItemSortOrder.AmmoTypeRank(AmmoType.Bolt));
        Assert.IsTrue(ItemSortOrder.AmmoTypeRank(AmmoType.Bolt) < ItemSortOrder.AmmoTypeRank(AmmoType.Atlatl));
        Assert.IsTrue(ItemSortOrder.AmmoTypeRank(AmmoType.AtlatlCrystal) < ItemSortOrder.AmmoTypeRank(AmmoType.None));
        Assert.AreEqual(ItemSortOrder.AmmoTypeRank(null), ItemSortOrder.AmmoTypeRank(AmmoType.None));

        Assert.IsTrue(ItemSortOrder.ElementRank(DamageType.Slash) < ItemSortOrder.ElementRank(DamageType.Fire));
        Assert.IsTrue(ItemSortOrder.ElementRank(DamageType.Electric) < ItemSortOrder.ElementRank(DamageType.Undef));
    }

    [TestMethod]
    public void KitVitalRank_GoesHealthStaminaManaThenOther()
    {
        Assert.IsTrue(ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Health) < ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Stamina));
        Assert.IsTrue(ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Stamina) < ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Mana));
        Assert.IsTrue(ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Mana) < ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Undef));

        // Kits that name the max vital count as that vital.
        Assert.AreEqual(ItemSortOrder.KitVitalRank(PropertyAttribute2nd.Health), ItemSortOrder.KitVitalRank(PropertyAttribute2nd.MaxHealth));
    }

    [TestMethod]
    public void TrophyTypeRank_IsTheSameForEveryQualityOfATrophy()
    {
        var hideBase = (uint)ACE.Entity.Enum.WeenieClassName.W_MATTEKARHIDETROPHY_CLASS;
        var hornBase = (uint)ACE.Entity.Enum.WeenieClassName.W_MATTEKARHORN_CLASS;

        var hideDamaged = TrophyWcids.GetTrophyWcid(hideBase, 1);
        var hidePeerless = TrophyWcids.GetTrophyWcid(hideBase, 10);

        Assert.AreEqual(ItemSortOrder.TrophyTypeRank(hideDamaged), ItemSortOrder.TrophyTypeRank(hidePeerless));
        Assert.AreNotEqual(ItemSortOrder.TrophyTypeRank(hideDamaged), ItemSortOrder.TrophyTypeRank(TrophyWcids.GetTrophyWcid(hornBase, 5)));

        // Anything that isn't one of the trophy types goes after them.
        Assert.AreEqual(long.MaxValue, ItemSortOrder.TrophyTypeRank(1));
        Assert.IsTrue(ItemSortOrder.TrophyTypeRank(hidePeerless) < ItemSortOrder.TrophyTypeRank(1));
    }

    [TestMethod]
    public void ComponentOrder_IsScarabsByTierThenHerbPowderPotionTalismanTaper()
    {
        Assert.IsTrue(ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Scarab) < ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Herb));
        Assert.IsTrue(ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Herb) < ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Powder));
        Assert.IsTrue(ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Powder) < ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Talisman));
        Assert.IsTrue(ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Talisman) < ItemSortOrder.GetComponentTypeOrder((uint)SpellComponentsTable.Type.Taper));

        Assert.IsTrue(ItemSortOrder.GetScarabMaterialOrder("Lead Scarab") < ItemSortOrder.GetScarabMaterialOrder("Iron Scarab"));
        Assert.IsTrue(ItemSortOrder.GetScarabMaterialOrder("Platinum Scarab") < ItemSortOrder.GetScarabMaterialOrder("Diamond Scarab"));
        Assert.IsTrue(ItemSortOrder.GetScarabMaterialOrder("Diamond Scarab") < ItemSortOrder.GetScarabMaterialOrder("Mana Scarab"));
    }

    [TestMethod]
    public void WeaponSkillOf_UsesTheWeaponsSkillAndFallsBackToItsSubtype()
    {
        Assert.AreEqual(Skill.Dagger, ItemSortOrder.WeaponSkillOf(Skill.Dagger, LootTables.WeaponSubtype.SwordLarge));

        Assert.AreEqual(Skill.MartialWeapons, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.MaceMedium));
        Assert.AreEqual(Skill.MartialWeapons, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.SwordSmall));
        Assert.AreEqual(Skill.Dagger, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.DaggerLarge));
        Assert.AreEqual(Skill.TwoHandedCombat, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.TwohandSword));
        Assert.AreEqual(Skill.Bow, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.CrossbowSmall));
        Assert.AreEqual(Skill.ThrownWeapon, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.AtlatlLarge));
        Assert.AreEqual(Skill.None, ItemSortOrder.WeaponSkillOf(Skill.None, LootTables.WeaponSubtype.Caster));
        Assert.AreEqual(Skill.None, ItemSortOrder.WeaponSkillOf(Skill.None, null));
    }
}
