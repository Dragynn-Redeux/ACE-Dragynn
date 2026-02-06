using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ACE.Server.Market;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Serilog;
using Timer = System.Timers.Timer;

namespace ACE.Server.Discord;

public static class MarketListingsPublisher
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(MarketListingsPublisher));

    private static MarketSnapshotUpdater? _snapshotUpdater;
    private static MarketLiveListingsPublisher? _liveListingsPublisher;

    private static readonly ConcurrentQueue<int> NewListingQueue = new();

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
        _snapshotUpdater = new MarketSnapshotUpdater(client);

        HookNewListings();

        _liveTarget = TryReadTarget(LiveTargetDefaults, marketChannelsSection);
        if (_liveTarget != null)
        {
            _liveListingsPublisher = _liveTarget.ChannelId == 0
                ? null
                : new MarketLiveListingsPublisher(client, _liveTarget.ChannelId, NewListingQueue);

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
            var publisher = _liveListingsPublisher;
            if (publisher == null)
            {
                return;
            }

            await publisher.FlushAsync();
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
            var updater = _snapshotUpdater;
            if (updater == null || tier.Target.ChannelId == 0)
            {
                return;
            }

            await updater.UpdateSnapshotAsync(tier.Target.ChannelId, tier.MarketTier, tier.Title);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Market snapshot update failed for {Tier}", tier.Title);
        }
    }
}
