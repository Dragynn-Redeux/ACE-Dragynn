using System;

namespace ACE.Database.Models.Shard;

/// <summary>
/// One entry in an account's bank log (/bank log): a deposit, withdrawal or salvage combine, and who did it.
/// </summary>
public class BankActivity
{
    public uint Id { get; set; }
    public uint AccountId { get; set; }
    public uint CharacterId { get; set; }
    public string CharacterName { get; set; }
    public string Action { get; set; }
    public string Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
