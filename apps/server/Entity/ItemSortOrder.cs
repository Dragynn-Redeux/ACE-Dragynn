using System;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Factories;
using ACE.Server.Factories.Tables.Wcids;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity;

/// <summary>
/// The order /sort and /bank sort put items in. First by bank category (weapons, armor, jewelry, ... see
/// BankCategories.Singles, uncategorized last), then by rules for that kind of item, then by name:
/// - Weapons: skill, then weapon subtype.
/// - Armor: weight class, then equip slot (head to toe), then armor style.
/// - Jewelry: slot (necklace, bracelet, ring).
/// - Trinkets: trinket type, then color.
/// - Ammo: ammo type, then element.
/// - Salvage: material category, then material, then workmanship.
/// - Components: scarabs by tier, herbs, powders, potions, talismans, tapers, then reusable (pea) components.
/// - Consumables: healing kits first, by vital (health, stamina, mana) then quality, best first; then the rest.
/// - Trophies: trophy type, then quality, best first.
/// - Animal parts: hides, bones, then meat, each by quality, best first.
/// - Everything else (gems, keys, mana stones, ...): name.
/// Names compare without the material first, then with it, so the same item in different materials sits together.
/// </summary>
public static class ItemSortOrder
{
    /// <summary>
    /// Weapon skills in sort order: melee, then missile, then magic. Other skills follow, and no skill goes last.
    /// These are the skills combat uses on this server (WorldObject.ConvertToMoASkill of the weapon's WeaponSkill).
    /// </summary>
    private static readonly Skill[] SkillOrder =
    [
        Skill.MartialWeapons,
        Skill.Dagger,
        Skill.Staff,
        Skill.UnarmedCombat,
        Skill.TwoHandedCombat,
        Skill.Bow,
        Skill.ThrownWeapon,
        Skill.WarMagic,
        Skill.LifeMagic,
        Skill.VoidMagic,
    ];

    /// <summary>
    /// Equip slots head to toe: armor, then under-clothes (shirts, then pants), then shields and cloaks.
    /// An item is placed by the first of these it covers, so a coat sorts with chest pieces.
    /// </summary>
    private static readonly EquipMask[] SlotOrder =
    [
        EquipMask.HeadWear,
        EquipMask.ChestArmor,
        EquipMask.AbdomenArmor,
        EquipMask.UpperArmArmor,
        EquipMask.LowerArmArmor,
        EquipMask.HandWear,
        EquipMask.UpperLegArmor,
        EquipMask.LowerLegArmor,
        EquipMask.FootWear,
        EquipMask.ChestWear,
        EquipMask.UpperArmWear,
        EquipMask.LowerArmWear,
        EquipMask.AbdomenWear,
        EquipMask.UpperLegWear,
        EquipMask.LowerLegWear,
        EquipMask.Shield,
        EquipMask.Cloak,
    ];

    public static int Compare(WorldObject a, WorldObject b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        var categoryA = BankCategories.Classify(a);
        var categoryB = BankCategories.Classify(b);

        var result = BankCategories.SortOrder(categoryA).CompareTo(BankCategories.SortOrder(categoryB));
        if (result != 0)
        {
            return result;
        }

        result = categoryA switch
        {
            BankCategory.Weapons => CompareWeapons(a, b),
            BankCategory.Armor => CompareArmor(a, b),
            BankCategory.Jewelry => JewelrySlotRank(a.ValidLocations).CompareTo(JewelrySlotRank(b.ValidLocations)),
            BankCategory.Trinkets => CompareTrinkets(a, b),
            BankCategory.Ammo => CompareAmmo(a, b),
            BankCategory.Salvage => Salvage.GetSalvageBagSortKey(a).CompareTo(Salvage.GetSalvageBagSortKey(b)),
            BankCategory.Components => GetSpellComponentSortKey(a).CompareTo(GetSpellComponentSortKey(b)),
            BankCategory.Consumables => CompareConsumables(a, b),
            BankCategory.Trophies => CompareTrophies(a, b),
            BankCategory.Animal => CompareAnimalParts(a, b),
            _ => 0,
        };
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        if (result != 0)
        {
            return result;
        }

        result = string.Compare(a.NameWithMaterial, b.NameWithMaterial, StringComparison.OrdinalIgnoreCase);
        if (result != 0)
        {
            return result;
        }

        result = (b.StackSize ?? 1).CompareTo(a.StackSize ?? 1);
        if (result != 0)
        {
            return result;
        }

        return a.Guid.Full.CompareTo(b.Guid.Full);
    }

