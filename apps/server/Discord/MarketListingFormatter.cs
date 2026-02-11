using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
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
        internal ACE.Server.WorldObjects.WorldObject? WorldObject;
        internal bool WorldObjectInitialized;
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

        var kind = ListingEmbedKind.Unknown;
        var obj = GetOrCreateWorldObject(listing, cache);
        if (obj != null)
        {
            try
            {
                kind = ResolveEmbedKind(obj);
            }
            finally
            {
                cache.WorldObject = null;
                cache.WorldObjectInitialized = false;
                obj.Destroy();
            }
        }

        var eb = BuildListingEmbed(line, kind);
        if (!string.IsNullOrWhiteSpace(footer))
        {
            eb.WithFooter(footer);
        }

        return eb;
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

        static int? TryResolveSalvageQtyFromInstance(uint weenieClassId, int stackSize, ACE.Server.WorldObjects.WorldObject? obj)
        {
            try
            {
                if (stackSize > 1)
                {
                    return null;
                }

                if (obj?.ItemType == ItemType.TinkeringMaterial && obj.Structure.HasValue && obj.Structure.Value > 0)
                {
                    return obj.Structure.Value;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        var priceText = stackSize > 1
            ? $"{listing.ListedPrice:N0} py ({(int)Math.Ceiling(listing.ListedPrice / (double)stackSize):N0} py ea)"
            : $"{listing.ListedPrice:N0} py";

        // Salvage (tinkering material): show unit price right after the price text in the title line.
        // Uses item `Structure` as the quantity.
        var salvageQty = TryResolveSalvageQtyFromInstance(listing.ItemWeenieClassId, stackSize, GetOrCreateWorldObject(listing, cache));
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

        string? usesLine = null;
        if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
        {
            try
            {
                var wo = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                if (wo != null)
                {
                    try
                    {
                        if (wo.Structure.HasValue && wo.MaxStructure.HasValue && wo.MaxStructure.Value > 0)
                        {
                            usesLine = $"- Uses {wo.Structure.Value}/{wo.MaxStructure.Value}";
                        }
                    }
                    finally
                    {
                        wo.Destroy();
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

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
        var obj = GetOrCreateWorldObject(listing, cache);
        if (obj == null)
        {
            return null;
        }

        try
        {
            string priceText;
            var stackSize = 1;
            if (!string.IsNullOrWhiteSpace(stackText)
                && stackText.StartsWith('x')
                && int.TryParse(stackText.AsSpan(1), out var parsed)
                && parsed > 1)
            {
                stackSize = parsed;
            }

            if (stackSize > 1)
            {
                var perUnit = (int)Math.Ceiling(listedPrice / (double)stackSize);
                priceText = $"{listedPrice:N0} py ({perUnit:N0} py ea)";
            }
            else
            {
                priceText = $"{listedPrice:N0} py";
            }

            if (stackSize <= 1 && obj.ItemType == ItemType.TinkeringMaterial)
            {
                var qty = obj.Structure ?? 0;
                if (qty > 0)
                {
                    var perUnit = (int)Math.Ceiling(listedPrice / (double)qty);
                    priceText += $" ({perUnit:N0} py/unit)";
                }
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

            static string Render(ListingDetails details)
            {
                var bodyLines = details.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                var body = bodyLines.Count > 0 ? ("\n" + string.Join("\n", bodyLines)).TrimEnd() : string.Empty;
                return ($"Seller: {details.SellerName} | expires {details.ExpiresAtText}" + body).TrimEnd();
            }

            static List<string> BuildCommonParts(ACE.Server.WorldObjects.WorldObject obj, string wieldReq)
            {
                var commonParts = new List<string>(8) { wieldReq };
                AppendCommonItemParts(obj, commonParts);

                if (obj.Structure.HasValue && obj.MaxStructure.HasValue && obj.MaxStructure.Value > 0)
                {
                    commonParts.Add($"Uses {obj.Structure.Value}/{obj.MaxStructure.Value}");
                }

                var jewelSockets = obj.GetProperty(PropertyInt.JewelSockets);
                if (jewelSockets.HasValue && jewelSockets.Value > 0)
                {
                    commonParts.RemoveAll(p => p.StartsWith("Sockets ", StringComparison.Ordinal));
                    commonParts.Add($"Sockets {jewelSockets.Value}");
                }

                return commonParts;
            }

            var commonParts = BuildCommonParts(obj, wieldReq);

            var details = TryBuildDetails(obj, listing, headerTitle, sellerName, expiresAtText, commonParts);
            if (details == null)
            {
                return null;
            }

            return (header + "\n" + Render(details)).TrimEnd();
        }
        catch
        {
            return null;
        }
        finally
        {
            cache.WorldObject = null;
            cache.WorldObjectInitialized = false;
            obj.Destroy();
        }
    }

    // WorldObject recreation/caching

    private static ACE.Server.WorldObjects.WorldObject? GetOrCreateWorldObject(ACE.Database.Models.Shard.PlayerMarketListing listing, ListingFormatCache cache)
    {
        if (cache.WorldObjectInitialized)
        {
            return cache.WorldObject;
        }

        cache.WorldObjectInitialized = true;
        cache.WorldObject = TryRecreateListingWorldObject(listing);
        return cache.WorldObject;
    }

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

    private static ACE.Server.WorldObjects.WorldObject? TryRecreateListingWorldObject(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        if (listing.ItemBiotaId <= 0)
        {
            return null;
        }

        try
        {
            var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
            if (biota == null)
            {
                if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
                {
                    return MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
                }

                return null;
            }

            var entityBiota = Database.Adapter.BiotaConverter.ConvertToEntityBiota(biota);
            return WorldObjectFactory.CreateWorldObject(entityBiota);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(listing.ItemSnapshotJson))
            {
                return MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listing.ItemSnapshotJson);
            }

            return null;
        }
    }

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
