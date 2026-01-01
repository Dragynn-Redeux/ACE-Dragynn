using System.Collections.Generic;

namespace ACE.Server.Factories.Tables;

public static class VendorBaseItems
{
    private const int
        HeritageAny = 0,
        HeritageAluvian = 1,
        HeritageGharundim = 2,
        HeritageSho = 3;

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> ArchmageItems =
    [
        (0, false, HeritageAluvian, 5539, 0, 0.0, -1), // Aluvian Wand
        (0, false, HeritageGharundim, 5541, 0, 0.0, -1), // Gharu Wand
        (0, false, HeritageSho, 5540, 0, 0.0, -1), // Sho Wand
        // Scarabs
        (0, false, HeritageAny, 691, 0, 0.0, -1), // Lead
        (2, false, HeritageAny, 689, 0, 0.0, -1), // Iron
        (3, false, HeritageAny, 686, 0, 0.0, -1), // Copper
        (4, false, HeritageAny, 688, 0, 0.0, -1), // Silver
        (5, false, HeritageAny, 687, 0, 0.0, -1), // Gold
        (6, false, HeritageAny, 690, 0, 0.0, -1), // Pyreal
        (7, false, HeritageAny, 8897, 0, 0.0, -1), // Platinum
        // Herbs
        (0, false, HeritageAny, 774, 0, 0.0, -1), // Hyssop
        (0, false, HeritageAny, 775, 0, 0.0, -1), // Mandrake
        (0, false, HeritageAny, 778, 0, 0.0, -1), // Saffron
        (0, false, HeritageAny, 768, 0, 0.0, -1), // Damiana
        (0, false, HeritageAny, 776, 0, 0.0, -1), // Mugwort
        (0, false, HeritageAny, 766, 0, 0.0, -1), // Bistort
        (0, false, HeritageAny, 780, 0, 0.0, -1), // Wormwood
        (0, false, HeritageAny, 765, 0, 0.0, -1), // Amaranth
        (0, false, HeritageAny, 625, 0, 0.0, -1), // Ginseng
        (0, false, HeritageAny, 772, 0, 0.0, -1), // Hawthorne
        (0, false, HeritageAny, 770, 0, 0.0, -1), // Eyebright
        (0, false, HeritageAny, 771, 0, 0.0, -1), // Frankincense
        (0, false, HeritageAny, 769, 0, 0.0, -1), // Dragonsblood
        (0, false, HeritageAny, 773, 0, 0.0, -1), // Henbane
        (0, false, HeritageAny, 767, 0, 0.0, -1), // Comfrey
        (0, false, HeritageAny, 781, 0, 0.0, -1), // Yarrow
        (0, false, HeritageAny, 779, 0, 0.0, -1), // Vervain
        (0, false, HeritageAny, 777, 0, 0.0, -1), // Myrrh
        // Powders
        (0, false, HeritageAny, 782, 0, 0.0, -1), // Agate
        (0, false, HeritageAny, 783, 0, 0.0, -1), // Amber
        (0, false, HeritageAny, 784, 0, 0.0, -1), // Azurite
        (0, false, HeritageAny, 785, 0, 0.0, -1), // Bloodstone
        (0, false, HeritageAny, 786, 0, 0.0, -1), // Carnelian
        (0, false, HeritageAny, 626, 0, 0.0, -1), // Hematite
        (0, false, HeritageAny, 787, 0, 0.0, -1), // Lapis Lazuli
        (0, false, HeritageAny, 788, 0, 0.0, -1), // Malachite
        (0, false, HeritageAny, 789, 0, 0.0, -1), // Moonstone
        (0, false, HeritageAny, 790, 0, 0.0, -1), // Onyx
        (0, false, HeritageAny, 791, 0, 0.0, -1), // Quartz
        (0, false, HeritageAny, 792, 0, 0.0, -1), // Turquoise
        // Potions
        (0, false, HeritageAny, 753, 0, 0.0, -1), // Brimstone
        (0, false, HeritageAny, 754, 0, 0.0, -1), // Cadmia
        (0, false, HeritageAny, 755, 0, 0.0, -1), // Cinnabar
        (0, false, HeritageAny, 756, 0, 0.0, -1), // Cobalt
        (0, false, HeritageAny, 757, 0, 0.0, -1), // Colcothar
        (0, false, HeritageAny, 758, 0, 0.0, -1), // Gypsum
        (0, false, HeritageAny, 759, 0, 0.0, -1), // Quicksilver
        (0, false, HeritageAny, 760, 0, 0.0, -1), // Realgar
        (0, false, HeritageAny, 761, 0, 0.0, -1), // Stibnite
        (0, false, HeritageAny, 762, 0, 0.0, -1), // Turpeth
        (0, false, HeritageAny, 763, 0, 0.0, -1), // Verdigris
        (0, false, HeritageAny, 764, 0, 0.0, -1), // Vitriol
        // Talismans
        (0, false, HeritageAny, 749, 0, 0.0, -1), // Poplar
        (0, false, HeritageAny, 742, 0, 0.0, -1), // Blackthorne
        (0, false, HeritageAny, 752, 0, 0.0, -1), // Yew
        (0, false, HeritageAny, 747, 0, 0.0, -1), // Hemlock
        (0, false, HeritageAny, 627, 0, 0.0, -1), // Alder
        (0, false, HeritageAny, 744, 0, 0.0, -1), // Ebony
        (0, false, HeritageAny, 741, 0, 0.0, -1), // Birch
        (0, false, HeritageAny, 740, 0, 0.0, -1), // Ashwood
        (0, false, HeritageAny, 745, 0, 0.0, -1), // Elder
        (0, false, HeritageAny, 750, 0, 0.0, -1), // Rowan
        (0, false, HeritageAny, 751, 0, 0.0, -1), // Willow
        (0, false, HeritageAny, 743, 0, 0.0, -1), // Cedar
        (0, false, HeritageAny, 748, 0, 0.0, -1), // Oak
        (0, false, HeritageAny, 746, 0, 0.0, -1), // Hazel
        // Tapers
        (0, false, HeritageAny, 1650, 0, 0.0, -1), // Red
        (0, false, HeritageAny, 1649, 0, 0.0, -1), // Pink
        (0, false, HeritageAny, 1648, 0, 0.0, -1), // Orange
        (0, false, HeritageAny, 1653, 0, 0.0, -1), // Yellow
        (0, false, HeritageAny, 1645, 0, 0.0, -1), // Green
        (0, false, HeritageAny, 1654, 0, 0.0, -1), // Turquoise
        (0, false, HeritageAny, 1643, 0, 0.0, -1), // Blue
        (0, false, HeritageAny, 1647, 0, 0.0, -1), // Indigo
        (0, false, HeritageAny, 1651, 0, 0.0, -1), // Violet
        (0, false, HeritageAny, 1644, 0, 0.0, -1), // Brown
        (0, false, HeritageAny, 1652, 0, 0.0, -1), // White
        (0, false, HeritageAny, 1646, 0, 0.0, -1), // Grey
        // Void Comps
        (0, false, HeritageAny, 43347, 0, 0.0, -1), // Nightshade
        (0, false, HeritageAny, 43348, 0, 0.0, -1), // Shadowroot
        (0, false, HeritageAny, 43349, 0, 0.0, -1), // Soulweed
        (0, false, HeritageAny, 43350, 0, 0.0, -1), // Essence of Kemeroi
        (0, false, HeritageAny, 43351, 0, 0.0, -1), // Bottled Rage
        // Mana Stones
        (0, false, HeritageAny, 1057002, 0, 0.0, -1), // Lesser
        (2, false, HeritageAny, 1057003, 0, 0.0, -1), // Standard
        (4, false, HeritageAny, 1057004, 0, 0.0, -1), // Greater
        (6, false, HeritageAny, 1057005, 0, 0.0, -1), // Titan
        // Mana Charges
        (0, false, HeritageAny, 1057006, 0, 0.0, -1), // Lesser
        (2, false, HeritageAny, 1057007, 0, 0.0, -1), // Standard
        (4, false, HeritageAny, 1057008, 0, 0.0, -1), // Greater
        (6, false, HeritageAny, 1057009, 0, 0.0, -1), // Tian
        // Alchemy Supplies
        (0, false, HeritageAny, 4747, 0, 0.0, -1), // Alembic
        (0, false, HeritageAny, 4751, 0, 0.0, -1), // Mortar and Pestle
        (0, false, HeritageAny, 4748, 0, 0.0, -1), // Aqua Incanta
        (0, false, HeritageAny, 5338, 0, 0.0, -1), // Neutral Balm
        (0, false, HeritageAny, 9379, 0, 0.0, -1), // Eye Dropper
        (0, false, HeritageAny, 5333, 0, 0.0, -1), // Health Oil
        (0, false, HeritageAny, 5335, 0, 0.0, -1), // Stamina Oil
        // Brown
        (0, true, HeritageAny, 1055030, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageAny, 1055030, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageAny, 1055040, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageAny, 1055040, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageAny, 1055050, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageAny, 1055050, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageAny, 1055050, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageAny, 1055050, 0, 0.0, -1), // Large Component Pouch
        // Green
        (0, true, HeritageAluvian, 1055032, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageAluvian, 1055032, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageAluvian, 1055042, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageAluvian, 1055042, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageAluvian, 1055052, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageAluvian, 1055052, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageAluvian, 1055052, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageAluvian, 1055052, 0, 0.0, -1), // Large Component Pouch
        // Blue
        (0, true, HeritageAluvian, 1055034, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageAluvian, 1055034, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageAluvian, 1055044, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageAluvian, 1055044, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageAluvian, 1055054, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageAluvian, 1055054, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageAluvian, 1055054, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageAluvian, 1055054, 0, 0.0, -1), // Large Component Pouch
        // Black
        (0, true, HeritageAluvian, 1055039, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageAluvian, 1055039, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageAluvian, 1055049, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageAluvian, 1055049, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageAluvian, 1055059, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageAluvian, 1055059, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageAluvian, 1055059, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageAluvian, 1055059, 0, 0.0, -1), // Large Component Pouch
        // Yellow
        (0, true, HeritageGharundim, 1055031, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageGharundim, 1055031, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageGharundim, 1055041, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageGharundim, 1055041, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageGharundim, 1055051, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageGharundim, 1055051, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageGharundim, 1055051, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageGharundim, 1055051, 0, 0.0, -1), // Large Component Pouch
        // Red
        (0, true, HeritageGharundim, 1055036, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageGharundim, 1055036, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageGharundim, 1055046, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageGharundim, 1055046, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageGharundim, 1055056, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageGharundim, 1055056, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageGharundim, 1055056, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageGharundim, 1055056, 0, 0.0, -1), // Large Component Pouch
        // Gray
        (0, true, HeritageGharundim, 1055038, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageGharundim, 1055038, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageGharundim, 1055048, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageGharundim, 1055048, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageGharundim, 1055058, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageGharundim, 1055058, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageGharundim, 1055058, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageGharundim, 1055058, 0, 0.0, -1), // Large Component Pouch
        // Teal
        (0, true, HeritageSho, 1055033, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageSho, 1055033, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageSho, 1055043, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageSho, 1055043, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageSho, 1055053, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageSho, 1055053, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageSho, 1055053, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageSho, 1055053, 0, 0.0, -1), // Large Component Pouch
        // Purple
        (0, true, HeritageSho, 1055035, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageSho, 1055035, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageSho, 1055045, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageSho, 1055045, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageSho, 1055055, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageSho, 1055055, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageSho, 1055055, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageSho, 1055055, 0, 0.0, -1), // Large Component Pouch
        // White
        (0, true, HeritageSho, 1055037, 0, 0.0, -1), // Small Component Pouch
        (1, true, HeritageSho, 1055037, 0, 0.0, -1), // Small Component Pouch
        (2, true, HeritageSho, 1055047, 0, 0.0, -1), // Component Pouch
        (3, true, HeritageSho, 1055047, 0, 0.0, -1), // Component Pouch
        (4, true, HeritageSho, 1055057, 0, 0.0, -1), // Large Component Pouch
        (5, true, HeritageSho, 1055057, 0, 0.0, -1), // Large Component Pouch
        (6, true, HeritageSho, 1055057, 0, 0.0, -1), // Large Component Pouch
        (7, true, HeritageSho, 1055057, 0, 0.0, -1), // Large Component Pouch
        // Other
        (0, false, HeritageAny, 1054000, 0, 0.0, -1), // Pearl of Spell Transference
        (0, false, HeritageAny, 1054005, 0, 0.0, -1), // Pearl of Spell Purging
        (0, false, HeritageAny, 1053979, 0, 0.0, -1), // Bezel (spellcrafting)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> ArmorerItems =
    [
        (0, false, HeritageAny, 1054004, 0, 0.0, -1), // Upgrade Kit
        (0, false, HeritageAny, 1053976, 0, 0.0, -1), // Bezel (blacksmithing)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> BarkeeperItems = [];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> BlacksmithItems =
    [
        (0, false, HeritageAny, 1054004, 0, 0.0, -1), // Upgrade Kit
        (0, false, HeritageAny, 1053976, 0, 0.0, -1), // Bezel (blacksmithing)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> BowyerItems =
    [
        (1, true, HeritageAny, 1052001, 0, 0.0, -1), // Slash
        (1, true, HeritageAny, 1052002, 0, 0.0, -1), // Pierce
        (1, true, HeritageAny, 1052003, 0, 0.0, -1), // Blunt
        (1, true, HeritageAny, 1052004, 0, 0.0, -1), // Acid
        (1, true, HeritageAny, 1052005, 0, 0.0, -1), // Fire
        (1, true, HeritageAny, 1052006, 0, 0.0, -1), // Frost
        (1, true, HeritageAny, 1052007, 0, 0.0, -1), // Lightning
        (2, true, HeritageAny, 1052008, 0, 0.0, -1), // Slash
        (2, true, HeritageAny, 1052009, 0, 0.0, -1), // Pierce
        (2, true, HeritageAny, 1052010, 0, 0.0, -1), // Blunt
        (2, true, HeritageAny, 1052011, 0, 0.0, -1), // Acid
        (2, true, HeritageAny, 1052012, 0, 0.0, -1), // Fire
        (2, true, HeritageAny, 1052013, 0, 0.0, -1), // Frost
        (2, true, HeritageAny, 1052014, 0, 0.0, -1), // Lightning
        (3, true, HeritageAny, 1052015, 0, 0.0, -1), // Slash
        (3, true, HeritageAny, 1052016, 0, 0.0, -1), // Pierce
        (3, true, HeritageAny, 1052017, 0, 0.0, -1), // Blunt
        (3, true, HeritageAny, 1052018, 0, 0.0, -1), // Acid
        (3, true, HeritageAny, 1052019, 0, 0.0, -1), // Fire
        (3, true, HeritageAny, 1052020, 0, 0.0, -1), // Frost
        (3, true, HeritageAny, 1052021, 0, 0.0, -1), // Lightning
        (4, true, HeritageAny, 1052022, 0, 0.0, -1), // Slash
        (4, true, HeritageAny, 1052023, 0, 0.0, -1), // Pierce
        (4, true, HeritageAny, 1052024, 0, 0.0, -1), // Blunt
        (4, true, HeritageAny, 1052025, 0, 0.0, -1), // Acid
        (4, true, HeritageAny, 1052026, 0, 0.0, -1), // Fire
        (4, true, HeritageAny, 1052027, 0, 0.0, -1), // Frost
        (4, true, HeritageAny, 1052028, 0, 0.0, -1), // Lightning
        (5, true, HeritageAny, 1052029, 0, 0.0, -1), // Slash
        (5, true, HeritageAny, 1052030, 0, 0.0, -1), // Pierce
        (5, true, HeritageAny, 1052031, 0, 0.0, -1), // Blunt
        (5, true, HeritageAny, 1052032, 0, 0.0, -1), // Acid
        (5, true, HeritageAny, 1052033, 0, 0.0, -1), // Fire
        (5, true, HeritageAny, 1052034, 0, 0.0, -1), // Frost
        (5, true, HeritageAny, 1052035, 0, 0.0, -1), // Lightning
        (6, true, HeritageAny, 1052036, 0, 0.0, -1), // Slash
        (6, true, HeritageAny, 1052037, 0, 0.0, -1), // Pierce
        (6, true, HeritageAny, 1052038, 0, 0.0, -1), // Blunt
        (6, true, HeritageAny, 1052039, 0, 0.0, -1), // Acid
        (6, true, HeritageAny, 1052040, 0, 0.0, -1), // Fire
        (6, true, HeritageAny, 1052041, 0, 0.0, -1), // Frost
        (6, true, HeritageAny, 1052042, 0, 0.0, -1), // Lightning
        (7, true, HeritageAny, 1052043, 0, 0.0, -1), // Slash
        (7, true, HeritageAny, 1052044, 0, 0.0, -1), // Pierce
        (7, true, HeritageAny, 1052045, 0, 0.0, -1), // Blunt
        (7, true, HeritageAny, 1052046, 0, 0.0, -1), // Acid
        (7, true, HeritageAny, 1052047, 0, 0.0, -1), // Fire
        (7, true, HeritageAny, 1052048, 0, 0.0, -1), // Frost
        (7, true, HeritageAny, 1052049, 0, 0.0, -1), // Lightning
        // Quarrels
        (1, true, HeritageAny, 1052051, 0, 0.0, -1), // Slash
        (1, true, HeritageAny, 1052052, 0, 0.0, -1), // Pierce
        (1, true, HeritageAny, 1052053, 0, 0.0, -1), // Blunt
        (1, true, HeritageAny, 1052054, 0, 0.0, -1), // Acid
        (1, true, HeritageAny, 1052055, 0, 0.0, -1), // Fire
        (1, true, HeritageAny, 1052056, 0, 0.0, -1), // Frost
        (1, true, HeritageAny, 1052057, 0, 0.0, -1), // Lightning
        (2, true, HeritageAny, 1052058, 0, 0.0, -1), // Slash
        (2, true, HeritageAny, 1052059, 0, 0.0, -1), // Pierce
        (2, true, HeritageAny, 1052060, 0, 0.0, -1), // Blunt
        (2, true, HeritageAny, 1052061, 0, 0.0, -1), // Acid
        (2, true, HeritageAny, 1052062, 0, 0.0, -1), // Fire
        (2, true, HeritageAny, 1052063, 0, 0.0, -1), // Frost
        (2, true, HeritageAny, 1052064, 0, 0.0, -1), // Lightning
        (3, true, HeritageAny, 1052065, 0, 0.0, -1), // Slash
        (3, true, HeritageAny, 1052066, 0, 0.0, -1), // Pierce
        (3, true, HeritageAny, 1052067, 0, 0.0, -1), // Blunt
        (3, true, HeritageAny, 1052068, 0, 0.0, -1), // Acid
        (3, true, HeritageAny, 1052069, 0, 0.0, -1), // Fire
        (3, true, HeritageAny, 1052070, 0, 0.0, -1), // Frost
        (3, true, HeritageAny, 1052071, 0, 0.0, -1), // Lightning
        (4, true, HeritageAny, 1052072, 0, 0.0, -1), // Slash
        (4, true, HeritageAny, 1052073, 0, 0.0, -1), // Pierce
        (4, true, HeritageAny, 1052074, 0, 0.0, -1), // Blunt
        (4, true, HeritageAny, 1052075, 0, 0.0, -1), // Acid
        (4, true, HeritageAny, 1052076, 0, 0.0, -1), // Fire
        (4, true, HeritageAny, 1052077, 0, 0.0, -1), // Frost
        (4, true, HeritageAny, 1052078, 0, 0.0, -1), // Lightning
        (5, true, HeritageAny, 1052079, 0, 0.0, -1), // Slash
        (5, true, HeritageAny, 1052080, 0, 0.0, -1), // Pierce
        (5, true, HeritageAny, 1052081, 0, 0.0, -1), // Blunt
        (5, true, HeritageAny, 1052082, 0, 0.0, -1), // Acid
        (5, true, HeritageAny, 1052083, 0, 0.0, -1), // Fire
        (5, true, HeritageAny, 1052084, 0, 0.0, -1), // Frost
        (5, true, HeritageAny, 1052085, 0, 0.0, -1), // Lightning
        (6, true, HeritageAny, 1052086, 0, 0.0, -1), // Slash
        (6, true, HeritageAny, 1052087, 0, 0.0, -1), // Pierce
        (6, true, HeritageAny, 1052088, 0, 0.0, -1), // Blunt
        (6, true, HeritageAny, 1052089, 0, 0.0, -1), // Acid
        (6, true, HeritageAny, 1052090, 0, 0.0, -1), // Fire
        (6, true, HeritageAny, 1052091, 0, 0.0, -1), // Frost
        (6, true, HeritageAny, 1052092, 0, 0.0, -1), // Lightning
        (7, true, HeritageAny, 1052093, 0, 0.0, -1), // Slash
        (7, true, HeritageAny, 1052094, 0, 0.0, -1), // Pierce
        (7, true, HeritageAny, 1052095, 0, 0.0, -1), // Blunt
        (7, true, HeritageAny, 1052096, 0, 0.0, -1), // Acid
        (7, true, HeritageAny, 1052097, 0, 0.0, -1), // Fire
        (7, true, HeritageAny, 1052098, 0, 0.0, -1), // Frost
        (7, true, HeritageAny, 1052099, 0, 0.0, -1), // Lightning
        // Atlatl Darts
        (1, true, HeritageAny, 1052101, 0, 0.0, -1), // Slash
        (1, true, HeritageAny, 1052102, 0, 0.0, -1), // Pierce
        (1, true, HeritageAny, 1052103, 0, 0.0, -1), // Blunt
        (1, true, HeritageAny, 1052104, 0, 0.0, -1), // Acid
        (1, true, HeritageAny, 1052105, 0, 0.0, -1), // Fire
        (1, true, HeritageAny, 1052106, 0, 0.0, -1), // Frost
        (1, true, HeritageAny, 1052107, 0, 0.0, -1), // Lightning
        (2, true, HeritageAny, 1052108, 0, 0.0, -1), // Slash
        (2, true, HeritageAny, 1052109, 0, 0.0, -1), // Pierce
        (2, true, HeritageAny, 1052110, 0, 0.0, -1), // Blunt
        (2, true, HeritageAny, 1052111, 0, 0.0, -1), // Acid
        (2, true, HeritageAny, 1052112, 0, 0.0, -1), // Fire
        (2, true, HeritageAny, 1052113, 0, 0.0, -1), // Frost
        (2, true, HeritageAny, 1052114, 0, 0.0, -1), // Lightning
        (3, true, HeritageAny, 1052115, 0, 0.0, -1), // Slash
        (3, true, HeritageAny, 1052116, 0, 0.0, -1), // Pierce
        (3, true, HeritageAny, 1052117, 0, 0.0, -1), // Blunt
        (3, true, HeritageAny, 1052118, 0, 0.0, -1), // Acid
        (3, true, HeritageAny, 1052119, 0, 0.0, -1), // Fire
        (3, true, HeritageAny, 1052120, 0, 0.0, -1), // Frost
        (3, true, HeritageAny, 1052121, 0, 0.0, -1), // Lightning
        (4, true, HeritageAny, 1052122, 0, 0.0, -1), // Slash
        (4, true, HeritageAny, 1052123, 0, 0.0, -1), // Pierce
        (4, true, HeritageAny, 1052124, 0, 0.0, -1), // Blunt
        (4, true, HeritageAny, 1052125, 0, 0.0, -1), // Acid
        (4, true, HeritageAny, 1052126, 0, 0.0, -1), // Fire
        (4, true, HeritageAny, 1052127, 0, 0.0, -1), // Frost
        (4, true, HeritageAny, 1052128, 0, 0.0, -1), // Lightning
        (5, true, HeritageAny, 1052129, 0, 0.0, -1), // Slash
        (5, true, HeritageAny, 1052130, 0, 0.0, -1), // Pierce
        (5, true, HeritageAny, 1052131, 0, 0.0, -1), // Blunt
        (5, true, HeritageAny, 1052132, 0, 0.0, -1), // Acid
        (5, true, HeritageAny, 1052133, 0, 0.0, -1), // Fire
        (5, true, HeritageAny, 1052134, 0, 0.0, -1), // Frost
        (5, true, HeritageAny, 1052135, 0, 0.0, -1), // Lightning
        (6, true, HeritageAny, 1052136, 0, 0.0, -1), // Slash
        (6, true, HeritageAny, 1052137, 0, 0.0, -1), // Pierce
        (6, true, HeritageAny, 1052138, 0, 0.0, -1), // Blunt
        (6, true, HeritageAny, 1052139, 0, 0.0, -1), // Acid
        (6, true, HeritageAny, 1052140, 0, 0.0, -1), // Fire
        (6, true, HeritageAny, 1052141, 0, 0.0, -1), // Frost
        (6, true, HeritageAny, 1052142, 0, 0.0, -1), // Lightning
        (7, true, HeritageAny, 1052143, 0, 0.0, -1), // Slash
        (7, true, HeritageAny, 1052144, 0, 0.0, -1), // Pierce
        (7, true, HeritageAny, 1052145, 0, 0.0, -1), // Blunt
        (7, true, HeritageAny, 1052146, 0, 0.0, -1), // Acid
        (7, true, HeritageAny, 1052147, 0, 0.0, -1), // Fire
        (7, true, HeritageAny, 1052148, 0, 0.0, -1), // Frost
        (7, true, HeritageAny, 1052149, 0, 0.0, -1), // Lightning
        // Arrowheads
        (1, true, HeritageAny, 1052151, 0, 0.0, -1), // Slash
        (1, true, HeritageAny, 1052152, 0, 0.0, -1), // Pierce
        (1, true, HeritageAny, 1052153, 0, 0.0, -1), // Blunt
        (1, true, HeritageAny, 1052154, 0, 0.0, -1), // Acid
        (1, true, HeritageAny, 1052155, 0, 0.0, -1), // Fire
        (1, true, HeritageAny, 1052156, 0, 0.0, -1), // Frost
        (1, true, HeritageAny, 1052157, 0, 0.0, -1), // Lightning
        (2, true, HeritageAny, 1052158, 0, 0.0, -1), // Slash
        (2, true, HeritageAny, 1052159, 0, 0.0, -1), // Pierce
        (2, true, HeritageAny, 1052160, 0, 0.0, -1), // Blunt
        (2, true, HeritageAny, 1052161, 0, 0.0, -1), // Acid
        (2, true, HeritageAny, 1052162, 0, 0.0, -1), // Fire
        (2, true, HeritageAny, 1052163, 0, 0.0, -1), // Frost
        (2, true, HeritageAny, 1052164, 0, 0.0, -1), // Lightning
        (3, true, HeritageAny, 1052165, 0, 0.0, -1), // Slash
        (3, true, HeritageAny, 1052166, 0, 0.0, -1), // Pierce
        (3, true, HeritageAny, 1052167, 0, 0.0, -1), // Blunt
        (3, true, HeritageAny, 1052168, 0, 0.0, -1), // Acid
        (3, true, HeritageAny, 1052169, 0, 0.0, -1), // Fire
        (3, true, HeritageAny, 1052170, 0, 0.0, -1), // Frost
        (3, true, HeritageAny, 1052171, 0, 0.0, -1), // Lightning
        (4, true, HeritageAny, 1052172, 0, 0.0, -1), // Slash
        (4, true, HeritageAny, 1052173, 0, 0.0, -1), // Pierce
        (4, true, HeritageAny, 1052174, 0, 0.0, -1), // Blunt
        (4, true, HeritageAny, 1052175, 0, 0.0, -1), // Acid
        (4, true, HeritageAny, 1052176, 0, 0.0, -1), // Fire
        (4, true, HeritageAny, 1052177, 0, 0.0, -1), // Frost
        (4, true, HeritageAny, 1052178, 0, 0.0, -1), // Lightning
        (5, true, HeritageAny, 1052179, 0, 0.0, -1), // Slash
        (5, true, HeritageAny, 1052180, 0, 0.0, -1), // Pierce
        (5, true, HeritageAny, 1052181, 0, 0.0, -1), // Blunt
        (5, true, HeritageAny, 1052182, 0, 0.0, -1), // Acid
        (5, true, HeritageAny, 1052183, 0, 0.0, -1), // Fire
        (5, true, HeritageAny, 1052184, 0, 0.0, -1), // Frost
        (5, true, HeritageAny, 1052185, 0, 0.0, -1), // Lightning
        (6, true, HeritageAny, 1052186, 0, 0.0, -1), // Slash
        (6, true, HeritageAny, 1052187, 0, 0.0, -1), // Pierce
        (6, true, HeritageAny, 1052188, 0, 0.0, -1), // Blunt
        (6, true, HeritageAny, 1052189, 0, 0.0, -1), // Acid
        (6, true, HeritageAny, 1052190, 0, 0.0, -1), // Fire
        (6, true, HeritageAny, 1052191, 0, 0.0, -1), // Frost
        (6, true, HeritageAny, 1052192, 0, 0.0, -1), // Lightning
        (7, true, HeritageAny, 1052193, 0, 0.0, -1), // Slash
        (7, true, HeritageAny, 1052194, 0, 0.0, -1), // Pierce
        (7, true, HeritageAny, 1052195, 0, 0.0, -1), // Blunt
        (7, true, HeritageAny, 1052196, 0, 0.0, -1), // Acid
        (7, true, HeritageAny, 1052197, 0, 0.0, -1), // Fire
        (7, true, HeritageAny, 1052198, 0, 0.0, -1), // Frost
        (7, true, HeritageAny, 1052199, 0, 0.0, -1), // Lightning
        // Fletching Supplies
        (0, false, HeritageAny, 9377, 0, 0.0, -1), // Wrapped Bundle of Arrowshafts
        (0, false, HeritageAny, 9378, 0, 0.0, -1), // Wrapped Bundle of Quarrelshafts
        (0, false, HeritageAny, 15298, 0, 0.0, -1), // Wrapped Bundle of Atlatl Dartshafts
        (0, false, HeritageAny, 4757, 0, 0.0, -1), // Carving Knife
        (0, false, HeritageAny, 9295, 0, 0.0, -1), // Intricate Carving Tool
        // Brown
        (0, false, HeritageAny, 1055000, 0, 0.0, -1), // small quiver
        (1, false, HeritageAny, 1055000, 0, 0.0, -1), // small quiver
        (2, false, HeritageAny, 1055010, 0, 0.0, -1), // quiver
        (3, false, HeritageAny, 1055010, 0, 0.0, -1), // quiver
        (4, false, HeritageAny, 1055020, 0, 0.0, -1), // large quiver
        (5, false, HeritageAny, 1055020, 0, 0.0, -1), // large quiver
        (6, false, HeritageAny, 1055020, 0, 0.0, -1), // large quiver
        (7, false, HeritageAny, 1055020, 0, 0.0, -1), // large quiver
        // Green
        (0, false, HeritageAluvian, 1055002, 0, 0.0, -1), // small quiver
        (1, false, HeritageAluvian, 1055002, 0, 0.0, -1), // small quiver
        (2, false, HeritageAluvian, 1055012, 0, 0.0, -1), // quiver
        (3, false, HeritageAluvian, 1055012, 0, 0.0, -1), // quiver
        (4, false, HeritageAluvian, 1055022, 0, 0.0, -1), // large quiver
        (5, false, HeritageAluvian, 1055022, 0, 0.0, -1), // large quiver
        (6, false, HeritageAluvian, 1055022, 0, 0.0, -1), // large quiver
        (7, false, HeritageAluvian, 1055022, 0, 0.0, -1), // large quiver
        // Blue
        (0, false, HeritageAluvian, 1055004, 0, 0.0, -1), // small quiver
        (1, false, HeritageAluvian, 1055004, 0, 0.0, -1), // small quiver
        (2, false, HeritageAluvian, 1055014, 0, 0.0, -1), // quiver
        (3, false, HeritageAluvian, 1055014, 0, 0.0, -1), // quiver
        (4, false, HeritageAluvian, 1055024, 0, 0.0, -1), // large quiver
        (5, false, HeritageAluvian, 1055024, 0, 0.0, -1), // large quiver
        (6, false, HeritageAluvian, 1055024, 0, 0.0, -1), // large quiver
        (7, false, HeritageAluvian, 1055024, 0, 0.0, -1), // large quiver
        // Black
        (0, false, HeritageAluvian, 1055009, 0, 0.0, -1), // small quiver
        (1, false, HeritageAluvian, 1055009, 0, 0.0, -1), // small quiver
        (2, false, HeritageAluvian, 1055019, 0, 0.0, -1), // quiver
        (3, false, HeritageAluvian, 1055019, 0, 0.0, -1), // quiver
        (4, false, HeritageAluvian, 1055029, 0, 0.0, -1), // large quiver
        (5, false, HeritageAluvian, 1055029, 0, 0.0, -1), // large quiver
        (6, false, HeritageAluvian, 1055029, 0, 0.0, -1), // large quiver
        (7, false, HeritageAluvian, 1055029, 0, 0.0, -1), // large quiver
        // Yellow
        (0, false, HeritageGharundim, 1055001, 0, 0.0, -1), // small quiver
        (1, false, HeritageGharundim, 1055001, 0, 0.0, -1), // small quiver
        (2, false, HeritageGharundim, 1055011, 0, 0.0, -1), // quiver
        (4, false, HeritageGharundim, 1055011, 0, 0.0, -1), // quiver
        (4, false, HeritageGharundim, 1055021, 0, 0.0, -1), // large quiver
        (5, false, HeritageGharundim, 1055021, 0, 0.0, -1), // large quiver
        (6, false, HeritageGharundim, 1055021, 0, 0.0, -1), // large quiver
        (7, false, HeritageGharundim, 1055021, 0, 0.0, -1), // large quiver
        // Red
        (0, false, HeritageGharundim, 1055006, 0, 0.0, -1), // small quiver
        (1, false, HeritageGharundim, 1055006, 0, 0.0, -1), // small quiver
        (2, false, HeritageGharundim, 1055016, 0, 0.0, -1), // quiver
        (3, false, HeritageGharundim, 1055016, 0, 0.0, -1), // quiver
        (4, false, HeritageGharundim, 1055026, 0, 0.0, -1), // large quiver
        (5, false, HeritageGharundim, 1055026, 0, 0.0, -1), // large quiver
        (6, false, HeritageGharundim, 1055026, 0, 0.0, -1), // large quiver
        (7, false, HeritageGharundim, 1055026, 0, 0.0, -1), // large quiver
        // Gray
        (0, false, HeritageGharundim, 1055008, 0, 0.0, -1), // small quiver
        (1, false, HeritageGharundim, 1055008, 0, 0.0, -1), // small quiver
        (2, false, HeritageGharundim, 1055018, 0, 0.0, -1), // quiver
        (3, false, HeritageGharundim, 1055018, 0, 0.0, -1), // quiver
        (4, false, HeritageGharundim, 1055028, 0, 0.0, -1), // large quiver
        (5, false, HeritageGharundim, 1055028, 0, 0.0, -1), // large quiver
        (6, false, HeritageGharundim, 1055028, 0, 0.0, -1), // large quiver
        (7, false, HeritageGharundim, 1055028, 0, 0.0, -1), // large quiver
        // Teal
        (0, false, HeritageSho, 1055003, 0, 0.0, -1), // small quiver
        (1, false, HeritageSho, 1055003, 0, 0.0, -1), // small quiver
        (2, false, HeritageSho, 1055013, 0, 0.0, -1), // quiver
        (3, false, HeritageSho, 1055013, 0, 0.0, -1), // quiver
        (4, false, HeritageSho, 1055023, 0, 0.0, -1), // large quiver
        (5, false, HeritageSho, 1055023, 0, 0.0, -1), // large quiver
        (6, false, HeritageSho, 1055023, 0, 0.0, -1), // large quiver
        (7, false, HeritageSho, 1055023, 0, 0.0, -1), // large quiver
        // Purple
        (0, false, HeritageSho, 1055005, 0, 0.0, -1), // small quiver
        (1, false, HeritageSho, 1055005, 0, 0.0, -1), // small quiver
        (2, false, HeritageSho, 1055015, 0, 0.0, -1), // quiver
        (3, false, HeritageSho, 1055015, 0, 0.0, -1), // quiver
        (4, false, HeritageSho, 1055025, 0, 0.0, -1), // large quiver
        (5, false, HeritageSho, 1055025, 0, 0.0, -1), // large quiver
        (6, false, HeritageSho, 1055025, 0, 0.0, -1), // large quiver
        (7, false, HeritageSho, 1055025, 0, 0.0, -1), // large quiver
        // White
        (0, false, HeritageSho, 1055007, 0, 0.0, -1), // small quiver
        (1, false, HeritageSho, 1055007, 0, 0.0, -1), // small quiver
        (2, false, HeritageSho, 1055017, 0, 0.0, -1), // quiver
        (3, false, HeritageSho, 1055017, 0, 0.0, -1), // quiver
        (4, false, HeritageSho, 1055027, 0, 0.0, -1), // large quiver
        (5, false, HeritageSho, 1055027, 0, 0.0, -1), // large quiver
        (6, false, HeritageSho, 1055027, 0, 0.0, -1), // large quiver
        (7, false, HeritageSho, 1055027, 0, 0.0, -1), // large quiver
        //other
        (0, false, HeritageAny, 1053978, 0, 0.0, -1) // Bezel (woodworking)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> ButcherItems = [];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> GrocerItems = [];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> HealerItems =
    [
        (0, false, HeritageAny, 1053958, 0, 0.0, -1), // health draught
        (2, false, HeritageAny, 1053962, 0, 0.0, -1), // health potion
        (3, false, HeritageAny, 1053966, 0, 0.0, -1), // health tonic
        (4, false, HeritageAny, 1053970, 0, 0.0, -1), // health tincture
        //{ (5, ??, 0, 0.0, -1) },     // health elixir?
        (0, false, HeritageAny, 1053959, 0, 0.0, -1), // stamina draught
        (2, false, HeritageAny, 1053963, 0, 0.0, -1), // stamina potion
        (3, false, HeritageAny, 1053967, 0, 0.0, -1), // stamina tonic
        (4, false, HeritageAny, 1053971, 0, 0.0, -1), // stamina tincture
        //{ (5, ??, 0, 0.0, -1) },     // stamina elixir?
        (0, false, HeritageAny, 1053957, 0, 0.0, -1), // mana draught
        (2, false, HeritageAny, 1053961, 0, 0.0, -1), // mana potion
        (3, false, HeritageAny, 1053965, 0, 0.0, -1), // mana tonic
        (4, false, HeritageAny, 1053969, 0, 0.0, -1), // mana tincture
        //{ (5, ??, 0, 0.0, -1) },     // mana elixir?
        (0, false, HeritageAny, 628, 0, 0.0, -1), // Crude Healing Kit
        (2, false, HeritageAny, 629, 0, 0.0, -1), // Plain Healing Kit
        (3, false, HeritageAny, 630, 0, 0.0, -1), // Good Healing Kit
        (4, false, HeritageAny, 631, 0, 0.0, -1), // Excellent Healing Kit
        (5, false, HeritageAny, 632, 0, 0.0, -1), // Peerless Healing Kit
        (6, false, HeritageAny, 9229, 0, 0.0, -1), // Treated Healing Kit
        (0, false, HeritageAny, 1054510, 0, 0.0, -1), // Crude Stamina Kit
        (2, false, HeritageAny, 1054511, 0, 0.0, -1), // Plain Stamina Kit
        (3, false, HeritageAny, 1054512, 0, 0.0, -1), // Good Stamina Kit
        (4, false, HeritageAny, 1054513, 0, 0.0, -1), // Excellent Stamina Kit
        (5, false, HeritageAny, 1054514, 0, 0.0, -1), // Peerless Stamina Kit
        (6, false, HeritageAny, 1054515, 0, 0.0, -1), // Treated Stamina Kit
        (0, false, HeritageAny, 1054520, 0, 0.0, -1), // Crude Mana Kit
        (2, false, HeritageAny, 1054521, 0, 0.0, -1), // Plain Mana Kit
        (3, false, HeritageAny, 1054522, 0, 0.0, -1), // Good Mana Kit
        (4, false, HeritageAny, 1054523, 0, 0.0, -1), // Excellent Mana Kit
        (5, false, HeritageAny, 1054524, 0, 0.0, -1), // Peerless Mana Kit
        (6, false, HeritageAny, 1054525, 0, 0.0, -1) // Treated Mana Kit
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> IvoryTraderItems = [];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> JewelerItems =
    [
        (0, false, HeritageAny, 1053980, 0, 0.0, -1), // Bezel (jewelcrafting)
        (0, false, HeritageAny, 1053975, 0, 0.0, -1), // Bezel Fragment
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> LeatherTraderItems = [];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> ProvisionerItems =
    [
        (0, false, HeritageAny, 1055060, 0, 0.0, -1), // Salvage Sacks
        (2, false, HeritageAny, 1055070, 0, 0.0, -1),
        (4, false, HeritageAny, 1055080, 0, 0.0, -1)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> ScribeItems = [];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> ShopkeeperItems =
    [
        (0, false, HeritageAny, 136, 21, 0.0, -1), // brown - pack
        (0, false, HeritageAny, 166, 21, 0.0, -1), // brown - sack
        (0, false, HeritageAny, 138, 4, 0.0, -1), // brown - belt pouch
        (0, false, HeritageAny, 139, 4, 0.0, -1), // brown - small belt pouch
        (0, false, HeritageAluvian, 136, 8, 0.0, -1), // green - pack
        (0, false, HeritageAluvian, 166, 8, 0.0, -1), // green - sack
        (0, false, HeritageAluvian, 138, 84, 0.0, -1), // green - belt pouch
        (0, false, HeritageAluvian, 139, 84, 0.0, -1), // green - small belt pouch
        (0, false, HeritageAluvian, 136, 2, 0.0, -1), // blue - pack
        (0, false, HeritageAluvian, 166, 2, 0.0, -1), // blue - sack
        (0, false, HeritageAluvian, 138, 91, 0.0, -1), // blue - belt pouch
        (0, false, HeritageAluvian, 139, 91, 0.0, -1), // blue - small belt pouch
        (0, false, HeritageAluvian, 136, 39, 0.0, -1), // black - pack
        (0, false, HeritageAluvian, 166, 39, 0.0, -1), // black - sack
        (0, false, HeritageAluvian, 138, 93, 0.0, -1), // black - belt pouch
        (0, false, HeritageAluvian, 139, 93, 0.0, -1), // black - small belt pouch
        (0, false, HeritageGharundim, 136, 17, 0.0, -1), // yellow - pack
        (0, false, HeritageGharundim, 166, 17, 0.0, -1), // yellow - sack
        (0, false, HeritageGharundim, 138, 86, 0.0, -1), // yellow - belt pouch
        (0, false, HeritageGharundim, 139, 86, 0.0, -1), // yellow - small belt pouch
        (0, false, HeritageGharundim, 136, 14, 0.0, -1), // red - pack
        (0, false, HeritageGharundim, 166, 14, 0.0, -1), // red - sack
        (0, false, HeritageGharundim, 138, 85, 0.0, -1), // red - belt pouch
        (0, false, HeritageGharundim, 139, 85, 0.0, -1), // red - small belt pouch
        (0, false, HeritageGharundim, 136, 9, 0.0, -1), // gray - pack
        (0, false, HeritageGharundim, 166, 9, 0.0, -1), // gray - sack
        (0, false, HeritageGharundim, 138, 90, 0.0, -1), // gray - belt pouch
        (0, false, HeritageGharundim, 139, 90, 0.0, -1), // gray - small belt pouch
        (0, false, HeritageSho, 136, 77, 0.0, -1), // teal - pack
        (0, false, HeritageSho, 166, 77, 0.0, -1), // teal - sack
        (0, false, HeritageSho, 138, 88, 0.0, -1), // teal - belt pouch
        (0, false, HeritageSho, 139, 88, 0.0, -1), // teal - small belt pouch
        (0, false, HeritageSho, 136, 13, 0.0, -1), // purple - pack
        (0, false, HeritageSho, 166, 13, 0.0, -1), // purple - sack
        (0, false, HeritageSho, 138, 92, 0.0, -1), // purple - belt pouch
        (0, false, HeritageSho, 139, 92, 0.0, -1), // purple - small belt pouch
        (0, false, HeritageSho, 136, 61, 0.0, -1), // white - pack
        (0, false, HeritageSho, 166, 61, 0.0, -1), // white - sack
        (0, false, HeritageSho, 138, 90, 0.0, -1), // white - belt pouch
        (0, false, HeritageSho, 139, 90, 0.0, -1), // white - small belt pouch
        // Salvage Sacks
        (0, false, HeritageAny, 1055060, 0, 0.0, -1),
        (1, false, HeritageAny, 1055060, 0, 0.0, -1),
        (2, false, HeritageAny, 1055070, 0, 0.0, -1),
        (3, false, HeritageAny, 1055070, 0, 0.0, -1),
        (4, false, HeritageAny, 1055080, 0, 0.0, -1),
        (5, false, HeritageAny, 1055080, 0, 0.0, -1),
        (6, false, HeritageAny, 1055080, 0, 0.0, -1),
        (7, false, HeritageAny, 1055080, 0, 0.0, -1)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> TailorItems =
    [
        (0, false, HeritageAny, 1054004, 0, 0.0, -1), // Upgrade Kit
        (0, false, HeritageAny, 1053977, 0, 0.0, -1) // Bezel (tailoring)
    ];

    // <(tier, onlyThisTier, heritage, wcid, paletteTemplate, shade, stackSize)>
    public static readonly List<(int, bool, int, uint, int, double, int)> WeaponsmithItems =
    [
        (0, false, HeritageAny, 1054004, 0, 0.0, -1), // Upgrade Kit
        (0, false, HeritageAny, 1053976, 0, 0.0, -1) // Bezel (blacksmithing)
    ];
}
