using Serilog;
using ACE.Server.WorldObjects.Managers;
using ACE.Server.Config;

namespace ACE.Server.Managers;

public static class ShroudManager
{
    private static readonly ILogger _log = Log.ForContext(typeof(ShroudManager));

    public static ShroudZoneService Zones { get; private set; }

    private static string _lastZonesRaw = string.Empty;

    public static void Initialize()
    {
        ReloadIfChanged(force: true);

        if (Zones == null)
        {
            _log.Warning("ShroudZoneService failed to initialize");
        }
    }

    // Call this once per server tick (NOT per player tick)
    public static void Tick(double currentUnixTime)
    {
        ReloadIfChanged(force: false);
        Zones?.Tick(currentUnixTime);
    }

    private static void ReloadIfChanged(bool force)
    {
        var raw = PropertyManager.GetString(ShroudZoneConfig.ZonesKey, string.Empty).Item ?? string.Empty;

        if (!force && raw == _lastZonesRaw)
        {
            return;
        }

        _lastZonesRaw = raw;
        Zones = ShroudZoneService.CreateFromConfig();
    }
}
