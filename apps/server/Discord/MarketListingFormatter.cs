using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Market;
using Discord;

namespace ACE.Server.Discord;

internal static class MarketListingFormatter
{
    // Types
    private sealed class ListingFormatCache
    {
        internal ACE.Entity.Models.Weenie? Weenie;
        internal string? Name;
        internal int? StackSize;
        internal MarketListingSnapshot? Snapshot;
        internal bool SnapshotInitialized;
    }

    private enum ListingEmbedKind
    {
        Unknown = 0,
        Weapon = 1,
        MissileWeapon = 5,
        Caster = 6,
        Armor = 2,
        Jewelry = 3,
        SigilTrinket = 4,
        Clothing = 7,
    }

    // Public API
    internal static string BuildListingMarkdown(ACE.Database.Models.Shard.PlayerMarketListing listing, DateTime now)
    {
        var cache = new ListingFormatCache();
        return FormatListingLine(listing, now, cache);
    }

    internal static EmbedBuilder BuildListingEmbed(ACE.Database.Models.Shard.PlayerMarketListing listing, DateTime now, string? footer = null)
    {
        var cache = new ListingFormatCache();
        var line = FormatListingLine(listing, now, cache);

        // Do not recreate/destroy WorldObject instances for listing formatting.
        // Derive embed kind from cached weenie data only.
        var kind = ResolveEmbedKindFromWeenie(listing.ItemWeenieClassId);

        var eb = BuildListingEmbed(line, kind);
        if (!string.IsNullOrWhiteSpace(footer))
        {
            eb.WithFooter(footer);
        }

        return eb;
    }

