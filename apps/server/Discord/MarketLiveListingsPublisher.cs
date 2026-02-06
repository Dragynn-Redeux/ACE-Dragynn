using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ACE.Server.Market;
using Discord;
using Discord.WebSocket;

namespace ACE.Server.Discord;

internal sealed class MarketLiveListingsPublisher
{
    // Instance
    private readonly DiscordSocketClient _client;
    private readonly ulong _channelId;
    private readonly ConcurrentQueue<int> _newListingQueue;

    // Ctor
    internal MarketLiveListingsPublisher(DiscordSocketClient client, ulong channelId, ConcurrentQueue<int> newListingQueue)
    {
        _client = client;
        _channelId = channelId;
        _newListingQueue = newListingQueue;
    }

    // Public API
    internal async Task FlushAsync()
    {
        if (_channelId == 0)
        {
            return;
        }

        var listingIds = new List<int>();
        while (_newListingQueue.TryDequeue(out var id))
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

        var channel = await _client.GetChannelAsync(_channelId) as IMessageChannel;
        if (channel == null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var id in listingIds.Distinct())
        {
            var listing = MarketServiceLocator.PlayerMarketRepository.GetListingById(id);
            if (listing == null || listing.IsSold || listing.IsCancelled || listing.ExpiresAtUtc <= now)
            {
                continue;
            }

            var eb = MarketListingFormatter.BuildListingEmbed(listing, now)
                .WithAuthor("New Market Listing");
            await channel.SendMessageAsync(embeds: [eb.Build()]);
        }
    }
}
