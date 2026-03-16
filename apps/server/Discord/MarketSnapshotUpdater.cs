using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Market;
using Discord;
using Discord.WebSocket;

namespace ACE.Server.Discord;

internal sealed class MarketSnapshotUpdater
{
    // Static
    private static readonly IReadOnlyDictionary<int, int> ItemTypeOrder = new Dictionary<int, int>
    {
        { (int)ItemType.MeleeWeapon, 1 },
        { (int)ItemType.MissileWeapon, 2 },
        { (int)ItemType.Caster, 3 },
        { (int)ItemType.Armor, 4 },
        { (int)ItemType.Jewelry, 5 },
        { (int)ItemType.Clothing, 6 },
    };

    // Instance
    private readonly DiscordSocketClient _client;
    private readonly ConcurrentDictionary<ulong, List<ulong>> _snapshotMessageIdsByThreadId = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _channelLocks = new();
    private readonly MarketSnapshotRenderer _renderer = new();
    private readonly MarketSnapshotUpdatePolicy _policy;

    // Ctor
    internal MarketSnapshotUpdater(DiscordSocketClient client, MarketSnapshotUpdatePolicy? policy = null)
    {
        _client = client;
        _policy = policy ?? MarketSnapshotUpdatePolicy.Default;
    }

    // Public API
    internal async Task UpdateSnapshotAsync(ulong channelId, int marketTier, string tierTitle)
    {
        if (channelId == 0)
        {
            return;
        }

        var gate = _channelLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var threadChannel = await _client.GetChannelAsync(channelId) as IMessageChannel;
            if (threadChannel == null)
            {
                return;
            }

            if (_client.GetChannel(channelId) is SocketThreadChannel socketThread && socketThread.IsArchived)
            {
                try
                {
                    await socketThread.ModifyAsync(p => p.Archived = false);
                }
                catch
                {
                    return;
                }
            }

            var now = DateTime.UtcNow;
            var unixNow = new DateTimeOffset(now).ToUnixTimeSeconds();

            var listings = MarketServiceLocator.PlayerMarketRepository
                .GetListingsForVendorTier(marketTier, now)
                .ToList();

            var totalActive = listings.Count;

            var sorted = listings
                .Select(l =>
                {
                    var sort = GetSortKey(l);
                    var sectionKey = GetSnapshotSectionKey(l);
                    var itemType = sort.ItemType; // Store raw item type
                    var subType = sort.SubType; // Store subType for sorting
                    var priceKey = l.ListedPrice;
                    var salvageMaterialKey = int.MaxValue;
                    var salvageWorkKey = float.MaxValue;
                    var salvageUnitPriceKey = int.MaxValue;
                    var gemMaterialKey = int.MaxValue;
                    var gemWorkKey = float.MaxValue;
                    var consumableWeenieTypeKey = int.MaxValue;
                    var consumableWcidKey = uint.MaxValue;
                    var consumableUnitPriceKey = int.MaxValue;
                    try
                    {
                        var weenie = DatabaseManager.World.GetCachedWeenie(l.ItemWeenieClassId);
                        if (weenie != null && (weenie.WeenieType == WeenieType.Food || weenie.WeenieType == WeenieType.Healer))
                        {
                            itemType = (int)ItemType.Food;
                            consumableWeenieTypeKey = (int)weenie.WeenieType;
                            consumableWcidKey = l.ItemWeenieClassId;
                        }

                        if (weenie?.PropertiesInt != null
                            && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var it)
                            && it == (int)ItemType.Armor
                            && weenie.PropertiesInt.TryGetValue(PropertyInt.ArmorSlots, out var slots)
                            && slots > 0)
                        {
                            priceKey = (int)Math.Ceiling(l.ListedPrice / (double)slots);
                        }

                        if (itemType == (int)ItemType.Food)
                        {
                            var wo = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(l.ItemSnapshotJson);
                            if (wo != null)
                            {
                                try
                                {
                                    var qty = wo.StackSize ?? 0;
                                    if (qty > 1)
                                    {
                                        consumableUnitPriceKey = (int)Math.Ceiling(l.ListedPrice / (double)qty);
                                    }
                                    else
                                    {
                                        consumableUnitPriceKey = l.ListedPrice;
                                    }
                                }
                                finally
                                {
                                    wo.Destroy();
                                }
                            }
                            else
                            {
                                consumableUnitPriceKey = l.ListedPrice;
                            }
                        }
                        else
                        {
                            var text = MarketListingFormatter.BuildListingMarkdown(l, now);
                            var firstLine = text.Split('\n', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                            var idx = firstLine.IndexOf('x', StringComparison.Ordinal);
                            if (idx >= 0)
                            {
                                var span = firstLine.AsSpan(idx + 1);
                                var len = 0;
                                while (len < span.Length && char.IsDigit(span[len]))
                                {
                                    len++;
                                }

                                if (len > 0 && int.TryParse(span[..len], out var stack) && stack > 1)
                                {
                                    priceKey = (int)Math.Ceiling(l.ListedPrice / (double)stack);
                                }
                            }
                        }

                        if (sort.ItemType == (int)ItemType.TinkeringMaterial)
                        {
                            var wo = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(l.ItemSnapshotJson);
                            if (wo != null)
                            {
                                try
                                {
                                    salvageMaterialKey = wo.MaterialType.HasValue ? (int)wo.MaterialType.Value : int.MaxValue;
                                    salvageWorkKey = wo.Workmanship ?? float.MaxValue;
                                    var qty = wo.Structure ?? 0;
                                    if (qty > 0)
                                    {
                                        salvageUnitPriceKey = (int)Math.Ceiling(l.ListedPrice / (double)qty);
                                    }
                                }
                                finally
                                {
                                    wo.Destroy();
                                }
                            }
                        }
                        else if (sort.ItemType == (int)ItemType.Gem)
                        {
                            var wo = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(l.ItemSnapshotJson);
                            if (wo != null)
                            {
                                try
                                {
                                    gemMaterialKey = wo.MaterialType.HasValue ? (int)wo.MaterialType.Value : int.MaxValue;
                                    gemWorkKey = wo.Workmanship ?? float.MaxValue;
                                }
                                finally
                                {
                                    wo.Destroy();
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                    return new MarketSnapshotRenderer.SortedListing(
                        l,
                        sectionKey,
                        itemType,
                        subType,
                        priceKey,
                        l.Id,
                        salvageMaterialKey,
                        salvageWorkKey,
                        salvageUnitPriceKey,
                        gemMaterialKey,
                        gemWorkKey,
                        consumableWeenieTypeKey,
                        consumableWcidKey,
                        consumableUnitPriceKey);
                })
                .OrderBy(x => GetSectionSortOrder(x.SectionKey, x.ItemType))
                .ThenBy(x => x.SubType)
                .ThenBy(x => x.ItemType == (int)ItemType.TinkeringMaterial ? x.SalvageMaterialKey : int.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.TinkeringMaterial ? x.SalvageWorkKey : float.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.TinkeringMaterial ? x.SalvageUnitPriceKey : int.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.Gem ? x.GemMaterialKey : int.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.Gem ? (marketTier == 0 ? x.Listing.ItemWeenieClassId : 0u) : (marketTier == 0 ? x.Listing.ItemWeenieClassId : 0u))
                .ThenBy(x => x.ItemType == (int)ItemType.Gem ? x.GemWorkKey : float.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.Food ? x.ConsumableWeenieTypeKey : int.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.Food ? x.ConsumableWcidKey : uint.MaxValue)
                .ThenBy(x => x.ItemType == (int)ItemType.Food ? x.ConsumableUnitPriceKey : int.MaxValue)
                .ThenBy(x => x.ListedPrice)
                .ThenBy(x => x.ListingId)
                .ToList();

            var snapshotPosts = _renderer.BuildPosts(sorted, tierTitle, totalActive, unixNow, now, _policy);

            var existing = await GetOrDiscoverSnapshotMessageIds(channelId, threadChannel);

            var keepIds = await ReconcileSnapshotMessages(threadChannel, existing, snapshotPosts, _policy);
            _snapshotMessageIdsByThreadId[channelId] = keepIds;
        }
        finally
        {
            gate.Release();
        }
    }

    // Discovery

    private async Task<List<ulong>> GetOrDiscoverSnapshotMessageIds(ulong threadId, IMessageChannel channel)
    {
        if (_snapshotMessageIdsByThreadId.TryGetValue(threadId, out var cached) && cached.Count > 0)
        {
            return cached;
        }

        var found = new List<IMessage>();
        var before = (ulong?)null;

        for (var batch = 0; batch < _policy.DiscoveryBatches; batch++)
        {
            var msgs = before.HasValue
                ? await channel.GetMessagesAsync(before.Value, Direction.Before, _policy.DiscoveryBatchSize).FlattenAsync()
                : await channel.GetMessagesAsync(_policy.DiscoveryBatchSize).FlattenAsync();
            var list = msgs.ToList();
            if (list.Count == 0)
            {
                break;
            }

            found.AddRange(list);
            before = list.Last().Id;
        }

        var botId = _client.CurrentUser?.Id;
        if (botId == null)
        {
            return [];
        }

        bool HasMarker(IMessage m)
        {
            if (m.Content.Contains(MarketSnapshotRenderer.SnapshotSentinel, StringComparison.Ordinal))
            {
                return true;
            }

            if (m.Embeds != null && m.Embeds.Any(e => (e.Footer?.Text ?? string.Empty).Contains(MarketSnapshotRenderer.SnapshotSentinel, StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
        }

        var snapshotMsgs = found
            .Where(m => m.Author.Id == botId)
            .Where(HasMarker)
            .OrderBy(m => m.Timestamp)
            .Select(m => m.Id)
            .ToList();

        _snapshotMessageIdsByThreadId[threadId] = snapshotMsgs;
        return snapshotMsgs;
    }

    // Reconcile / update

    private static async Task<List<ulong>> ReconcileSnapshotMessages(
        IMessageChannel channel,
        List<ulong> existing,
        List<MarketSnapshotRenderer.SnapshotPost> desired,
        MarketSnapshotUpdatePolicy policy)
    {
        var keepIds = new List<ulong>(desired.Count);
        var editsThisRun = 0;

        for (var i = 0; i < desired.Count; i++)
        {
            var post = desired[i];

            if (i < existing.Count)
            {
                var reused = await TryUpdateExistingSnapshotMessage(channel, existing[i], post, editsThisRun, policy);
                if (reused.KeptId.HasValue)
                {
                    keepIds.Add(reused.KeptId.Value);
                    editsThisRun = reused.EditsThisRun;
                    continue;
                }
            }

            var created = await CreateSnapshotMessage(channel, post);
            keepIds.Add(created.Id);
            await Task.Delay(policy.CreateDelay);
        }

        await DeleteExcessSnapshotMessages(channel, existing, keepIds.Count, policy);
        return keepIds;
    }

    private sealed record ExistingMessageResult(ulong? KeptId, int EditsThisRun);

    private static async Task<ExistingMessageResult> TryUpdateExistingSnapshotMessage(
        IMessageChannel channel,
        ulong messageId,
        MarketSnapshotRenderer.SnapshotPost desired,
        int editsThisRun,
        MarketSnapshotUpdatePolicy policy)
    {
        static string Normalize(string? s) => (s ?? string.Empty).Trim();

        var msg = await channel.GetMessageAsync(messageId);
        if (msg is not IUserMessage um)
        {
            return new ExistingMessageResult(null, editsThisRun);
        }

        var desiredContent = desired.Content ?? string.Empty;
        var desiredEmbed = desired.Embed?.Build();

        var currentContent = msg.Content ?? string.Empty;
        var currentEmbed = msg.Embeds.FirstOrDefault();

        var contentChanged = Normalize(currentContent) != Normalize(desiredContent);
        var embedChanged = (desiredEmbed == null) != (currentEmbed == null)
            || (desiredEmbed != null && currentEmbed != null && Normalize(desiredEmbed.Description) != Normalize(currentEmbed.Description));

        if (contentChanged || embedChanged)
        {
            if (editsThisRun >= policy.MaxEditsPerRun)
            {
                return new ExistingMessageResult(messageId, editsThisRun);
            }

            try
            {
                await um.ModifyAsync(p =>
                {
                    p.Content = desiredContent;
                    p.Embeds = desiredEmbed != null
                        ? new Optional<Embed[]>([desiredEmbed])
                        : new Optional<Embed[]>([]);
                });
            }
            catch (global::Discord.Net.HttpException ex) when ((int?)ex.DiscordCode == 50083)
            {
                return new ExistingMessageResult(messageId, editsThisRun);
            }

            editsThisRun++;
            await Task.Delay(policy.EditDelay);
        }

        return new ExistingMessageResult(messageId, editsThisRun);
    }

    private static async Task<IUserMessage> CreateSnapshotMessage(IMessageChannel channel, MarketSnapshotRenderer.SnapshotPost post)
    {
        if (post.Embed != null)
        {
            return await channel.SendMessageAsync(post.Content, embeds: [post.Embed.Build()]);
        }

        return await channel.SendMessageAsync(post.Content ?? string.Empty);
    }

    private static async Task DeleteExcessSnapshotMessages(
        IMessageChannel channel,
        List<ulong> existing,
        int keepCount,
        MarketSnapshotUpdatePolicy policy)
    {
        for (var i = keepCount; i < existing.Count; i++)
        {
            try
            {
                await channel.DeleteMessageAsync(existing[i]);
            }
            catch
            {
                // ignore
            }

            await Task.Delay(policy.DeleteDelay);
        }
    }

    // Sorting helpers

    private static MarketSnapshotRenderer.SnapshotSectionKey GetSnapshotSectionKey(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        if (listing.ItemWeenieClassId >= 1052500 && listing.ItemWeenieClassId <= 1052511)
        {
            return MarketSnapshotRenderer.SnapshotSectionKey.BeastParts;
        }

        try
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
            if (weenie != null && weenie.WeenieType == WeenieType.SigilTrinket)
            {
                return MarketSnapshotRenderer.SnapshotSectionKey.SigilTrinket;
            }
        }
        catch
        {
            // ignore
        }

        if (listing.ItemSnapshotJson != null && listing.ItemSnapshotJson.Contains("sigil", StringComparison.OrdinalIgnoreCase))
        {
            return MarketSnapshotRenderer.SnapshotSectionKey.SigilTrinket;
        }

        return MarketSnapshotRenderer.SnapshotSectionKey.ItemType;
    }

    private static int GetSectionSortOrder(MarketSnapshotRenderer.SnapshotSectionKey key, int itemType)
    {
        if (key == MarketSnapshotRenderer.SnapshotSectionKey.SigilTrinket)
        {
            return 7;
        }

        return ItemTypeOrder.TryGetValue(itemType, out var order) ? order : 999;
    }

    private sealed record ListingSortKey(int ItemType, int SubType);

    private static ListingSortKey GetSortKey(ACE.Database.Models.Shard.PlayerMarketListing listing)
    {
        var weenie = DatabaseManager.World.GetCachedWeenie(listing.ItemWeenieClassId);
        if (weenie == null)
        {
            return new ListingSortKey(int.MaxValue, int.MaxValue);
        }

        var itemTypeInt = int.MaxValue;
        if (weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var it))
        {
            itemTypeInt = it;
        }

        var subType = 0;
        if (itemTypeInt is (int)ItemType.Weapon or (int)ItemType.MeleeWeapon or (int)ItemType.MissileWeapon or (int)ItemType.Caster)
        {
            if (weenie.PropertiesInt != null && weenie.PropertiesInt.TryGetValue(PropertyInt.WeaponSkill, out var ws))
            {
                subType = ws;
            }
        }
        else if (itemTypeInt == (int)ItemType.Armor)
        {
            if (weenie.PropertiesInt != null)
            {
                // Armor sorting: WeightClass (int 393) then ClothingPriority (int 35)
                var wc = 0;
                if (weenie.PropertiesInt.TryGetValue((PropertyInt)393, out var weightClass))
                {
                    wc = weightClass;
                }

                var cp = 0;
                if (weenie.PropertiesInt.TryGetValue(PropertyInt.ClothingPriority, out var clothingPriority))
                {
                    cp = clothingPriority;
                }

                // Pack into one int so ThenBy(SubType) yields (wc, cp)
                subType = (wc << 16) | (cp & 0xFFFF);
            }
        }
        else if (itemTypeInt == (int)ItemType.Writable)
        {
            // Scroll sorting: spell level/difficulty, then school, then fall through to price.
            // Pack into one int so ThenBy(SubType) yields (levelLike, schoolLike).
            try
            {
                var spellId = 0;
                if (weenie.PropertiesInt != null)
                {
                    if (weenie.PropertiesInt.TryGetValue(PropertyInt.ItemSpellId, out var sid))
                    {
                        spellId = sid;
                    }
                }

                if (spellId > 0)
                {
                    var spell = new ACE.Server.Entity.Spell((uint)spellId, loadDB: true);
                    var levelLike = (int)Math.Clamp(spell.Level, 0u, 255u);
                    var schoolLike = (int)spell.School;
                    subType = (levelLike << 16) | (schoolLike & 0xFFFF);
                }
            }
            catch
            {
                // ignore
            }
        }

        return new ListingSortKey(itemTypeInt, subType);
    }
}
