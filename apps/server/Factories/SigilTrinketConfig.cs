using System;
using System.Collections.Generic;

namespace ACE.Server.Factories;

internal static class SigilTrinketConfig
{
    internal sealed record SigilStatConfig(
        string PaletteKey,
        string IconColorKey,
        string NameSuffix,
        string UseText,
        bool SetIntensity,
        bool SetReduction,
        bool SetManaReservedZero,
        double CooldownMultiplier,
        double TriggerChanceMultiplier,
        bool ZeroTriggerChance
    );

    public static IReadOnlyList<Dictionary<string, uint>> IconColorIds { get; } =
        new List<Dictionary<string, uint>>(6)
        {
            // Compass
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = 100690601,
                ["gray"] = 100690594,
                ["olive"] = 100690602,
                ["white"] = 100690596,
                ["purple"] = 100690600,
                ["blue"] = 100690595,
                ["green"] = 100690598,
                ["yellow"] = 100690566,
                ["orange"] = 100690602,
                ["red"] = 100690597,
                ["iron"] = 100690599
            },
            // Puzzle Box
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = 100690665,
                ["gray"] = 100690658,
                ["olive"] = 100690661,
                ["white"] = 100690664,
                ["purple"] = 100690663,
                ["blue"] = 100690657,
                ["green"] = 100690660,
                ["yellow"] = 100690662,
                ["orange"] = 100690666,
                ["red"] = 100690659,
                ["iron"] = 0
            },
            // Scarab
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = 100690698,
                ["gray"] = 100690701,
                ["olive"] = 100690705,
                ["white"] = 100690704,
                ["purple"] = 100690707,
                ["blue"] = 100690706,
                ["green"] = 100690700,
                ["yellow"] = 100690699,
                ["orange"] = 100690702,
                ["red"] = 100693226,
                ["iron"] = 100690703
            },
            // Pocket Watch
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = 100690620,
                ["gray"] = 0,
                ["olive"] = 100690619,
                ["white"] = 100690614,
                ["purple"] = 100690618,
                ["blue"] = 100690613,
                ["green"] = 100690616,
                ["yellow"] = 100690592,
                ["orange"] = 100690593,
                ["red"] = 100690615,
                ["iron"] = 100690617
            },
            // Top
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = 100690676,
                ["gray"] = 100690669,
                ["olive"] = 100690672,
                ["white"] = 100690675,
                ["purple"] = 100690674,
                ["blue"] = 100690668,
                ["green"] = 100690671,
                ["yellow"] = 100690673,
                ["orange"] = 100690677,
                ["red"] = 100690670,
                ["iron"] = 0
            },
            // Goggles
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = 100690611,
                ["gray"] = 100690610,
                ["olive"] = 100690612,
                ["white"] = 100690604,
                ["purple"] = 100690609,
                ["blue"] = 100690603,
                ["green"] = 100690606,
                ["yellow"] = 100690608,
                ["orange"] = 100690607,
                ["red"] = 100690605,
                ["iron"] = 100690703
            }
        };

    public static IReadOnlyDictionary<string, int> PaletteTemplateColors { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = 2,
            ["gray"] = 20,
            ["olive"] = 18,
            ["white"] = 61,
            ["purple"] = 39,
            ["blue"] = 77,
            ["green"] = 8,
            ["yellow"] = 21,
            ["orange"] = 19,
            ["red"] = 14,
            ["iron"] = 82
        };

    public static IReadOnlyDictionary<string, string> DuplicationElementString { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = " Bludgeoning",
            ["purple"] = " Lightning",
            ["blue"] = " Cold",
            ["green"] = "n Acid",
            ["yellow"] = " Piercing",
            ["orange"] = " Slashing",
            ["red"] = " Fire"
        };

    public static IReadOnlyDictionary<string, int> ElementId { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = 4,
            ["purple"] = 64,
            ["blue"] = 8,
            ["green"] = 32,
            ["yellow"] = 2,
            ["orange"] = 1,
            ["red"] = 16
        };

    public static IReadOnlyDictionary<string, uint> OverlayIds { get; } =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["life"] = 100689496,
            ["war"] = 100689495
        };

    public static IReadOnlyDictionary<int, uint> TierIconIds { get; } =
        new Dictionary<int, uint>
        {
            [1] = 0x06009001,
            [2] = 0x06009002,
            [3] = 0x06009003,
            [4] = 0x06009004,
            [5] = 0x06009005,
            [6] = 0x06009006,
            [7] = 0x06009007,
            [8] = 0x06009008,
            [9] = 0x06009009,
            [10] = 0x06009010
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> LifeMagicScarab { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "white",
                "white",
                " of Protection",
                @"Whenever the wielder casts a Heal, Revitalize, or Mana Boost spell, there is a chance the target will also gain elemental protection against each type of damage they took within the last 10 seconds.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [1] = new SigilStatConfig(
                "black",
                "black",
                " of Vulnerability",
                @"Whenever the wielder casts a Harm, Enfeeble, or Mana Drain spell, there is a chance the target will also be inflicted with an elemental vulnerability, based on their greatest weakness.

The level of the debuff is equal to the level of the cast spell.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [2] = new SigilStatConfig(
                "purple",
                "purple",
                " of Artifice",
                "Whenever the wielder casts a Heal spell, there is a chance the target will also be affected by Defender Other." +
                "\nWhenever the wielder casts a Revitalize spell, there is a chance the target will also be affected by Blood Drinker." +
                "\nWhenever the wielder casts a Mana Boost spell, there is a chance the target will also be affected by Spirit Drinker." +
                "\n\nThe level of the buff is equal to the level of the cast spell.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [3] = new SigilStatConfig(
                "green",
                "green",
                " of Growth",
                "Whenever the wielder casts a Heal or Harm spell, there is a chance the target will also be affected by Regeneration or Fester, respectively." +
                "\nWhenever the wielder casts a Revitalize or Enfeeble spell, there is a chance the target will also be affected by Rejuvenation or Exhaustion, respectively. " +
                "\nWhenever the wielder casts a Mana Boost or Mana Drain spell, there is a chance the target will also be affected by Mana Renewal or Mana Depletion. " +
                "\n\nThe level of the buff is equal to the level of the cast spell.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> WarMagicScarab { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "olive",
                "olive",
                " of Duplication",
                "Whenever the wielder casts a War spell, there is a chance that the spell will be duplicated and cast against the same target.",
                false,
                false,
                false,
                2.0,
                0.4,
                false
            ),
            [1] = new SigilStatConfig(
                "yellow",
                "yellow",
                " of Detonation",
                "Whenever the wielder damages a creature with a War spell, there is a chance that the spell will detonate on impact, causing an explosion of the same element.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [2] = new SigilStatConfig(
                "orange",
                "orange",
                " of Crushing",
                "Whenever the wielder performs a critical strike with a War spell, they have a chance to gain a 50% critical damage boost for 12 seconds.",
                false,
                false,
                false,
                1.0,
                2.5,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> LifeWarMagicScarab { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "red",
                "red",
                " of Intensity",
                "Whenever the wielder casts any spell, there is a chance the spell will gain intensity. Intensity increases damage and restoration amounts.",
                true,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [1] = new SigilStatConfig(
                "gray",
                "gray",
                " of Shielding",
                "Whenever the wielder casts any spell, there is a chance they will gain a protective buff, increasing damage reduction rating for 10 seconds.\n\nThe level of the buff is equal to the level of the cast spell.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [2] = new SigilStatConfig(
                "blue",
                "blue",
                " of Reduction",
                "Whenever the wielder casts any spell, there is a chance that the cost of the spell will be reduced.",
                false,
                true,
                true,
                0.5,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> ShieldTwohandedCompass { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "red",
                "red",
                " of Might",
                "Whenever the wielder attacks with more than 50% power, there is a chance that a normal hit will be converted into a critical hit.\n\nCan only occur while using a shield or two-handed weapon.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),
            [1] = new SigilStatConfig(
                "olive",
                "olive",
                " of Aggression",
                "Whenever the wielder attacks with more than 50% power, they have a chance to generate double threat towards that enemy.\n\nCan only occur while using a shield or two-handed weapon.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> TwohandedCompass { get; } =
        new Dictionary<int, SigilStatConfig>
        {

        };

    public static IReadOnlyDictionary<int, SigilStatConfig> ShieldCompass { get; } =
        new Dictionary<int, SigilStatConfig>
        {

        };

    public static IReadOnlyDictionary<int, SigilStatConfig> DualWieldPuzzleBox { get; } =
        new Dictionary<int, SigilStatConfig>
        {

        };

    public static IReadOnlyDictionary<int, SigilStatConfig> MissilePuzzleBox { get; } =
        new Dictionary<int, SigilStatConfig>
        {

        };

    public static IReadOnlyDictionary<int, SigilStatConfig> DualWieldMissilePuzzleBox { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "red",
                "red",
                " of Assailment",
                "Whenever the wielder performs a critical hit on an enemy, there is a chance to gain a 50% critical damage buff, additively, for 10 seconds. The length of the effect increases with higher wield requirement weapons.\n\nCan only occur while using a missile weapon or dual-wielding.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            ),

            [1] = new SigilStatConfig(
                "yellow",
                "yellow",
                " of Swift Killer",
                "Whenever the wielder attacks with more than 50% power, there is a chance to gain an attack speed buff, for 10 seconds. The level of the buff increases with higher wield weapons.\n\nCan only occur while using a missile weapon or dual-wielding.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> ThieveryPuzzleBox { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "green",
                "green",
                " of Treachery",
                "Whenever the wielder performs a sneak attack critical hit on an enemy, there is a chance to deal double critical damage.\n\nCan only occur while performing sneak attacks and wielding a weapon.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> PhysicalDefensePocketWatch { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "yellow",
                "yellow",
                " of Evasion",
                "Whenever the wielder receives a glancing blow, it has a chance to become a full evade.\n\nCan only occur while wielding a weapon.",
                false,
                false,
                false,
                0.5,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> MagicDefenseTop { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "purple",
                "purple",
                " of Absorption",
                "Whenever the wielder is damaged by a spell, they have a chance to prevent half of the damage and convert it into mana gained.\n\nCan only occur while wielding a weapon.",
                false,
                false,
                false,
                0.5,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> PerceptionGoggles { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "red",
                "red",
                " of Exposure",
                "Whenever the wielder successfully uses the Expose Weakness ability, there is a chance to gain a 5% critical chance boost for 10 seconds.\n\nThe length of the effect increases with higher wield requirement weapons.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            )
        };

    public static IReadOnlyDictionary<int, SigilStatConfig> DeceptionGoggles { get; } =
        new Dictionary<int, SigilStatConfig>
        {
            [0] = new SigilStatConfig(
                "black",
                "black",
                " of Avoidance",
                "Whenever the wielder performs an attack with more than 50% power, there is a chance for the attack to generate no threat towards the target.\n\nCan only occur while wielding a weapon.",
                false,
                false,
                false,
                1.0,
                1.0,
                false
            )
        };

    // quick lookup (case-insensitive) for map name -> typed map
    private static readonly Dictionary<string, IReadOnlyDictionary<int, SigilStatConfig>> MapLookup =
        new(StringComparer.OrdinalIgnoreCase);

    static SigilTrinketConfig()
    {
        // Register named maps for TryGetMap lookups.
        MapLookup["lifeMagicScarab"] = LifeMagicScarab;
        MapLookup["warMagicScarab"] = WarMagicScarab;
        MapLookup["lifeWarMagicScarab"] = LifeWarMagicScarab;
        MapLookup["shieldCompass"] = ShieldCompass;
        MapLookup["twohandedCompass"] = TwohandedCompass;
        MapLookup["shieldTwohandedCompass"] = ShieldTwohandedCompass;
        MapLookup["dualWieldPuzzleBox"] = DualWieldPuzzleBox;
        MapLookup["missilePuzzleBox"] = MissilePuzzleBox;
        MapLookup["dualWieldMissilePuzzleBox"] = DualWieldMissilePuzzleBox;
        MapLookup["thieveryPuzzleBox"] = ThieveryPuzzleBox;
        MapLookup["physicalDefensePocketWatch"] = PhysicalDefensePocketWatch;
        MapLookup["magicDefenseTop"] = MagicDefenseTop;
        MapLookup["perceptionGoggles"] = PerceptionGoggles;
        MapLookup["deceptionGoggles"] = DeceptionGoggles;
    }

    public static bool TryGetMap(string mapName, out IReadOnlyDictionary<int, SigilStatConfig> map)
    {
        return MapLookup.TryGetValue(mapName ?? string.Empty, out map);
    }
}
