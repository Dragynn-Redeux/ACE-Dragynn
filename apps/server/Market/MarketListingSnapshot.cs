using System;
using System.Collections.Generic;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;

namespace ACE.Server.Market;

/// <summary>
/// Minimal, JSON-serializable snapshot used to restore market escrow items if their Biota is lost.
/// </summary>
public sealed class MarketListingSnapshot
{
    public uint WeenieClassId { get; set; }
    public uint BiotaId { get; set; }

    public WeenieType WeenieType { get; set; }

    public Dictionary<PropertyInt, int> PropertiesInt { get; set; } = new();
    public Dictionary<PropertyInt64, long> PropertiesInt64 { get; set; } = new();
    public Dictionary<PropertyBool, bool> PropertiesBool { get; set; } = new();
    public Dictionary<PropertyFloat, double> PropertiesFloat { get; set; } = new();
    public Dictionary<PropertyString, string> PropertiesString { get; set; } = new();
    public Dictionary<PropertyDataId, uint> PropertiesDID { get; set; } = new();
    public Dictionary<PropertyInstanceId, uint> PropertiesIID { get; set; } = new();
    public Dictionary<Skill, PropertiesSkill> PropertiesSkill { get; set; } = new();

    public Dictionary<PositionType, PropertiesPosition> PropertiesPosition { get; set; } = new();

    public Dictionary<int, float> PropertiesSpellBook { get; set; } = new();

    public List<PropertiesAnimPart> PropertiesAnimPart { get; set; } = new();
    public List<PropertiesPalette> PropertiesPalette { get; set; } = new();
    public List<PropertiesTextureMap> PropertiesTextureMap { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
