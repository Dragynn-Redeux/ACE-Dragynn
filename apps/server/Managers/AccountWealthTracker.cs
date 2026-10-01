using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ACE.Database;
using ACE.Server.WorldObjects;

namespace ACE.Server.Managers;

/// <summary>
/// An account's pyreals (coins and trade notes) across every character and the bank, and the bank's share.
/// </summary>
public readonly record struct AccountWealth(long TotalPyreals, long BankPyreals);

public static class AccountWealthTracker
{
    private static readonly ConcurrentDictionary<uint, long> WealthByAccountId = new();

    /// <param name="onUpdated">
    /// Optional. Called once the update finishes, usually on a background thread, with the new figures, or with null
    /// if it failed. Get back on the world thread (WorldManager.EnqueueAction) before touching game state.
    /// </param>
    public static void Update(Player player, Action<AccountWealth?> onUpdated = null)
    {
        var accountId = player?.Session?.AccountId ?? 0;
        if (accountId == 0)
        {
            onUpdated?.Invoke(null);
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
            AccountWealth? result = null;

            try
            {
                var (offlineRaw, offlineTrophies) = PlayerWealthCalculator.GetOfflineWealth(accountId);
                var (bankRaw, bankTrophies) = PlayerWealthCalculator.GetBankWealth(accountId);
                var totalRaw = inMemRaw + offlineRaw + bankRaw;
                var totalTrophies = inMemTrophies + offlineTrophies + bankTrophies;

                WealthByAccountId[accountId] = totalRaw;

                DatabaseManager.Shard.BaseDatabase.UpsertAccountWealthSnapshot(
                    accountId,
                    characterId,
                    totalRaw,
                    totalTrophies,
                    DateTime.UtcNow
                );

                result = new AccountWealth(totalRaw, bankRaw);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[WEALTH] Background wealth update failed for account {AccountId}", accountId);
            }

            onUpdated?.Invoke(result);
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
