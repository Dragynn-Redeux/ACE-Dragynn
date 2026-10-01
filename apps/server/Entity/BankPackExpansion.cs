using System;
using System.Linq;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;

namespace ACE.Server.Entity;

/// <summary>
/// Plain packs and Trophy Packs hold more while they are in the bank (bank_pack_expansion_capacity, 100 by default).
/// A pack goes back to its own size when it is taken out, and can't be taken out while it holds more than that.
///
/// A pack's own size is the ItemsCapacity of its weenie: nothing on this server changes a pack's capacity otherwise.
/// Capacity is one byte on the wire, so 255 is the most a pack can hold.
/// </summary>
public static class BankPackExpansion
{
    public const string EnabledSetting = "bank_pack_expansion";
    public const string CapacitySetting = "bank_pack_expansion_capacity";

    public const int MaxCapacity = byte.MaxValue;

    /// <summary>
    /// Plain packs (no item-type restriction, not a Salvage Crate, Quiver or Component Pouch) and Trophy Packs.
    /// </summary>
    public static bool IsEligible(NamedPackKind kind, int merchandiseItemTypes)
    {
        return kind == NamedPackKind.TrophyPack || (kind == NamedPackKind.None && merchandiseItemTypes == 0);
    }

    public static bool IsEligible(Container pack)
    {
        return pack is { WeenieType: WeenieType.Container }
            && IsEligible(NamedPacks.KindOf(pack), pack.MerchandiseItemTypes ?? 0);
    }

    /// <summary>
    /// The capacity a pack gets in the bank: the configured size when the feature is on, its own size when it is off,
    /// never less than its own size, and never less than what it already holds (so turning the feature off or down
    /// never leaves items past the end of a pack).
    /// </summary>
    public static int CapacityInBank(bool enabled, long configuredCapacity, int ownCapacity, int itemCount)
    {
        var target = enabled ? (int)Math.Clamp(configuredCapacity, 0, MaxCapacity) : ownCapacity;

        return Math.Clamp(Math.Max(Math.Max(target, ownCapacity), itemCount), 0, MaxCapacity);
    }

    /// <summary>
    /// The capacity a pack gets when it leaves the bank: its own size, or what it holds if that is more.
    /// Taking an over-full pack out of the bank is refused, so the second case only happens if one got out anyway.
    /// </summary>
    public static int CapacityOutOfBank(int ownCapacity, int itemCount)
    {
        return Math.Clamp(Math.Max(ownCapacity, itemCount), 0, MaxCapacity);
    }

    /// <summary>
    /// The size of this kind of pack, from its weenie, or null if it can't be looked up.
    /// </summary>
    public static int? GetOwnCapacity(Container pack)
    {
        var weenie = DatabaseManager.World.GetCachedWeenie(pack.WeenieClassId);
        return weenie?.GetProperty(PropertyInt.ItemsCapacity);
    }

    public static int CountItems(Container pack)
    {
        return pack.Inventory.Values.Count(i => !i.UseBackpackSlot);
    }

    /// <summary>
    /// Gives an eligible pack in the bank its bank capacity. Returns true if the capacity changed.
    /// </summary>
    public static bool ApplyInBank(Container pack)
    {
        if (!IsEligible(pack) || GetOwnCapacity(pack) is not { } ownCapacity)
        {
            return false;
        }

        var capacity = CapacityInBank(
            PropertyManager.GetBool(EnabledSetting).Item,
            PropertyManager.GetLong(CapacitySetting).Item,
            ownCapacity,
            CountItems(pack)
        );

        return SetCapacity(pack, capacity);
    }

    /// <summary>
    /// Puts an eligible pack that is leaving the bank (or was found outside it) back to its own size.
    /// Returns true if the capacity changed.
    /// </summary>
    public static bool Revert(Container pack)
    {
        if (!IsEligible(pack) || GetOwnCapacity(pack) is not { } ownCapacity)
        {
            return false;
        }

        // Only undo an expansion; a pack at or under its own size is left as it is.
        if ((pack.ItemCapacity ?? 0) <= ownCapacity)
        {
            return false;
        }

        return SetCapacity(pack, CapacityOutOfBank(ownCapacity, CountItems(pack)));
    }

    /// <summary>
    /// False if pack holds more than its own size, so it has to stay in the bank until some of it is taken out.
    /// </summary>
    public static bool CanLeaveBank(Container pack, out int ownCapacity, out int itemCount)
    {
        ownCapacity = 0;
        itemCount = 0;

        if (!IsEligible(pack) || GetOwnCapacity(pack) is not { } capacity)
        {
            return true;
        }

        ownCapacity = capacity;
        itemCount = CountItems(pack);

        return itemCount <= ownCapacity;
    }

    private static bool SetCapacity(Container pack, int capacity)
    {
        if ((pack.ItemCapacity ?? 0) == capacity)
        {
            return false;
        }

        pack.ItemCapacity = (byte)capacity;
        return true;
    }
}