    private static int CompareWeapons(WorldObject a, WorldObject b)
    {
        var subtypeA = (LootTables.WeaponSubtype?)a.WeaponSubtype;
        var subtypeB = (LootTables.WeaponSubtype?)b.WeaponSubtype;

        var result = SkillRank(WeaponSkillOf(a.ConvertToMoASkill(a.WeaponSkill), subtypeA))
            .CompareTo(SkillRank(WeaponSkillOf(b.ConvertToMoASkill(b.WeaponSkill), subtypeB)));
        if (result != 0)
        {
            return result;
        }

        return SubtypeRank(subtypeA).CompareTo(SubtypeRank(subtypeB));
    }

    private static int CompareArmor(WorldObject a, WorldObject b)
    {
        var result = WeightClassRank((ArmorWeightClass?)a.ArmorWeightClass)
            .CompareTo(WeightClassRank((ArmorWeightClass?)b.ArmorWeightClass));
        if (result != 0)
        {
            return result;
        }

        result = SlotRank(a.ValidLocations).CompareTo(SlotRank(b.ValidLocations));
        if (result != 0)
        {
            return result;
        }

        return StyleRank((ArmorStyle?)a.ArmorStyle).CompareTo(StyleRank((ArmorStyle?)b.ArmorStyle));
    }

    private static int CompareTrinkets(WorldObject a, WorldObject b)
    {
        var result = RankOrLast(a.GetProperty(PropertyInt.SigilTrinketType))
            .CompareTo(RankOrLast(b.GetProperty(PropertyInt.SigilTrinketType)));
        if (result != 0)
        {
            return result;
        }

        return RankOrLast(a.GetProperty(PropertyInt.SigilTrinketColor))
            .CompareTo(RankOrLast(b.GetProperty(PropertyInt.SigilTrinketColor)));
    }

    private static int CompareAmmo(WorldObject a, WorldObject b)
    {
        var result = AmmoTypeRank(a.AmmoType).CompareTo(AmmoTypeRank(b.AmmoType));
        if (result != 0)
        {
            return result;
        }

        return ElementRank(a.W_DamageType).CompareTo(ElementRank(b.W_DamageType));
    }

    private static int CompareConsumables(WorldObject a, WorldObject b)
    {
        var aIsKit = a.WeenieType == WeenieType.Healer;
        var bIsKit = b.WeenieType == WeenieType.Healer;

        if (aIsKit != bIsKit)
        {
            return aIsKit ? -1 : 1;
        }

        if (!aIsKit)
        {
            return 0;
        }

        var result = KitVitalRank(a.BoosterEnum).CompareTo(KitVitalRank(b.BoosterEnum));
        if (result != 0)
        {
            return result;
        }

        // better kits (a bigger heal) first
        return (b.HealkitMod ?? 1.0).CompareTo(a.HealkitMod ?? 1.0);
    }

    private static int CompareAnimalParts(WorldObject a, WorldObject b)
    {
        BankCategories.TryGetAnimalPart(a.WeenieClassId, out var kindA, out var qualityA);
        BankCategories.TryGetAnimalPart(b.WeenieClassId, out var kindB, out var qualityB);

        var result = kindA.CompareTo(kindB);
        if (result != 0)
        {
            return result;
        }

        // better parts first
        return qualityB.CompareTo(qualityA);
    }

    private static int CompareTrophies(WorldObject a, WorldObject b)
    {
        var result = TrophyTypeRank(a.WeenieClassId).CompareTo(TrophyTypeRank(b.WeenieClassId));
        if (result != 0)
        {
            return result;
        }

        // better trophies first
        return (b.TrophyQuality ?? 0).CompareTo(a.TrophyQuality ?? 0);
    }

    /// <summary>
    /// Necklaces, bracelets, rings, then anything else worn as jewelry.
    /// </summary>
    public static int JewelrySlotRank(EquipMask? slots)
    {
        var mask = slots ?? EquipMask.None;

        if ((mask & EquipMask.NeckWear) != 0)
        {
            return 0;
        }

        if ((mask & EquipMask.WristWear) != 0)
        {
            return 1;
        }

        if ((mask & EquipMask.FingerWear) != 0)
        {
            return 2;
        }

        return 3;
    }

