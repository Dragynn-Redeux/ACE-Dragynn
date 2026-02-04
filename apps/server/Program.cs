using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ACE.Common;
using ACE.Database;
using ACE.DatLoader;
using ACE.Server.Commands.Handlers;
using ACE.Server.Discord;
using ACE.Server.Managers;
using ACE.Server.Market;
using ACE.Server.Mods;
using ACE.Server.Network;
using ACE.Server.Network.Managers;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace ACE.Server;

partial class Program
{
    /// <summary>
    /// The timeBeginPeriod function sets the minimum timer resolution for an application or device driver. Used to manipulate the timer frequency.
    /// https://docs.microsoft.com/en-us/windows/desktop/api/timeapi/nf-timeapi-timebeginperiod
    /// Important note: This function affects a global Windows setting. Windows uses the lowest value (that is, highest resolution) requested by any process.
    /// </summary>
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    public static extern uint MM_BeginPeriod(uint uMilliseconds);

    /// <summary>
    /// The timeEndPeriod function clears a previously set minimum timer resolution
    /// https://docs.microsoft.com/en-us/windows/desktop/api/timeapi/nf-timeapi-timeendperiod
    /// </summary>
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    public static extern uint MM_EndPeriod(uint uMilliseconds);

    private static readonly ILogger _log = Log.ForContext(typeof(Program));

    public static readonly bool IsRunningInContainer = Convert.ToBoolean(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")
    );

    public static async Task Main(string[] args)
    {
        var consoleTitle = $"ACEmulator - v{ServerBuildInfo.FullVersion}";

        Console.Title = consoleTitle;

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);

        // Typically, you wouldn't force the current culture on an entire application unless you know sure your application is used in a specific region (which ACE is not)
        // We do this because almost all of the client/user input/output code does not take culture into account, and assumes en-US formatting.
        // Without this, many commands that require special characters like , and . will break
        Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
        // Init our text encoding options. This will allow us to use more than standard ANSI text, which the client also supports.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var exeLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var containerConfigDirectory = "/ace/Config";

        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", reloadOnChange: false, optional: false)
            .AddUserSecrets<Program>(optional: true)
            .Build();

        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger();

        if (Environment.ProcessorCount < 2)
        {
            _log.Warning(
                "Only one vCPU was detected. ACE may run with limited performance. You should increase your vCPU count for anything more than a single player server."
            );
        }

