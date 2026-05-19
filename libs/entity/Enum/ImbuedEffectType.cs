using System;

namespace ACE.Entity.Enum;

[Flags]
public enum ImbuedEffectType : uint
{
    Undef = 0,
    CriticalStrike = 0x0001,
    CripplingBlow = 0x0002,
    ArmorRending = 0x0004,
    SlashRending = 0x0008,
    PierceRending = 0x0010,
    BludgeonRending = 0x0020,
    AcidRending = 0x0040,
    ColdRending = 0x0080,
    ElectricRending = 0x0100,
    FireRending = 0x0200,
    MeleeDefense = 0x0400,
    MissileDefense = 0x0800,
    MagicDefense = 0x1000,
    Spellbook = 0x2000,
    NetherRending = 0x4000,
    WardRending = 0x8000,

    IgnoreSomeMagicProjectileDamage = 0x20000000,
    AlwaysCritical = 0x40000000,
    IgnoreAllArmor = 0x80000000,

    // Defensive armor imbue effects
    ReducedPhysicalDamageTaken = 0x00010000, // Sunstone on armor
    ReducedMagicalDamageTaken  = 0x00020000, // Tourmaline on armor
    ReducedCriticalHitChance   = 0x00040000, // BlackOpal on armor
    ReducedCriticalDamageTaken = 0x00080000, // FireOpal on armor
}
