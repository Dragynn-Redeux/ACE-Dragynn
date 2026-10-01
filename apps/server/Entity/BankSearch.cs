using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Factories.Enum;
using ACE.Server.Factories.Tables.Wcids.Weapons;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity;

/// <summary>
/// The weapon classes /bank search understands. They are flags so a two-handed sword can be both Sword and TwoHanded.
/// </summary>
[Flags]
public enum WeaponClass
{
    None = 0,
    Axe = 1 << 0,
    Dagger = 1 << 1,
    Mace = 1 << 2,
    Spear = 1 << 3,
    Staff = 1 << 4,
    Sword = 1 << 5,
    Unarmed = 1 << 6,
    Bow = 1 << 7,
    Crossbow = 1 << 8,
    Atlatl = 1 << 9,
    Thrown = 1 << 10,
    TwoHanded = 1 << 11,
}

/// <summary>
/// Matching for /bank search: item names, ignoring spaces and punctuation, and weapon classes.
/// </summary>
public static class BankSearch
{
    private static readonly Dictionary<string, WeaponClass> ClassWords = new()
    {
        { "axe", WeaponClass.Axe },
        { "axes", WeaponClass.Axe },
        { "dagger", WeaponClass.Dagger },
        { "daggers", WeaponClass.Dagger },
        { "mace", WeaponClass.Mace },
        { "maces", WeaponClass.Mace },
        { "spear", WeaponClass.Spear },
        { "spears", WeaponClass.Spear },
        { "staff", WeaponClass.Staff },
        { "staffs", WeaponClass.Staff },
        { "staves", WeaponClass.Staff },
        { "sword", WeaponClass.Sword },
        { "swords", WeaponClass.Sword },
        { "unarmed", WeaponClass.Unarmed },
        { "ua", WeaponClass.Unarmed },
        { "bow", WeaponClass.Bow },
        { "bows", WeaponClass.Bow },
        { "crossbow", WeaponClass.Crossbow },
        { "crossbows", WeaponClass.Crossbow },
        { "atlatl", WeaponClass.Atlatl },
        { "atlatls", WeaponClass.Atlatl },
        { "thrown", WeaponClass.Thrown },
        { "twohanded", WeaponClass.TwoHanded },
        { "2h", WeaponClass.TwoHanded },
    };

    /// <summary>
    /// Lowercase letters and digits only, so "Long Sword", "long-sword" and "longsword" all read "longsword".
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>
    /// Reads a weapon class as typed: mace, swords, two-handed, 2h, ...
    /// </summary>
    public static bool TryParseWeaponClass(string query, out WeaponClass weaponClass)
    {
        return ClassWords.TryGetValue(Normalize(query), out weaponClass);
    }

    /// <summary>
    /// True if name contains query, either as typed or with spaces and punctuation left out of both,
    /// so "longsword" finds "Acid Long Sword" and "war hammer" finds "Warhammer".
    /// </summary>
    public static bool NameMatches(string name, string query)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        if (name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedQuery = Normalize(query);
        return normalizedQuery.Length > 0 && Normalize(name).Contains(normalizedQuery);
    }

    /// <summary>
    /// The class of a weapon the loot tables roll, by the table it is in (so a jitte is a mace and a war hammer an axe).
    /// </summary>
    public static WeaponClass ClassOf(TreasureWeaponType weaponType)
    {
        return weaponType switch
        {
            TreasureWeaponType.Axe => WeaponClass.Axe,
            TreasureWeaponType.Dagger or TreasureWeaponType.DaggerMS => WeaponClass.Dagger,
            TreasureWeaponType.Mace or TreasureWeaponType.MaceJitte => WeaponClass.Mace,
            TreasureWeaponType.Spear => WeaponClass.Spear,
            TreasureWeaponType.Staff => WeaponClass.Staff,
            TreasureWeaponType.Sword or TreasureWeaponType.SwordMS => WeaponClass.Sword,
            TreasureWeaponType.Unarmed => WeaponClass.Unarmed,
            TreasureWeaponType.Bow or TreasureWeaponType.BowShort => WeaponClass.Bow,
            TreasureWeaponType.Crossbow or TreasureWeaponType.CrossbowLight => WeaponClass.Crossbow,
            TreasureWeaponType.Atlatl or TreasureWeaponType.AtlatlRegular => WeaponClass.Atlatl,
            TreasureWeaponType.Thrown => WeaponClass.Thrown,
            TreasureWeaponType.TwoHandedAxe => WeaponClass.Axe | WeaponClass.TwoHanded,
            TreasureWeaponType.TwoHandedMace => WeaponClass.Mace | WeaponClass.TwoHanded,
            TreasureWeaponType.TwoHandedSpear => WeaponClass.Spear | WeaponClass.TwoHanded,
            TreasureWeaponType.TwoHandedSword => WeaponClass.Sword | WeaponClass.TwoHanded,
            TreasureWeaponType.TwoHandedWeapon => WeaponClass.TwoHanded,
            _ => WeaponClass.None,
        };
    }

    /// <summary>
    /// The class of a weapon by its own WeaponType property, for weapons the loot tables don't roll (quest weapons, ...).
    /// </summary>
    public static WeaponClass ClassOf(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.Axe => WeaponClass.Axe,
            WeaponType.Dagger => WeaponClass.Dagger,
            WeaponType.Mace => WeaponClass.Mace,
            WeaponType.Spear => WeaponClass.Spear,
            WeaponType.Staff => WeaponClass.Staff,
            WeaponType.Sword => WeaponClass.Sword,
            WeaponType.Unarmed => WeaponClass.Unarmed,
            WeaponType.Bow => WeaponClass.Bow,
            WeaponType.Crossbow => WeaponClass.Crossbow,
            WeaponType.Thrown => WeaponClass.Thrown,
            WeaponType.TwoHanded => WeaponClass.TwoHanded,
            _ => WeaponClass.None,
        };
    }

    /// <summary>
    /// A weapon's class: from the loot table that rolls its weenie if there is one, as loot generation looks it up,
    /// otherwise from its WeaponType.
    /// </summary>
    public static WeaponClass GetWeaponClass(uint weenieClassId, WeaponType weaponType)
    {
        var wcid = (ACE.Server.Factories.Enum.WeenieClassName)weenieClassId;

        if (
            HeavyWeaponWcids.TryGetValue(wcid, out var treasureType)
            || LightWeaponWcids.TryGetValue(wcid, out treasureType)
            || FinesseWeaponWcids.TryGetValue(wcid, out treasureType)
            || TwoHandedWeaponWcids.TryGetValue(wcid, out treasureType)
            || ThrownWcids.TryGetValue(wcid, out treasureType)
            || BowWcids.TryGetValue(wcid, out treasureType)
            || CrossbowWcids.TryGetValue(wcid, out treasureType)
            || AtlatlWcids.TryGetValue(wcid, out treasureType)
        )
        {
            var fromTable = ClassOf(treasureType);
            if (fromTable != WeaponClass.None)
            {
                return fromTable;
            }
        }

        return ClassOf(weaponType);
    }

    /// <summary>
    /// The weapon class of an item, or None for anything that isn't a weapon (arrows and casters included).
    /// </summary>
    public static WeaponClass GetWeaponClass(WorldObject item)
    {
        if (BankCategories.Classify(item) != BankCategory.Weapons)
        {
            return WeaponClass.None;
        }

        return GetWeaponClass(item.WeenieClassId, item.W_WeaponType);
    }
}
