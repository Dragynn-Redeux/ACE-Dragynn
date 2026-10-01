using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Database;
using ACE.Database.Adapter;
using ACE.Server.Entity;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers;

/// <summary>
/// Pyreal wealth is coins plus trade notes. Both are counted by Value, which for a trade note is its face value
/// times its stack size (the way house payments count notes), so every denomination counts, 150k-250k included.
/// </summary>
public static class PlayerWealthCalculator
{
    public static (long rawPyrealCurrency, long trophyValue) GetAccountWealth(Player player)
    {
        if (player == null)
        {
            return (0, 0);
        }

        var (inMemRaw, inMemTrophies) = GetInMemoryWealth(player);
        var accountId = player.Account?.AccountId ?? 0;
        var (dbRaw, dbTrophies) = GetOfflineAndBankWealth(accountId);

        return (inMemRaw + dbRaw, inMemTrophies + dbTrophies);
    }

    /// <summary>
    /// Scans only the in-memory possessions of all online characters on the account.
    /// No database calls are made — safe to call on the landblock/action thread.
    /// </summary>
    public static (long rawPyrealCurrency, long trophyValue) GetInMemoryWealth(Player player)
    {
        if (player == null)
        {
            return (0, 0);
        }

        long raw = 0;
        long trophies = 0;

        foreach (var item in EnumerateAccountOwnedItems(player))
        {
            if (item.WeenieType == WeenieType.Coin)
            {
                raw += item.Value ?? 0;
                continue;
            }

            if (item.IsTradeNote)
            {
                raw += item.Value ?? 0;
            }

            var tq = item.GetProperty(PropertyInt.TrophyQuality);
            if (tq.HasValue && tq.Value > 0)
            {
                trophies += (long)tq.Value * tq.Value * 100;
            }
        }

        return (raw, trophies);
    }

    /// <summary>
    /// Loads wealth for offline characters and bank storage via database queries.
    /// Safe to call on a background thread — does not access any live WorldObjects.
    /// </summary>
    public static (long rawPyrealCurrency, long trophyValue) GetOfflineAndBankWealth(uint accountId)
    {
        var (offlineRaw, offlineTrophies) = GetOfflineWealth(accountId);
        var (bankRaw, bankTrophies) = GetBankWealth(accountId);

        return (offlineRaw + bankRaw, offlineTrophies + bankTrophies);
    }

    /// <summary>
    /// Wealth carried by the account's offline characters, via database queries.
    /// Safe to call on a background thread — does not access any live WorldObjects.
    /// </summary>
    public static (long rawPyrealCurrency, long trophyValue) GetOfflineWealth(uint accountId)
    {
        if (accountId == 0)
        {
            return (0, 0);
        }

        long raw = 0;
        long trophies = 0;

        foreach (var biota in EnumerateOfflineAccountBiotas(accountId))
        {
            var (biotaRaw, biotaTrophies) = GetBiotaWealth(biota);
            raw += biotaRaw;
            trophies += biotaTrophies;
        }

        return (raw, trophies);
    }

    /// <summary>
    /// Wealth in the account's bank, including the bank's side packs, as saved in the database.
    /// Safe to call on a background thread — does not access any live WorldObjects.
    /// </summary>
    public static (long rawPyrealCurrency, long trophyValue) GetBankWealth(uint accountId)
    {
        if (accountId == 0)
        {
            return (0, 0);
        }

        return DatabaseManager.Shard.BaseDatabase.GetBankWealthAggregate(accountId);
    }

    /// <summary>
    /// Yields in-memory WorldObjects for all online players on the account.
    /// No database calls are made.
    /// </summary>
    private static IEnumerable<WorldObject> EnumerateAccountOwnedItems(Player player)
    {
        var accountId = player.Account?.AccountId ?? 0;

        if (accountId == 0)
        {
            foreach (var item in player.GetAllPossessions())
            {
                yield return item;
            }

            yield break;
        }

        // Use PlayerManager directly — no GetCharacters DB call needed for the in-memory scan.
        var accountPlayers = PlayerManager.GetAccountPlayers(accountId) ?? new Dictionary<uint, IPlayer>();

        foreach (var kvp in accountPlayers)
        {
            if (kvp.Value is Player online)
            {
                foreach (var item in online.GetAllPossessions())
                {
                    yield return item;
                }
            }
        }
    }

    /// <summary>
    /// Yields biotass for offline characters on the account via database queries.
    /// Safe to call from a background thread — does not access any live WorldObjects.
    /// </summary>
    private static IEnumerable<ACE.Entity.Models.Biota> EnumerateOfflineAccountBiotas(uint accountId)
    {
        if (accountId == 0)
        {
            yield break;
        }

        var accountPlayers = PlayerManager.GetAccountPlayers(accountId) ?? new Dictionary<uint, IPlayer>();

        var characters = DatabaseManager.Shard.BaseDatabase.GetCharacters(accountId, includeDeleted: false);
        foreach (var character in characters.Where(c => !c.IsDeleted))
        {
            // Online characters are already covered via in-memory scan.
            if (accountPlayers.TryGetValue(character.Id, out var cached) && cached is Player)
            {
                continue;
            }

            var possessed = DatabaseManager.Shard.BaseDatabase.GetPossessedBiotasInParallel(character.Id);
            foreach (var biota in possessed.Inventory)
            {
                yield return BiotaConverter.ConvertToEntityBiota(biota);
            }

            foreach (var biota in possessed.WieldedItems)
            {
                yield return BiotaConverter.ConvertToEntityBiota(biota);
            }
        }
    }

    private static (long rawPyrealCurrency, long trophyValue) GetBiotaWealth(ACE.Entity.Models.Biota biota)
    {
        if (biota == null)
        {
            return (0, 0);
        }

        long raw = 0;
        long trophies = 0;

        if (biota.WeenieType == WeenieType.Coin)
        {
            // For coins, use `CoinValue` (preferred) then fall back to standard `Value`.
            if (biota.PropertiesInt != null && biota.PropertiesInt.TryGetValue(PropertyInt.CoinValue, out var coinValue))
            {
                raw += coinValue;
            }
            else if (biota.PropertiesInt != null && biota.PropertiesInt.TryGetValue(PropertyInt.Value, out var value))
            {
                raw += value;
            }

            return (raw, trophies);
        }

        if (
            biota.PropertiesInt != null
            && biota.PropertiesInt.TryGetValue(PropertyInt.ItemType, out var itemType)
            && (ItemType)itemType == ItemType.PromissoryNote
            && biota.PropertiesInt.TryGetValue(PropertyInt.Value, out var noteValue)
        )
        {
            raw += noteValue;
        }

        if (biota.PropertiesInt != null && biota.PropertiesInt.TryGetValue(PropertyInt.TrophyQuality, out var tq) && tq > 0)
        {
            trophies += (long)tq * tq * 100;
        }

        return (raw, trophies);
    }
}
