using System.Collections.Generic;
using System.Linq;

namespace ACE.Entity.Enum;

/// <summary>
/// note: even though these are unnumbered, order is very important.  values of "none" or commented
/// as retired or unused --ABSOLUTELY CANNOT-- be removed. Skills that are none, retired, or not
/// implemented have been removed from the SkillHelper.ValidSkills hashset below.
/// </summary>
public enum Skill
{
    None,
    Axe, /* Retired */
    Bow, /* Retired */
    Crossbow, /* Retired */
    Dagger, /* Retired */
    Mace, /* Retired */
    PhysicalDefense,
    MissileDefense,
    Sling, /* Retired */
    Spear, /* Retired */
    Staff, /* Retired */
    Sword, /* Retired */
    ThrownWeapon, /* Retired */
    UnarmedCombat, /* Retired */
    ArcaneLore,
    MagicDefense,
    ManaConversion,
    Spellcraft, /* Unimplemented */
    Jewelcrafting,
    AssessPerson,
    Deception,
    Healing,
    Jump,
    Thievery,
    Run,
    Awareness, /* Unimplemented */
    ArmsAndArmorRepair, /* Unimplemented */
    Perception,
    Blacksmithing,
    Tailoring,
    Spellcrafting,
    CreatureEnchantment,
    PortalMagic,
    LifeMagic,
    WarMagic,
    Leadership,
    Loyalty,
    Woodworking,
    Alchemy,
    Cooking,
    Salvaging,
    TwoHandedCombat,
    Gearcraft, /* Retired */
    VoidMagic,
    MartialWeapons,
    LightWeapons,
    FinesseWeapons,
    MissileWeapons,
    Shield,
    DualWield,
    Recklessness,
    SneakAttack,
    DirtyFighting,
    Challenge, /* Unimplemented */
    Summoning
}

public enum NewSkillNames
{
    None,
    Axe, /* Retired */
    Bow, /* Retired */
    Crossbow, /* Retired */
    Dagger, /* Retired */
    Mace, /* Retired */
    PhysicalDefense,
    MissileDefense,
    Sling, /* Retired */
    Spear, /* Retired */
    Staff, /* Retired */
    Sword, /* Retired */
    ThrownWeapon, /* Retired */
    UnarmedCombat, /* Retired */
    ArcaneLore,
    MagicDefense,
    ManaConversion,
    Spellcraft, /* Unimplemented */
    Jewelcrafting,
    AssessPerson,
    Deception,
    Healing,
    Jump,
    Thievery,
    Run,
    Awareness, /* Unimplemented */
    ArmsAndArmorRepair, /* Unimplemented */
    Perception,
    Blacksmithing,
    Tailoring,
    Spellcrafting,
    CreatureEnchantment,
    PortalMagic,
    LifeMagic,
    WarMagic,
    Leadership,
    Loyalty,
    Woodworking,
    Alchemy,
    Cooking,
    Salvaging,
    TwoHandedCombat,
    Gearcraft, /* Retired */
    VoidMagic,
    MartialWeapons,
    LightWeapons,
    FinesseWeapons,
    MissileWeapons,
    Shield,
    DualWield,
    Recklessness,
    SneakAttack,
    DirtyFighting,
    Challenge, /* Unimplemented */
    Summoning
}

public static class SkillExtensions
{
    public static List<Skill> RetiredMelee = new List<Skill>()
    {
        Skill.Axe,
        Skill.Dagger,
        Skill.Mace,
        Skill.Spear,
        Skill.Staff,
        Skill.Sword,
        Skill.UnarmedCombat
    };

    public static List<Skill> RetiredMissile = new List<Skill>()
    {
        Skill.Bow,
        Skill.Crossbow,
        Skill.Sling,
        Skill.ThrownWeapon
    };

    public static List<Skill> RetiredWeapons = RetiredMelee.Concat(RetiredMissile).ToList();

