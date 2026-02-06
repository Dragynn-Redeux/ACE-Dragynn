using System;

namespace ACE.Server.Discord;

// Snapshot tuning knobs
internal sealed record MarketSnapshotUpdatePolicy(
    int MaxEditsPerRun,
    TimeSpan EditDelay,
    TimeSpan CreateDelay,
    TimeSpan DeleteDelay,
    int DiscoveryBatches,
    int DiscoveryBatchSize,
    int MaxListingsPerEmbed
)
{
    internal static readonly MarketSnapshotUpdatePolicy Default = new(
        MaxEditsPerRun: 10,
        EditDelay: TimeSpan.FromMilliseconds(5000),
        CreateDelay: TimeSpan.FromMilliseconds(1500),
        DeleteDelay: TimeSpan.FromMilliseconds(750),
        DiscoveryBatches: 5,
        DiscoveryBatchSize: 100,
        MaxListingsPerEmbed: 25);
}
