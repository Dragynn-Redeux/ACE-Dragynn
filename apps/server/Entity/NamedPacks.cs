using System;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity;

/// <summary>
/// Packs that take a kind of item because of their name: Salvage Crate, Quiver, Component Pouch, Trophy Pack.
/// /sort fills these first, and so do /bank deposit and /bank sort, ahead of any inscribed pack.
/// </summary>
public enum NamedPackKind
{
    None,
    SalvageCrate,
    Quiver,
    ComponentPouch,
    TrophyPack,
}

public static class NamedPacks
{
    /// <summary>
    /// Which named pack this is, by its name containing "Salvage Crate", "Quiver", "Component Pouch" or "Trophy Pack".
    /// </summary>
    public static NamedPackKind KindOf(string packName)
    {
        if (string.IsNullOrEmpty(packName))
        {
            return NamedPackKind.None;
        }

        if (packName.Contains("Salvage Crate", StringComparison.OrdinalIgnoreCase))
        {
            return NamedPackKind.SalvageCrate;
        }

        if (packName.Contains("Quiver", StringComparison.OrdinalIgnoreCase))
        {
            return NamedPackKind.Quiver;
        }

        if (packName.Contains("Component Pouch", StringComparison.OrdinalIgnoreCase))
        {
            return NamedPackKind.ComponentPouch;
        }

        if (packName.Contains("Trophy Pack", StringComparison.OrdinalIgnoreCase))
        {
            return NamedPackKind.TrophyPack;
        }

        return NamedPackKind.None;
    }

    public static NamedPackKind KindOf(Container pack)
    {
        return KindOf(pack?.Name);
    }

    /// <summary>
    /// True if a pack of this kind is where the item goes: salvage in a salvage crate, ammunition in a quiver,
    /// spell components in a component pouch, trophies (and trophy essences) in a trophy pack.
    /// </summary>
    public static bool Takes(NamedPackKind kind, WeenieType weenieType, bool isTrophy)
    {
        return kind switch
        {
            NamedPackKind.SalvageCrate => weenieType == WeenieType.Salvage,
            NamedPackKind.Quiver => weenieType == WeenieType.Ammunition,
            NamedPackKind.ComponentPouch => weenieType == WeenieType.SpellComponent,
            NamedPackKind.TrophyPack => isTrophy,
            _ => false,
        };
    }

    public static bool Takes(NamedPackKind kind, WorldObject item)
    {
        return Takes(kind, item.WeenieType, item.IsTrophy);
    }

    /// <summary>
    /// True if pack is a named pack meant for this item.
    /// </summary>
    public static bool IsHomeFor(Container pack, WorldObject item)
    {
        return Takes(KindOf(pack), item);
    }

    /// <summary>
    /// Everything a pack collects: its inscription's categories, plus its own kind if it is a named pack.
    /// </summary>
    public static BankCategory Collects(Container pack)
    {
        var collects = BankCategories.ParseInscription(pack.Inscription).Categories | CategoryOf(KindOf(pack));

        // A pack for a whole category takes every kind in it (a "weapons" pack takes swords, a "salvage" pack imbue),
        // and a pack for a kind takes part of its category (a "swords" pack gets weapons in a /bank sort weapons).
        return collects | BankCategories.KindsWithin(collects) | BankCategories.ParentsOf(collects);
    }

    /// <summary>
    /// The bank category a named pack collects, for showing what it holds.
    /// </summary>
    public static BankCategory CategoryOf(NamedPackKind kind)
    {
        return kind switch
        {
            NamedPackKind.SalvageCrate => BankCategory.Salvage,
            NamedPackKind.Quiver => BankCategory.Ammo,
            NamedPackKind.ComponentPouch => BankCategory.Components,
            NamedPackKind.TrophyPack => BankCategory.Trophies,
            _ => BankCategory.None,
        };
    }
}