    /// <summary>
    /// Arrows, bolts, atlatl darts, then the crystal ones, in AmmoType order. No type goes last.
    /// </summary>
    public static int AmmoTypeRank(AmmoType? ammoType)
    {
        return ammoType is null or AmmoType.None ? int.MaxValue : (int)ammoType.Value;
    }

    /// <summary>
    /// In DamageType order (slash, pierce, bludgeon, cold, fire, acid, electric, ...). No element goes last.
    /// </summary>
    public static int ElementRank(DamageType damageType)
    {
        return damageType == DamageType.Undef ? int.MaxValue : (int)damageType;
    }

    /// <summary>
    /// Health kits, stamina kits, mana kits, then anything else.
    /// </summary>
    public static int KitVitalRank(PropertyAttribute2nd vital)
    {
        return vital switch
        {
            PropertyAttribute2nd.Health or PropertyAttribute2nd.MaxHealth => 0,
            PropertyAttribute2nd.Stamina or PropertyAttribute2nd.MaxStamina => 1,
            PropertyAttribute2nd.Mana or PropertyAttribute2nd.MaxMana => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// A trophy's type is the trophy it is at any quality: its base (quality 1) wcid, legacy trophies included.
    /// The bases run in creature order, so types come out grouped and in that order. Other trophies go last.
    /// </summary>
    public static long TrophyTypeRank(uint weenieClassId)
    {
        var baseWcid = TrophyWcids.ToBaseTrophyWcid(weenieClassId);
        return baseWcid == 0 ? long.MaxValue : baseWcid;
    }

    private static int RankOrLast(int? value)
    {
        return value ?? int.MaxValue;
    }

    // Desired spell component ordering: Scarab > Herb > Powder > Talisman > Taper (Potion is a
    // formula-only category with no droppable item, kept in its dat-defined slot between Powder
    // and Talisman). Scarabs are further ordered by material tier.
    private static readonly string[] ScarabMaterialOrder =
    {
        "Lead",
        "Iron",
        "Copper",
        "Silver",
        "Gold",
        "Pyreal",
        "Platinum",
        "Diamond"
    };

    public static int GetComponentTypeOrder(uint componentType)
    {
        if (componentType == (uint)SpellComponentsTable.Type.Scarab)
        {
            return 0;
        }

        if (componentType == (uint)SpellComponentsTable.Type.Herb)
        {
            return 1;
        }

        if (componentType == (uint)SpellComponentsTable.Type.Powder)
        {
            return 2;
        }

        if (componentType == (uint)SpellComponentsTable.Type.Potion)
        {
            return 3;
        }

        if (componentType == (uint)SpellComponentsTable.Type.Talisman)
        {
            return 4;
        }

        if (componentType == (uint)SpellComponentsTable.Type.Taper)
        {
            return 5;
        }

        return 6;
    }

    public static int GetScarabMaterialOrder(string componentName)
    {
        if (!string.IsNullOrEmpty(componentName))
        {
            for (var i = 0; i < ScarabMaterialOrder.Length; i++)
            {
                if (componentName.Contains(ScarabMaterialOrder[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return ScarabMaterialOrder.Length;
    }

    // Non-component items (and any component whose id fails the dat lookup) return the same
    // neutral key, so they fall through unchanged to the name ordering.
    //
    // "Pea" (reusable) components are separate weenies from their consumable counterparts and
    // carry no runtime flag for it, but they share the consumable's PropertyDataId.SpellComponent
    // id (so spellcasting/formula matching treats them the same) and their WeenieClassName always
    // carries a "pea" prefix (peascarablead, peaherbamaranth, etc.), so that prefix is what we key
    // the "own section after the above" split on.
    private static (bool IsPea, int TypeOrder, int MaterialOrder) GetSpellComponentSortKey(WorldObject item)
    {
        if (item.WeenieType != WeenieType.SpellComponent)
        {
            return (false, 0, 0);
        }

        var isPea = item.WeenieClassName?.StartsWith("pea", StringComparison.OrdinalIgnoreCase) ?? false;

        var componentId = item.GetProperty(PropertyDataId.SpellComponent) ?? 0;
        if (!DatManager.PortalDat.SpellComponentsTable.SpellComponents.TryGetValue(componentId, out var component))
        {
            return (isPea, 6, 0);
        }

        var typeOrder = GetComponentTypeOrder(component.Type);
        var materialOrder = component.Type == (uint)SpellComponentsTable.Type.Scarab
            ? GetScarabMaterialOrder(component.Name)
            : 0;

        return (isPea, typeOrder, materialOrder);
    }

    /// <summary>
    /// A weapon's skill as combat uses it. A weapon without one gets the skill its subtype belongs to.
    /// </summary>
    public static Skill WeaponSkillOf(Skill convertedWeaponSkill, LootTables.WeaponSubtype? subtype)
    {
        if (convertedWeaponSkill != Skill.None)
        {
            return convertedWeaponSkill;
        }

        return subtype switch
        {
            LootTables.WeaponSubtype.AxeLarge or LootTables.WeaponSubtype.AxeMedium or LootTables.WeaponSubtype.AxeSmall
                or LootTables.WeaponSubtype.MaceLarge or LootTables.WeaponSubtype.MaceMedium or LootTables.WeaponSubtype.MaceSmall
                or LootTables.WeaponSubtype.SpearLarge or LootTables.WeaponSubtype.SpearMedium or LootTables.WeaponSubtype.SpearSmall
                or LootTables.WeaponSubtype.SwordLarge or LootTables.WeaponSubtype.SwordMedium or LootTables.WeaponSubtype.SwordSmall
                => Skill.MartialWeapons,
            LootTables.WeaponSubtype.DaggerLarge or LootTables.WeaponSubtype.DaggerSmall => Skill.Dagger,
            LootTables.WeaponSubtype.StaffLarge or LootTables.WeaponSubtype.StaffMedium or LootTables.WeaponSubtype.StaffSmall
                => Skill.Staff,
            LootTables.WeaponSubtype.Ua => Skill.UnarmedCombat,
            LootTables.WeaponSubtype.TwohandAxe or LootTables.WeaponSubtype.TwohandMace
                or LootTables.WeaponSubtype.TwohandSpear or LootTables.WeaponSubtype.TwohandSword
                => Skill.TwoHandedCombat,
            LootTables.WeaponSubtype.BowLarge or LootTables.WeaponSubtype.BowSmall
                or LootTables.WeaponSubtype.CrossbowLarge or LootTables.WeaponSubtype.CrossbowSmall
                => Skill.Bow,
            LootTables.WeaponSubtype.AtlatlLarge or LootTables.WeaponSubtype.AtlatlSmall
                or LootTables.WeaponSubtype.ThrownAxe or LootTables.WeaponSubtype.ThrownJavelin
                or LootTables.WeaponSubtype.ThrownClub or LootTables.WeaponSubtype.ThrownDart
                or LootTables.WeaponSubtype.ThrownDagger or LootTables.WeaponSubtype.ThrownShuriken
                => Skill.ThrownWeapon,
            _ => Skill.None,
        };
    }

    public static int SkillRank(Skill skill)
    {
        var index = Array.IndexOf(SkillOrder, skill);
        if (index >= 0)
        {
            return index;
        }

        return skill == Skill.None ? SkillOrder.Length + 1 : SkillOrder.Length;
    }

    /// <summary>
    /// In WeaponSubtype order (each weapon large to small, then two-handed, missile, caster, thrown). None goes last.
    /// </summary>
    public static int SubtypeRank(LootTables.WeaponSubtype? subtype)
    {
        return subtype is null or LootTables.WeaponSubtype.Undef ? int.MaxValue : (int)subtype.Value;
    }

    /// <summary>
    /// Cloth, light, heavy, then armor with no weight class.
    /// </summary>
    public static int WeightClassRank(ArmorWeightClass? weightClass)
    {
        return weightClass switch
        {
            ArmorWeightClass.Cloth => 0,
            ArmorWeightClass.Light => 1,
            ArmorWeightClass.Heavy => 2,
            _ => 3,
        };
    }

    public static int SlotRank(EquipMask? slots)
    {
        if (slots is { } mask)
        {
            for (var i = 0; i < SlotOrder.Length; i++)
            {
                if ((mask & SlotOrder[i]) != 0)
                {
                    return i;
                }
            }
        }

        return SlotOrder.Length;
    }

    /// <summary>
    /// In ArmorStyle order (cloth, leather, studded, chain, plate, ... shields). None goes last.
    /// </summary>
    public static int StyleRank(ArmorStyle? style)
    {
        return style is null or ArmorStyle.None ? int.MaxValue : (int)style.Value;
    }
}
