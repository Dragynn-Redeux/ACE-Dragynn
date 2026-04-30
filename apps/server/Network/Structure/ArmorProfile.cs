using System;
using System.IO;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects;

namespace ACE.Server.Network.Structure;

/// <summary>
/// All of the resistance levels for a piece of armor / clothing
/// (natural, banes, lures)
/// </summary>
public class ArmorProfile
{
    public float SlashingProtection;
    public float PiercingProtection;
    public float BludgeoningProtection;
    public float ColdProtection;
    public float FireProtection;
    public float AcidProtection;
    public float NetherProtection;
    public float LightningProtection;

    public ArmorProfile(WorldObject armor)
    {
        SlashingProtection = GetArmorMod(armor, DamageType.Slash);
        PiercingProtection = GetArmorMod(armor, DamageType.Pierce);
        BludgeoningProtection = GetArmorMod(armor, DamageType.Bludgeon);
        ColdProtection = GetArmorMod(armor, DamageType.Cold);
        FireProtection = GetArmorMod(armor, DamageType.Fire);
        AcidProtection = GetArmorMod(armor, DamageType.Acid);
        NetherProtection = GetArmorMod(armor, DamageType.Nether);
        LightningProtection = GetArmorMod(armor, DamageType.Electric);
    }

    /// <summary>
    /// Calculates the effective RL for a piece of armor or clothing
    /// against a particular damage type
    /// </summary>
    private static float GetArmorMod(WorldObject armor, DamageType damageType)
    {
        var type = armor.EnchantmentManager.GetImpenBaneKey(damageType);
        var baseResistance = armor.GetProperty(type) ?? 1.0f;

        // banes/lures
        var resistanceMod = armor.EnchantmentManager.GetArmorModVsType(damageType);
        var effectiveRL = (float)(baseResistance + resistanceMod);

        // jewelrating: direct below 1.0, diminishing returns above 1.0, hard cap at 1.5
        var jewelBaneRating = GetJewelBaneRating(armor, damageType);
        var directPortion = Math.Clamp(1.0f - effectiveRL, 0.0f, jewelBaneRating);
        var excessPortion = jewelBaneRating - directPortion;
        var postDirect = effectiveRL + directPortion;
        effectiveRL = Math.Min(postDirect + excessPortion * (1.5f - postDirect), 1.5f);
        
        // resistance clamp
        // TODO: this would be a good place to test with client values
        //if (effectiveRL > 2.0f)
        //effectiveRL = 2.0f;
        effectiveRL = Math.Clamp(effectiveRL, -2.0f, 2.0f);

        return effectiveRL;
    }

    public static float GetJewelBaneRating(WorldObject armor, DamageType damageType)
    {
        if (armor.Wielder is not Player player)
        {
            return 0.0f;
        }

        return damageType switch
        {
            DamageType.Slash => Jewel.GetJewelEffectMod(player, PropertyInt.GearSlashBane, "", false, true),
            DamageType.Pierce => Jewel.GetJewelEffectMod(player, PropertyInt.GearPierceBane, "", false, true),
            DamageType.Bludgeon => Jewel.GetJewelEffectMod(player, PropertyInt.GearBludgeonBane, "", false, true),
            DamageType.Cold => Jewel.GetJewelEffectMod(player, PropertyInt.GearFrostBane, "", false, true),
            DamageType.Fire => Jewel.GetJewelEffectMod(player, PropertyInt.GearFireBane, "", false, true),
            DamageType.Acid => Jewel.GetJewelEffectMod(player, PropertyInt.GearAcidBane, "", false, true),
            DamageType.Electric => Jewel.GetJewelEffectMod(player, PropertyInt.GearLightningBane, "", false, true),
            _ => 0.0f
        };
    }
}

public static class ArmorProfileExtensions
{
    /// <summary>
    /// Writes the ArmorProfile to the network stream
    /// </summary>
    public static void Write(this BinaryWriter writer, ArmorProfile profile)
    {
        writer.Write(profile.SlashingProtection);
        writer.Write(profile.PiercingProtection);
        writer.Write(profile.BludgeoningProtection);
        writer.Write(profile.ColdProtection);
        writer.Write(profile.FireProtection);
        writer.Write(profile.AcidProtection);
        writer.Write(profile.NetherProtection);
        writer.Write(profile.LightningProtection);
    }
}
