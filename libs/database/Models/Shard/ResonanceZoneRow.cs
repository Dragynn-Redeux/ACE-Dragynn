using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ACE.Database.Models.Shard;

[Table("resonance_zone_entries")]
public class ResonanceZoneRow
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("is_enabled")]
    public bool IsEnabled { get; set; }

    [Column("cell_id")]
    public uint CellId { get; set; }

    [Column("x")]
    public float X { get; set; }

    [Column("y")]
    public float Y { get; set; }

    [Column("z")]
    public float Z { get; set; }

    [Column("qx")]
    public float Qx { get; set; }

    [Column("qy")]
    public float Qy { get; set; }

    [Column("qz")]
    public float Qz { get; set; }

    [Column("qw")]
    public float Qw { get; set; }

    [Column("radius")]
    public float Radius { get; set; }

    [Column("max_distance")]
    public float MaxDistance { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("shroud_event_key")]
    public string ShroudEventKey { get; set; } = "";

    [Column("storm_event_key")]
    public string StormEventKey { get; set; } = "";

    [Column("modified_at")]
    public DateTime ModifiedAt { get; set; }
}