    private static ListingEmbedKind ResolveEmbedKindFromWeenie(uint weenieClassId)
    {
        try
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(weenieClassId);
            if (weenie == null)
            {
                return ListingEmbedKind.Unknown;
            }

            if (weenie.WeenieType == WeenieType.SigilTrinket)
            {
                return ListingEmbedKind.SigilTrinket;
            }

            if (weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var it))
            {
                return (ItemType)it switch
                {
                    ItemType.MeleeWeapon => ListingEmbedKind.Weapon,
                    ItemType.MissileWeapon => ListingEmbedKind.MissileWeapon,
                    ItemType.Caster => ListingEmbedKind.Caster,
                    ItemType.Armor => ListingEmbedKind.Armor,
                    ItemType.Jewelry => ListingEmbedKind.Jewelry,
                    ItemType.Clothing => ListingEmbedKind.Clothing,
                    _ => ListingEmbedKind.Unknown,
                };
            }
        }
        catch
        {
            // ignore
        }

        return ListingEmbedKind.Unknown;
    }

    // Embed helpers

    private static ListingEmbedKind ResolveEmbedKind(ACE.Server.WorldObjects.WorldObject obj)
    {
        if (obj.WeenieType == WeenieType.SigilTrinket)
        {
            return ListingEmbedKind.SigilTrinket;
        }

        return obj.ItemType switch
        {
            ItemType.MeleeWeapon => ListingEmbedKind.Weapon,
            ItemType.MissileWeapon => ListingEmbedKind.MissileWeapon,
            ItemType.Caster => ListingEmbedKind.Caster,
            ItemType.Armor => ListingEmbedKind.Armor,
            ItemType.Jewelry => ListingEmbedKind.Jewelry,
            ItemType.Clothing => ListingEmbedKind.Clothing,
            _ => ListingEmbedKind.Unknown,
        };
    }

    private static Color? ResolveEmbedColor(ListingEmbedKind kind)
    {
        return kind switch
        {
            ListingEmbedKind.Weapon => Color.Red,
            ListingEmbedKind.MissileWeapon => new Color(255, 165, 0),
            ListingEmbedKind.Caster => new Color(128, 0, 128),
            ListingEmbedKind.Armor => Color.Gold,
            ListingEmbedKind.Jewelry => Color.Blue,
            ListingEmbedKind.SigilTrinket => new Color(0, 128, 128),
            ListingEmbedKind.Clothing => Color.Green,
            _ => null,
        };
    }

    private static EmbedBuilder BuildListingEmbed(string line, ListingEmbedKind kind)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new EmbedBuilder();
        }

        var parts = line
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var title = parts.Count > 0 ? parts[0].TrimStart('#', ' ').Trim() : string.Empty;
        if (parts.Count > 0)
        {
            parts.RemoveAt(0);
        }

        string? footerText = null;
        if (parts.Count > 0 && parts[^1].StartsWith("Footer: ", StringComparison.Ordinal))
        {
            footerText = parts[^1].Substring("Footer: ".Length).Trim();
            parts.RemoveAt(parts.Count - 1);
        }

        var eb = new EmbedBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            eb.WithTitle(title);
        }

        var detailText = string.Join("\n", parts.Select(p => p.Trim()).Where(p => p.Length > 0)).Trim();
        if (!string.IsNullOrWhiteSpace(detailText))
        {
            eb.WithDescription(detailText);
        }

        if (!string.IsNullOrWhiteSpace(footerText))
        {
            eb.WithFooter(footerText);
        }

        var color = ResolveEmbedColor(kind);
        if (color.HasValue)
        {
            eb.WithColor(color.Value);
        }

        return eb;
    }

    // Formatting pipeline

    private static string FormatListingLine(ACE.Database.Models.Shard.PlayerMarketListing listing, DateTime now, ListingFormatCache cache)
    {
        var name = ResolveItemName(listing, cache);

        var expiresAtUnix = new DateTimeOffset(listing.ExpiresAtUtc).ToUnixTimeSeconds();
        var expiresAtText = $"<t:{expiresAtUnix}:F> | <t:{expiresAtUnix}:R>";

        var stackSize = ResolveStackSize(listing, cache);
        var reqLabel = ResolveReqLabelFromWeenie(listing.ItemWeenieClassId);
        var wieldReq = listing.WieldReq.HasValue ? $"{reqLabel} {listing.WieldReq.Value}" : $"{reqLabel} -";
        var stackText = stackSize > 1 ? $"x{stackSize}" : "";

        var priceText = stackSize > 1
            ? $"{listing.ListedPrice:N0} py ({(int)Math.Ceiling(listing.ListedPrice / (double)stackSize):N0} py ea)"
            : $"{listing.ListedPrice:N0} py";

        // Salvage (tinkering material): prefer snapshot Structure quantity when available.
        var salvageQty = TryResolveSalvageQtyFromSnapshot(stackSize, listing, cache);
        if (salvageQty.HasValue && salvageQty.Value > 0)
        {
            var perUnit = (int)Math.Ceiling(listing.ListedPrice / (double)salvageQty.Value);
            priceText += $" ({perUnit:N0} py/unit)";
        }

        try
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
            if (weenie?.PropertiesInt != null
                && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var it)
                && it == (int)ItemType.Armor
                && weenie.PropertiesInt.TryGetValue(PropertyInt.ArmorSlots, out var slots)
                && slots > 0)
            {
                var perSlot = (int)Math.Ceiling(listing.ListedPrice / (double)slots);
                priceText += $" ({perSlot:N0} py/slot)";
            }
        }
        catch
        {
            // ignore
        }

        var details = TryBuildItemDetails(listing, name, stackText, listing.ListedPrice, wieldReq, listing.SellerName, expiresAtText, cache);
        if (!string.IsNullOrWhiteSpace(details))
        {
            return details.TrimEnd();
        }

        // Fallback (multi-line) for snapshot field splitting.
        // Option B: omit req/details when we can't recreate the WorldObject.
        var header = $"### {name} {stackText} | {priceText}".TrimEnd();

        var usesLine = TryResolveUsesLineFromSnapshot(listing, cache);
        return (header + "\n" + $"Seller: {listing.SellerName} | expires {expiresAtText}" + (usesLine != null ? "\n" + usesLine : string.Empty)).TrimEnd();
    }

    private static string? TryBuildItemDetails(
        ACE.Database.Models.Shard.PlayerMarketListing listing,
        string name,
        string stackText,
        int listedPrice,
        string wieldReq,
        string sellerName,
        string expiresAtText,
        ListingFormatCache cache)
    {
        var snapshot = GetOrCreateSnapshot(listing, cache);
        if (snapshot == null)
        {
            return null;
        }

        try
        {
            var stackSize = 1;
            if (!string.IsNullOrWhiteSpace(stackText)
                && stackText.StartsWith('x')
                && int.TryParse(stackText.AsSpan(1), out var parsed)
                && parsed > 1)
            {
                stackSize = parsed;
            }

            var priceText = stackSize > 1
                ? $"{listedPrice:N0} py ({(int)Math.Ceiling(listedPrice / (double)stackSize):N0} py ea)"
                : $"{listedPrice:N0} py";

            // Salvage unit pricing via snapshot Structure
            if (stackSize <= 1 && TryGetPropertyInt(snapshot, PropertyInt.Structure, out var qty) && qty > 0)
            {
                var perUnit = (int)Math.Ceiling(listedPrice / (double)qty);
                priceText += $" ({perUnit:N0} py/unit)";
            }

            // Armor per-slot pricing via weenie
            try
            {
                var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
                if (weenie?.PropertiesInt != null
                    && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var it)
                    && it == (int)ItemType.Armor
                    && weenie.PropertiesInt.TryGetValue(PropertyInt.ArmorSlots, out var slots)
                    && slots > 0)
                {
                    var perSlot = (int)Math.Ceiling(listedPrice / (double)slots);
                    priceText += $" ({perSlot:N0} py/slot)";
                }
            }
            catch
            {
                // ignore
            }

            var header = $"### {name} {stackText} | {priceText}";
            var headerTitle = header.Split(" | ", 2, StringSplitOptions.None)[0].TrimEnd();

            var commonParts = BuildCommonPartsFromSnapshot(snapshot, wieldReq);
            var details = TryBuildDetailsFromSnapshot(snapshot, listing, headerTitle, sellerName, expiresAtText, commonParts);
            if (details == null)
            {
                return null;
            }

            static string Render(ListingDetails details)
            {
                var bodyLines = details.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                var body = bodyLines.Count > 0 ? ("\n" + string.Join("\n", bodyLines)).TrimEnd() : string.Empty;
                return ($"Seller: {details.SellerName} | expires {details.ExpiresAtText}" + body).TrimEnd();
            }

            return (header + "\n" + Render(details)).TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private static MarketListingSnapshot? GetOrCreateSnapshot(ACE.Database.Models.Shard.PlayerMarketListing listing, ListingFormatCache cache)
    {
        if (cache.SnapshotInitialized)
        {
            return cache.Snapshot;
        }

        cache.SnapshotInitialized = true;
        cache.Snapshot = null;

        if (string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
        {
            return null;
        }

        cache.Snapshot = MarketListingSnapshotSerializer.TryDeserializeSnapshot(listing.ItemSnapshotJson);
        return cache.Snapshot;
    }

    private static int? TryResolveSalvageQtyFromSnapshot(int stackSize, ACE.Database.Models.Shard.PlayerMarketListing listing, ListingFormatCache cache)
    {
        if (stackSize > 1)
        {
            return null;
        }

        var snap = GetOrCreateSnapshot(listing, cache);
        if (snap == null)
        {
            return null;
        }

        return TryGetPropertyInt(snap, PropertyInt.Structure, out var qty) && qty > 0 ? qty : null;
    }

    private static string? TryResolveUsesLineFromSnapshot(ACE.Database.Models.Shard.PlayerMarketListing listing, ListingFormatCache cache)
    {
        var snap = GetOrCreateSnapshot(listing, cache);
        if (snap == null)
        {
            return null;
        }

        if (TryGetPropertyInt(snap, PropertyInt.Structure, out var cur) && TryGetPropertyInt(snap, PropertyInt.MaxStructure, out var max) && max > 0)
        {
            return $"- Uses {cur}/{max}";
        }

        return null;
    }

    private static bool TryGetPropertyInt(MarketListingSnapshot snap, PropertyInt prop, out int value)
    {
        value = 0;
        if (snap.PropertiesInt != null && snap.PropertiesInt.TryGetValue(prop, out var v))
        {
            value = v;
            return true;
        }
        return false;
    }

    private static bool TryGetPropertyFloat(MarketListingSnapshot snap, PropertyFloat prop, out double value)
    {
        value = 0;
        if (snap.PropertiesFloat != null && snap.PropertiesFloat.TryGetValue(prop, out var v))
        {
            value = v;
            return true;
        }
        return false;
    }

    private static bool TryGetPropertyDataId(MarketListingSnapshot snap, PropertyDataId prop, out uint value)
    {
        value = 0;
        if (snap.PropertiesDID != null && snap.PropertiesDID.TryGetValue(prop, out var v))
        {
            value = v;
            return true;
        }
        return false;
    }

    private static bool TryGetPropertyString(MarketListingSnapshot snap, PropertyString prop, out string value)
    {
        value = string.Empty;
        if (snap.PropertiesString != null && snap.PropertiesString.TryGetValue(prop, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            value = v;
            return true;
        }
        return false;
    }

    private static List<string> BuildCommonPartsFromSnapshot(MarketListingSnapshot snap, string wieldReq)
    {
        var commonParts = new List<string>(8) { wieldReq };

        // Workmanship is stored as an int (ItemWorkmanship) on items.
        if (TryGetPropertyInt(snap, PropertyInt.ItemWorkmanship, out var iw) && iw > 0)
        {
            var numItemsInMaterial = 1;
            if (TryGetPropertyInt(snap, PropertyInt.NumItemsInMaterial, out var nim) && nim > 0)
            {
                numItemsInMaterial = nim;
            }
            commonParts.Add($"Wk {(iw / (double)numItemsInMaterial):0.##}");
        }

        if (TryGetPropertyInt(snap, PropertyInt.JewelSockets, out var sockets) && sockets > 0)
        {
            commonParts.Add($"Sockets {sockets}");
        }

        if (TryGetPropertyInt(snap, PropertyInt.NumTimesTinkered, out var tinks) && tinks > 0)
        {
            commonParts.Add($"Tinks {tinks}");
        }

        if (TryGetPropertyInt(snap, PropertyInt.Structure, out var cur) && TryGetPropertyInt(snap, PropertyInt.MaxStructure, out var max) && max > 0)
        {
            commonParts.Add($"Uses {cur}/{max}");
        }

        return commonParts;
    }

    private static ListingDetails? TryBuildDetailsFromSnapshot(
        MarketListingSnapshot snap,
        ACE.Database.Models.Shard.PlayerMarketListing listing,
        string headerTitle,
        string sellerName,
        string expiresAtText,
        List<string> commonParts)
    {
        // Resolve ItemType from snapshot first, otherwise fall back to weenie.
        var itemType = (ItemType)0;
        if (TryGetPropertyInt(snap, PropertyInt.ItemType, out var it))
        {
            itemType = (ItemType)unchecked((uint)it);
        }
        else
        {
            try
            {
                var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
                if (weenie?.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var wit))
                {
                    itemType = (ItemType)unchecked((uint)wit);
                }
            }
            catch { }
        }

        // We re-use the old formatting logic where possible, but now backed by snapshot properties.
        if (itemType is ItemType.MeleeWeapon or ItemType.MissileWeapon or ItemType.Caster or ItemType.Weapon)
        {
            var lines = FormatWeaponDetailsMultilineFromSnapshot(snap);
            var allLines = new List<string>(lines.Count + 1);
            var commonLine = new List<string>(1);
            AddIndentedLine(commonLine, commonParts);
            if (commonLine.Count > 0)
            {
                allLines.Add(commonLine[0]);
            }
            allLines.AddRange(lines);
            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (snap.WeenieType == WeenieType.SigilTrinket)
        {
            var sigil = FormatSigilTrinketDetailsFromSnapshot(snap);
            var allLines = string.IsNullOrWhiteSpace(sigil)
                ? []
                : sigil.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (itemType == ItemType.Armor)
        {
            var lines = FormatArmorDetailsMultilineFromSnapshot(snap);
            var allLines = new List<string>(lines.Count + 1);
            var commonLine = new List<string>(1);
            var armorCommon = new List<string>(commonParts);
            if (TryGetPropertyInt(snap, PropertyInt.ArmorWeightClass, out var wc))
            {
                var wcText = wc switch
                {
                    1 => "Cloth",
                    2 => "Light",
                    4 => "Heavy",
                    _ => $"Wt {wc}",
                };
                armorCommon.Insert(Math.Min(1, armorCommon.Count), wcText);
            }
            AddIndentedLine(commonLine, armorCommon);
            if (commonLine.Count > 0)
            {
                allLines.Add(commonLine[0]);
            }
            allLines.AddRange(lines);
            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (itemType == ItemType.Jewelry)
        {
            var lines = FormatJewelryDetailsMultilineFromSnapshot(snap);
            var allLines = new List<string>(lines.Count + 1);
            var commonLine = new List<string>(1);
            AddIndentedLine(commonLine, commonParts);
            if (commonLine.Count > 0)
            {
                allLines.Add(commonLine[0]);
            }
            allLines.AddRange(lines);
            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (itemType == ItemType.Clothing)
        {
            var lines = FormatClothingDetailsMultilineFromSnapshot(snap, commonParts);
            return new ListingDetails(headerTitle, sellerName, expiresAtText, lines);
        }

        if (itemType == ItemType.Gem)
        {
            var lines = new List<string>(2);
            var commonLine = new List<string>(1);
            var gemCommon = new List<string>(commonParts);
            if (gemCommon.Count > 0)
            {
                gemCommon.RemoveAt(0);
            }
            AddIndentedLine(commonLine, gemCommon);
            if (commonLine.Count > 0)
            {
                lines.Add(commonLine[0]);
            }
            return lines.Count > 0
                ? new ListingDetails(headerTitle, sellerName, expiresAtText, lines)
                : null;
        }

        if (itemType == ItemType.TinkeringMaterial)
        {
            var lines = new List<string>(2);
            var commonLine = new List<string>(1);
            var salvageCommon = new List<string>(commonParts);
            if (salvageCommon.Count > 0)
            {
                salvageCommon.RemoveAt(0);
            }
            AddIndentedLine(commonLine, salvageCommon);
            if (commonLine.Count > 0)
            {
                lines.Add(commonLine[0]);
            }
            return lines.Count > 0
                ? new ListingDetails(headerTitle, sellerName, expiresAtText, lines)
                : null;
        }

        if (itemType == ItemType.Useless)
        {
            var lines = new List<string>(2);
            var commonLine = new List<string>(1);
            var trophyCommon = new List<string>(commonParts);
            if (trophyCommon.Count > 0)
            {
                trophyCommon.RemoveAt(0);
            }
            AddIndentedLine(commonLine, trophyCommon);
            if (commonLine.Count > 0)
            {
                lines.Add(commonLine[0]);
            }

            var line2 = new List<string>(6);
            if (TryGetPropertyInt(snap, PropertyInt.TrophyQuality, out var tq))
            {
                line2.Add($"Quality {tq}");
            }
            AddIndentedLine(lines, line2);

            return lines.Count > 0
                ? new ListingDetails(headerTitle, sellerName, expiresAtText, lines)
                : null;
        }

        // Fallback: no rich snapshot details.
        return null;
    }

    private static void AppendPropertyFloatIfPresent(
        MarketListingSnapshot snap,
        List<string> parts,
        PropertyFloat prop,
        string label,
        bool multiplyBy100 = false,
        bool skipIfZero = false,
        bool subtractBy1 = false)
    {
        if (!TryGetPropertyFloat(snap, prop, out var val))
        {
            return;
        }

        if (skipIfZero && Math.Abs(val) < 0.001)
        {
            return;
        }

        if (subtractBy1)
        {
            val -= 1;
        }

        var outVal = multiplyBy100 ? val * 100.0 : val;
        outVal = Math.Round(outVal, 1, MidpointRounding.AwayFromZero);
        parts.Add($"{label} {outVal:+0.0;-0.0;0.0}%");
    }

    private static void AppendPropertyIntIfPresent(
        MarketListingSnapshot snap,
        List<string> parts,
        PropertyInt prop,
        string label,
        bool skipIfZero = false)
    {
        if (!TryGetPropertyInt(snap, prop, out var val))
        {
            return;
        }

        if (skipIfZero && val == 0)
        {
            return;
        }

        parts.Add($"{label} {val}");
    }

    private static void AppendWeaponMultiplierAsBonusPercentIfPresent(
        MarketListingSnapshot snap,
        List<string> parts,
        PropertyFloat prop,
        string label,
        bool skipIfZero = false,
        double baseStat = 0.0)
    {
        if (!TryGetPropertyFloat(snap, prop, out var val))
        {
            return;
        }

        if (skipIfZero && Math.Abs(val - baseStat) < 0.001)
        {
            return;
        }

        var bonusPct = (val - baseStat) * 100.0;
        bonusPct = Math.Round(bonusPct, 1, MidpointRounding.AwayFromZero);
        parts.Add($"{label} {bonusPct:+0.0;-0.0;0.0}%");
    }

    private static string? TryFormatSpellsFromSnapshot(MarketListingSnapshot snap)
    {
        try
        {
            if (snap.PropertiesSpellBook != null && snap.PropertiesSpellBook.Count > 0)
            {
                var spellIds = snap.PropertiesSpellBook.Keys.OrderBy(id => id).ToList();
                var names = new List<string>(spellIds.Count);
                foreach (var id in spellIds)
                {
                    try
                    {
                        names.Add(new Spell(id, loadDB: false).Name);
                    }
                    catch
                    {
                        names.Add($"Spell {id}");
                    }
                }

                return "Spells: " + string.Join(", ", names);
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static ImbuedEffectType GetImbuedEffectsFromSnapshot(MarketListingSnapshot snap)
    {
        // Imbued effects are stored as a bitfield in PropertyInt.ImbuedEffect.
        if (TryGetPropertyInt(snap, PropertyInt.ImbuedEffect, out var imb) && imb != 0)
        {
            return (ImbuedEffectType)imb;
        }
        return ImbuedEffectType.Undef;
    }

    private static List<string> FormatWeaponDetailsMultilineFromSnapshot(MarketListingSnapshot snap)
    {
        var lines = new List<string>(5);
        var line1 = new List<string>(6);

        if (TryGetPropertyInt(snap, PropertyInt.DamageType, out var element))
        {
            line1.Add(((DamageType)element).ToString());
        }

        if (TryGetPropertyInt(snap, PropertyInt.ItemType, out var it) && (ItemType)unchecked((uint)it) == ItemType.Caster)
        {
            AppendPropertyFloatIfPresent(snap, line1, PropertyFloat.ElementalDamageMod, "D.Mod%", multiplyBy100: true, skipIfZero: true, subtractBy1: true);
            AppendPropertyFloatIfPresent(snap, line1, PropertyFloat.WeaponRestorationSpellsMod, "Resto%", multiplyBy100: true, skipIfZero: true, subtractBy1: true);
        }

        if (TryGetPropertyInt(snap, PropertyInt.Damage, out var dmg) && dmg != 0)
        {
            if (TryGetPropertyFloat(snap, PropertyFloat.DamageVariance, out var variance))
            {
                var max = dmg;
                var min = (int)Math.Round(max * (1.0 - variance), MidpointRounding.AwayFromZero);
                min = Math.Clamp(min, 0, max);
                line1.Add($"Dmg {min}-{max}");
            }
            else
            {
                line1.Add($"Dmg {dmg}");
            }
        }

        AppendPropertyFloatIfPresent(snap, line1, PropertyFloat.DamageMod, "D.Mod%", multiplyBy100: true, skipIfZero: true, subtractBy1: true);

        if (TryGetPropertyInt(snap, PropertyInt.WeaponTime, out var weaponTime) && weaponTime > 0)
        {
            line1.Add($"Speed {weaponTime}");
        }

        if (line1.Count == 0)
        {
            line1.Add("Dmg -");
        }

        AddIndentedLine(lines, line1);

        var line3 = new List<string>(10);
        AppendWeaponMultiplierAsBonusPercentIfPresent(snap, line3, PropertyFloat.WeaponOffense, "Atk", skipIfZero: true, baseStat: 1.0);
        AppendWeaponMultiplierAsBonusPercentIfPresent(snap, line3, PropertyFloat.WeaponPhysicalDefense, "P.Def", skipIfZero: true, baseStat: 1.0);
        AppendWeaponMultiplierAsBonusPercentIfPresent(snap, line3, PropertyFloat.WeaponMagicalDefense, "M.Def", skipIfZero: true, baseStat: 1.0);
        AppendWeaponMultiplierAsBonusPercentIfPresent(snap, line3, PropertyFloat.WeaponWarMagicMod, "War", skipIfZero: true);
        AppendWeaponMultiplierAsBonusPercentIfPresent(snap, line3, PropertyFloat.WeaponLifeMagicMod, "Life", skipIfZero: true);
        AddIndentedLine(lines, line3);

        var line4 = new List<string>(10);
        var imbue = GetImbuedEffectsFromSnapshot(snap);
        if (imbue != ImbuedEffectType.Undef)
        {
            line4.Add($"Imbue {imbue}");
        }
        AppendPropertyFloatIfPresent(snap, line4, PropertyFloat.CriticalMultiplier, "Crushing Blow", multiplyBy100: true, subtractBy1: true);
        if (TryGetPropertyFloat(snap, PropertyFloat.CriticalFrequency, out var cf) && cf > 0.1)
        {
            var bonusPct = (cf - 0.1) * 100.0;
            line4.Add($"Biting Strike {bonusPct:+0.##;-0.##;0}%");
        }
        if (TryGetPropertyFloat(snap, PropertyFloat.IgnoreArmor, out var ia))
        {
            var pct = (1 - ia) * 100.0;
            line4.Add($"Armor Cleave {pct:0.##}%");
        }
        if (TryGetPropertyFloat(snap, PropertyFloat.ResistanceModifier, out var rm))
        {
            var pct = rm * 100.0;
            line4.Add($"Resist Cleave {pct:0.##}%");
        }
        AddIndentedLine(lines, line4);

        var line5 = new List<string>(12);
        AppendPropertyIntIfPresent(snap, line5, PropertyInt.ItemDifficulty, "Diff");
        if (TryGetPropertyInt(snap, PropertyInt.ItemSpellcraft, out var sc) && sc > 0)
        {
            line5.Add($"Spellcraft {sc}");
        }
        if (TryGetPropertyDataId(snap, PropertyDataId.ProcSpell, out var procSpell))
        {
            line5.Add($"Proc {new Spell((SpellId)procSpell).Name}");
        }
        AppendPropertyFloatIfPresent(snap, line5, PropertyFloat.ProcSpellRate, "Proc%", multiplyBy100: true);

        var spellText = TryFormatSpellsFromSnapshot(snap);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line5.Add(spellText);
        }
        AddIndentedLine(lines, line5);

        return lines;
    }

    private static List<string> FormatArmorDetailsMultilineFromSnapshot(MarketListingSnapshot snap)
    {
        var lines = new List<string>(3);
        var line1 = new List<string>(6);
        if (TryGetPropertyInt(snap, PropertyInt.ArmorLevel, out var al) && al > 0)
        {
            line1.Add($"AL {al}");
        }
        if (TryGetPropertyInt(snap, PropertyInt.WardLevel, out var wl) && wl > 0)
        {
            line1.Add($"WL {wl}");
        }
        AddIndentedLine(lines, line1);

        var line2 = new List<string>(12);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorPhysicalDefMod, "P.Def", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorMagicDefMod, "M.Def", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorAttackMod, "Atk", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorWarMagicMod, "War", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorLifeMagicMod, "Life", multiplyBy100: true, skipIfZero: true);
        AddIndentedLine(lines, line2);

        var line3 = new List<string>(16);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorDualWieldMod, "Dual", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorRunMod, "Run", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorHealthRegenMod, "H.Regen", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorStaminaRegenMod, "S.Regen", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorManaRegenMod, "M.Regen", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorShieldMod, "Shield", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorPerceptionMod, "Perc", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorThieveryMod, "Thiev", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line3, PropertyFloat.ArmorDeceptionMod, "Decep", multiplyBy100: true, skipIfZero: true);
        AddIndentedLine(lines, line3);

        var line4 = new List<string>(12);
        AppendPropertyIntIfPresent(snap, line4, PropertyInt.ItemDifficulty, "Diff");
        var spellText = TryFormatSpellsFromSnapshot(snap);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line4.Add(spellText);
        }
        AddIndentedLine(lines, line4);

        return lines;
    }

    private static List<string> FormatJewelryDetailsMultilineFromSnapshot(MarketListingSnapshot snap)
    {
        var lines = new List<string>(5);

        var line1 = new List<string>(4);
        if (TryGetPropertyInt(snap, PropertyInt.WardLevel, out var wl) && wl > 0)
        {
            line1.Add($"Ward {wl}");
        }
        AddIndentedLine(lines, line1);

        var line2 = new List<string>(10);
        AppendPropertyIntIfPresent(snap, line2, PropertyInt.DamageRating, "DR", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line2, PropertyInt.DamageResistRating, "DRR", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line2, PropertyInt.CritRating, "CR", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line2, PropertyInt.CritDamageRating, "CDR", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line2, PropertyInt.CritResistRating, "CRR", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line2, PropertyInt.CritDamageResistRating, "CDRR", skipIfZero: true);
        AddIndentedLine(lines, line2);

        var line3 = new List<string>(8);
        AppendPropertyIntIfPresent(snap, line3, PropertyInt.HealingBoostRating, "HBR", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line3, PropertyInt.WeaknessRating, "Weak", skipIfZero: true);
        AppendPropertyIntIfPresent(snap, line3, PropertyInt.NetherResistRating, "NRR", skipIfZero: true);
        AddIndentedLine(lines, line3);

        var line4 = new List<string>(12);
        AppendPropertyIntIfPresent(snap, line4, PropertyInt.ItemDifficulty, "Diff");
        if (TryGetPropertyInt(snap, PropertyInt.ItemSpellcraft, out var sc) && sc > 0)
        {
            line4.Add($"Spellcraft {sc}");
        }
        if (TryGetPropertyDataId(snap, PropertyDataId.ProcSpell, out var procSpell))
        {
            line4.Add($"Proc {new Spell((SpellId)procSpell).Name}");
        }
        AppendPropertyFloatIfPresent(snap, line4, PropertyFloat.ProcSpellRate, "Proc%", multiplyBy100: true);
        var spellText = TryFormatSpellsFromSnapshot(snap);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line4.Add(spellText);
        }
        AddIndentedLine(lines, line4);

        return lines;
    }

    private static List<string> FormatClothingDetailsMultilineFromSnapshot(MarketListingSnapshot snap, List<string> commonParts)
    {
        var lines = new List<string>(3);
        var commonLine = new List<string>(1);
        AddIndentedLine(commonLine, commonParts);
        if (commonLine.Count > 0)
        {
            lines.Add(commonLine[0]);
        }

        var line2 = new List<string>(8);
        if (TryGetPropertyInt(snap, PropertyInt.WardLevel, out var wl) && wl > 0)
        {
            line2.Add($"Ward {wl}");
        }
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorHealthMod, "Health", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorStaminaMod, "Stam", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.ArmorManaMod, "Mana", multiplyBy100: true, skipIfZero: true);
        AddIndentedLine(lines, line2);

        var line3 = new List<string>(12);
        AppendPropertyIntIfPresent(snap, line3, PropertyInt.ItemDifficulty, "Diff");
        var spellText = TryFormatSpellsFromSnapshot(snap);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line3.Add(spellText);
        }
        AddIndentedLine(lines, line3);
        return lines;
    }

    private static string? FormatSigilTrinketDetailsFromSnapshot(MarketListingSnapshot snap)
    {
        var lines = new List<string>(2);
        var line1 = new List<string>(8);
        if (TryGetPropertyInt(snap, PropertyInt.WieldDifficulty, out var req) && req > 0)
        {
            line1.Add($"Lv.Req {req}");
        }
        AddIndentedLine(lines, line1);

        var line2 = new List<string>(8);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.SigilTrinketTriggerChance, "Proc%", multiplyBy100: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.CooldownDuration, "Cooldown");
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.SigilTrinketReductionAmount, "Reduction", skipIfZero: true);
        AppendPropertyFloatIfPresent(snap, line2, PropertyFloat.SigilTrinketIntensity, "Intensity", skipIfZero: true);
        AddIndentedLine(lines, line2);

        return lines.Count > 0 ? string.Join("\n", lines).TrimEnd() : null;
    }

    // WorldObject recreation/caching

    // WorldObject recreation intentionally disabled; keep formatting DB/weenie-driven only.

    private static ListingDetails? TryBuildDetails(
        ACE.Server.WorldObjects.WorldObject obj,
        ACE.Database.Models.Shard.PlayerMarketListing listing,
        string headerTitle,
        string sellerName,
        string expiresAtText,
        List<string> commonParts)
    {
        if (obj.ItemType == ItemType.Gem)
        {
            var lines = new List<string>(1);

            if (obj.Workmanship.HasValue)
            {
                lines.Add($"- Wk {obj.Workmanship.Value:0.##}");
            }

            return lines.Count > 0
                ? new ListingDetails(headerTitle, sellerName, expiresAtText, lines)
                : null;
        }

        if (obj.ItemType == ItemType.TinkeringMaterial)
        {
            var lines = new List<string>(2);

            if (obj.Workmanship.HasValue && obj.MaterialType.HasValue && obj.Structure.HasValue && obj.Structure.Value > 0)
            {
                lines.Add($"- Wk {obj.Workmanship.Value:0.##} | Type {obj.MaterialType.Value} | Qty {obj.Structure.Value}");
            }

            return lines.Count > 0
                ? new ListingDetails(headerTitle, sellerName, expiresAtText, lines)
                : null;
        }

        if (obj.ItemType == ItemType.Useless)
        {
            var lines = new List<string>(1);
            var tq = obj.GetProperty(PropertyInt.TrophyQuality);
            if (tq.HasValue)
            {
                lines.Add($"- Quality {tq.Value}");
            }

            return lines.Count > 0
                ? new ListingDetails(headerTitle, sellerName, expiresAtText, lines)
                : null;
        }

        if (obj.ItemType == ItemType.ManaStone)
        {
            // Mana stones: omit req line; show mana-related stats compactly.
            var lines = new List<string>(2);

            var cap = obj.GetProperty(PropertyInt.ItemMaxMana);
            var stored = obj.GetProperty(PropertyInt.ItemCurMana);
            lines.Add($"- M.Cap: {(cap.HasValue ? cap.Value.ToString("N0") : "-")} | M.Stored: {(stored.HasValue ? stored.Value.ToString("N0") : "-")}");

            var eff = obj.GetProperty(PropertyFloat.ItemEfficiency);
            var destroyChance = obj.GetProperty(PropertyFloat.ManaStoneDestroyChance);
            lines.Add($"- Eff: {(eff.HasValue ? (eff.Value * 100).ToString("0.#") + "%" : "-")} | Dest%: {(destroyChance.HasValue ? (destroyChance.Value * 100).ToString("0.#") + "%" : "-")}");

            return new ListingDetails(headerTitle, sellerName, expiresAtText, lines);
        }

        if (obj.ItemType is ItemType.Weapon or ItemType.MeleeWeapon or ItemType.MissileWeapon or ItemType.Caster)
        {
            var lines = FormatWeaponDetailsMultiline(obj);
            var allLines = new List<string>(lines.Count + 1);
            var commonLine = new List<string>(1);
            var weaponCommon = new List<string>(commonParts);
            if (weaponCommon.Count > 0)
            {
                var reqVal = listing.WieldReq.HasValue ? listing.WieldReq.Value.ToString() : "-";
                var label = ResolveWieldLabelFromSkillType(obj.WieldSkillType, ResolveReqLabelFromWeenie(listing.ItemWeenieClassId));
                weaponCommon[0] = $"{label} {reqVal}";
            }

            AddIndentedLine(commonLine, weaponCommon);

            if (commonLine.Count > 0)
            {
                allLines.Add(commonLine[0]);
            }
            allLines.AddRange(lines);

            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (obj.WeenieType == WeenieType.SigilTrinket)
        {
            var sigilDetails = FormatSigilTrinketDetails(obj);
            var allLines = string.IsNullOrWhiteSpace(sigilDetails)
                ? []
                : sigilDetails.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (obj.ItemType is ItemType.Armor)
        {
            var lines = FormatArmorDetailsMultiline(obj);
            var allLines = new List<string>(lines.Count + 1);
            var commonLine = new List<string>(1);
            var armorCommon = new List<string>(commonParts);
            if (armorCommon.Count > 0)
            {
                var reqVal = listing.WieldReq.HasValue ? listing.WieldReq.Value.ToString() : "-";
                var label = ResolveWieldLabelFromSkillType(obj.WieldSkillType, ResolveReqLabelFromWeenie(listing.ItemWeenieClassId));
                armorCommon[0] = $"{label} {reqVal}";
            }

            var wc = obj.GetProperty(PropertyInt.ArmorWeightClass);
            if (wc.HasValue)
            {
                var wcText = wc.Value switch
                {
                    1 => "Cloth",
                    2 => "Light",
                    4 => "Heavy",
                    _ => $"Wt {wc.Value}",
                };

                armorCommon.Insert(Math.Min(1, armorCommon.Count), wcText);
            }

            AddIndentedLine(commonLine, armorCommon);

            if (commonLine.Count > 0)
            {
                allLines.Add(commonLine[0]);
            }
            allLines.AddRange(lines);

            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (obj.ItemType is ItemType.Jewelry)
        {
            var lines = FormatJewelryDetailsMultiline(obj);
            var allLines = new List<string>(lines.Count + 1);
            var commonLine = new List<string>(1);
            AddIndentedLine(commonLine, commonParts);

            if (commonLine.Count > 0)
            {
                allLines.Add(commonLine[0]);
            }
            allLines.AddRange(lines);

            return new ListingDetails(headerTitle, sellerName, expiresAtText, allLines);
        }

        if (obj.ItemType is ItemType.Clothing)
        {
            var lines = FormatClothingDetailsMultiline(obj, commonParts);
            return new ListingDetails(headerTitle, sellerName, expiresAtText, lines);
        }

        return null;
    }

    // WorldObject creation

    // WorldObject recreation intentionally disabled; keep formatting DB/weenie-driven only.

    // Detail formatting helpers

    private static List<string> FormatWeaponDetailsMultiline(ACE.Server.WorldObjects.WorldObject obj)
    {
        var lines = new List<string>(5);
        var line1 = new List<string>(6);
        var element = obj.GetProperty(PropertyInt.DamageType);
        if (element.HasValue)
        {
            line1.Add(((DamageType)element.Value).ToString());
        }

        if (obj.ItemType is ItemType.Caster)
        {
            AppendPropertyFloatIfPresent(obj, line1, PropertyFloat.ElementalDamageMod, "D.Mod%", multiplyBy100: true, skipIfZero: true, subtractBy1: true);
            AppendPropertyFloatIfPresent(obj, line1, PropertyFloat.WeaponRestorationSpellsMod, "Resto%", multiplyBy100: true, skipIfZero: true, subtractBy1: true);
        }

        var dmg = obj.GetProperty(PropertyInt.Damage);
        var variance = obj.GetProperty(PropertyFloat.DamageVariance);
        if (dmg.HasValue && dmg.Value != 0)
        {
            if (variance.HasValue)
            {
                var max = dmg.Value;
                var min = (int)Math.Round(max * (1.0 - variance.Value), MidpointRounding.AwayFromZero);
                min = Math.Clamp(min, 0, max);
                line1.Add($"Dmg {min}-{max}");
            }
            else
            {
                line1.Add($"Dmg {dmg.Value}");
            }
        }

        AppendPropertyFloatIfPresent(obj, line1, PropertyFloat.DamageMod, "D.Mod%", multiplyBy100: true, skipIfZero: true, subtractBy1: true);

        if (obj.WeaponTime.HasValue)
        {
            line1.Add($"Speed {obj.WeaponTime.Value}");
        }

        if (line1.Count == 0)
        {
            line1.Add("Dmg -");
        }

        AddIndentedLine(lines, line1);

        var line3 = new List<string>(10);
        AppendWeaponMultiplierAsBonusPercentIfPresent(obj, line3, PropertyFloat.WeaponOffense, "Atk", skipIfZero: true, baseStat: 1.0f);
        AppendWeaponMultiplierAsBonusPercentIfPresent(obj, line3, PropertyFloat.WeaponPhysicalDefense, "P.Def", skipIfZero: true, baseStat: 1.0f);
        AppendWeaponMultiplierAsBonusPercentIfPresent(obj, line3, PropertyFloat.WeaponMagicalDefense, "M.Def", skipIfZero: true, baseStat: 1.0f);
        AppendWeaponMultiplierAsBonusPercentIfPresent(obj, line3, PropertyFloat.WeaponWarMagicMod, "War", skipIfZero: true);
        AppendWeaponMultiplierAsBonusPercentIfPresent(obj, line3, PropertyFloat.WeaponLifeMagicMod, "Life", skipIfZero: true);
        AddIndentedLine(lines, line3);

        var line4 = new List<string>(10);
        var imbue = obj.GetImbuedEffects();
        if (imbue != ImbuedEffectType.Undef)
        {
            line4.Add($"Imbue {imbue}");
        }
        AppendWeaponCritMultiplierIfPresent(obj, line4);
        AppendWeaponCritFrequencyIfPresent(obj, line4);
        AppendWeaponIgnoreArmorIfPresent(obj, line4);
        AppendWeaponResistanceModIfPresent(obj, line4);
        AddIndentedLine(lines, line4);

        var line5 = new List<string>(12);
        AppendPropertyIntIfPresent(obj, line5, PropertyInt.ItemDifficulty, "Diff");
        if (obj.ItemSpellcraft.HasValue)
        {
            line5.Add($"Spellcraft {obj.ItemSpellcraft.Value}");
        }
        var procSpell = obj.GetProperty(PropertyDataId.ProcSpell);
        if (procSpell.HasValue)
        {
            line5.Add($"Proc {new Spell((SpellId)procSpell.Value).Name}");
        }
        AppendPropertyFloatIfPresent(obj, line5, PropertyFloat.ProcSpellRate, "Proc%", multiplyBy100: true);

        var spellText = TryFormatSpells(obj);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line5.Add(spellText);
        }

        AddIndentedLine(lines, line5);

        return lines;
    }

    private static List<string> FormatClothingDetailsMultiline(ACE.Server.WorldObjects.WorldObject obj, List<string> commonParts)
    {
        var lines = new List<string>(2);

        var commonLine = new List<string>(1);
        AddIndentedLine(commonLine, commonParts);
        if (commonLine.Count > 0)
        {
            lines.Add(commonLine[0]);
        }

        var line2 = new List<string>(8);
        if (obj.WardLevel.HasValue)
        {
            line2.Add($"Ward {obj.WardLevel.Value}");
        }
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorHealthMod, "Health", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorStaminaMod, "Stam", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorManaMod, "Mana", multiplyBy100: true, skipIfZero: true);
        AddIndentedLine(lines, line2);

        return lines;
    }

    private static List<string> FormatArmorDetailsMultiline(ACE.Server.WorldObjects.WorldObject obj)
    {
        var lines = new List<string>(6);

        var line1 = new List<string>(6);
        if (obj.ArmorLevel.HasValue)
        {
            line1.Add($"AL {obj.ArmorLevel.Value}");
        }
        if (obj.WardLevel.HasValue)
        {
            line1.Add($"WL {obj.WardLevel.Value}");
        }
        AddIndentedLine(lines, line1);

        var line2 = new List<string>(12);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorPhysicalDefMod, "P.Def", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorMagicDefMod, "M.Def", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorAttackMod, "Atk", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorWarMagicMod, "War", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.ArmorLifeMagicMod, "Life", multiplyBy100: true, skipIfZero: true);
        AddIndentedLine(lines, line2);

        var line3 = new List<string>(16);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorDualWieldMod, "Dual", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorRunMod, "Run", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorHealthRegenMod, "H.Regen", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorStaminaRegenMod, "S.Regen", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorManaRegenMod, "M.Regen", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorShieldMod, "Shield", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorPerceptionMod, "Perc", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorThieveryMod, "Thiev", multiplyBy100: true, skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line3, PropertyFloat.ArmorDeceptionMod, "Decep", multiplyBy100: true, skipIfZero: true);
        AddIndentedLine(lines, line3);

        var line4 = new List<string>(12);
        AppendPropertyIntIfPresent(obj, line4, PropertyInt.ItemDifficulty, "Diff");

        var spellText = TryFormatSpells(obj);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line4.Add(spellText);
        }

        AddIndentedLine(lines, line4);

        return lines;
    }

    private static List<string> FormatJewelryDetailsMultiline(ACE.Server.WorldObjects.WorldObject obj)
    {
        var lines = new List<string>(5);

        var line1 = new List<string>(4);
        if (obj.WardLevel.HasValue)
        {
            line1.Add($"Ward {obj.WardLevel.Value}");
        }
        AddIndentedLine(lines, line1);

        var line2 = new List<string>(10);
        AppendPropertyIntIfPresent(obj, line2, PropertyInt.DamageRating, "DR", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line2, PropertyInt.DamageResistRating, "DRR", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line2, PropertyInt.CritRating, "CR", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line2, PropertyInt.CritDamageRating, "CDR", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line2, PropertyInt.CritResistRating, "CRR", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line2, PropertyInt.CritDamageResistRating, "CDRR", skipIfZero: true);
        AddIndentedLine(lines, line2);

        var line3 = new List<string>(8);
        AppendPropertyIntIfPresent(obj, line3, PropertyInt.HealingBoostRating, "HBR", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line3, PropertyInt.WeaknessRating, "Weak", skipIfZero: true);
        AppendPropertyIntIfPresent(obj, line3, PropertyInt.NetherResistRating, "NRR", skipIfZero: true);
        AddIndentedLine(lines, line3);

        var line4 = new List<string>(12);
        AppendPropertyIntIfPresent(obj, line4, PropertyInt.ItemDifficulty, "Diff");
        if (obj.ItemSpellcraft.HasValue)
        {
            line4.Add($"Spellcraft {obj.ItemSpellcraft.Value}");
        }
        var procSpell = obj.GetProperty(PropertyDataId.ProcSpell);
        if (procSpell.HasValue)
        {
            line4.Add($"Proc {new Spell((SpellId)procSpell.Value).Name}");
        }
        AppendPropertyFloatIfPresent(obj, line4, PropertyFloat.ProcSpellRate, "Proc%", multiplyBy100: true);

        var spellText = TryFormatSpells(obj);
        if (!string.IsNullOrWhiteSpace(spellText))
        {
            line4.Add(spellText);
        }

        AddIndentedLine(lines, line4);

        return lines;
    }

    private static void AddIndentedLine(List<string> lines, List<string> parts)
    {
        if (parts.Count == 0)
        {
            return;
        }

        var text = string.Join(" | ", parts).Trim();
        if (text.Length == 0)
        {
            return;
        }

        lines.Add("- " + text);
    }

    private static void AppendWeaponMultiplierAsBonusPercentIfPresent(
        ACE.Server.WorldObjects.WorldObject obj,
        List<string> parts,
        PropertyFloat prop,
        string label,
        bool skipIfZero = false,
        float baseStat = 0.0f)
    {
        var val = obj.GetProperty(prop);
        if (!val.HasValue)
        {
            return;
        }

        if (skipIfZero && Math.Abs(val.Value) < baseStat + 0.001)
        {
            return;
        }

        var bonusPct = (val.Value - baseStat) * 100.0;
        bonusPct = Math.Round(bonusPct, 1, MidpointRounding.AwayFromZero);
        parts.Add($"{label} {bonusPct:+0.0;-0.0;0.0}%");
    }

    private static string? TryFormatSpells(ACE.Server.WorldObjects.WorldObject obj)
    {
        try
        {
            if (obj.Biota?.PropertiesSpellBook != null)
            {
                var spellIds = obj.Biota.PropertiesSpellBook.Keys.OrderBy(id => id).ToList();
                if (spellIds.Count > 0)
                {
                    var names = new List<string>(spellIds.Count);
                    foreach (var id in spellIds)
                    {
                        try
                        {
                            names.Add(new Spell(id, loadDB: false).Name);
                        }
                        catch
                        {
                            names.Add($"Spell {id}");
                        }
                    }

                    return "Spells: " + string.Join(", ", names);
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static void AppendWeaponCritMultiplierIfPresent(ACE.Server.WorldObjects.WorldObject obj, List<string> parts)
    {
        var val = obj.GetProperty(PropertyFloat.CriticalMultiplier);
        if (!val.HasValue)
        {
            return;
        }

        var bonusPct = (val.Value - 1.0) * 100.0;
        parts.Add($"Crushing Blow {bonusPct:+0.##;-0.##;0}%");
    }

    private static void AppendWeaponCritFrequencyIfPresent(ACE.Server.WorldObjects.WorldObject obj, List<string> parts)
    {
        var val = obj.GetProperty(PropertyFloat.CriticalFrequency);
        if (!val.HasValue || val <= 0.1f)
        {
            return;
        }

        var bonusPct = (val.Value - 0.1) * 100.0;
        parts.Add($"Biting Strike {bonusPct:+0.##;-0.##;0}%");
    }

    private static void AppendWeaponIgnoreArmorIfPresent(ACE.Server.WorldObjects.WorldObject obj, List<string> parts)
    {
        var val = obj.GetProperty(PropertyFloat.IgnoreArmor);
        if (!val.HasValue)
        {
            return;
        }

        var pct = (1 - val.Value) * 100.0;
        parts.Add($"Armor Cleave {pct:0.##}%");
    }

    private static void AppendWeaponResistanceModIfPresent(ACE.Server.WorldObjects.WorldObject obj, List<string> parts)
    {
        var val = obj.GetProperty(PropertyFloat.ResistanceModifier);
        if (!val.HasValue)
        {
            return;
        }

        var pct = val.Value * 100.0;
        parts.Add($"Resist Cleave {pct:0.##}%");
    }

    private static string? FormatSigilTrinketDetails(ACE.Server.WorldObjects.WorldObject obj)
    {
        var lines = new List<string>(2);

        var line1 = new List<string>(6);

        var req = obj.WieldDifficulty;
        var reqLabel = ResolveWieldLabelFromSkillType(obj.WieldSkillType, "Lv");
        line1.Add(req.HasValue ? $"{reqLabel} {req.Value}" : $"{reqLabel} -");

        var color = obj.GetProperty(PropertyInt.SigilTrinketColor);
        if (color.HasValue)
        {
            var colorText = color.Value switch
            {
                0 => "Blue",
                1 => "Yellow",
                2 => "Red",
                _ => $"Color {color.Value}",
            };
            line1.Add(colorText);
        }

        var maxLevel = obj.GetProperty(PropertyInt.ItemMaxLevel);
        var curLevel = obj.ItemLevel;
        if (curLevel.HasValue && maxLevel.HasValue)
        {
            line1.Add($"Level {curLevel.Value}/{maxLevel.Value}");
        }

        AddIndentedLine(lines, line1);

        var line2 = new List<string>(8);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.SigilTrinketTriggerChance, "Proc%", multiplyBy100: true);
        var cooldown = obj.GetProperty(PropertyFloat.CooldownDuration);
        if (cooldown.HasValue)
        {
            var seconds = Math.Round(cooldown.Value, 1, MidpointRounding.AwayFromZero);
            line2.Add($"Cooldown {seconds:0.#}s");
        }
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.SigilTrinketReductionAmount, "Reduction", skipIfZero: true);
        AppendPropertyFloatIfPresent(obj, line2, PropertyFloat.SigilTrinketIntensity, "Intensity", skipIfZero: true);
        AddIndentedLine(lines, line2);

        return lines.Count > 0 ? string.Join("\n", lines).TrimEnd() : null;
    }

    private static void AppendCommonItemParts(ACE.Server.WorldObjects.WorldObject obj, List<string> parts)
    {
        if (obj.Workmanship.HasValue)
        {
            parts.Add($"Wk {obj.Workmanship.Value:0.##}");
        }

        var sockets = obj.JewelSockets;
        if (sockets.HasValue && sockets.Value > 0)
        {
            parts.Add($"Sockets {sockets.Value}");
        }

        var tink = obj.NumTimesTinkered;
        if (tink > 0)
        {
            parts.Add($"Tinks {tink}");
        }
    }

    private static void AppendPropertyFloatIfPresent(
        ACE.Server.WorldObjects.WorldObject obj,
        List<string> parts,
        PropertyFloat prop,
        string label,
        bool multiplyBy100 = false,
        bool skipIfZero = false,
        bool subtractBy1 = false)
    {
        var val = obj.GetProperty(prop);
        if (!val.HasValue)
        {
            return;
        }

        if (skipIfZero && Math.Abs(val.Value) < 0.001)
        {
            return;
        }

        if (subtractBy1)
        {
            val -= 1;
        }

        var outVal = multiplyBy100 ? val.Value * 100.0 : val.Value;
        outVal = Math.Round(outVal, 1, MidpointRounding.AwayFromZero);
        parts.Add($"{label} {outVal:+0.0;-0.0;0.0}%");
    }

    private static void AppendPropertyIntIfPresent(
        ACE.Server.WorldObjects.WorldObject obj,
        List<string> parts,
        PropertyInt prop,
        string label,
        bool skipIfZero = false)
    {
        var val = obj.GetProperty(prop);
        if (!val.HasValue)
        {
            return;
        }

        if (skipIfZero && val.Value == 0)
        {
            return;
        }

        parts.Add($"{label} {val.Value}");
    }

    private static int ResolveStackSize(ACE.Database.Models.Shard.PlayerMarketListing listing, ListingFormatCache cache)
    {
        if (cache.StackSize.HasValue)
        {
            return cache.StackSize.Value;
        }

        if (listing.ItemBiotaId <= 0)
        {
            cache.StackSize = 1;
            return 1;
        }

        try
        {
            var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
            var stack = biota?.BiotaPropertiesInt?.FirstOrDefault(p => p.Type == (ushort)PropertyInt.StackSize)?.Value;
            var resolved = stack.HasValue && stack.Value > 0 ? stack.Value : 1;
            cache.StackSize = resolved;
            return resolved;
        }
        catch
        {
            cache.StackSize = 1;
            return 1;
        }
    }

    private static string ResolveItemName(ACE.Database.Models.Shard.PlayerMarketListing listing, ListingFormatCache cache)
    {
        if (!string.IsNullOrWhiteSpace(cache.Name))
        {
            return cache.Name;
        }

        // Prefer a reconstructed WorldObject when possible so item names include dynamic fields
        // like salvage material types.
        try
        {
            if (listing.ItemBiotaId > 0)
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                if (biota != null)
                {
                    var entityBiota = Database.Adapter.BiotaConverter.ConvertToEntityBiota(biota);
                    var obj = WorldObjectFactory.CreateWorldObject(entityBiota);
                    try
                    {
                        if (obj.ItemType == ItemType.TinkeringMaterial)
                        {
                            cache.Name = obj.NameWithMaterial;
                            return cache.Name;
                        }

                        if (!string.IsNullOrWhiteSpace(obj.Name))
                        {
                            cache.Name = obj.Name;
                            return cache.Name;
                        }
                    }
                    finally
                    {
                        obj.Destroy();
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        if (listing.ItemBiotaId > 0)
        {
            try
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                var name = biota?.BiotaPropertiesString?.FirstOrDefault(p => p.Type == (ushort)PropertyString.Name)?.Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    cache.Name = name;
                    return name;
                }
            }
            catch
            {
                // ignore
            }
        }

        if (cache.Weenie == null)
        {
            cache.Weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
        }

        var weenie = cache.Weenie;
        if (weenie?.PropertiesString != null
            && weenie.PropertiesString.TryGetValue(PropertyString.Name, out var weenieName)
            && !string.IsNullOrWhiteSpace(weenieName))
        {
            cache.Name = weenieName;
            return weenieName;
        }

        cache.Name = $"WCID {listing.ItemWeenieClassId}";
        return cache.Name;
    }

    private static string ResolveReqLabelFromWeenie(uint wcid)
    {
        try
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
            if (weenie?.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var it))
            {
                if (it == (int)ItemType.Jewelry || it == (int)ItemType.Clothing)
                {
                    return "Lv";
                }
            }
        }
        catch
        {
            // ignore
        }

        return "Att.Req";
    }

    private static string ResolveWieldLabelFromSkillType(int? wieldSkillType, string fallback)
    {
        return wieldSkillType switch
        {
            1 => "Str",
            4 => "Crd",
            6 => "Slf",
            _ => fallback,
        };
    }
}
