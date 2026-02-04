using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ACE.Common;
using ACE.Database;
using ACE.Server.Managers;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Timer = System.Timers.Timer;

namespace ACE.Server.Discord;

public class DiscordBot
{
    private readonly ILogger _log = Log.ForContext<DiscordBot>();
    private IConfiguration _configuration;
    private IServiceProvider _services;
    private DiscordSocketClient _client;
    private IConfigurationSection _discordSection;

    private readonly DiscordSocketConfig _socketConfig =
        new()
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
            AlwaysDownloadUsers = true,
        };

    public async Task Initialize(IConfiguration configuration)
    {
        _configuration = configuration;

        // Support being initialized with either the root configuration (containing a `Discord` section)
        // or directly with the `Discord` section itself.
        var maybeDiscord = _configuration.GetSection("Discord");
        _discordSection = maybeDiscord.Exists() ? maybeDiscord : _configuration as IConfigurationSection;

        if (_discordSection == null || !_discordSection.Exists())
        {
            _log.Information("Discord configuration section is missing; Discord integration will not start.");
            return;
        }

        var discordEnabled = _discordSection.GetValue<bool?>("Enabled") ?? true;
        if (!discordEnabled)
        {
            _log.Information("Discord integration is disabled via configuration (Discord:Enabled=false).");
            return;
        }

        _services = new ServiceCollection()
            .AddHttpClient()
            .AddSingleton(_configuration)
            .AddSingleton(_socketConfig)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
            .AddSingleton<InteractionHandler>()
            .BuildServiceProvider();

        _client = _services.GetRequiredService<DiscordSocketClient>();

        _client.Log += LogAsync;

        await _services.GetRequiredService<InteractionHandler>().InitializeAsync();

        // Feature toggles: each feature can be disabled via appsettings.
        // Defaults to enabled when missing.
        _client.Ready += () => RunInBackground(CreatePopulationChannelUpdateTimer);
        _client.Ready += () => RunInBackground(CreateUniquePopulationChannelUpdateTimer);
        _client.Ready += () => RunInBackground(CreateDayNightCycleChannelUpdateTimer);
        _client.Ready += () => RunInBackground(CreateMarketChannelsUpdateTimers);

        var token = _discordSection.GetValue<string>("Token");
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(Timeout.Infinite);
    }

    private Task CreateMarketChannelsUpdateTimers()
    {
        try
        {
            var marketSection = _discordSection.GetSection("MarketChannels");
            if (!marketSection.Exists())
            {
                return Task.CompletedTask;
            }

            if (!IsFeatureEnabled(marketSection))
            {
                return Task.CompletedTask;
            }

            MarketListingsPublisher.Initialize(_client, marketSection);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to initialize MarketListingsPublisher");
        }

        return Task.CompletedTask;
    }

    private Task CreatePopulationChannelUpdateTimer()
    {
        var section = _discordSection.GetSection("StatChannels").GetSection("Population");
        if (!section.Exists() || !IsFeatureEnabled(section))
        {
            return Task.CompletedTask;
        }

        var updateInterval = section.GetValue<double>("UpdateInterval");

        var timer = new Timer { AutoReset = true, Interval = updateInterval };

        timer.Elapsed += DoPopulationChannelUpdate;

        timer.Start();

        return Task.CompletedTask;
    }

    private async void DoPopulationChannelUpdate(object sender, ElapsedEventArgs e)
    {
        var populationChannelId = _discordSection
            .GetSection("StatChannels")
            .GetSection("Population")
            .GetValue<ulong>("ChannelId");
        if (await _client.GetChannelAsync(populationChannelId) is not IVoiceChannel channel)
        {
            return;
        }

        var oldChannelName = channel.Name;
        var newChannelName = $"Population: \ud83d\udfe2 {PlayerManager.GetOnlineCount()}";
        if (string.Equals(oldChannelName, newChannelName))
        {
            return;
        }

        _log.Information(
            "Updating population channel name. Old: {OldPopulationChannelName} | New: {NewPopulationChannelName}",
            oldChannelName,
            newChannelName
        );

        try
        {
            await channel.ModifyAsync(prop => prop.Name = newChannelName);
            _log.Information("Population channel name updated.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, ex.Message);
        }
    }

    private Task CreateUniquePopulationChannelUpdateTimer()
    {
        var section = _discordSection.GetSection("StatChannels").GetSection("UniquePop");
        if (!section.Exists() || !IsFeatureEnabled(section))
        {
            return Task.CompletedTask;
        }

        var updateInterval = section.GetValue<double>("UpdateInterval");

        var timer = new Timer { AutoReset = true, Interval = updateInterval };

        timer.Elapsed += DoUniquePopulationChannelUpdate;

        timer.Start();

        return Task.CompletedTask;
    }

    private async void DoUniquePopulationChannelUpdate(object sender, ElapsedEventArgs e)
    {
        var uniquePopulationChannelId = _discordSection
            .GetSection("StatChannels")
            .GetSection("UniquePop")
            .GetValue<ulong>("ChannelId");
        if (await _client.GetChannelAsync(uniquePopulationChannelId) is not IVoiceChannel channel)
        {
            return;
        }

        var uniqueIpsOneDay = DatabaseManager.Shard.BaseDatabase.GetUniqueIPsInTheLast(TimeSpan.FromHours(24));

        var oldChannelName = channel.Name;
        var newChannelName = $"Unique Pop (24h): {uniqueIpsOneDay}";
        if (string.Equals(oldChannelName, newChannelName))
        {
            return;
        }

        _log.Information(
            "Updating population channel name. Old: {OldPopulationChannelName} | New: {NewPopulationChannelName}",
            oldChannelName,
            newChannelName
        );

        try
        {
            await channel.ModifyAsync(prop => prop.Name = newChannelName);
            _log.Information("Unique population channel name updated.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, ex.Message);
        }
    }

    private Task CreateDayNightCycleChannelUpdateTimer()
    {
        var section = _discordSection.GetSection("StatChannels").GetSection("DayNightCycle");
        if (!section.Exists() || !IsFeatureEnabled(section))
        {
            return Task.CompletedTask;
        }

        var updateInterval = section.GetValue<double>("UpdateInterval");

        var timer = new Timer { AutoReset = true, Interval = updateInterval };

        timer.Elapsed += DoDayNightCycleChannelUpdate;

        timer.Start();

        return Task.CompletedTask;
    }

    private async void DoDayNightCycleChannelUpdate(object sender, ElapsedEventArgs e)
    {
        var dayNightCycleChannelId = _discordSection
            .GetSection("StatChannels")
            .GetSection("DayNightCycle")
            .GetValue<ulong>("ChannelId");
        if (await _client.GetChannelAsync(dayNightCycleChannelId) is not IVoiceChannel channel)
        {
            return;
        }

        // If the feature is disabled at runtime (config reload), stop doing work.
        var section = _discordSection.GetSection("StatChannels").GetSection("DayNightCycle");
        if (!IsFeatureEnabled(section))
        {
            return;
        }

        var oldChannelName = channel.Name;
        var newChannelName = GetCurrentDayNightCycleChannelName();
        if (string.Equals(oldChannelName.Split(" ").Last(), newChannelName.Split(" ").Last()))
        {
            return;
        }

        _log.Information(
            "Updating day/night cycle channel name. Old: {OldDayNightCycleChannelName} | New: {NewDayNightCycleChannelName}",
            oldChannelName,
            newChannelName
        );

        try
        {
            await channel.ModifyAsync(prop => prop.Name = newChannelName);
            _log.Information("Day/Night cycle channel name updated.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, ex.Message);
        }
    }

    private static string GetCurrentDayNightCycleChannelName()
    {
        var isNightTime = DerethDateTime.UtcNowToEMUTime.IsNighttime;

        return isNightTime ? "Time: \ud83c\udf11 Night" : "Time: \ud83c\udf1e Day";
    }

    private async Task LogAsync(LogMessage message)
    {
        var severity = message.Severity switch
        {
            LogSeverity.Critical => LogEventLevel.Fatal,
            LogSeverity.Error => LogEventLevel.Error,
            LogSeverity.Warning => LogEventLevel.Warning,
            LogSeverity.Info => LogEventLevel.Information,
            LogSeverity.Verbose => LogEventLevel.Verbose,
            LogSeverity.Debug => LogEventLevel.Debug,
            _ => LogEventLevel.Information
        };

        _log.Write(severity, message.Exception, "[{Source}] {Message}", message.Source, message.Message);

        await Task.CompletedTask;
    }

    private static bool IsFeatureEnabled(IConfiguration section)
    {
        // If missing, default to enabled to preserve legacy behavior.
        return section.GetValue<bool?>("Enabled") ?? true;
    }

    private static Task RunInBackground(Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch
            {
                // ignore; individual initializers handle their own logging
            }
        });

        return Task.CompletedTask;
    }
}
