using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ACE.Entity.Enum;
using Discord;

namespace ACE.Server.Discord;

internal sealed class MarketSnapshotRenderer
{
    // Constants / types
    internal const string SnapshotSentinel = "\u200C";

    internal enum SnapshotSectionKey
    {
        ItemType = 0,
        SigilTrinket = 1,
        BeastParts = 2,
    }

    private static readonly Regex CamelCaseSplit = new("(?<!^)([A-Z])", RegexOptions.Compiled);

    internal sealed record SnapshotPost(string? Content, EmbedBuilder? Embed);

    internal sealed record SortedListing(
        ACE.Database.Models.Shard.PlayerMarketListing Listing,
        SnapshotSectionKey SectionKey,
        int ItemType,
        int SubType,
        int ListedPrice,
        int ListingId,
        int SalvageMaterialKey = int.MaxValue,
        float SalvageWorkKey = float.MaxValue,
        int SalvageUnitPriceKey = int.MaxValue,
        int GemMaterialKey = int.MaxValue,
        float GemWorkKey = float.MaxValue,
        int ConsumableWeenieTypeKey = int.MaxValue,
        uint ConsumableWcidKey = uint.MaxValue,
        int ConsumableUnitPriceKey = int.MaxValue);

    // Public API
    internal List<SnapshotPost> BuildPosts(
        List<SortedListing> sorted,
        string tierTitle,
        int totalActive,
        long updatedUnix,
        DateTime now,
        MarketSnapshotUpdatePolicy policy)
    {
        static string SplitCamel(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return s;
            }

            return CamelCaseSplit.Replace(s, " $1");
        }

        static string SectionLabel(SnapshotSectionKey sectionKey, int itemType)
        {
            if (sectionKey == SnapshotSectionKey.SigilTrinket)
            {
                return "Sigil Trinkets";
            }

            if (sectionKey == SnapshotSectionKey.BeastParts)
            {
                return "Beast Parts";
            }

            var itemTypeValue = unchecked((uint)itemType);
            return Enum.IsDefined(typeof(ItemType), itemTypeValue)
                ? (((ItemType)itemTypeValue) switch
                {
                    ItemType.MeleeWeapon => "Melee Weapons",
                    ItemType.MissileWeapon => "Missile Weapons",
                    ItemType.Caster => "Casters",
                    ItemType.TinkeringMaterial => "Salvage",
                    ItemType.Useless => "Trophies",
                    ItemType.Writable => "Scrolls",
                    ItemType.Food => "Consumables",
                    ItemType.Gem => "Gems",
                    ItemType.ManaStone => "Mana Stones",
                    _ => SplitCamel(((ItemType)itemTypeValue).ToString()),
                })
                : $"ItemType {itemType}";
        }

        var posts = new List<SnapshotPost>(Math.Max(1, sorted.Count / Math.Max(1, policy.MaxListingsPerEmbed) + 8));

        static (string title, string value) SplitListing(string listingText)
        {
            if (string.IsNullOrWhiteSpace(listingText))
            {
                return (string.Empty, string.Empty);
            }

            var parts = listingText
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var title = parts.Count > 0 ? parts[0].TrimStart('#', ' ').Trim() : string.Empty;
            if (parts.Count > 0)
            {
                parts.RemoveAt(0);
            }

            parts.RemoveAll(p => p.StartsWith("Footer:", StringComparison.Ordinal));

            var value = string.Join("\n", parts.Select(p => p.Trim()).Where(p => p.Length > 0)).Trim();
            return (title, value);
        }

        static Color? ResolveSectionColor(SnapshotSectionKey sectionKey, int itemType)
        {
            if (sectionKey == SnapshotSectionKey.SigilTrinket)
            {
                return new Color(0, 128, 128);
            }

            if (sectionKey == SnapshotSectionKey.BeastParts)
            {
                return new Color(139, 69, 19);
            }

            return itemType switch
            {
                (int)ItemType.MeleeWeapon => Color.Red,
                (int)ItemType.MissileWeapon => new Color(255, 165, 0),
                (int)ItemType.Caster => new Color(128, 0, 128),
                (int)ItemType.Armor => Color.Gold,
                (int)ItemType.Jewelry => Color.Blue,
                (int)ItemType.Clothing => Color.Green,
                _ => null,
            };
        }

