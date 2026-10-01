using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Managers;

/// <summary>
/// The bank log behind /bank log: an account's recent deposits, withdrawals and salvage combines, by any of its
/// characters, kept in the shard database (bank_activity_log) so it outlasts restarts. The database work runs off the
/// world thread.
/// </summary>
public static class BankActivityLog
{
    private static readonly ILogger _log = Log.ForContext(typeof(BankActivityLog));

    public const int KeepPerAccount = 100;
    public const int MaxDetailsLength = 500;

    public const string Deposited = "deposited";
    public const string Withdrew = "withdrew";
    public const string Combined = "combined";

    // A missing table (its update script not applied yet) would fail every bank move, so that is reported once.
    private static int _writeFailureReported;

    /// <summary>
    /// Adds an entry for player's account, e.g. ("deposited", "12 items: Iron Jitte, ..."). Nothing is logged for empty details.
    /// </summary>
    public static void Record(Player player, string action, string details)
    {
        if (player?.Account == null || string.IsNullOrEmpty(details))
        {
            return;
        }

        var entry = new BankActivity
        {
            AccountId = player.Account.AccountId,
            CharacterId = player.Guid.Full,
            CharacterName = player.Name,
            Action = action,
            Details = details.Length > MaxDetailsLength ? details[..MaxDetailsLength] : details,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _ = Task.Run(() =>
        {
            try
            {
                DatabaseManager.Shard.BaseDatabase.AddBankActivity(entry, KeepPerAccount);
            }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Exchange(ref _writeFailureReported, 1) == 0)
                {
                    _log.Error(ex, "[BANKING] Couldn't save to bank_activity_log; /bank log stays empty until it can. Has the 2026-09-30-00-Bank-Activity-Log.sql update run?");
                }
            }
        });
    }

    /// <summary>
    /// Reads an account's newest entries, newest first, and hands them to onRead on a background thread: null if they
    /// couldn't be read. Get back on the world thread (WorldManager.EnqueueAction) before touching game state.
    /// </summary>
    public static void Read(uint accountId, int count, Action<List<BankActivity>> onRead)
    {
        _ = Task.Run(() =>
        {
            List<BankActivity> entries = null;

            try
            {
                entries = DatabaseManager.Shard.BaseDatabase.GetBankActivity(accountId, count);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[BANKING] Couldn't read bank_activity_log for account {AccountId}.", accountId);
            }

            onRead(entries);
        });
    }

    /// <summary>
    /// How one item reads in the log: "Iron Jitte", "Pyreal (5,000)", or "Pack holding 12 items".
    /// </summary>
    public static string DescribeItem(WorldObject item, int amount)
    {
        if (item is Container pack)
        {
            return $"{pack.Name} holding {BankReport.Plural(pack.Inventory.Count, "item")}";
        }

        return DescribeItem(item.NameWithMaterial, item is Stackable ? amount : null);
    }

    public static string DescribeItem(string name, int? amount)
    {
        return amount == null ? name : $"{name} ({amount:N0})";
    }

    /// <summary>
    /// Several items for one entry: the item alone if there is one, else "12 items: Iron Jitte, Long Sword, ..." with
    /// as many names as fit in maxLength and "and N more" for the rest.
    /// </summary>
    public static string DescribeItems(IReadOnlyList<string> items, int maxLength = MaxDetailsLength)
    {
        if (items.Count == 0)
        {
            return "";
        }

        if (items.Count == 1)
        {
            return items[0].Length > maxLength ? items[0][..maxLength] : items[0];
        }

        var prefix = $"{BankReport.Plural(items.Count, "item")}: ";
        var text = prefix;

        for (var i = 0; i < items.Count; i++)
        {
            var next = i == 0 ? items[i] : $", {items[i]}";
            var rest = items.Count - i - 1;

            // leave room to say how many are left out, if any will be
            var reserve = rest > 0 ? $", and {items.Count} more".Length : 0;

            if (text.Length + next.Length + reserve > maxLength)
            {
                return $"{text}{(i == 0 ? "" : ", ")}and {items.Count - i} more";
            }

            text += next;
        }

        return text;
    }

    /// <summary>
    /// How long ago an entry was: "just now", "5m ago", "3h ago", "2d ago".
    /// </summary>
    public static string DescribeAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes}m ago";
        }

        if (age.TotalDays < 1)
        {
            return $"{(int)age.TotalHours}h ago";
        }

        return $"{(int)age.TotalDays}d ago";
    }
}
