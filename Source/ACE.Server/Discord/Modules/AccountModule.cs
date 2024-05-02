using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Managers;
using Discord.Interactions;

namespace ACE.Server.Discord.Modules;

public class AccountModule : InteractionModuleBase<SocketInteractionContext>
{
    [RequireRole("Admin")]
    [SlashCommand("allow-ip", "Temporarily adds a user's IP to the multiple account allow list.")]
    public async Task AllowIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            await RespondAsync($"Cannot add IP: `{ip}`. Please enter a valid IPv4 address.", ephemeral: true);
            return;
        }

        var regex = new Regex(@"^((25[0-5]|(2[0-4]|1\d|[1-9]|)\d)\.?\b){4}$");
        if (!regex.IsMatch(ip))
        {
            await RespondAsync($"Cannot add IP: `{ip}`. Please enter a valid IPv4 address.", ephemeral: true);
            return;
        }

        var allowList = ConfigManager.Config.Server.Network.AllowUnlimitedSessionsFromIPAddresses;
        if (allowList.Contains(ip))
        {
            await RespondAsync($"IP `{ip}` already added to allow list.", ephemeral: true);
            return;
        }

        Array.Resize(ref allowList, allowList.Length + 1);
        allowList[^1] = ip;
        ConfigManager.Config.Server.Network.AllowUnlimitedSessionsFromIPAddresses = allowList;

        await RespondAsync($"Successfully added `{ip}` to the server allow list. **Reminder:** This is temporary and should be added to the config", ephemeral: true);
    }

    [RequireRole("Admin")]
    [SlashCommand("list-players", "List currently online players")]
    public async Task ListPlayers(bool showAdmins = true, bool ephemeral = true)
    {
        var players = PlayerManager.GetAllOnline();
        if (players.Count == 0)
        {
            await RespondAsync("Total players online: 0", ephemeral: ephemeral);
            return;
        }

        var orderedPlayers = players.Where(x =>
        {
            var isAdmin = x.Account.AccessLevel == (uint)AccessLevel.Admin;
            return !isAdmin || showAdmins;
        }).OrderByDescending(x => x.Account.AccessLevel).ThenBy(x => x.Name).Select(x => (x.Name, x.Account.AccountName));
        var playerAccountTable = GeneratePlayerAccountTable(orderedPlayers);


        var adminCount = players.Count(x => x.Account.AccessLevel == (uint)AccessLevel.Admin);
        var adminOrAdmins = adminCount == 1 ? "admin" : "admins";

        var message = @$"Total players online: **{players.Count}** ({adminCount} {adminOrAdmins})
```{playerAccountTable}```";

        await RespondAsync(message, ephemeral: ephemeral);
    }


    private string GeneratePlayerAccountTable(IEnumerable<(string Name, string AccountName)> players)
    {
        var longestNameLength = 6;
        var longestAccountLength = 7;

        var valueTuples = players.ToList();
        foreach (var (name, accountName) in valueTuples)
        {
            if (name.Length > longestNameLength)
            {
                longestNameLength = name.Length;
            }

            if (accountName.Length > longestAccountLength)
            {
                longestAccountLength = accountName.Length;
            }
        }

        var header = $"| {"Player".PadRight(longestNameLength)} | {"Account".PadRight(longestAccountLength)} |";
        var divider = $"|{"".PadRight(longestNameLength + 2, '-')}|{"".PadRight(longestAccountLength + 2, '-')}|";
        var playerTable = valueTuples.Select(x => $"| {x.Name.PadRight(longestNameLength)} | {x.AccountName.PadRight(longestAccountLength)} |\n").ToList();
        var concatedPlayerTable = string.Join("", playerTable);

        return @$"{header}
{divider}
{concatedPlayerTable}";
    }
}
