using System;
using Serilog;
using ACE.Database;
using ACE.Server.WorldObjects.Managers;

namespace ACE.Server.Managers;

public static class ResonanceManager
{
    private static readonly ILogger _log = Log.ForContext(typeof(ResonanceManager));

    public static ResonanceZoneService Zones { get; private set; }

    private static DateTime? _lastZonesModifiedUtc;
    private static double _nextZoneCheckUnixTime;
    private const double ZoneCheckIntervalSeconds = 2.0;

    public static void Initialize()
    {
        ReloadIfChanged(force: true, currentUnixTime: 0);

        if (Zones == null)
        {
            _log.Warning("ResonanceZoneService failed to initialize");
        }
    }

    // Call this once per server tick (NOT per player tick)
    public static void Tick(double currentUnixTime)
    {
        ReloadIfChanged(force: false, currentUnixTime);
        Zones?.Tick(currentUnixTime);
    }

    private static void ReloadIfChanged(bool force, double currentUnixTime)
    {
        if (!force && currentUnixTime < _nextZoneCheckUnixTime)
        {
            return;
        }

        _nextZoneCheckUnixTime = currentUnixTime + ZoneCheckIntervalSeconds;

        var modified =
            DatabaseManager.ShardConfig
                .GetResonanceZoneEntriesLastModifiedUtc();

        if (!force && modified == _lastZonesModifiedUtc)
        {
            return;
        }

        _lastZonesModifiedUtc = modified;
        Zones = ResonanceZoneService.CreateFromConfig();
    }
}