    /// <summary>
    /// Will add a space infront of capital letter words in a string
    /// </summary>
    /// <param name="skill"></param>
    /// <returns>string with spaces infront of capital letters</returns>
    public static string ToSentence(this Skill skill)
    {
        switch (skill)
        {
            case Skill.None:
                return "None";
            case Skill.Axe:
                return "Axe";
            case Skill.Bow:
                return "Bow";
            case Skill.Crossbow:
                return "Crossbow";
            case Skill.Dagger:
                return "Dagger";
            case Skill.Mace:
                return "Mace";
            case Skill.PhysicalDefense:
                return "Melee Defense";
            case Skill.MissileDefense:
                return "Missile Defense";
            case Skill.Sling:
                return "Sling";
            case Skill.Spear:
                return "Spear";
            case Skill.Staff:
                return "Staff";
            case Skill.Sword:
                return "Sword";
            case Skill.ThrownWeapon:
                return "Thrown Weapon";
            case Skill.UnarmedCombat:
                return "Unarmed Combat";
            case Skill.ArcaneLore:
                return "Arcane Lore";
            case Skill.MagicDefense:
                return "Magic Defense";
            case Skill.ManaConversion:
                return "Mana Conversion";
            case Skill.Spellcraft:
                return "Spellcraft";
            case Skill.Jewelcrafting:
                return "Jewelcrafting";
            case Skill.AssessPerson:
                return "Assess Person";
            case Skill.Deception:
                return "Deception";
            case Skill.Healing:
                return "Healing";
            case Skill.Jump:
                return "Jump";
            case Skill.Thievery:
                return "Thievery";
            case Skill.Run:
                return "Run";
            case Skill.Awareness:
                return "Awareness";
            case Skill.ArmsAndArmorRepair:
                return "Arms And Armor Repair";
            case Skill.Perception:
                return "Perception";
            case Skill.Blacksmithing:
                return "Blacksmithing";
            case Skill.Tailoring:
                return "Tailoring";
            case Skill.Spellcrafting:
                return "Spellcrafting";
            case Skill.CreatureEnchantment:
                return "Creature Enchantment";
            case Skill.LifeMagic:
                return "Life Magic";
            case Skill.WarMagic:
                return "War Magic";
            case Skill.Leadership:
                return "Leadership";
            case Skill.Loyalty:
                return "Loyalty";
            case Skill.Woodworking:
                return "Woodworking";
            case Skill.Alchemy:
                return "Alchemy";
            case Skill.Cooking:
                return "Cooking";
            case Skill.Salvaging:
                return "Salvaging";
            case Skill.TwoHandedCombat:
                return "Two Handed Combat";
            case Skill.Gearcraft:
                return "Gearcraft";
            case Skill.VoidMagic:
                return "Void Magic";
            case Skill.MartialWeapons:
                return "Heavy Weapons";
            case Skill.LightWeapons:
                return "Light Weapons";
            case Skill.FinesseWeapons:
                return "Finesse Weapons";
            case Skill.MissileWeapons:
                return "Missile Weapons";
            case Skill.Shield:
                return "Shield";
            case Skill.DualWield:
                return "Dual Wield";
            case Skill.Recklessness:
                return "Recklessness";
            case Skill.SneakAttack:
                return "Sneak Attack";
            case Skill.DirtyFighting:
                return "Dirty Fighting";
            case Skill.Challenge:
                return "Challenge";
            case Skill.Summoning:
                return "Summoning";
        }

        // TODO we really should log this as a warning to indicate that we're missing a case up above, and that the inefficient (GC unfriendly) line below will be used
        return new string(
            skill
                .ToString()
                .ToCharArray()
                .SelectMany((c, i) => i > 0 && char.IsUpper(c) ? new char[] { ' ', c } : new char[] { c })
                .ToArray()
        );
    }

    /// <summary>
    /// Will add a space infront of capital letter words in a string
    /// </summary>
    /// <param name="skill"></param>
    /// <returns>string with spaces infront of capital letters</returns>
    public static string ToSentence(this NewSkillNames skill)
    {
        return new string(
            skill
                .ToString()
                .ToCharArray()
                .SelectMany((c, i) => i > 0 && char.IsUpper(c) ? new char[] { ' ', c } : new char[] { c })
                .ToArray()
        );
    }
}

public static class SkillHelper
{
    static SkillHelper() { }

    public static HashSet<Skill> ValidSkills = new HashSet<Skill>
    {
        Skill.MartialWeapons, // Martial Weapons
        Skill.Dagger,
        Skill.Staff,
        Skill.UnarmedCombat,
        Skill.Bow, // Bows (and crossbows)
        Skill.ThrownWeapon,
        Skill.TwoHandedCombat,
        Skill.DualWield,
        Skill.LifeMagic,
        Skill.WarMagic,
        Skill.PortalMagic,
        Skill.ManaConversion,
        Skill.ArcaneLore,
        Skill.PhysicalDefense, // Physical Defense
        Skill.MagicDefense,
        Skill.Shield,
        Skill.Healing,
        Skill.Perception, // Perception
        Skill.Deception,
        Skill.Thievery, // Thievery
        Skill.Jump,
        Skill.Run,
        Skill.Leadership,
        Skill.Loyalty,
        Skill.Woodworking, // Woodworking
        Skill.Alchemy,
        Skill.Cooking,
        Skill.Blacksmithing, // Blacksmithing
        Skill.Tailoring, // Tailoring
        Skill.Spellcrafting, // Spellcrafting
        Skill.Jewelcrafting, // Jewelcrafting

        //Skill.Axe,
        //Skill.Crossbow,
        //Skill.Mace,
        //Skill.Sword,
        //Skill.Sling,
        //Skill.Spear,
        //Skill.SneakAttack,
        //Skill.AssessPerson,
        //Skill.CreatureEnchantment,
        //Skill.Salvaging,
        //Skill.VoidMagic,
        //Skill.LightWeapons,
        //Skill.FinesseWeapons,
        //Skill.MissileWeapons,
        //Skill.Recklessness,
        //Skill.DirtyFighting,
        //Skill.Summoning
        //Skill.MissileDefense,
    };

    public static HashSet<Skill> AttackSkills = new HashSet<Skill>
    {
        Skill.MartialWeapons, // Martial Weapons
        Skill.Dagger,
        Skill.Staff,
        Skill.UnarmedCombat,
        Skill.Bow, // Bows (and crossbows)
        Skill.ThrownWeapon,
        Skill.TwoHandedCombat,
        Skill.DualWield,
        Skill.LifeMagic,
        Skill.WarMagic,
    };

    public static HashSet<Skill> DefenseSkills = new HashSet<Skill>()
    {
        Skill.PhysicalDefense,
        Skill.MissileDefense,
        Skill.MagicDefense,
        Skill.Shield // confirmed in client
    };
}
