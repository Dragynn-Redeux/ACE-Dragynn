using System;

namespace ACE.Database.Models.Shard;

#nullable enable

public class AccountWealthSnapshot
{
    public int Id { get; set; }

    public uint AccountId { get; set; }
    public uint? CharacterId { get; set; }

    public long PyrealWealth { get; set; }

    public long TrophyWealth { get; set; }

    public long TotalWealth { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }
}

#nullable disable