        // Do system specific initializations here
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On many windows systems, the default resolution for Thread.Sleep is 15.6ms. This allows us to command a tighter resolution
                MM_BeginPeriod(1);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Could not set timer resolution to 1ms");
        }

        _log.Information("Starting ACEmulator...");

        if (IsRunningInContainer)
        {
            _log.Information("ACEmulator is running in a container...");
        }

        var configFile = Path.Combine(exeLocation, "Config.js");
        var configConfigContainer = Path.Combine(containerConfigDirectory, "Config.js");

        if (IsRunningInContainer && File.Exists(configConfigContainer))
        {
            File.Copy(configConfigContainer, configFile, true);
        }

        if (!File.Exists(configFile))
        {
            if (!IsRunningInContainer)
            {
                DoOutOfBoxSetup(configFile);
            }
            else
            {
                if (!File.Exists(configConfigContainer))
                {
                    DoOutOfBoxSetup(configFile);
                    File.Copy(configFile, configConfigContainer);
                }
                else
                {
                    File.Copy(configConfigContainer, configFile);
                }
            }
        }

        _log.Information("Initializing ConfigManager...");
        ConfigManager.Initialize();

        _log.Information("Initializing ModManager...");
        ModManager.Initialize();

        if (ConfigManager.Config.Server.WorldName != "ACEmulator")
        {
            consoleTitle = $"{ConfigManager.Config.Server.WorldName} | {consoleTitle}";
            Console.Title = consoleTitle;
        }

        if (ConfigManager.Config.Offline.PurgeDeletedCharacters)
        {
            _log.Information(
                $"Purging deleted characters, and their possessions, older than {ConfigManager.Config.Offline.PurgeDeletedCharactersDays} days ({DateTime.Now.AddDays(-ConfigManager.Config.Offline.PurgeDeletedCharactersDays)})..."
            );
            ShardDatabaseOfflineTools.PurgeCharactersInParallel(
                ConfigManager.Config.Offline.PurgeDeletedCharactersDays,
                out var charactersPurged,
                out var playerBiotasPurged,
                out var possessionsPurged
            );
            _log.Information(
                $"Purged {charactersPurged:N0} characters, {playerBiotasPurged:N0} player biotas and {possessionsPurged:N0} possessions."
            );
        }

        if (ConfigManager.Config.Offline.PurgeOrphanedBiotas)
        {
            _log.Information($"Purging orphaned biotas...");
            ShardDatabaseOfflineTools.PurgeOrphanedBiotasInParallel(out var numberOfBiotasPurged);
            _log.Information($"Purged {numberOfBiotasPurged:N0} biotas.");
        }

        if (ConfigManager.Config.Offline.PruneDeletedCharactersFromFriendLists)
        {
            _log.Information($"Pruning invalid friends from all friend lists...");
            ShardDatabaseOfflineTools.PruneDeletedCharactersFromFriendLists(out var numberOfFriendsPruned);
            _log.Information($"Pruned {numberOfFriendsPruned:N0} invalid friends found on friend lists.");
        }

        if (ConfigManager.Config.Offline.PruneDeletedObjectsFromShortcutBars)
        {
            _log.Information($"Pruning invalid shortcuts from all shortcut bars...");
            ShardDatabaseOfflineTools.PruneDeletedObjectsFromShortcutBars(out var numberOfShortcutsPruned);
            _log.Information($"Pruned {numberOfShortcutsPruned:N0} deleted objects found on shortcut bars.");
        }

        if (ConfigManager.Config.Offline.PruneDeletedCharactersFromSquelchLists)
        {
            _log.Information($"Pruning invalid squelches from all squelch lists...");
            ShardDatabaseOfflineTools.PruneDeletedCharactersFromSquelchLists(out var numberOfSquelchesPruned);
            _log.Information(
                $"Pruned {numberOfSquelchesPruned:N0} invalid squelched characters found on squelch lists."
            );
        }

        if (ConfigManager.Config.Offline.AutoServerUpdateCheck)
        {
            CheckForServerUpdate();
        }
        else
        {
            _log.Information($"AutoServerVersionCheck is disabled...");
        }

        if (ConfigManager.Config.Offline.AutoUpdateWorldDatabase)
        {
            CheckForWorldDatabaseUpdate();

            if (ConfigManager.Config.Offline.AutoApplyWorldCustomizations)
            {
                AutoApplyWorldCustomizations();
            }
        }
        else
        {
            _log.Information($"AutoUpdateWorldDatabase is disabled...");
        }

        if (ConfigManager.Config.Offline.AutoApplyDatabaseUpdates)
        {
            AutoApplyDatabaseUpdates();
        }
        else
        {
            _log.Information($"AutoApplyDatabaseUpdates is disabled...");
        }

        // This should only be enabled manually. To enable it, simply uncomment this line
        //ACE.Database.OfflineTools.Shard.BiotaGuidConsolidator.ConsolidateBiotaGuids(0xA0000000, true, false, out int numberOfBiotasConsolidated, out int numberOfBiotasSkipped, out int numberOfErrors);
        //ACE.Database.OfflineTools.Shard.BiotaGuidConsolidator.ConsolidateBiotaGuids(0xD0000000, false, true, out int numberOfBiotasConsolidated2, out int numberOfBiotasSkipped2, out int numberOfErrors2);

        ShardDatabaseOfflineTools.CheckForBiotaPropertiesPaletteOrderColumnInShard();

        // pre-load starterGear.json, abort startup if file is not found as it is required to create new characters.
        if (Factories.StarterGearFactory.GetStarterGearConfiguration() == null)
        {
            _log.Fatal("Unable to load or parse starterGear.json. ACEmulator will now abort startup.");
            ServerManager.StartupAbort();
            Environment.Exit(0);
        }

        _log.Information("Initializing ServerManager...");
        ServerManager.Initialize();

        _log.Information("Initializing DatManager...");
        DatManager.Initialize(ConfigManager.Config.Server.DatFilesDirectory, true);

        if (ConfigManager.Config.DDD.EnableDATPatching)
        {
            _log.Information("Initializing DDDManager...");
            DDDManager.Initialize();
        }
        else
        {
            _log.Information("DAT Patching Disabled...");
        }

        _log.Information("Initializing DatabaseManager...");
        DatabaseManager.Initialize();

        if (DatabaseManager.InitializationFailure)
        {
            _log.Fatal("DatabaseManager initialization failed. ACEmulator will now abort startup.");
            ServerManager.StartupAbort();
            Environment.Exit(0);
        }

        _log.Information("Starting DatabaseManager...");
        DatabaseManager.Start();

        _log.Information("Starting PropertyManager...");
        PropertyManager.Initialize();

        _log.Information("Initializing PlayerMarket...");
        var marketListingLifetimeSeconds = PropertyManager.GetDouble("market_listing_lifetime_seconds").Item;
        if (marketListingLifetimeSeconds < 0)
        {
            marketListingLifetimeSeconds = 0;
        }

        MarketServiceLocator.Initialize(
            new DbPlayerMarketRepository(),
            new PlayerMarketConfig
            {
                ListingLifetime = TimeSpan.FromSeconds(marketListingLifetimeSeconds),
                MaxActiveListingsPerAccount = 50,
                DefaultPayoutCurrency = MarketCurrencyType.Pyreal,
                RequireManualReclaimOnExpire = true
            });

        RecipeComponentUseEmote.Initialize();

        _log.Information("Initializing GuidManager...");
        GuidManager.Initialize();

        if (ConfigManager.Config.Server.ServerPerformanceMonitorAutoStart)
        {
            _log.Information("Server Performance Monitor auto starting...");
            ServerPerformanceMonitor.Start();
        }

        if (ConfigManager.Config.Server.WorldDatabasePrecaching)
        {
            _log.Information("Precaching Weenies...");
            DatabaseManager.World.CacheAllWeenies();
            _log.Information("Precaching Cookbooks...");
            DatabaseManager.World.CacheAllCookbooks();
            _log.Information("Precaching Events...");
            DatabaseManager.World.GetAllEvents();
            _log.Information("Precaching House Portals...");
            DatabaseManager.World.CacheAllHousePortals();
            _log.Information("Precaching Points Of Interest...");
            DatabaseManager.World.CacheAllPointsOfInterest();
            _log.Information("Precaching Spells...");
            DatabaseManager.World.CacheAllSpells();
            _log.Information("Precaching Treasures - Death...");
            DatabaseManager.World.CacheAllTreasuresDeath();
            _log.Information("Precaching Treasures - Material Base...");
            DatabaseManager.World.CacheAllTreasureMaterialBase();
            _log.Information("Precaching Treasures - Material Groups...");
            DatabaseManager.World.CacheAllTreasureMaterialGroups();
            _log.Information("Precaching Treasures - Material Colors...");
            DatabaseManager.World.CacheAllTreasureMaterialColor();
            _log.Information("Precaching Treasures - Wielded...");
            DatabaseManager.World.CacheAllTreasureWielded();
        }
        else
        {
            _log.Information("Precaching World Database Disabled...");
        }

        _log.Information("Initializing PlayerManager...");
        PlayerManager.Initialize();

        _log.Information("Initializing HouseManager...");
        HouseManager.Initialize();

        _log.Information("Initializing InboundMessageManager...");
        InboundMessageManager.Initialize();

        _log.Information("Initializing SocketManager...");
        SocketManager.Initialize();

        _log.Information("Initializing WorldManager...");
        WorldManager.Initialize();

        _log.Information("Initializing EventManager...");
        EventManager.Initialize();

        // Free up memory before the server goes online. This can free up 6 GB+ on larger servers.
        _log.Information("Forcing .net garbage collection...");
        for (var i = 0; i < 10; i++)
        {
            // https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals
            // https://learn.microsoft.com/en-us/dotnet/api/system.runtime.gcsettings.largeobjectheapcompactionmode
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

            GC.Collect();
        }

        // This should be last
        _log.Information("Initializing CommandManager...");
        CommandManager.Initialize();

        _log.Information("Initializing ModManager...");
        ModManager.Initialize();
        //Register mod commands
        _log.Information("Registering ModManager commands...");
        ModManager.RegisterCommands();
        ModManager.ListMods();

        DiscordChatBridge.Initialize(configuration);
        await DiscordChatBridge.Start();

        var discordConfig = configuration.GetSection("Discord");
        if (discordConfig.GetValue<bool>("Enabled"))
        {
            var discordBot = new DiscordBot();
            await discordBot.Initialize(discordConfig);
        }

        if (!PropertyManager.GetBool("world_closed", false).Item)
        {
            WorldManager.Open(null);
        }
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log.Error(e.ExceptionObject as Exception, "An unhandled exception occurred.");
        Thread.Sleep(1000);
    }

    private static void OnProcessExit(object sender, EventArgs e)
    {
        if (!IsRunningInContainer)
        {
            if (!ServerManager.ShutdownInitiated)
            {
                _log.Warning("Unsafe server shutdown detected! Data loss is possible!");
            }

            PropertyManager.StopUpdating();
            DatabaseManager.Stop();

            // Do system specific cleanup here
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    MM_EndPeriod(1);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex.ToString());
            }
        }
        else
        {
            ServerManager.DoShutdownNow();
            DatabaseManager.Stop();
        }
    }
}
