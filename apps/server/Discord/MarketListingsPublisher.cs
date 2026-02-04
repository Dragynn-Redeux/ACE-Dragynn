using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACE.Database;
using ACE.Entity.Enum.Properties;
using ACE.Server.Market;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Serilog;
using Timer = System.Timers.Timer;

namespace ACE.Server.Discord;

public static class MarketListingsPublisher
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(MarketListingsPublisher));

    private const string SnapshotMarker = "[market-snapshot]";

    private static DiscordSocketClient? _client;

    private static readonly ConcurrentQueue<int> NewListingQueue = new();

    private static readonly ConcurrentDictionary<ulong, List<ulong>> SnapshotMessageIdsByThreadId = new();

    private sealed record MarketChannelTarget(string Name, ulong ChannelId, double UpdateIntervalSeconds);

    private sealed record TierTarget(
        MarketChannelTarget Target,
        int MarketTier,
        string Title);

    private static readonly IReadOnlyList<TierTarget> TierTargets =
    [
        new(new("MiscThread", 0, 90), 0, "Miscellaneous Wares"),
        new(new("Tier1Thread", 0, 90), 2, "Crude Wares"),
        new(new("Tier2Thread", 0, 90), 3, "Common Wares"),
        new(new("Tier3Thread", 0, 90), 4, "Fine Wares"),
        new(new("Tier4Thread", 0, 90), 5, "Superior Wares"),
        new(new("Tier5Thread", 0, 90), 6, "Exceptional Wares"),
        new(new("Tier6Thread", 0, 90), 7, "Exquisite Wares"),
        new(new("Tier7Thread", 0, 90), 8, "Peerless Wares"),
    ];

    private static MarketChannelTarget LiveTargetDefaults = new("MarketLiveListings", 0, 20);
    private static MarketChannelTarget? _liveTarget;

    public static void Initialize(DiscordSocketClient client, IConfiguration marketChannelsSection)
    {
        _client = client;

        HookNewListings();

        _liveTarget = TryReadTarget(LiveTargetDefaults, marketChannelsSection);
        if (_liveTarget != null)
        {
            var liveTimer = new Timer { AutoReset = true, Interval = Math.Max(1, _liveTarget.UpdateIntervalSeconds) * 1000 };
            liveTimer.Elapsed += async (_, _) => await FlushLiveListings();
            liveTimer.Start();
        }

        foreach (var tier in TierTargets)
        {
            var target = TryReadTarget(tier.Target, marketChannelsSection);
            if (target == null || target.ChannelId == 0)
            {
                continue;
            }

            var resolved = tier with { Target = target };

            var timer = new Timer { AutoReset = true, Interval = Math.Max(1, resolved.Target.UpdateIntervalSeconds) * 1000 };
            timer.Elapsed += async (_, _) => await UpdateSnapshot(resolved);
            timer.Start();

            // fire soon after startup
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await UpdateSnapshot(resolved);
            });
        }
    }

    private static void HookNewListings()
    {
        // We hook into MarketServiceLocator's repository by wrapping CreateListingFromWorldObject
        // at the source (DbPlayerMarketRepository) using a lightweight global event.
        MarketListingEvents.ListingCreated += id => NewListingQueue.Enqueue(id);
    }

    private static MarketChannelTarget? TryReadTarget(MarketChannelTarget defaults, IConfiguration section)
    {
        var s = section.GetSection(defaults.Name);
        if (!s.Exists())
        {
            return null;
        }

        var channelId = s.GetValue<ulong>("ChannelId");
        var interval = s.GetValue<double>("UpdateInterval", defaults.UpdateIntervalSeconds);
        return new MarketChannelTarget(defaults.Name, channelId, interval);
    }

    private static async Task FlushLiveListings()
    {
        try
        {
            var client = _client;
            var live = _liveTarget;
            if (client == null || live == null || live.ChannelId == 0)
            {
                return;
            }

            var listingIds = new List<int>();
            while (NewListingQueue.TryDequeue(out var id))
            {
                listingIds.Add(id);
                if (listingIds.Count >= 50)
                {
                    break;
                }
            }

            if (listingIds.Count == 0)
            {
                return;
            }

            var channel = await client.GetChannelAsync(live.ChannelId) as IMessageChannel;
            if (channel == null)
            {
                return;
            }

            var now = DateTime.UtcNow;

            var lines = new List<string>();
            foreach (var id in listingIds.Distinct())
            {
                var listing = MarketServiceLocator.PlayerMarketRepository.GetListingById(id);
                if (listing == null)
                {
                    continue;
                }

                if (listing.IsSold || listing.IsCancelled || listing.ExpiresAtUtc <= now)
                {
                    continue;
                }

                lines.Add(FormatListingLine(listing, now));
            }

            if (lines.Count == 0)
            {
                return;
            }

            foreach (var page in BuildPages(lines, header: "**New Market Listings**"))
            {
                await channel.SendMessageAsync(page);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Market live listing flush failed");
        }
    }

    private static async Task UpdateSnapshot(TierTarget tier)
    {
        try
        {
            var client = _client;
            if (client == null || tier.Target.ChannelId == 0)
            {
                return;
            }

            var threadChannel = await client.GetChannelAsync(tier.Target.ChannelId) as IMessageChannel;
            if (threadChannel == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var listings = MarketServiceLocator.PlayerMarketRepository
                .GetListingsForVendorTier(tier.MarketTier, now)
                .OrderBy(l => l.ListedPrice)
                .ThenBy(l => l.Id)
                .ToList();

            var lines = listings.Select(l => FormatListingLine(l, now)).ToList();
            if (lines.Count == 0)
            {
                lines.Add("(no active listings)");
            }

            var pages = BuildPages(lines, header: $"**{tier.Title}**   *(updated {now:yyyy-MM-dd HH:mm} UTC)*");

            var existing = await GetOrDiscoverSnapshotMessageIds(tier.Target.ChannelId, threadChannel);

            // edit existing pages
            var keepIds = new List<ulong>();
            for (var i = 0; i < pages.Count; i++)
            {
                var content = pages[i] + $"\n\n{SnapshotMarker}";
                if (i < existing.Count)
                {
                    var msg = await threadChannel.GetMessageAsync(existing[i]);
                    if (msg is IUserMessage um)
                    {
                        await um.ModifyAsync(p => p.Content = content);
                        keepIds.Add(existing[i]);
                        continue;
                    }
                }

                var created = await threadChannel.SendMessageAsync(content);
                keepIds.Add(created.Id);
            }

            // delete extra pages
            for (var i = keepIds.Count; i < existing.Count; i++)
            {
                try
                {
                    await threadChannel.DeleteMessageAsync(existing[i]);
                }
                catch
                {
                    // ignore
                }
            }

            SnapshotMessageIdsByThreadId[tier.Target.ChannelId] = keepIds;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Market snapshot update failed for {Tier}", tier.Title);
        }
    }

    private static async Task<List<ulong>> GetOrDiscoverSnapshotMessageIds(ulong threadId, IMessageChannel channel)
    {
        if (SnapshotMessageIdsByThreadId.TryGetValue(threadId, out var cached) && cached.Count > 0)
        {
            return cached;
        }

        var client = _client;
        if (client == null)
        {
            return [];
        }

        // Reuse existing: find most recent bot-authored messages, oldest-first, that look like our snapshot pages.
        // We only search a bounded recent window.
        var found = new List<IMessage>();
        var before = (ulong?)null;

        for (var batch = 0; batch < 5; batch++)
        {
            var msgs = before.HasValue
                ? await channel.GetMessagesAsync(before.Value, Direction.Before, 100).FlattenAsync()
                : await channel.GetMessagesAsync(100).FlattenAsync();
            var list = msgs.ToList();
            if (list.Count == 0)
            {
                break;
            }

            found.AddRange(list);
            before = list.Last().Id;
        }

        var botId = client.CurrentUser?.Id;
        if (botId == null)
        {
            return [];
        }

        var snapshotMsgs = found
            .Where(m => m.Author.Id == botId)
            .Where(m => m.Content.Contains(SnapshotMarker, StringComparison.Ordinal))
            .OrderBy(m => m.Timestamp)
            .Select(m => m.Id)
            .ToList();

        SnapshotMessageIdsByThreadId[threadId] = snapshotMsgs;
        return snapshotMsgs;
    }

    private static string FormatListingLine(ACE.Database.Models.Shard.PlayerMarketListing listing, DateTime now)
    {
        var name = ResolveItemName(listing);
        var expiresIn = listing.ExpiresAtUtc - now;
        if (expiresIn < TimeSpan.Zero)
        {
            expiresIn = TimeSpan.Zero;
        }

        var stackSize = ResolveStackSize(listing);
        var wieldReq = listing.WieldReq.HasValue ? $"WR {listing.WieldReq.Value}" : "WR -";
        var stackText = stackSize > 1 ? $"x{stackSize}" : "";

        return $"• {name} {stackText} | {listing.ListedPrice:N0} py | {wieldReq} | {listing.SellerName} | expires in {FormatRemaining(expiresIn)}";
    }

    private static int ResolveStackSize(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        if (listing.ItemBiotaId <= 0)
        {
            return 1;
        }

        try
        {
            var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
            var stack = biota?.BiotaPropertiesInt?.FirstOrDefault(p => p.Type == (ushort)PropertyInt.StackSize)?.Value;
            return stack.HasValue && stack.Value > 0 ? stack.Value : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static string ResolveItemName(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        if (listing.ItemBiotaId > 0)
        {
            try
            {
                var biota = DatabaseManager.Shard.BaseDatabase.GetBiota(listing.ItemBiotaId, true);
                var name = biota?.BiotaPropertiesString?.FirstOrDefault(p => p.Type == (ushort)PropertyString.Name)?.Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch
            {
                // ignore
            }
        }

        var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
        if (weenie?.PropertiesString != null
            && weenie.PropertiesString.TryGetValue(PropertyString.Name, out var weenieName)
            && !string.IsNullOrWhiteSpace(weenieName))
        {
            return weenieName;
        }

        return $"WCID {listing.ItemWeenieClassId}";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return $"{(int)Math.Floor(remaining.TotalDays)}d {remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)Math.Floor(remaining.TotalHours)}h {remaining.Minutes}m";
        }

        if (remaining.TotalMinutes >= 1)
        {
            return $"{(int)Math.Floor(remaining.TotalMinutes)}m {remaining.Seconds}s";
        }

        return $"{Math.Max(0, remaining.Seconds)}s";
    }

    private static List<string> BuildPages(List<string> lines, string header)
    {
        const int maxLen = 1800;

        var pages = new List<string>();
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length == 0)
            {
                return;
            }

            pages.Add(sb.ToString().TrimEnd());
            sb.Clear();
        }

        sb.AppendLine(header);
        sb.AppendLine();

        foreach (var line in lines)
        {
            var toAdd = line + "\n";
            if (sb.Length + toAdd.Length > maxLen)
            {
                Flush();
                sb.AppendLine(header);
                sb.AppendLine();
            }

            sb.Append(toAdd);
        }

        Flush();

        return pages.Count == 0 ? [header] : pages;
    }
}