        var grouped = sorted
            .GroupBy(s => new { s.SectionKey, s.ItemType })
            .ToList();

        foreach (var g in grouped)
        {
            var sectionLabel = SectionLabel(g.Key.SectionKey, g.Key.ItemType);
            var items = g.ToList();
            var pageSize = Math.Max(1, policy.MaxListingsPerEmbed);
            var pages = (int)Math.Ceiling(items.Count / (double)pageSize);

            for (var page = 0; page < pages; page++)
            {
                var pageItems = items.Skip(page * pageSize).Take(pageSize).ToList();
                var pageLabel = pages > 1 ? $"{sectionLabel} (page {page + 1}/{pages})" : sectionLabel;

                var sectionEmbed = new EmbedBuilder()
                    .WithTitle(pageLabel)
                    .WithDescription($"updated <t:{updatedUnix}:R>\n{SnapshotSentinel}")
                    .WithFooter(SnapshotSentinel);

                var sectionColor = ResolveSectionColor(g.Key.SectionKey, g.Key.ItemType);
                if (sectionColor.HasValue)
                {
                    sectionEmbed.WithColor(sectionColor.Value);
                }

                foreach (var e in pageItems)
                {
                    var listingText = MarketListingFormatter.BuildListingMarkdown(e.Listing, now);
                    var (title, value) = SplitListing(listingText);

                    if (string.IsNullOrWhiteSpace(title))
                    {
                        title = $"Listing {e.ListingId}";
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        value = "-";
                    }

                    if (value.Length > 1024)
                    {
                        value = value.Substring(0, 1021) + "...";
                    }

                    // Discord embed hard limit: total (title + description + fields + footer etc) <= 6000
                    // Keep a small safety margin to avoid build-time failures.
                    const int embedHardLimit = 6000;
                    const int safety = 32;
                    var currentLen = 0;
                    if (!string.IsNullOrEmpty(sectionEmbed.Title)) currentLen += sectionEmbed.Title.Length;
                    if (!string.IsNullOrEmpty(sectionEmbed.Description)) currentLen += sectionEmbed.Description.Length;
                    if (sectionEmbed.Footer != null && !string.IsNullOrEmpty(sectionEmbed.Footer.Text)) currentLen += sectionEmbed.Footer.Text.Length;
                    if (sectionEmbed.Fields != null)
                    {
                        foreach (var f in sectionEmbed.Fields)
                        {
                            currentLen += (f.Name?.Length ?? 0) + (f.Value?.ToString()?.Length ?? 0);
                        }
                    }

                    var budget = embedHardLimit - safety - currentLen;
                    if (budget <= 0)
                    {
                        break;
                    }

                    // budget must cover both name+value
                    var needed = title.Length + value.Length;
                    if (needed > budget)
                    {
                        var availForValue = Math.Max(0, budget - title.Length);
                        if (availForValue <= 0)
                        {
                            break;
                        }

                        if (value.Length > availForValue)
                        {
                            value = availForValue <= 3 ? value.Substring(0, availForValue) : value.Substring(0, availForValue - 3) + "...";
                        }
                    }

                    sectionEmbed.AddField(title, value, inline: false);
                }

                posts.Add(new SnapshotPost(null, sectionEmbed));
            }
        }

        if (posts.Count == 0)
        {
            var empty = $"**{tierTitle} ({totalActive})**\n" +
                        $"(no active listings)\n" +
                        $"updated <t:{updatedUnix}:R>\n" +
                        SnapshotSentinel;
            posts.Add(new SnapshotPost(empty, null));
        }

        return posts;
    }
}
