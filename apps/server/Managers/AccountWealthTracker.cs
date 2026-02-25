using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ACE.Database;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers;

public static class AccountWealthTracker
{
    private static readonly ConcurrentDictionary<uint, long> WealthByAccountId = new();

    public static void Update(Player player)
    {
        var accountId = player?.Session?.AccountId ?? 0;
        if (accountId == 0)
        {
            return;
        }

        // Scan in-memory possessions of all online characters synchronously.
        // This must stay on the calling thread to safely access WorldObject inventories,
        // but makes no DB calls so it completes in microseconds.
        var (inMemRaw, inMemTrophies) = PlayerWealthCalculator.GetInMemoryWealth(player);
        var characterId = player?.Character?.Id;

        // Offload the DB-heavy work (offline alt biotass + bank aggregate + snapshot write)
        // to a background thread so the landblock thread is never stalled by database I/O.
        _ = Task.Run(() =>
        {
            try
            {
                var (dbRaw, dbTrophies) = PlayerWealthCalculator.GetOfflineAndBankWealth(accountId);
                var totalRaw = inMemRaw + dbRaw;
                var totalTrophies = inMemTrophies + dbTrophies;

                WealthByAccountId[accountId] = totalRaw;

                DatabaseManager.Shard.BaseDatabase.UpsertAccountWealthSnapshot(
                    accountId,
                    characterId,
                    totalRaw,
                    totalTrophies,
                    DateTime.UtcNow
                );
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[WEALTH] Background wealth update failed for account {AccountId}", accountId);
            }
        });
    }

    public static bool TryGet(uint accountId, out long wealth) => WealthByAccountId.TryGetValue(accountId, out wealth);

    public static long GetOrDefault(uint accountId) => WealthByAccountId.TryGetValue(accountId, out var value) ? value : 0;

    public static void Remove(uint accountId) => WealthByAccountId.TryRemove(accountId, out _);

    public static void Remove(Player player)
    {
        var accountId = player?.Session?.AccountId ?? 0;
        if (accountId != 0)
        {
            Remove(accountId);
        }
    }
}
