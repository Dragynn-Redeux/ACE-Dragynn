using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ACE.Database;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.Enum;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Network.Structure;

/// <summary>
/// Handles calculating and sending all object appraisal info
/// </summary>
public class AppraiseInfo
{
    private const uint EnchantmentMask = 0x80000000;

    public IdentifyResponseFlags Flags;

    public bool Success; // assessment successful?

    public Dictionary<PropertyInt, int> PropertiesInt;
    public Dictionary<PropertyInt64, long> PropertiesInt64;
    public Dictionary<PropertyBool, bool> PropertiesBool;
    public Dictionary<PropertyFloat, double> PropertiesFloat;
    public Dictionary<PropertyString, string> PropertiesString;
    public Dictionary<PropertyDataId, uint> PropertiesDID;
    public Dictionary<PropertyInstanceId, uint> PropertiesIID;

    public List<uint> SpellBook;

    public ArmorProfile ArmorProfile;
    public CreatureProfile CreatureProfile;
    public WeaponProfile WeaponProfile;
    public HookProfile HookProfile;

    public ArmorMask ArmorHighlight;
    public ArmorMask ArmorColor;
    public WeaponMask WeaponHighlight;
    public WeaponMask WeaponColor;
    public ResistMask ResistHighlight;
    public ResistMask ResistColor;

    public ArmorLevel ArmorLevels;

    public bool IsArmorCapped = false;
    public bool IsArmorBuffed = false;
    public bool IsAttackModBuffed = false;

    // This helps ensure the item will identify properly. Some "items" are technically "Creatures".
    private bool NPCLooksLikeObject;

    // Custom 'Use' and 'LongDesc'
    private bool _hasAdditionalProperties;
    private List<string> _additionalPropertiesList = new List<string>();
    private bool _hasLongDescAdditions = false;
    private string _longDescAdditions = "";
    private string _extraPropertiesText;
    private string _additionalPropertiesLongDescriptionsText = "";
    private bool _hasExtraPropertiesText = false;

    public AppraiseInfo()
    {
        Flags = IdentifyResponseFlags.None;
        Success = false;
    }

    /// <summary>
    /// Construct all of the info required for appraising any WorldObject
    /// </summary>
    public AppraiseInfo(WorldObject wo, Player examiner, bool success = true)
    {
        BuildProfile(wo, examiner, success);
    }

    public void BuildProfile(WorldObject wo, Player examiner, bool success = true)
    {
        //Console.WriteLine($"Appraise: {wo.Guid} {wo.Name}");
        Success = success;

        BuildProperties(wo);
        BuildSpells(wo);

        // Help us make sure the item identify properly
        NPCLooksLikeObject = wo.GetProperty(PropertyBool.NpcLooksLikeObject) ?? false;

        if (
            PropertiesIID.ContainsKey(PropertyInstanceId.AllowedWielder)
            && !PropertiesBool.ContainsKey(PropertyBool.AppraisalHasAllowedWielder)
        )
        {
            PropertiesBool.Add(PropertyBool.AppraisalHasAllowedWielder, true);
        }

        if (
            PropertiesIID.ContainsKey(PropertyInstanceId.AllowedActivator)
            && !PropertiesBool.ContainsKey(PropertyBool.AppraisalHasAllowedActivator)
        )
        {
            PropertiesBool.Add(PropertyBool.AppraisalHasAllowedActivator, true);
        }

        if (
            PropertiesString.ContainsKey(PropertyString.ScribeAccount)
            && !examiner.IsAdmin
            && !examiner.IsSentinel
            && !examiner.IsEnvoy
            && !examiner.IsArch
            && !examiner.IsPsr
        )
        {
            PropertiesString.Remove(PropertyString.ScribeAccount);
        }

        if (
            PropertiesString.ContainsKey(PropertyString.HouseOwnerAccount)
            && !examiner.IsAdmin
            && !examiner.IsSentinel
            && !examiner.IsEnvoy
            && !examiner.IsArch
            && !examiner.IsPsr
        )
        {
            PropertiesString.Remove(PropertyString.HouseOwnerAccount);
        }

        if (PropertiesInt.ContainsKey(PropertyInt.Lifespan))
        {
            PropertiesInt[PropertyInt.RemainingLifespan] = wo.GetRemainingLifespan();
        }

        if (PropertiesInt.TryGetValue(PropertyInt.Faction1Bits, out var faction1Bits))
        {
            // hide any non-default factions, prevent client from displaying ???
            // this is only needed for non-standard faction creatures that use templates, to hide the ??? in the client
            var sendBits = faction1Bits & (int)FactionBits.ValidFactions;
            if (sendBits != faction1Bits)
            {
                if (sendBits != 0)
                {
                    PropertiesInt[PropertyInt.Faction1Bits] = sendBits;
                }
                else
                {
                    PropertiesInt.Remove(PropertyInt.Faction1Bits);
                }
            }
        }

        // salvage bag cleanup
        if (wo.WeenieType == WeenieType.Salvage)
        {
            if (wo.GetProperty(PropertyInt.Structure).HasValue)
            {
                PropertiesInt.Remove(PropertyInt.Structure);
            }

            if (wo.GetProperty(PropertyInt.MaxStructure).HasValue)
            {
                PropertiesInt.Remove(PropertyInt.MaxStructure);
            }
        }

        // armor / clothing / shield
        if (wo is Clothing || wo.IsShield)
        {
            BuildArmor(wo);
        }

        if (wo is Creature creature)
        {
            BuildCreature(creature);
        }

        if (
            wo.Damage != null && !(wo is Clothing)
            || wo is MeleeWeapon
            || wo is Missile
            || wo is MissileLauncher
            || wo is Ammunition
            || wo is Caster
        )
        {
            BuildWeapon(wo);
        }

        // TODO: Resolve this issue a better way?
        // Because of the way ACE handles default base values in recipe system (or rather the lack thereof)
        // we need to check the following weapon properties to see if they're below expected minimum and adjust accordingly
        // The issue is that the recipe system likely added 0.005 to 0 instead of 1, which is what *should* have happened.

        //if (wo.WeaponMagicDefense.HasValue && wo.WeaponMagicDefense.Value > 0 && wo.WeaponMagicDefense.Value < 1 && ((wo.GetProperty(PropertyInt.ImbueStackingBits) ?? 0) & 1) != 0)
        //    PropertiesFloat[PropertyFloat.WeaponMagicDefense] += 1;
        //if (wo.WeaponMissileDefense.HasValue && wo.WeaponMissileDefense.Value > 0 && wo.WeaponMissileDefense.Value < 1 && ((wo.GetProperty(PropertyInt.ImbueStackingBits) ?? 0) & 1) != 0)
        //    PropertiesFloat[PropertyFloat.WeaponMissileDefense] += 1;

        // Mask real value of AbsorbMagicDamage and/or Add AbsorbMagicDamage for ImbuedEffectType.IgnoreSomeMagicProjectileDamage
        if (
            PropertiesFloat.ContainsKey(PropertyFloat.AbsorbMagicDamage)
            || wo.HasImbuedEffect(ImbuedEffectType.IgnoreSomeMagicProjectileDamage)
        )
        {
            PropertiesFloat[PropertyFloat.AbsorbMagicDamage] = 1;
        }

        if (wo is PressurePlate)
        {
            if (PropertiesInt.ContainsKey(PropertyInt.ResistLockpick))
            {
                PropertiesInt.Remove(PropertyInt.ResistLockpick);
            }

            if (PropertiesInt.ContainsKey(PropertyInt.Value))
            {
                PropertiesInt.Remove(PropertyInt.Value);
            }

            if (PropertiesInt.ContainsKey(PropertyInt.EncumbranceVal))
            {
                PropertiesInt.Remove(PropertyInt.EncumbranceVal);
            }

            PropertiesString.Add(PropertyString.ShortDesc, wo.Active ? "Status: Armed" : "Status: Disarmed");
        }
        else if (wo is Door || wo is Chest)
        {
            // If wo is not locked, do not send ResistLockpick value. If ResistLockpick is sent for unlocked objects, id panel shows bonus to Lockpick skill
            if (!wo.IsLocked && PropertiesInt.ContainsKey(PropertyInt.ResistLockpick))
            {
                PropertiesInt.Remove(PropertyInt.ResistLockpick);
            }

            // If wo is locked, append skill check percent, as int, to properties for id panel display on chances of success
            if (wo.IsLocked)
            {
                var resistLockpick = LockHelper.GetResistLockpick(wo);

                if (resistLockpick != null)
                {
                    PropertiesInt[PropertyInt.ResistLockpick] = (int)resistLockpick;

                    var pickSkill = examiner.Skills[Skill.Thievery].Current;

                    var successChance = SkillCheck.GetSkillChance((int)pickSkill, (int)resistLockpick) * 100;

                    if (!PropertiesInt.ContainsKey(PropertyInt.AppraisalLockpickSuccessPercent))
                    {
                        PropertiesInt.Add(PropertyInt.AppraisalLockpickSuccessPercent, (int)successChance);
                    }
                }
            }
            // if wo has DefaultLocked property and is unlocked, add that state to the property buckets
            else if (PropertiesBool.ContainsKey(PropertyBool.DefaultLocked))
            {
                PropertiesBool[PropertyBool.Locked] = false;
            }
        }

        if (wo is Corpse)
        {
            PropertiesBool.Clear();
            PropertiesDID.Clear();
            PropertiesFloat.Clear();
            PropertiesInt64.Clear();

            var discardInts = PropertiesInt
                .Where(x => x.Key != PropertyInt.EncumbranceVal && x.Key != PropertyInt.Value)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in discardInts)
            {
                PropertiesInt.Remove(key);
            }

            var discardString = PropertiesString
                .Where(x => x.Key != PropertyString.LongDesc)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in discardString)
            {
                PropertiesString.Remove(key);
            }

            PropertiesInt[PropertyInt.Value] = 0;
        }

        if (wo is Portal)
        {
            if (PropertiesInt.ContainsKey(PropertyInt.EncumbranceVal))
            {
                PropertiesInt.Remove(PropertyInt.EncumbranceVal);
            }
        }

        if (wo is SlumLord slumLord)
        {
            PropertiesBool.Clear();
            PropertiesDID.Clear();
            PropertiesFloat.Clear();
            PropertiesIID.Clear();
            //PropertiesInt.Clear();
            PropertiesInt64.Clear();
            PropertiesString.Clear();

            var longDesc = "";

            if (slumLord.HouseOwner.HasValue && slumLord.HouseOwner.Value > 0)
            {
                longDesc =
                    $"The current maintenance has {(slumLord.IsRentPaid() || !PropertyManager.GetBool("house_rent_enabled").Item ? "" : "not ")}been paid.\n";

                PropertiesInt.Clear();
            }
            else
            {
                //longDesc = $"This house is {(slumLord.HouseStatus == HouseStatus.Disabled ? "not " : "")}available for purchase.\n"; // this was the retail msg.
                longDesc =
                    $"This {(slumLord.House.HouseType == HouseType.Undef ? "house" : slumLord.House.HouseType.ToString().ToLower())} is {(slumLord.House.HouseStatus == HouseStatus.Disabled ? "not " : "")}available for purchase.\n";

                var discardInts = PropertiesInt
                    .Where(x =>
                        x.Key != PropertyInt.HouseStatus
                        && x.Key != PropertyInt.HouseType
                        && x.Key != PropertyInt.MinLevel
                        && x.Key != PropertyInt.MaxLevel
                        && x.Key != PropertyInt.AllegianceMinLevel
                        && x.Key != PropertyInt.AllegianceMaxLevel
                    )
                    .Select(x => x.Key)
                    .ToList();
                foreach (var key in discardInts)
                {
                    PropertiesInt.Remove(key);
                }
            }

            if (slumLord.HouseRequiresMonarch)
            {
                longDesc += "You must be a monarch to purchase and maintain this dwelling.\n";
            }

            if (slumLord.AllegianceMinLevel.HasValue)
            {
                var allegianceMinLevel = PropertyManager.GetLong("mansion_min_rank", -1).Item;
                if (allegianceMinLevel == -1)
                {
                    allegianceMinLevel = slumLord.AllegianceMinLevel.Value;
                }

                longDesc += $"Restricted to characters of allegiance rank {allegianceMinLevel} or greater.\n";
            }

            PropertiesString.Add(PropertyString.LongDesc, longDesc);
        }

        if (wo is Container)
        {
            if (PropertiesInt.ContainsKey(PropertyInt.Value))
            {
                PropertiesInt[PropertyInt.Value] =
                    DatabaseManager.World.GetCachedWeenie(wo.WeenieClassId).GetValue() ?? 0; // Value is masked to base value of Weenie
            }
        }

        if (wo is Storage)
        {
            var longDesc = "";

            if (wo.HouseOwner.HasValue && wo.HouseOwner.Value > 0)
            {
                longDesc = $"Owned by {wo.ParentLink.HouseOwnerName}\n";
            }

            var discardString = PropertiesString.Where(x => x.Key != PropertyString.Use).Select(x => x.Key).ToList();
            foreach (var key in discardString)
            {
                PropertiesString.Remove(key);
            }

            PropertiesString.Add(PropertyString.LongDesc, longDesc);
        }

        if (wo is Hook)
        {
            // If the hook has any inventory, we need to send THOSE properties instead.
            var hook = wo as Container;

            var baseDescString = "";
            if (wo.ParentLink.HouseOwner != null)
            {
                // This is for backwards compatibility. This value was not set/saved in earlier versions.
                // It will get the player's name and save that to the HouseOwnerName property of the house. This is now done when a player purchases a house.
                if (wo.ParentLink.HouseOwnerName == null)
                {
                    var houseOwnerPlayer = PlayerManager.FindByGuid((uint)wo.ParentLink.HouseOwner);
                    if (houseOwnerPlayer != null)
                    {
                        wo.ParentLink.HouseOwnerName = houseOwnerPlayer.Name;
                        wo.ParentLink.SaveBiotaToDatabase();
                    }
                }
                baseDescString = "This hook is owned by " + wo.ParentLink.HouseOwnerName + ". "; //if house is owned, display this text
            }

            var containsString = "";
            if (hook.Inventory.Count == 1)
            {
                var hookedItem = hook.Inventory.First().Value;

                // Hooked items have a custom "description", containing the desc of the sub item and who the owner of the house is (if any)
                BuildProfile(hookedItem, examiner, success);

                containsString = "It contains: \n";

                if (!string.IsNullOrWhiteSpace(hookedItem.LongDesc))
                {
                    containsString += hookedItem.LongDesc;
                }
                //else if (PropertiesString.ContainsKey(PropertyString.ShortDesc) && PropertiesString[PropertyString.ShortDesc] != null)
                //{
                //    containsString += PropertiesString[PropertyString.ShortDesc];
                //}
                else
                {
                    containsString += hookedItem.Name;
                }

                BuildHookProfile(hookedItem);
            }

            //if (PropertiesString.ContainsKey(PropertyString.LongDesc) && PropertiesString[PropertyString.LongDesc] != null)
            //    PropertiesString[PropertyString.LongDesc] = baseDescString + containsString;
            ////else if (PropertiesString.ContainsKey(PropertyString.ShortDesc) && PropertiesString[PropertyString.ShortDesc] != null)
            ////    PropertiesString[PropertyString.LongDesc] = baseDescString + containsString;
            //else
            //    PropertiesString[PropertyString.LongDesc] = baseDescString + containsString;

            PropertiesString[PropertyString.LongDesc] = baseDescString + containsString;

            PropertiesInt.Remove(PropertyInt.Structure);

            // retail should have removed this property and then server side built the same result for the hook longdesc replacement but didn't and ends up with some odd looking appraisals as seen on video/pcaps
            //PropertiesInt.Remove(PropertyInt.AppraisalLongDescDecoration);
        }

        if (wo is ManaStone)
        {
            var useMessage = "";

            if (wo.ItemCurMana.HasValue)
            {
                useMessage =
                    $"Use on a magic item to give the stone's stored Mana to that item.\n\nMana Capacity: {wo.ItemMaxMana ?? 10}";
            }
            else
            {
                useMessage =
                    $"Use on a magic item to destroy that item and drain its Mana.\n\nMana Capacity: {wo.ItemMaxMana ?? 10}";
            }

            PropertiesString[PropertyString.Use] = useMessage;
        }

        if (
            wo is CraftTool
            && (
                wo.ItemType == ItemType.TinkeringMaterial
                || wo.WeenieClassId >= 36619 && wo.WeenieClassId <= 36628
                || wo.WeenieClassId >= 36634 && wo.WeenieClassId <= 36636
            )
        )
        {
            if (PropertiesInt.ContainsKey(PropertyInt.Structure))
            {
                PropertiesInt.Remove(PropertyInt.Structure);
            }
        }

        // convert legacy trophies
        if (wo is { ItemType: ItemType.Misc, TrophyQuality: not null })
        {
            wo.ItemType = ItemType.Useless;
            examiner.Session.Network.EnqueueSend(new GameMessageUpdateObject(wo));
        }

        // fix broken jewel ratings
        if (wo is { ItemWorkmanship: not null})
        {
            RemoveJewelRatings(wo);
        }


        if (!Success)
        {
            // todo: what specifically to keep/what to clear

            //PropertiesBool.Clear();
            //PropertiesDID.Clear();
            //PropertiesFloat.Clear();
            //PropertiesIID.Clear();
            //PropertiesInt.Clear();
            //PropertiesInt64.Clear();
            //PropertiesString.Clear();

            if (PropertiesInt.ContainsKey(PropertyInt.Value))
            {
                PropertiesInt.Remove(PropertyInt.Value);
            }
        }

        BuildFlags();
    }

    private void RemoveJewelRatings(WorldObject wo)
    {
        foreach (var rating in JewelRatingIntIds)
        {
            if (wo.GetProperty(rating) > 0)
            {
                wo.SetProperty(rating, 0);
            }
        }
    }

    private readonly List<PropertyInt> JewelRatingIntIds =
    [
        PropertyInt.GearStrength,
        PropertyInt.GearEndurance,
        PropertyInt.GearCoordination,
        PropertyInt.GearQuickness,
        PropertyInt.GearFocus,
        PropertyInt.GearSelf,
        PropertyInt.GearLifesteal,
        PropertyInt.GearSelfHarm,
        PropertyInt.GearThreatGain,
        PropertyInt.GearThreatReduction,
        PropertyInt.GearElementalWard,
        PropertyInt.GearPhysicalWard,
        PropertyInt.GearMagicFind,
        PropertyInt.GearBlock,
        PropertyInt.GearItemManaUsage,
        PropertyInt.GearThorns,
        PropertyInt.GearVitalsTransfer,
        PropertyInt.GearRedFury,
        PropertyInt.GearSelflessness,
        PropertyInt.GearVipersStrike,
        PropertyInt.GearFamiliarity,
        PropertyInt.GearBravado,
        PropertyInt.GearHealthToStamina,
        PropertyInt.GearHealthToMana,
        PropertyInt.GearExperienceGain,
        PropertyInt.GearManasteal,
        PropertyInt.GearBludgeon,
        PropertyInt.GearPierce,
        PropertyInt.GearSlash,
        PropertyInt.GearFire,
        PropertyInt.GearFrost,
        PropertyInt.GearAcid,
        PropertyInt.GearLightning,
        PropertyInt.GearHealBubble,
        PropertyInt.GearCompBurn,
        PropertyInt.GearPyrealFind,
        PropertyInt.GearNullification,
        PropertyInt.GearWardPen,
        PropertyInt.GearStaminasteal,
        PropertyInt.GearHardenedDefense,
        PropertyInt.GearReprisal,
        PropertyInt.GearElementalist,
        PropertyInt.GearYellowFury,
        PropertyInt.GearBlueFury,
        PropertyInt.GearToughness,
        PropertyInt.GearResistance,
        PropertyInt.GearSlashBane,
        PropertyInt.GearBludgeonBane,
        PropertyInt.GearPierceBane,
        PropertyInt.GearAcidBane,
        PropertyInt.GearFireBane,
        PropertyInt.GearFrostBane,
        PropertyInt.GearLightningBane
    ];

    private void BuildProperties(WorldObject wo)
    {
        PropertiesInt = wo.GetAllPropertyIntWhere(ClientProperties.PropertiesInt);
        PropertiesInt64 = wo.GetAllPropertyInt64Where(ClientProperties.PropertiesInt64);
        PropertiesBool = wo.GetAllPropertyBoolsWhere(ClientProperties.PropertiesBool);
        PropertiesFloat = wo.GetAllPropertyFloatWhere(ClientProperties.PropertiesDouble);
        PropertiesString = wo.GetAllPropertyStringWhere(ClientProperties.PropertiesString);
        PropertiesDID = wo.GetAllPropertyDataIdWhere(ClientProperties.PropertiesDataId);
        PropertiesIID = wo.GetAllPropertyInstanceIdWhere(ClientProperties.PropertiesInstanceId);

        if (wo is Player player)
        {
            // handle character options
            if (!player.GetCharacterOption(CharacterOption.AllowOthersToSeeYourDateOfBirth))
            {
                PropertiesString.Remove(PropertyString.DateOfBirth);
            }

            if (!player.GetCharacterOption(CharacterOption.AllowOthersToSeeYourAge))
            {
                PropertiesInt.Remove(PropertyInt.Age);
            }

            if (!player.GetCharacterOption(CharacterOption.AllowOthersToSeeYourChessRank))
            {
                PropertiesInt.Remove(PropertyInt.ChessRank);
            }

            if (!player.GetCharacterOption(CharacterOption.AllowOthersToSeeYourFishingSkill))
            {
                PropertiesInt.Remove(PropertyInt.FakeFishingSkill);
            }

            if (!player.GetCharacterOption(CharacterOption.AllowOthersToSeeYourNumberOfDeaths))
            {
                PropertiesInt.Remove(PropertyInt.NumDeaths);
            }

            if (!player.GetCharacterOption(CharacterOption.AllowOthersToSeeYourNumberOfTitles))
            {
                PropertiesInt.Remove(PropertyInt.NumCharacterTitles);
            }

            // handle dynamic properties for appraisal
            if (player.Allegiance != null && player.AllegianceNode != null)
            {
                if (player.Allegiance.AllegianceName != null)
                {
                    PropertiesString[PropertyString.AllegianceName] = player.Allegiance.AllegianceName;
                }

                if (player.AllegianceNode.IsMonarch)
                {
                    PropertiesInt[PropertyInt.AllegianceFollowers] = player.AllegianceNode.TotalFollowers;
                }
                else
                {
                    var monarch = player.Allegiance.Monarch;
                    var patron = player.AllegianceNode.Patron;

                    PropertiesString[PropertyString.MonarchsTitle] =
                        AllegianceTitle.GetTitle(
                            (HeritageGroup)(monarch.Player.Heritage ?? 0),
                            (Gender)(monarch.Player.Gender ?? 0),
                            monarch.Rank
                        )
                        + " "
                        + monarch.Player.Name;
                    PropertiesString[PropertyString.PatronsTitle] =
                        AllegianceTitle.GetTitle(
                            (HeritageGroup)(patron.Player.Heritage ?? 0),
                            (Gender)(patron.Player.Gender ?? 0),
                            patron.Rank
                        )
                        + " "
                        + patron.Player.Name;
                }
            }

            if (player.Fellowship != null)
            {
                PropertiesString[PropertyString.Fellowship] = player.Fellowship.FellowshipName;
            }
        }
        AddPropertyEnchantments(wo);
    }

    private void AddPropertyEnchantments(WorldObject wo)
    {
        if (wo == null)
        {
            return;
        }

        if (PropertiesInt.ContainsKey(PropertyInt.ArmorLevel))
        {
            PropertiesInt[PropertyInt.ArmorLevel] += wo.EnchantmentManager.GetArmorMod();

            var baseArmor = PropertiesInt[PropertyInt.ArmorLevel];

            var wielder = wo.Wielder as Player;
            if (wielder != null && ((wo.ClothingPriority ?? 0) & (CoverageMask)CoverageMaskHelper.Underwear) == 0)
            {
                int armor;

                if (wo.IsShield)
                {
                    armor = (int)wielder.GetSkillModifiedShieldLevel(baseArmor);
                }
                else
                {
                    armor = baseArmor;
                }

                if (armor < baseArmor)
                {
                    PropertiesInt[PropertyInt.ArmorLevel] = armor;
                    IsArmorCapped = true;
                }
                else if (armor > baseArmor)
                {
                    PropertiesInt[PropertyInt.ArmorLevel] = armor;
                    IsArmorBuffed = true;
                }
            }
        }

        if (wo.ItemSkillLimit != null)
        {
            PropertiesInt[PropertyInt.AppraisalItemSkill] = (int)wo.ItemSkillLimit;
        }
        else
        {
            PropertiesInt.Remove(PropertyInt.AppraisalItemSkill);
        }

        //if (PropertiesFloat.ContainsKey(PropertyFloat.WeaponDefense) && !(wo is Ammunition))
        //{
        //    var defenseMod = wo.EnchantmentManager.GetDefenseMod();
        //    var auraDefenseMod = wo.Wielder != null && wo.IsEnchantable ? wo.Wielder.EnchantmentManager.GetDefenseMod() : 0.0f;

        //    PropertiesFloat[PropertyFloat.WeaponDefense] += defenseMod + auraDefenseMod;
        //}

        if (PropertiesFloat.ContainsKey(PropertyFloat.WeaponOffense))
        {
            var attackMod = wo.EnchantmentManager.GetAttackMod();
            var auraAttackMod =
                wo.Wielder != null && wo.IsEnchantable ? wo.Wielder.EnchantmentManager.GetAttackMod() : 0.0f;

            PropertiesFloat[PropertyFloat.WeaponOffense] += attackMod + auraAttackMod;

            if (auraAttackMod > 0)
            {
                IsAttackModBuffed = true;
            }
        }

        if (PropertiesFloat.TryGetValue(PropertyFloat.ManaConversionMod, out var manaConvMod))
        {
            if (manaConvMod != 0)
            {
                // hermetic link/void
                var enchantmentMod = ResistMaskHelper.GetManaConversionMod(wo);

                if (enchantmentMod != 1.0f)
                {
                    PropertiesFloat[PropertyFloat.ManaConversionMod] *= enchantmentMod;

                    ResistHighlight = ResistMaskHelper.GetHighlightMask(wo);
                    ResistColor = ResistMaskHelper.GetColorMask(wo);
                }
            }
            else if (!PropertyManager.GetBool("show_mana_conv_bonus_0").Item)
            {
                PropertiesFloat.Remove(PropertyFloat.ManaConversionMod);
            }
        }

        if (PropertiesFloat.ContainsKey(PropertyFloat.ElementalDamageMod))
        {
            var enchantmentBonus = ResistMaskHelper.GetElementalDamageBonus(wo);

            if (enchantmentBonus != 0)
            {
                PropertiesFloat.TryGetValue(PropertyFloat.ElementalDamageMod, out var baseElementalDamageMod);

                PropertiesFloat[PropertyFloat.ElementalDamageMod] = (baseElementalDamageMod - 1.0f) * (1.0f + enchantmentBonus) + 1.0f;

                ResistHighlight = ResistMaskHelper.GetHighlightMask(wo);
                ResistColor = ResistMaskHelper.GetColorMask(wo);
            }
        }

        // LONG DESCRIPTION

        // if (wo.NumTimesTinkered <= 0)
        // {
        //     var appraisalLongDescDecoration = AppraisalLongDescDecorations.None;
        //
        //     if (wo.ItemWorkmanship > 0)
        //     {
        //         appraisalLongDescDecoration |= AppraisalLongDescDecorations.PrependWorkmanship;
        //     }
        //
        //     if (wo.MaterialType > 0)
        //     {
        //         appraisalLongDescDecoration |= AppraisalLongDescDecorations.PrependMaterial;
        //     }
        //
        //     if (wo.GemType > 0 && wo.GemCount > 0)
        //     {
        //         appraisalLongDescDecoration |= wo.NumTimesTinkered > 0 ? 0 : AppraisalLongDescDecorations.AppendGemInfo;
        //     }
        //
        //     if (appraisalLongDescDecoration > 0 && wo.LongDesc != null && wo.LongDesc.StartsWith(wo.Name))
        //     {
        //         PropertiesInt[PropertyInt.AppraisalLongDescDecoration] = (int)appraisalLongDescDecoration;
        //     }
        //     else
        //     {
        //         PropertiesInt.Remove(PropertyInt.AppraisalLongDescDecoration);
        //     }
        // }


        SetCustomDecorationLongText(wo);

        SetTinkeringLongText(wo);

        if (_hasLongDescAdditions)
        {
            _longDescAdditions += "";
            PropertiesString[PropertyString.LongDesc] = _longDescAdditions;
        }

        // USE
        if (PropertiesString.TryGetValue(PropertyString.Use, out var useText) && useText.Length > 0)
        {
            _extraPropertiesText = $"{useText}\n";
        }
        else
        {
            _extraPropertiesText = "";
        }

        // Trophy Quality Level
        SetTrophyQualityLevelText(wo);

        // Protection Levels ('Use' text)
        SetProtectionLevelsUseText(wo);

        // Retail Imbues ('Use' and 'LongDesc' text)
        SetArmorRendUseLongText(wo);
        SetArmorCleavingUseLongText(wo);

        SetResistanceRendLongText(ImbuedEffectType.AcidRending, wo, "Acid");
        SetResistanceRendLongText(ImbuedEffectType.BludgeonRending, wo, "Bludgeoning");
        SetResistanceRendLongText(ImbuedEffectType.ColdRending, wo, "Cold");
        SetResistanceRendLongText(ImbuedEffectType.ElectricRending, wo, "Lightning");
        SetResistanceRendLongText(ImbuedEffectType.FireRending, wo, "Fire");
        SetResistanceRendLongText(ImbuedEffectType.PierceRending, wo, "Pierce");
        SetResistanceRendLongText(ImbuedEffectType.SlashRending, wo, "Slash");

        SetResistanceCleavingUseLongText(wo);

        SetCripplingBlowUseLongText(wo);
        SetCrushingBlowUseLongText(wo);

        SetCriticalStrikeUseLongText(wo);
        SetBitingStrikeUseLongText(wo);

        // "Additional Properties" ('Use' and 'LongDesc' text)
        SetWardRendingUseLongText(wo);
        SetWardCleavingUseLongText(wo);

        SetStaminaReductionUseLongText(wo);
        SetNoCompsRequiredSchoolUseLongText(wo);

        SetGearRatingText(wo, PropertyInt.GearStrength, "Mighty Thews", "Grants +10 to current Strength, plus an additional +1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 10);
        SetGearRatingText(wo, PropertyInt.GearEndurance, "Perseverance", "Grants +10 to current Endurance, plus an additional +1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 10);
        SetGearRatingText(wo, PropertyInt.GearCoordination, "Dexterous Hand", "Grants +10 to current Coordination, plus an additional +1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 10);
        SetGearRatingText(wo, PropertyInt.GearQuickness, "Swift-footed", "Grants +10 to current Quickness, plus an additional +1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 10);
        SetGearRatingText(wo, PropertyInt.GearFocus, "Focused Mind", "Grants +10 to current Focus, plus an additional +1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 10);
        SetGearRatingText(wo, PropertyInt.GearSelf, "Erudite Mind", "Grants +10 to current Self, plus an additional +1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 10);
        SetGearRatingText(wo, PropertyInt.GearSelfHarm, "Blood Frenzy", $"Grants 10% increased damage with all attacks, plus an additional 0.5% per equipped rating ((ONE) total). However, you will occasionally deal the extra damage to yourself as well.", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearThreatGain, "Provocation", $"Grants 10% increased threat from your actions, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearThreatReduction, "Clouded Vision", $"Grants 10% reduced threat from your actions, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearElementalWard, "Prismatic Ward", $"Grants 10%% protection against Flame, Frost, Lightning, and Acid damage types, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearPhysicalWard, "Black Bulwark", $"Grants 10%% protection against Slashing, Bludgeoning, and Piercing damage types, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearMagicFind, "Seeker", $"Grants a 5% bonus to monster loot quality, plus an additional 0.25% per equipped rating ((ONE) total).", 0.25f, 1.0f, 5, 0, true);
        SetGearRatingText(wo, PropertyInt.GearBlock, "Stalwart Defense", $"Grants a 10% bonus to block attacks, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearItemManaUsage, "Thrifty Scholar", $"Grants a 20% cost reduction to mana consumed by equipped items, plus an additional 1% per equipped rating ((ONE) total).", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearThorns, "Swift Retribution", $"Deflect 10% of a blocked attack's damage back to a close-range attacker, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearVitalsTransfer, "Tilted Scales", $"Grants a 10% bonus to your Vitals Transfer spells, plus an additional 0.5% per equipped rating ((ONE) total). Receive an equivalent reduction in the effectiveness of your other Restoration spells.", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearRedFury, "Red Fury", $"Grants increased damage as you lose health, up to a maximum bonus of 20% at 0 health, plus an additional 1% per equipped rating ((ONE) total).", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearYellowFury, "Yellow Fury", $"Grants increased physical damage as you lose stamina, up to a maximum bonus of 20% at 0 stamina, plus an additional 1% per equipped rating ((ONE) total).", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearBlueFury, "Blue Fury", $"Grants increased magical damage as you lose mana, up to a maximum bonus of 20% at 0 mana, plus an additional 1% per equipped rating ((ONE) total).", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearSelflessness, "Selfless Spirit", $"Grants a 10% bonus to your restoration spells when cast on others, plus an additional 0.5% per equipped rating ((ONE) total). Receive an equivalent reduction in their effectiveness when cast on yourself.", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearFamiliarity, "Familiar Foe", $"Grants up to a 20% bonus to defense skill against a target you are attacking, plus an additional 1% per equipped rating ((ONE) total). The chance builds up from 0%, based on how often you have hit the target.", 1.0f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearBravado, "Bravado", $"Grants up to a 20% bonus to attack skill against a target you are attacking, plus an additional 1% per equipped rating ((ONE) total). The chance builds up from 0%, based on how often you have hit the target.", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearHealthToStamina, "Masochist", $"Grants a 10% chance to regain the hit damage received from an attack as stamina, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearHealthToMana, "Austere Anchorite", $"Grants a 10% chance to regain the hit damage received from an attack as mana, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearExperienceGain, "Illuminated Mind", $"Grants a 5% bonus to experience gain, plus an additional 0.25% per equipped rating ((ONE) total).", 0.25f, 1.0f, 5, 0, true);
        SetGearRatingText(wo, PropertyInt.GearLifesteal, "Sanguine Thirst", "Grants a 10% chance on hit to gain health, plus an additional 0.5% per equipped rating ((ONE) total). Amount stolen is equal to 10% of damage dealt.", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearStaminasteal, "Vigor Siphon", $"Grants a 10% chance on hit to gain stamina, plus an additional 0.5% per equipped rating ((ONE) total). Amount stolen is equal to 10% of damage dealt.", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearManasteal, "Ophidian", $"Grants a 10% chance on hit to steal mana from your target, plus an additional 0.5% per equipped rating ((ONE) total). Amount stolen is equal to 10% of damage dealt.", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearBludgeon, "Skull-cracker", $"Grants up to 20% bonus critical hit damage, plus an additional 1% per equipped rating ((ONE) total). The bonus builds up from 0%, based on how often you have hit the target.", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearPierce, "Precision Strikes", $"Grants up to 20% piercing resistance penetration, plus an additional 1% per equipped rating ((ONE) total). The bonus builds up from 0%, based on how often you have hit the target", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearSlash, "Falcon's Gyre", $"Grants a 10% chance to cleave an additional target, plus an additional 0.5% per equipped rating ((ONE) total).", 0.5f, 1.0f, 10, 0, true);
        SetGearRatingText(wo, PropertyInt.GearFire, "Blazing Brand", $"Grants a 10% bonus to Fire damage, plus an additional 0.5% per equipped rating ((ONE) total). Also grants a 2% chance on hit to set the ground beneath your target ablaze, plus an additional 0.1% per equipped rating ((TWO) total).", 0.5f, 0.1f, 10, 2, true);
        SetGearRatingText(wo, PropertyInt.GearFrost, "Bone-chiller", $"Grants a 10% bonus to Cold damage, plus an additional 0.5% per equipped rating ((ONE) total). Also grants a 2% chance on hit to surround your target with chilling mist, plus an additional 0.1% per equipped rating ((TWO) total).", 0.5f, 0.1f, 10, 2, true);
        SetGearRatingText(wo, PropertyInt.GearAcid, "Devouring Mist", $"Grants a 10% bonus to Acid damage, plus an additional 0.5% per equipped rating ((ONE) total). Also grants a 2% chance on hit to surround your target with acidic mist, plus an additional 0.1% per equipped rating ((TWO) total).", 0.5f, 0.1f, 10, 2, true);
        SetGearRatingText(wo, PropertyInt.GearLightning, "Astyrrian's Rage", $"Grants a 10% bonus to Lightning damage, plus an additional 0.5% per equipped rating ((ONE) total). Also grants a 2% chance on hit to electrify the ground beneath your target, plus an additional 0.1% per equipped rating ((TWO) total).", 0.5f, 0.1f, 10, 2, true);
        SetGearRatingText(wo, PropertyInt.GearHealBubble, "Purified Soul", $"Grants a 10% bonus to your restoration spells, plus an additional 0.5% per equipped rating ((ONE) total). Also grants a 2% chance to create a sphere of healing energy on top of your target when casting a restoration spell, plus an additional 0.1% per equipped rating ((ONE) total).", 0.5f, 0.1f, 10, 2, true);
        SetGearRatingText(wo, PropertyInt.GearCompBurn, "Meticulous Magus", $"Grants a 20% reduction to your chance to burn spell components, plus an additional 1% per equipped rating ((ONE) total).", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearPyrealFind, "Prosperity", $"Grants a 5% chance for a monster to drop an extra item, plus an additional 0.25% per equipped rating ((ONE) total).", 0.25f, 1.0f, 5, 0, true);
        SetGearRatingText(wo, PropertyInt.GearNullification, "Nullification", $"Grants up to 20% reduced magic damage taken, plus an additional 1% per equipped rating ((ONE) total). The amount builds up from 0%, based on how often you have been hit with a damaging spell.", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearWardPen, "Ruthless Discernment", $"Grants up to 20% ward penetration, plus an additional 1% per equipped rating ((ONE) total). The Amount builds up from 0%, based on how often you have hit your target.", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearHardenedDefense, "Hardened Fortification", $"Grants up to 20% reduced physical damage taken, plus an additional 1% per equipped rating ((ONE) total). Th amount builds up from 0%, based on how often you have been hit with a damaging physical attack.", 1.0f, 10f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearReprisal, "Vicious Reprisal", $"Grants a 5% chance to evade an incoming critical hit, plus an additional 0.25% per equipped rating ((ONE) total). Your next attack after a the evade is a guaranteed critical.", 0.25f, 1.0f, 5, 0, true);
        SetGearRatingText(wo, PropertyInt.GearElementalist, "Elementalist", $"Grants up to a 20% damage bonus to war spells, plus an additional 1% per equipped rating ((ONE) total). The amount builds up from 0%, based on how often you have hit your target.", 1.0f, 1.0f, 20, 0, true);
        SetGearRatingText(wo, PropertyInt.GearToughness, "Toughness", $"Grants +20 physical defense, plus an additional 1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 20);
        SetGearRatingText(wo, PropertyInt.GearResistance, "Resistance", $"Grants +20 magic defense, plus an additional 1 per equipped rating ((ONE) total).", 1.0f, 1.0f, 20);
        SetGearRatingText(wo, PropertyInt.GearSlashBane, "Swordsman's Bane", $"Grants +0.2 slashing protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total). The protection level cannot be increased beyond 1.0 (average), from this effect.", 0.01f, 1.0f, 0.2f);
        SetGearRatingText(wo, PropertyInt.GearBludgeonBane, "Tusker's Bane", $"Grants +0.2 bludgeoning protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total). The protection level cannot be increased beyond 1.0 (average), from this effect.", 0.01f, 1.0f, 0.2f);
        SetGearRatingText(wo, PropertyInt.GearPierceBane, "Archer's Bane", $"Grants +0.2 piercing protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total). The protection level cannot be increased beyond 1.0 (average), from this effect.", 0.01f, 1.0f, 0.2f);
        SetGearRatingText(wo, PropertyInt.GearAcidBane, "Olthoi's Bane", $"Grants +0.2 acid protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total). The protection level cannot be increased beyond 1.0 (average), from this effect.", 0.01f, 1.0f, 0.2f);
        SetGearRatingText(wo, PropertyInt.GearFireBane, "Inferno's Bane", $"Grants +0.2 fire protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total). The protection level cannot be increased beyond 1.0 (average), from this effect.", 0.01f, 1.0f, 0.2f);
        SetGearRatingText(wo, PropertyInt.GearFrostBane, "Gelidite's Bane", $"Grants +0.2 cold protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total). The protection level cannot be increased beyond 1.0 (average), from this effect.", 0.01f, 1.0f, 0.2f);
        SetGearRatingText(wo, PropertyInt.GearLightningBane, "Astyrrian's Bane", $"Grants +0.2 electric protection to all equipped armor, plus an additional 0.01 per equipped rating ((ONE) total) The protection level cannot be increased beyond 1.0 (average), from this effect..", 0.01f, 1.0f, 0.2f);

        SetAdditionalPropertiesUseText(wo);

        // Spell proc rate ('Use' text)
        SetSpellProcRateUseText(wo);

        // -------- WEAPON ATTACK/DEFENSE MODS --------
        _extraPropertiesText += "\n";

        // Attack Mod for Bows
        SetBowAttackModUseText(wo);

        // Ammo
        SetAmmoEffectUseText(wo);

        SetWeaponWarMagicUseText();
        SetWeaponLifeMagicUseText();
        SetWeaponPhysicalDefenseUseText(wo);
        SetWeaponMagicDefenseUseText(wo);
        SetWeaponRestoModUseText();
        SetBowElementalWarningUseText(wo);

        // -- ARMOR --
        SetArmorWardLevelUseText(wo);
        SetArmorWeightClassUseText(wo);
        SetArmorResourcePenaltyUseText(wo);

        SetWeaponSpellcraftText(wo);

        SetJewelryManaConUseText(wo);

        var playerWielder = wo as Player;
        SetArmorModUseText(PropertyFloat.ArmorWarMagicMod, wo, "Bonus to War Magic Skill: +(ONE)%", (float)(playerWielder?.GetArmorWarMagicMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorLifeMagicMod, wo, "Bonus to Life Magic Skill: +(ONE)%", (float)(playerWielder?.GetArmorLifeMagicMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorAttackMod, wo, "Bonus to Attack Skill: +(ONE)%", (float)(playerWielder?.GetArmorAttackMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorPhysicalDefMod, wo, "Bonus to Physical Defense: +(ONE)%", (float)(playerWielder?.GetArmorPhysicalDefMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorMagicDefMod, wo, "Bonus to Magic Defense: +(ONE)%", (float)(playerWielder?.GetArmorMagicDefMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorDualWieldMod, wo, "Bonus to Dual Wield Skill: +(ONE)%", (float)(playerWielder?.GetArmorDualWieldMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorTwohandedCombatMod, wo, "Bonus to Two-handed Combat Skill: +(ONE)%", (float)(playerWielder?.GetArmorTwohandedCombatMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorRunMod, wo, "Bonus to Run Skill: +(ONE)%", (float)(playerWielder?.GetArmorRunMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorThieveryMod, wo, "Bonus to Thievery Skill: +(ONE)%", (float)(playerWielder?.GetArmorThieveryMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorShieldMod, wo, "Bonus to Shield Skill: +(ONE)%", (float)(playerWielder?.GetArmorShieldMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorPerceptionMod, wo, "Bonus to Perception Skill: +(ONE)%", (float)(playerWielder?.GetArmorPerceptionMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorDeceptionMod, wo, "Bonus to Deception Skill: +(ONE)%", (float)(playerWielder?.GetArmorDeceptionMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorHealthMod, wo, "Bonus to Maximum Health: +(ONE)%", (float)(playerWielder?.GetArmorHealthMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorHealthRegenMod, wo, "Bonus to Health Regen: +(ONE)%", (float)(playerWielder?.GetArmorHealthRegenMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorStaminaMod, wo, "Bonus to Maximum Stamina: +(ONE)%", (float)(playerWielder?.GetArmorStaminaMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorStaminaRegenMod, wo, "Bonus to Stamina Regen: +(ONE)%", (float)(playerWielder?.GetArmorStaminaRegenMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorManaMod, wo, "Bonus to Maximum Mana: +(ONE)%", (float)(playerWielder?.GetArmorManaMod() ?? 0.0));
        SetArmorModUseText(PropertyFloat.ArmorManaRegenMod, wo, "Bonus to Mana Regen: +(ONE)%", (float)(playerWielder?.GetArmorManaRegenMod() ?? 0.0));

        SetDamagePenaltyUseText();

        SetJewelcraftingUseText(wo);

        SetSalvageBagUseText(wo);

        // -------- EMPOWERED SCARABS --------

        SetSigilTrinketUseText(wo);

        if (_hasExtraPropertiesText)
        {
            _extraPropertiesText += "";
            PropertiesString[PropertyString.Use] = _extraPropertiesText;

            // Additional Long
            if (_additionalPropertiesLongDescriptionsText.Length > 0)
            {
                PropertiesString.TryGetValue(PropertyString.LongDesc, out var longDescString);

                _additionalPropertiesLongDescriptionsText =
                    "Property Descriptions:\n" + _additionalPropertiesLongDescriptionsText + "\n\n" +
                    longDescString;

                PropertiesString[PropertyString.LongDesc] = _additionalPropertiesLongDescriptionsText;
            }
        }
    }

    private void SetWeaponSpellcraftText(WorldObject wo)
    {
        if (wo.ItemCurMana is not null)
        {
            return;
        }

        if (PropertiesInt.TryGetValue(PropertyInt.ItemSpellcraft, out var itemSpellcraft))
        {
            _extraPropertiesText += $"\nSpellcraft: {itemSpellcraft}.";
            PropertiesString[PropertyString.Use] = _extraPropertiesText;
        }

    }

    private void SetTrophyQualityLevelText(WorldObject wo)
    {
        if (PropertiesInt.TryGetValue(PropertyInt.TrophyQuality, out var trophyQuality) && trophyQuality > 0)
        {
            var qualityName = LootGenerationFactory.GetTrophyQualityName(trophyQuality);

            _extraPropertiesText += $"\nQuality Level: {trophyQuality}";
            PropertiesString[PropertyString.Use] = _extraPropertiesText;
        }
    }

    private void SetCustomDecorationLongText(WorldObject wo)
    {
        if (wo.MaterialType != null && wo.ItemWorkmanship != null)
        {
            var prependMaterial = RecipeManager.GetMaterialName((MaterialType)wo.MaterialType);
            var prependWorkmanship = Salvage.WorkmanshipNames[Math.Clamp((int)wo.ItemWorkmanship, 1, 10) - 1];
            var modifiedGemType = RecipeManager.GetMaterialName(wo.GemType ?? MaterialType.Unknown);

            if (wo.GemType != null && wo.GemCount is >= 1)
            {
                if (wo.GemCount > 1)
                {
                    if (
                        (int)wo.GemType == 26
                        || (int)wo.GemType == 37
                        || (int)wo.GemType == 40
                        || (int)wo.GemType == 46
                        || (int)wo.GemType == 49
                    )
                    {
                        modifiedGemType += "es";
                    }
                    else if ((int)wo.GemType == 38)
                    {
                        modifiedGemType = "Rubies";
                    }
                    else
                    {
                        modifiedGemType += "s";
                    }
                }

                _longDescAdditions =
                    $"{prependWorkmanship} {prependMaterial} {wo.Name}, set with {wo.GemCount} {modifiedGemType}";
            }
            else
            {
                _longDescAdditions =
                    $"{prependWorkmanship} {prependMaterial} {wo.Name}";
            }

            _hasLongDescAdditions = true;
        }
    }

    private void SetTinkeringLongText(WorldObject wo)
    {
        if (wo.NumTimesTinkered < 1)
        {
            return;
        }
        // for tinkered items, we need to replace the Decorations


        // then we need to check the tinker log array, parse it and convert values to items to write to long desc

        if (wo.NumTimesTinkered <= 0 || wo.TinkerLog == null)
        {
            return;
        }

        var tinkerLogArray = wo.TinkerLog.Split(',');

        var tinkeringTypes = new int[80];

        _hasLongDescAdditions = true;

        _longDescAdditions +=
            $"This item has been tinkered with:\n";

        foreach (var s in tinkerLogArray)
        {
            if (int.TryParse(s, out var index))
            {
                if (index >= 0 && index < tinkeringTypes.Length)
                {
                    tinkeringTypes[index] += 1;
                }
            }
        }

        var sumofTinksinLog = 0;

        // cycle through the parsed tinkering log, adding Material Name and value stored in the element (num times that salvage has been applied)

        for (var index = 0; index < tinkeringTypes.Length; index++)
        {
            var value = tinkeringTypes[index];
            if (value > 0)
            {
                if (System.Enum.IsDefined(typeof(MaterialType), (MaterialType)index))
                {
                    var materialType = (MaterialType)index;
                    _longDescAdditions += $"\n \t    {RecipeManager.GetMaterialName(materialType)}:  {value}";
                }
                else
                {
                    Console.WriteLine($"Unknown variable at index {index}: {value}");
                }
                sumofTinksinLog += value;
            }
        }

        // check for failure on first tink, or string of failures with no success
        if (sumofTinksinLog == 0 && wo.NumTimesTinkered >= 1)
        {
            _longDescAdditions += $"\n\n \t    Failures:    {wo.NumTimesTinkered}";
        }
        // check for any failures whatsoever by comparing sumofTinksInLog to NumTimesTinkered
        else
        {
            sumofTinksinLog -= wo.NumTimesTinkered;
            if (sumofTinksinLog < 0)
            {
                _longDescAdditions += $"\n\n \t    Failures:  {Math.Abs(sumofTinksinLog)}";
            }
        }
    }

    private void SetSigilTrinketUseText(WorldObject wo)
    {
        if (wo is not SigilTrinket sigilTrinket)
        {
            return;
        }

        // Proc Chance
        if (
            PropertiesFloat.TryGetValue(PropertyFloat.SigilTrinketTriggerChance, out var sigilTrinketTriggerChance)
            && sigilTrinketTriggerChance > 0.01
        )
        {
            _extraPropertiesText += $"Proc Chance: {Math.Round(sigilTrinketTriggerChance * 100, 0)}%\n";

            _hasExtraPropertiesText = true;
        }

        // Frequency
        if (
            PropertiesFloat.TryGetValue(PropertyFloat.CooldownDuration, out var cooldownDuration)
            && cooldownDuration > 0.01
        )
        {
            _extraPropertiesText += $"Cooldown: {Math.Round(cooldownDuration, 1)} seconds\n";

            _hasExtraPropertiesText = true;
        }

        // Max Level
        if (PropertiesInt.TryGetValue(PropertyInt.MaxStructure, out var maxStructure) && maxStructure > 0)
        {
            _extraPropertiesText += $"Max Number of Uses: {maxStructure}\n";

            _hasExtraPropertiesText = true;
        }

        // Intensity
        if (
            PropertiesFloat.TryGetValue(PropertyFloat.SigilTrinketIntensity, out var sigilTrinketIntensity)
            && sigilTrinketIntensity > 0.01
        )
        {
            _extraPropertiesText += $"Bonus Intensity: {Math.Round(sigilTrinketIntensity * 100, 1)}%\n";

            _hasExtraPropertiesText = true;
        }

        // Mana Reduction
        if (
            PropertiesFloat.TryGetValue(PropertyFloat.SigilTrinketReductionAmount, out var sigilTrinketReductionAmount)
            && sigilTrinketReductionAmount > 0.01
        )
        {
            _extraPropertiesText += $"Mana Cost Reduction: {Math.Round(sigilTrinketReductionAmount * 100, 1)}%\n";

            _hasExtraPropertiesText = true;
        }

        // Reserved Health
        if (PropertiesFloat.TryGetValue(PropertyFloat.SigilTrinketHealthReserved, out var sigilTrinketHealthReserved)
            && sigilTrinketHealthReserved > 0)
        {
            var wielder = (Creature)wo.Wielder;

            if (wielder != null)
            {
                var equippedSigilTrinkets = wielder.GetEquippedSigilTrinkets();
                var totalReservedHealth = 0.0;

                foreach (var equippedSigilTrinket in equippedSigilTrinkets)
                {
                    totalReservedHealth += equippedSigilTrinket.SigilTrinketHealthReserved ?? 0;
                }

                _extraPropertiesText +=
                    $"Health Reservation: {Math.Round(sigilTrinketHealthReserved * 100, 1)}% ({Math.Round(totalReservedHealth * 100, 1)}%)\n";
            }
            else
            {
                _extraPropertiesText += $"Health Reservation: {Math.Round(sigilTrinketHealthReserved * 100, 1)}%\n";
            }

            _hasExtraPropertiesText = true;
        }

        // Reserved Stamina
        if (PropertiesFloat.TryGetValue(PropertyFloat.SigilTrinketStaminaReserved, out var sigilTrinketStaminaReserved)
            && sigilTrinketStaminaReserved > 0)
        {
            var wielder = (Creature)wo.Wielder;

            if (wielder != null)
            {
                var equippedSigilTrinkets = wielder.GetEquippedSigilTrinkets();
                var totalReservedStamina = 0.0;

                foreach (var equippedSigilTrinket in equippedSigilTrinkets)
                {
                    totalReservedStamina += equippedSigilTrinket.SigilTrinketStaminaReserved ?? 0;
                }

                _extraPropertiesText +=
                    $"Stamina Reservation: {Math.Round(sigilTrinketStaminaReserved * 100, 1)}% ({Math.Round(totalReservedStamina * 100, 1)}%)\n";
            }
            else
            {
                _extraPropertiesText +=
                    $"Stamina Reservation: {Math.Round(sigilTrinketStaminaReserved * 100, 1)}%\n";
            }

            _hasExtraPropertiesText = true;
        }

        // Reserved Mana
        if (PropertiesFloat.TryGetValue(PropertyFloat.SigilTrinketManaReserved, out var sigilTrinketManaReserved) &&
            sigilTrinketManaReserved > 0)
        {
            var wielder = (Creature)wo.Wielder;

            if (wielder != null)
            {
                var equippedSigilTrinkets = wielder.GetEquippedSigilTrinkets();
                var totalReservedMana = 0.0;

                foreach (var equippedSigilTrinket in equippedSigilTrinkets)
                {
                    totalReservedMana += equippedSigilTrinket.SigilTrinketManaReserved ?? 0;
                }

                _extraPropertiesText +=
                    $"Mana Reservation: {Math.Round(sigilTrinketManaReserved * 100, 1)}% ({Math.Round(totalReservedMana * 100, 1)}%)\n";
            }
            else
            {
                _extraPropertiesText += $"Mana Reservation: {Math.Round(sigilTrinketManaReserved * 100, 1)}%\n";
            }

            _hasExtraPropertiesText = true;
        }

        // Wield Skill Req
        if (wo is SigilTrinket { AllowedSpecializedSkills: not null } sigilTrinketSkills)
        {
            var skills = sigilTrinketSkills.AllowedSpecializedSkills;
            if (skills.Count > 0)
            {
                try
                {
                    var names = new List<string>(skills.Count);
                    foreach (var sk in skills)
                    {
                        // Prefer human-friendly name if available
                        try
                        {
                            names.Add(((NewSkillNames)sk).ToSentence());
                        }
                        catch
                        {
                            names.Add(((NewSkillNames)sk).ToString());
                        }
                    }

                    // Deduplicate while preserving order
                    var unique = new List<string>();
                    foreach (var n in names)
                    {
                        if (!unique.Contains(n))
                        {
                            unique.Add(n);
                        }
                    }

                    var wieldReqStr = unique.Count == 1
                        ? $"Wield requires specialized {unique[0]}"
                        : $"Wield requires specialized {string.Join(" or ", unique)}";

                    _extraPropertiesText += wieldReqStr + "\n";
                    _hasExtraPropertiesText = true;
                }
                catch
                {
                    // resilient: if anything goes wrong, do not break appraisal
                }
            }
        }
    }

    private void SetSalvageBagUseText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.Structure, out var structure) || structure < 0)
        {
            return;
        }

        if (wo.WeenieType != WeenieType.Salvage)
        {
            return;
        }

        _extraPropertiesText += $"\nThis bag contains {structure} units of salvage.\n";
        _hasExtraPropertiesText = true;
    }

    private void SetJewelcraftingUseText(WorldObject wo)
    {
        _hasExtraPropertiesText = true;

        if (wo.WeenieType is WeenieType.Jewel)
        {
            _extraPropertiesText += Jewel.GetJewelDescription(wo);
        }
        else
        {
            for (var i = 0; i < (wo.JewelSockets ?? 0); i++)
            {
                var currentSocketMaterialTypeId = wo.GetProperty(Jewel.SocketedJewelDetails[i].JewelSocketMaterialIntId);
                var currentSocketQualityLevel = wo.GetProperty(Jewel.SocketedJewelDetails[i].JewelSocketQualityIntId);

                if (i == 0 && wo.JewelSocket1 is not "Empty" and not null)
                {
                    // 0 - prepended quality, 1 - gemstone type, 2 - appended name, 3 - property type, 4 - amount of property, 5 - original gem
                    var jewelString = wo.JewelSocket1.Split('/');

                    if (Jewel.StringToMaterialType.TryGetValue(jewelString[1], out var materialType))
                    {
                        currentSocketMaterialTypeId = (int)materialType;
                    }

                    if (Jewel.JewelQualityStringToValue.TryGetValue(jewelString[0], out var qualityLevel))
                    {
                        currentSocketQualityLevel = qualityLevel;
                    }
                }

                if (i == 1 && wo.JewelSocket2 is not "Empty" and not null)
                {
                    // 0 - prepended quality, 1 - gemstone type, 2 - appended name, 3 - property type, 4 - amount of property, 5 - original gem
                    var jewelString = wo.JewelSocket2.Split('/');

                    if (Jewel.StringToMaterialType.TryGetValue(jewelString[1], out var materialType))
                    {
                        currentSocketMaterialTypeId = (int)materialType;
                    }

                    if (Jewel.JewelQualityStringToValue.TryGetValue(jewelString[0], out var qualityLevel))
                    {
                        currentSocketQualityLevel = qualityLevel;
                    }
                }

                if (currentSocketMaterialTypeId is null or < 1 || currentSocketQualityLevel is null or < 1)
                {
                    _extraPropertiesText += "\n\t  Empty Jewel Socket\n";
                    continue;
                }

                _extraPropertiesText += Jewel.GetSocketDescription((MaterialType)currentSocketMaterialTypeId, currentSocketQualityLevel.Value);
            }
        }
    }

    private void SetDamagePenaltyUseText()
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.DamageRating, out var damageRating) || damageRating >= 0)
        {
            return;
        }

        _extraPropertiesText += $"Damage Penalty: {damageRating}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetArmorModUseText(PropertyFloat propertyFloat, WorldObject wo, string text, float totalMod, float multiplierOne = 100.0f, float multiplierTwo = 100.0f)
    {
        if (!PropertiesFloat.TryGetValue(propertyFloat, out var armorMod) || !(armorMod >= 0.001))
        {
            return;
        }

        var wielder = (Creature)wo.Wielder;

        var mod = Math.Round((armorMod) * multiplierOne, 1);
        var finalText = text.Replace("(ONE)", $"{mod}");

        if (wielder != null && totalMod != 0.0f)
        {
            totalMod = (float)Math.Round((totalMod * multiplierTwo), 2);

            finalText += $"  ({totalMod}%)";

            _extraPropertiesText = finalText + "\n";
        }
        else
        {
            _extraPropertiesText += finalText + "\n";
        }

        _hasExtraPropertiesText = true;
    }

    private void SetJewelryManaConUseText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.ManaConversionMod, out var manaConversionMod) ||
            !(manaConversionMod >= 0.001))
        {
            return;
        }

        if (wo.ItemType == ItemType.Jewelry || wo.ItemType == ItemType.Armor || wo.ItemType == ItemType.Clothing)
        {
            _extraPropertiesText += $"Bonus to Mana Conversion Skill: +{Math.Round(manaConversionMod * 100, 1)}%\n";
        }

        _hasExtraPropertiesText = true;
    }

    private void SetArmorResourcePenaltyUseText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.ArmorResourcePenalty, out var armoResourcePenalty) ||
            !(armoResourcePenalty >= 0.001))
        {
            return;
        }

        var wielder = (Creature)wo.Wielder;

        if (wielder != null)
        {
            var totalArmorResourcePenalty = wielder.GetArmorResourcePenalty();
            _extraPropertiesText +=
                $"Penalty to Stamina/Mana usage: {Math.Round((armoResourcePenalty) * 100, 1)}%  ({Math.Round((double)(totalArmorResourcePenalty * 100), 2)}%)\n";
        }
        else
        {
            _extraPropertiesText +=
                $"Penalty to Stamina/Mana usage: {Math.Round((armoResourcePenalty) * 100, 1)}%\n";
        }

        _hasExtraPropertiesText = true;
    }

    private void SetArmorWardLevelUseText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.WardLevel, out var wardLevel) || wardLevel == 0)
        {
            return;
        }

        var wielder = (Creature)wo.Wielder;

        if (wielder != null)
        {
            var totalWardLevel = wielder.GetWardLevel();
            _extraPropertiesText += $"Ward Level: {wardLevel}  ({totalWardLevel})\n";
        }
        else
        {
            _extraPropertiesText += $"Ward Level: {wardLevel}\n\n";
        }

        _hasExtraPropertiesText = true;
    }

    private void SetArmorWeightClassUseText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.ArmorWeightClass, out var armorWieghtClass) || armorWieghtClass <= 0)
        {
            return;
        }

        var weightClassText = "";

        if (wo.ArmorWeightClass == (int)ArmorWeightClass.Cloth)
        {
            weightClassText = "Cloth";
        }
        else if (wo.ArmorWeightClass == (int)ArmorWeightClass.Light)
        {
            weightClassText = "Light";
        }
        else if (wo.ArmorWeightClass == (int)ArmorWeightClass.Heavy)
        {
            weightClassText = "Heavy";
        }

        _extraPropertiesText += $"Weight Class: {weightClassText}\n";

        _hasExtraPropertiesText = true;
    }

    private void SetBowElementalWarningUseText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.DamageType, out var damageType) ||
            damageType == (int)DamageType.Undef)
        {
            return;
        }

        if (!wo.IsAmmoLauncher)
        {
            return;
        }

        var element = "";
        switch (damageType)
        {
            case (int)DamageType.Slash:
                element = "slashing";
                break;
            case (int)DamageType.Pierce:
                element = "piercing";
                break;
            case (int)DamageType.Bludgeon:
                element = "bludgeoning";
                break;
            case (int)DamageType.Acid:
                element = "acid";
                break;
            case (int)DamageType.Fire:
                element = "fire";
                break;
            case (int)DamageType.Cold:
                element = "cold";
                break;
            case (int)DamageType.Electric:
                element = "electric";
                break;
            default:
                element = "";
                break;
        }

        _extraPropertiesText += $"\nThe Damage Modifier on this weapon only applies to {element} damage.\n";

        _hasExtraPropertiesText = true;
    }

    private void SetWeaponRestoModUseText()
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.WeaponRestorationSpellsMod, out var weaponLifeMagicVitalMod) ||
            !(weaponLifeMagicVitalMod >= 1.001))
        {
            return;
        }

        _extraPropertiesText += $"Healing Bonus for Restoration Spells: +{Math.Round((weaponLifeMagicVitalMod - 1) * 100, 1)}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetWeaponMagicDefenseUseText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.WeaponMagicalDefense, out var weaponMagicalDefense) ||
            !(weaponMagicalDefense > 1.001))
        {
            return;
        }

        var weaponMod = (weaponMagicalDefense + wo.EnchantmentManager.GetAdditiveMod(PropertyFloat.WeaponPhysicalDefense) - 1) * 100;

        _extraPropertiesText += $"Bonus to Magic Defense: +{Math.Round(weaponMod, 1)}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetWeaponPhysicalDefenseUseText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.WeaponPhysicalDefense, out var weaponPhysicalDefense) ||
            !(weaponPhysicalDefense > 1.001))
        {
            return;
        }

        var weaponMod = (weaponPhysicalDefense + wo.EnchantmentManager.GetAdditiveMod(PropertyFloat.WeaponPhysicalDefense) - 1) * 100;

        _extraPropertiesText += $"Bonus to Physical Defense: +{Math.Round(weaponMod, 1)}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetWeaponLifeMagicUseText()
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.WeaponLifeMagicMod, out var weaponLifeMagicMod) ||
            !(weaponLifeMagicMod >= 0.001))
        {
            return;
        }

        _extraPropertiesText += $"Bonus to Life Magic Skill: +{Math.Round((weaponLifeMagicMod) * 100, 1)}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetWeaponWarMagicUseText()
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.WeaponWarMagicMod, out var weaponWarMagicMod) ||
            !(weaponWarMagicMod >= 0.001))
        {
            return;
        }

        _extraPropertiesText += $"Bonus to War Magic Skill: +{Math.Round((weaponWarMagicMod) * 100, 1)}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetBowAttackModUseText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.WeaponOffense, out var weaponOffense) ||
            !(weaponOffense > 1.001))
        {
            return;
        }

        var weaponMod = (weaponOffense - 1) * 100;
        if (wo.WeaponSkill is Skill.Bow or Skill.Crossbow or Skill.MissileWeapons)
        {
            _extraPropertiesText += $"Bonus to Attack Skill: +{Math.Round(weaponMod, 1)}%\n";
        }

        _hasExtraPropertiesText = true;
    }

    private void SetAmmoEffectUseText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.AmmoEffectUsesRemaining, out var ammoEffectUsesRemaining) ||
            !(ammoEffectUsesRemaining > 0))
        {
            return;
        }

        if (!PropertiesInt.TryGetValue(PropertyInt.AmmoEffect, out var ammoEffect) ||
            !(ammoEffect >= 0))
        {
            return;
        }

        var ammoEffectString = Regex.Replace(((AmmoEffect)ammoEffect).ToString(), "(\\B[A-Z])", " $1");

        if (wo.WeenieType is WeenieType.Ammunition)
        {
            _extraPropertiesText += $"Ammo Effect: {ammoEffectString}\n";
            _extraPropertiesText += $"Effect Uses Remaining: {ammoEffectUsesRemaining}\n";

            var propertyDescription = "";

            switch ((AmmoEffect)ammoEffect)
            {
                case AmmoEffect.Sharpened:
                    propertyDescription = "~Sharpened: Increases damage by 10%.";
                    break;
            }

            _additionalPropertiesLongDescriptionsText += $"{propertyDescription}";
            _hasExtraPropertiesText = true;
        }
    }

    private void SetSpellProcRateUseText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.ProcSpellRate, out var procSpellRate) || !(procSpellRate > 0.0f))
        {
            return;
        }

        if (wo.ProcSpell is null)
        {
            return;
        }

        var wielder = (Creature)wo.Wielder;

        _extraPropertiesText += $"Cast on strike chance: {Math.Round(procSpellRate * 100, 1)}%\n";

        _hasExtraPropertiesText = true;
    }

    private void SetAdditionalPropertiesUseText(WorldObject wo)
    {
        if (!_hasAdditionalProperties)
        {
            return;
        }

        var additionaPropertiesString = "";

        foreach (var property in _additionalPropertiesList)
        {
            additionaPropertiesString += property + ", ";
        }

        // Use
        additionaPropertiesString = additionaPropertiesString.TrimEnd();
        additionaPropertiesString = additionaPropertiesString.TrimEnd(',');

        var oomText = wo.Workmanship != null ? "" : "This item's properties will not activate if it is out of mana";

        _extraPropertiesText += $"Additional Properties: {additionaPropertiesString}.\n\n{oomText}\n\n";

        _hasExtraPropertiesText = true;
    }

    private void SetStaminaReductionUseLongText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.StaminaCostReductionMod, out var staminaCostReductionMod) ||
            !(staminaCostReductionMod > 0.001f))
        {
            return;
        }

        _additionalPropertiesList.Add("Stamina Cost Reduction");

        var ratingAmount = Math.Round((staminaCostReductionMod * 100), 0);

        var itemTier = LootGenerationFactory.GetTierFromWieldDifficulty(wo.WieldDifficulty ?? 1);
        var rangeMinAtTier = Math.Round(LootTables.StaminaCostReductionPerTier[itemTier - 1] * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Stamina Cost Reduction: Reduces stamina cost of attack by {ratingAmount}%. " +
            $"Roll range is based on item tier ({rangeMinAtTier}% to {rangeMinAtTier + 10}%).\n";
    }

    private void SetBitingStrikeUseLongText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.CriticalFrequency, out var critFrequency) ||
            !(critFrequency > 0.0f))
        {
            return;
        }

        var wielder = (Creature)wo.Wielder;

        var ratingAmount = Math.Round((critFrequency - 0.1) * 100, 0);

        var itemTier = LootGenerationFactory.GetTierFromWieldDifficulty(wo.WieldDifficulty ?? 1);
        var rangeMinAtTier = Math.Round(LootTables.BonusCritChancePerTier[itemTier - 1] * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Biting Strike: Increases critical chance by +{ratingAmount}%, additively. " +
            $"Roll range is based on item tier ({rangeMinAtTier}% to {rangeMinAtTier + 5}%).\n";
    }

    private void SetCriticalStrikeUseLongText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.ImbuedEffect, out var imbuedEffectCriticalStrike) ||
            imbuedEffectCriticalStrike != (int)ImbuedEffectType.CriticalStrike)
        {
            return;
        }

        var wielder = wo.Wielder as Player;

        if (wo.OwnerId == null && wielder == null)
        {
            return;
        }

        var owner = wielder ?? PlayerManager.GetOnlinePlayer(wo.OwnerId.Value);

        if (owner == null)
        {
            return;
        }

        var criticalStrikeAmount = WorldObject.GetCriticalStrikeMod(owner.GetCreatureSkill(wo.WeaponSkill), owner);
        var amountFormatted = Math.Round(criticalStrikeAmount * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Critical Strike: Increases critical chance by +{amountFormatted}%, additively. " +
            $"Value is based on wielder attack skill (5% to 10%).\n";
    }

    private void SetCrushingBlowUseLongText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.CriticalMultiplier, out var critMultiplier) ||
            !(critMultiplier > 1))
        {
            return;
        }

        var wielder = (Creature)wo.Wielder;

        var ratingAmount = Math.Round((critMultiplier - 1) * 100, 0);

        var itemTier = LootGenerationFactory.GetTierFromWieldDifficulty(wo.WieldDifficulty ?? 1);
        var rangeMinAtTier = Math.Round(LootTables.BonusCritMultiplierPerTier[itemTier - 1] * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Crushing Blow: Increases critical damage by +{ratingAmount}%, additively. " +
            $"Roll range is based on item tier ({rangeMinAtTier}% to {rangeMinAtTier + 50}%)\n";
    }

    private void SetCripplingBlowUseLongText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.ImbuedEffect, out var imbuedEffectCripplingBlow) ||
            imbuedEffectCripplingBlow != (int)ImbuedEffectType.CripplingBlow)
        {
            return;
        }

        var wielder = wo.Wielder as Player;

        if (wo.OwnerId == null && wielder == null)
        {
            return;
        }

        var owner = wielder ?? PlayerManager.GetOnlinePlayer(wo.OwnerId.Value);

        if (owner == null)
        {
            return;
        }

        var cripplingBlowAmount = WorldObject.GetCripplingBlowMod(owner.GetCreatureSkill(wo.WeaponSkill), owner);
        var amountFormatted = Math.Round(cripplingBlowAmount * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Crippling Blow: Increases critical damage by +{amountFormatted}%, additively. " +
            $"Value is based on wielder attack skill (range: 50% to 100%).\n";
    }

    private void SetArmorCleavingUseLongText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.IgnoreArmor, out var ignoreArmor) || ignoreArmor == 0)
        {
            return;
        }

        var ratingAmount = 100 - Math.Round((ignoreArmor * 100), 0);

        var itemTier = LootGenerationFactory.GetTierFromWieldDifficulty(wo.WieldDifficulty ?? 1);
        var rangeMinAtTier = 10 + Math.Round(LootTables.BonusIgnoreArmorPerTier[itemTier - 1] * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Armor Cleaving: Increases armor ignored by {ratingAmount}%, additively. " +
            $"Roll range is based on item tier ({rangeMinAtTier}% to {rangeMinAtTier + 10}%)\n";
    }

    private void SetArmorRendUseLongText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.ImbuedEffect, out var imbuedEffectArmorRend) ||
            imbuedEffectArmorRend != (int)ImbuedEffectType.ArmorRending)
        {
            return;
        }

        var wielder = wo.Wielder as Player;

        if (wo.OwnerId == null && wielder == null)
        {
            return;
        }

        var owner = wielder ?? PlayerManager.GetOnlinePlayer(wo.OwnerId.Value);

        if (owner == null)
        {
            return;
        }

        var rendingAmount = WorldObject.GetArmorRendingMod(owner.GetCreatureSkill(wo.WeaponSkill), owner);
        var amountFormatted = Math.Round(rendingAmount * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Armor Rending: Increases armor ignored by {amountFormatted}%, additively. " +
            $"Value is based on wielder attack skill (10% to 20%).\n";
    }

    private void SetResistanceCleavingUseLongText(WorldObject wo)
    {
        if (!PropertiesFloat.TryGetValue(PropertyFloat.ResistanceModifier, out var resistanceModifier) || resistanceModifier == 0)
        {
            return;
        }

        var ratingAmount = Math.Round((resistanceModifier * 100), 0);

        var element = "";
        switch (wo.ResistanceModifierType)
        {
            case DamageType.Acid:
                element = "Acid";
                break;
            case DamageType.Bludgeon:
                element = "Bludgeoning";
                break;
            case DamageType.Cold:
                element = "Cold";
                break;
            case DamageType.Electric:
                element = "Lightning";
                break;
            case DamageType.Fire:
                element = "Fire";
                break;
            case DamageType.Pierce:
                element = "Piercing";
                break;
            case DamageType.Slash:
                element = "Slashing";
                break;
        }

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Resistance Cleaving ({element}): Increases {element.ToLower()} damage by +{ratingAmount}%, additively.\n";
    }

    private void SetResistanceRendLongText(ImbuedEffectType imbuedEffectType, WorldObject wo, string elementName)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.ImbuedEffect, out var imbuedEffect) ||
            imbuedEffect != (int)imbuedEffectType)
        {
            return;
        }

        var wielder = wo.Wielder as Player;

        if (wo.OwnerId == null && wielder == null)
        {
            return;
        }

        var owner = wielder ?? PlayerManager.GetOnlinePlayer(wo.OwnerId.Value);

        if (owner == null)
        {
            return;
        }

        var rendingAmount = WorldObject.GetRendingMod(owner.GetCreatureSkill(wo.WeaponSkill), owner);
        var amountFormatted = Math.Round(rendingAmount * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ {elementName} Rending: Increases {elementName.ToLower()} damage by +{amountFormatted}%, additively. " +
            $"Value is based on wielder attack skill (15% to 30%).\n";
    }

    private void SetWardCleavingUseLongText(WorldObject wo)
    {
        if (PropertiesFloat.TryGetValue(PropertyFloat.IgnoreWard, out var ignoreWard) && ignoreWard != 0)
        {
            _additionalPropertiesList.Add("Ward Cleaving");

            var ratingAmount = 100.0f - Math.Round((ignoreWard * 100), 0);

            var itemTier = LootGenerationFactory.GetTierFromWieldDifficulty(wo.WieldDifficulty ?? 1);
            var rangeMinAtTier = 10 + Math.Round(LootTables.BonusIgnoreWardPerTier[itemTier - 1] * 100, 0);

            _hasExtraPropertiesText = true;

            _additionalPropertiesLongDescriptionsText +=
                $"~ Ward Cleaving: Increases ward ignored by {ratingAmount}%, additively. " +
                $"Roll range is based on item tier ({rangeMinAtTier}% to {rangeMinAtTier + 10}%).\n";
        }
    }

    private void SetWardRendingUseLongText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.ImbuedEffect, out var imbuedEffect) || imbuedEffect != 0x8000)
        {
            return;
        }

        _additionalPropertiesList.Add("Ward Rending");

        var wielder = wo.Wielder as Player;

        if (wo.OwnerId == null && wielder == null)
        {
            return;
        }

        var owner = wielder ?? PlayerManager.GetOnlinePlayer(wo.OwnerId.Value);

        if (owner == null)
        {
            return;
        }

        var rendingAmount = WorldObject.GetWardRendingMod(owner.GetCreatureSkill(wo.WeaponSkill));
        var amountFormatted = Math.Round(rendingAmount * 100, 0);

        _hasExtraPropertiesText = true;

        _additionalPropertiesLongDescriptionsText +=
            $"~ Ward Rending: Increases ward ignored by +{amountFormatted}%, additively. " +
            $"Value is based on wielder attack skill (10% to 20%).\n";
    }

    private void SetNoCompsRequiredSchoolUseLongText(WorldObject wo)
    {
        if (!PropertiesInt.TryGetValue(PropertyInt.NoCompsRequiredForMagicSchool, out var noCompsForPortalSpells) ||
            noCompsForPortalSpells is 0)
        {
            return;
        }

        switch (noCompsForPortalSpells)
        {
            case (int)MagicSchool.WarMagic:
                _additionalPropertiesList.Add("War Primacy");
                _additionalPropertiesLongDescriptionsText +=
                    $"~ War Primacy: War Magic spells cast do not require or consume components. Spells from other schools cannot be cast.";
                break;
            case (int)MagicSchool.LifeMagic:
                _additionalPropertiesList.Add("Life Primacy");
                _additionalPropertiesLongDescriptionsText +=
                    $"~ Life Primacy: Life Magic spells cast do not require or consume components. Spells from other schools cannot be cast.";
                break;
            case (int)MagicSchool.PortalMagic:
                _additionalPropertiesList.Add("Portal Primacy");
                _additionalPropertiesLongDescriptionsText +=
                    $"~ Portal Primacy: Portal Magic spells cast do not require or consume components. Spells from other schools cannot be cast.";
                break;
        }


        _hasExtraPropertiesText = true;
        _hasAdditionalProperties = true;

    }

    private void SetProtectionLevelsUseText(WorldObject wo)
    {
        if (PropertiesInt.TryGetValue(PropertyInt.ArmorLevel, out var armorLevel) && armorLevel == 0 && wo.ArmorWeightClass == (int)ArmorWeightClass.Cloth)
        {
            var slashingMod = (float)(wo.ArmorModVsSlash ?? 1.0f);
            var piercingMod = (float)(wo.ArmorModVsPierce ?? 1.0f);
            var bludgeoningMod = (float)(wo.ArmorModVsBludgeon ?? 1.0f);
            var fireMod = (float)(wo.ArmorModVsFire ?? 1.0f);
            var coldMod = (float)(wo.ArmorModVsCold ?? 1.0f);
            var acidMod = (float)(wo.ArmorModVsAcid ?? 1.0f);
            var electricMod = (float)(wo.ArmorModVsElectric ?? 1.0f);

            _extraPropertiesText += $"Slashing: {GetProtectionLevelText(slashingMod)} ({string.Format("{0:0.00}", slashingMod)}) \n";
            _extraPropertiesText += $"Piercing: {GetProtectionLevelText(piercingMod)} ({string.Format("{0:0.00}", piercingMod)}) \n";
            _extraPropertiesText += $"Bludgeoning: {GetProtectionLevelText(bludgeoningMod)} ({string.Format("{0:0.00}", bludgeoningMod)}) \n";
            _extraPropertiesText += $"Fire: {GetProtectionLevelText(fireMod)} ({string.Format("{0:0.00}", fireMod)}) \n";
            _extraPropertiesText += $"Cold: {GetProtectionLevelText(coldMod)} ({string.Format("{0:0.00}", coldMod)}) \n";
            _extraPropertiesText += $"Acid: {GetProtectionLevelText(acidMod)} ({string.Format("{0:0.00}", acidMod)}) \n";
            _extraPropertiesText += $"Electric: {GetProtectionLevelText(electricMod)} ({string.Format("{0:0.00}", electricMod)}) \n\n";

            _hasExtraPropertiesText = true;
        }
    }

    private void SetGearRatingText(WorldObject worldObject, PropertyInt propertyInt, string name, string description, float multiplierOne = 1.0f, float multiplierTwo = 1.0f, float baseOne = 0.0f, float baseTwo = 0.0f, bool percent = false)
    {
        var itemGearRating = worldObject.GetProperty(propertyInt) ?? 0;
        var jewelGearRating = WorldObject.GetJewelRating(worldObject, propertyInt);
        var totalRatingOnItem = itemGearRating + jewelGearRating;

        if (totalRatingOnItem < 1)
        {
            return;
        }

        var ratingRomanNumeral = ToRoman(totalRatingOnItem);

        _additionalPropertiesList.Add($"{name} {ratingRomanNumeral}");

        _hasAdditionalProperties = true;

        var ratingFromAllEquippedItems = 0.0f;
        if (worldObject.Wielder is Player wielder)
        {
            ratingFromAllEquippedItems = wielder.GetEquippedAndActivatedItemRatingSum(propertyInt);
        }

        var percentSign = percent ? "%" : "";
        var amountOne = Math.Round(baseOne + ratingFromAllEquippedItems * multiplierOne, 2);
        var amountTwo = Math.Round(baseTwo + ratingFromAllEquippedItems * multiplierTwo, 2);
;
        var desc = description.Replace("(ONE)", $"{amountOne}{percentSign}");
        desc = desc.Replace("(TWO)", $"{amountTwo}{percentSign}");

        _additionalPropertiesLongDescriptionsText += $"~ {name}: {desc}\n";
    }

    private static string ToRoman(int number)
    {
        return number switch
        {
            < 0 or > 3999 => throw new ArgumentOutOfRangeException(nameof(number), "insert value between 1 and 3999"),
            < 1 => string.Empty,
            >= 1000 => "M" + ToRoman(number - 1000),
            >= 900 => "CM" + ToRoman(number - 900),
            >= 500 => "D" + ToRoman(number - 500),
            >= 400 => "CD" + ToRoman(number - 400),
            >= 100 => "C" + ToRoman(number - 100),
            >= 90 => "XC" + ToRoman(number - 90),
            >= 50 => "L" + ToRoman(number - 50),
            >= 40 => "XL" + ToRoman(number - 40),
            >= 10 => "X" + ToRoman(number - 10),
            >= 9 => "IX" + ToRoman(number - 9),
            >= 5 => "V" + ToRoman(number - 5),
            >= 4 => "IV" + ToRoman(number - 4),
            >= 1 => "I" + ToRoman(number - 1)
        };
    }

    private string GetProtectionLevelText(float protectionMod)
    {
        switch (protectionMod)
        {
            case <= 0.39f:
                return "Poor";
            case <= 0.79f:
                return "Below Average";
            case <= 1.19f:
                return "Average";
            case <= 1.59f:
                return "Above Average";
            default:
                return "Unparalleled";
        }
    }

    private void BuildSpells(WorldObject wo)
    {
        SpellBook = new List<uint>();

        if (wo is Creature)
        {
            return;
        }

        // add primary spell, if exists
        if (wo.SpellDID.HasValue)
        {
            SpellBook.Add(wo.SpellDID.Value);
        }

        // add proc spell, if exists
        if (wo.ProcSpell.HasValue)
        {
            SpellBook.Add(wo.ProcSpell.Value);
        }

        var woSpellDID = wo.SpellDID; // prevent recursive lock
        var woProcSpell = wo.ProcSpell;

        foreach (
            var spellId in wo.Biota.GetKnownSpellsIdsWhere(
                i => i != woSpellDID && i != woProcSpell,
                wo.BiotaDatabaseLock
            )
        )
        {
            SpellBook.Add((uint)spellId);
        }
    }

    private void AddEnchantments(WorldObject wo)
    {
        if (wo == null)
        {
            return;
        }

        // get all currently active item enchantments on the item
        var woEnchantments = wo.EnchantmentManager.GetEnchantments(MagicSchool.PortalMagic);

        foreach (var enchantment in woEnchantments)
        {
            SpellBook.Add((uint)enchantment.SpellId | EnchantmentMask);
        }

        // show auras from wielder, if applicable

        // this technically wasn't a feature in retail

        if (
            wo.Wielder != null
            && wo.IsEnchantable
            && wo.WeenieType != WeenieType.Clothing
            && !wo.IsShield
            && PropertyManager.GetBool("show_aura_buff").Item
        )
        {
            // get all currently active item enchantment auras on the player
            var wielderEnchantments = wo.Wielder.EnchantmentManager.GetEnchantments(MagicSchool.PortalMagic);

            // Only show reflected Auras from player appropriate for wielded weapons
            foreach (var enchantment in wielderEnchantments)
            {
                if (wo is Caster)
                {
                    // Caster weapon only item Auras
                    if (
                        (enchantment.SpellCategory == SpellCategory.DefenseModRaising)
                        || (enchantment.SpellCategory == SpellCategory.DefenseModRaisingRare)
                        || (enchantment.SpellCategory == SpellCategory.ManaConversionModRaising)
                        || (enchantment.SpellCategory == SpellCategory.SpellDamageRaising)
                    )
                    {
                        SpellBook.Add((uint)enchantment.SpellId | EnchantmentMask);
                    }
                }
                else if (wo is Missile || wo is Ammunition)
                {
                    if (
                        (enchantment.SpellCategory == SpellCategory.DamageRaising)
                        || (enchantment.SpellCategory == SpellCategory.DamageRaisingRare)
                    )
                    {
                        SpellBook.Add((uint)enchantment.SpellId | EnchantmentMask);
                    }
                }
                else
                {
                    // Other weapon type Auras
                    if (
                        (enchantment.SpellCategory == SpellCategory.AttackModRaising)
                        || (enchantment.SpellCategory == SpellCategory.AttackModRaisingRare)
                        || (enchantment.SpellCategory == SpellCategory.DamageRaising)
                        || (enchantment.SpellCategory == SpellCategory.DamageRaisingRare)
                        || (enchantment.SpellCategory == SpellCategory.DefenseModRaising)
                        || (enchantment.SpellCategory == SpellCategory.DefenseModRaisingRare)
                        || (enchantment.SpellCategory == SpellCategory.WeaponTimeRaising)
                        || (enchantment.SpellCategory == SpellCategory.WeaponTimeRaisingRare)
                    )
                    {
                        SpellBook.Add((uint)enchantment.SpellId | EnchantmentMask);
                    }
                }
            }
        }
    }

    private void BuildArmor(WorldObject wo)
    {
        if (!Success)
        {
            return;
        }

        ArmorProfile = new ArmorProfile(wo);
        ArmorHighlight = ArmorMaskHelper.GetHighlightMask(wo, IsArmorCapped || IsArmorBuffed);
        ArmorColor = ArmorMaskHelper.GetColorMask(wo, IsArmorBuffed);

        AddEnchantments(wo);
    }

    private void BuildCreature(Creature creature)
    {
        CreatureProfile = new CreatureProfile(creature, Success);

        // only creatures?
        ResistHighlight = ResistMaskHelper.GetHighlightMask(creature);
        ResistColor = ResistMaskHelper.GetColorMask(creature);

        if (Success && (creature is Player || !creature.Attackable))
        {
            ArmorLevels = new ArmorLevel(creature);
        }

        AddRatings(creature);

        if (NPCLooksLikeObject)
        {
            var weenie = creature.Weenie ?? DatabaseManager.World.GetCachedWeenie(creature.WeenieClassId);

            if (!weenie.GetProperty(PropertyInt.EncumbranceVal).HasValue)
            {
                PropertiesInt.Remove(PropertyInt.EncumbranceVal);
            }
        }
        else
        {
            PropertiesInt.Remove(PropertyInt.EncumbranceVal);
        }

        // see notes in CombatPet.Init()
        if (creature is CombatPet && PropertiesInt.ContainsKey(PropertyInt.Faction1Bits))
        {
            PropertiesInt.Remove(PropertyInt.Faction1Bits);
        }
    }

    private void AddRatings(Creature creature)
    {
        if (!Success)
        {
            return;
        }

        var damageRating = creature.GetDamageRating();

        // include heritage / weapon type rating?
        var weapon = creature.GetEquippedWeapon() ?? creature.GetEquippedWand();
        if (creature.GetHeritageBonus(weapon))
        {
            damageRating += 5;
        }

        // factor in weakness here?

        var damageResistRating = creature.GetDamageResistRating();

        // factor in nether dot damage here?

        var critRating = creature.GetCritRating();
        var critDamageRating = creature.GetCritDamageRating();

        var critResistRating = creature.GetCritResistRating();
        var critDamageResistRating = creature.GetCritDamageResistRating();

        var healingBoostRating = creature.GetHealingBoostRating();
        var dotResistRating = creature.GetDotResistanceRating();
        var netherResistRating = creature.GetNetherResistRating();

        var lifeResistRating = creature.GetLifeResistRating(); // drain / harm resistance
        var gearMaxHealth = creature.GetGearMaxHealth();

        var pkDamageRating = creature.GetPKDamageRating();
        var pkDamageResistRating = creature.GetPKDamageResistRating();

        if (damageRating != 0)
        {
            PropertiesInt[PropertyInt.DamageRating] = damageRating;
        }

        if (damageResistRating != 0)
        {
            PropertiesInt[PropertyInt.DamageResistRating] = damageResistRating;
        }

        if (critRating != 0)
        {
            PropertiesInt[PropertyInt.CritRating] = critRating;
        }

        if (critDamageRating != 0)
        {
            PropertiesInt[PropertyInt.CritDamageRating] = critDamageRating;
        }

        if (critResistRating != 0)
        {
            PropertiesInt[PropertyInt.CritResistRating] = critResistRating;
        }

        if (critDamageResistRating != 0)
        {
            PropertiesInt[PropertyInt.CritDamageResistRating] = critDamageResistRating;
        }

        if (healingBoostRating != 0)
        {
            PropertiesInt[PropertyInt.HealingBoostRating] = healingBoostRating;
        }

        if (netherResistRating != 0)
        {
            PropertiesInt[PropertyInt.NetherResistRating] = netherResistRating;
        }

        if (dotResistRating != 0)
        {
            PropertiesInt[PropertyInt.DotResistRating] = dotResistRating;
        }

        if (lifeResistRating != 0)
        {
            PropertiesInt[PropertyInt.LifeResistRating] = lifeResistRating;
        }

        if (gearMaxHealth != 0)
        {
            PropertiesInt[PropertyInt.GearMaxHealth] = gearMaxHealth;
        }

        if (pkDamageRating != 0)
        {
            PropertiesInt[PropertyInt.PKDamageRating] = pkDamageRating;
        }

        if (pkDamageResistRating != 0)
        {
            PropertiesInt[PropertyInt.PKDamageResistRating] = pkDamageResistRating;
        }

        // add ratings from equipped items?
    }

    private void BuildWeapon(WorldObject weapon)
    {
        if (!Success)
        {
            return;
        }

        var weaponProfile = new WeaponProfile(weapon);

        //WeaponHighlight = WeaponMaskHelper.GetHighlightMask(weapon, wielder);
        //WeaponColor = WeaponMaskHelper.GetColorMask(weapon, wielder);
        WeaponHighlight = WeaponMaskHelper.GetHighlightMask(weaponProfile, IsAttackModBuffed);
        WeaponColor = WeaponMaskHelper.GetColorMask(weaponProfile, IsAttackModBuffed);

        if (!(weapon is Caster))
        {
            WeaponProfile = weaponProfile;
        }

        // item enchantments can also be on wielder currently
        AddEnchantments(weapon);
    }

    private void BuildHookProfile(WorldObject hookedItem)
    {
        HookProfile = new HookProfile();
        if (hookedItem.Inscribable)
        {
            HookProfile.Flags |= HookFlags.Inscribable;
        }

        if (hookedItem is Healer)
        {
            HookProfile.Flags |= HookFlags.IsHealer;
        }

        if (hookedItem is Food)
        {
            HookProfile.Flags |= HookFlags.IsFood;
        }

        if (hookedItem is Lockpick)
        {
            HookProfile.Flags |= HookFlags.IsLockpick;
        }

        if (hookedItem.ValidLocations != null)
        {
            HookProfile.ValidLocations = hookedItem.ValidLocations.Value;
        }

        if (hookedItem.AmmoType != null)
        {
            HookProfile.AmmoType = hookedItem.AmmoType.Value;
        }
    }

    /// <summary>
    /// Constructs the bitflags for appraising a WorldObject
    /// </summary>
    private void BuildFlags()
    {
        if (PropertiesInt.Count > 0)
        {
            Flags |= IdentifyResponseFlags.IntStatsTable;
        }

        if (PropertiesInt64.Count > 0)
        {
            Flags |= IdentifyResponseFlags.Int64StatsTable;
        }

        if (PropertiesBool.Count > 0)
        {
            Flags |= IdentifyResponseFlags.BoolStatsTable;
        }

        if (PropertiesFloat.Count > 0)
        {
            Flags |= IdentifyResponseFlags.FloatStatsTable;
        }

        if (PropertiesString.Count > 0)
        {
            Flags |= IdentifyResponseFlags.StringStatsTable;
        }

        if (PropertiesDID.Count > 0)
        {
            Flags |= IdentifyResponseFlags.DidStatsTable;
        }

        if (SpellBook.Count > 0)
        {
            Flags |= IdentifyResponseFlags.SpellBook;
        }

        if (ResistHighlight != 0)
        {
            Flags |= IdentifyResponseFlags.ResistEnchantmentBitfield;
        }

        if (ArmorProfile != null)
        {
            Flags |= IdentifyResponseFlags.ArmorProfile;
        }

        if (CreatureProfile != null && !NPCLooksLikeObject)
        {
            Flags |= IdentifyResponseFlags.CreatureProfile;
        }

        if (WeaponProfile != null)
        {
            Flags |= IdentifyResponseFlags.WeaponProfile;
        }

        if (HookProfile != null)
        {
            Flags |= IdentifyResponseFlags.HookProfile;
        }

        if (ArmorHighlight != 0)
        {
            Flags |= IdentifyResponseFlags.ArmorEnchantmentBitfield;
        }

        if (WeaponHighlight != 0)
        {
            Flags |= IdentifyResponseFlags.WeaponEnchantmentBitfield;
        }

        if (ArmorLevels != null)
        {
            Flags |= IdentifyResponseFlags.ArmorLevels;
        }
    }
}

public static class AppraiseInfoExtensions
{
    /// <summary>
    /// Writes the AppraiseInfo to the network stream
    /// </summary>
    public static void Write(this BinaryWriter writer, AppraiseInfo info)
    {
        writer.Write((uint)info.Flags);
        writer.Write(Convert.ToUInt32(info.Success));
        if (info.Flags.HasFlag(IdentifyResponseFlags.IntStatsTable))
        {
            writer.Write(info.PropertiesInt);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.Int64StatsTable))
        {
            writer.Write(info.PropertiesInt64);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.BoolStatsTable))
        {
            writer.Write(info.PropertiesBool);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.FloatStatsTable))
        {
            writer.Write(info.PropertiesFloat);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.StringStatsTable))
        {
            writer.Write(info.PropertiesString);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.DidStatsTable))
        {
            writer.Write(info.PropertiesDID);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.SpellBook))
        {
            writer.Write(info.SpellBook);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.ArmorProfile))
        {
            writer.Write(info.ArmorProfile);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.CreatureProfile))
        {
            writer.Write(info.CreatureProfile);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.WeaponProfile))
        {
            writer.Write(info.WeaponProfile);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.HookProfile))
        {
            writer.Write(info.HookProfile);
        }

        if (info.Flags.HasFlag(IdentifyResponseFlags.ArmorEnchantmentBitfield))
        {
            writer.Write((ushort)info.ArmorHighlight);
            writer.Write((ushort)info.ArmorColor);
        }
        if (info.Flags.HasFlag(IdentifyResponseFlags.WeaponEnchantmentBitfield))
        {
            writer.Write((ushort)info.WeaponHighlight);
            writer.Write((ushort)info.WeaponColor);
        }
        if (info.Flags.HasFlag(IdentifyResponseFlags.ResistEnchantmentBitfield))
        {
            writer.Write((ushort)info.ResistHighlight);
            writer.Write((ushort)info.ResistColor);
        }
        if (info.Flags.HasFlag(IdentifyResponseFlags.ArmorLevels))
        {
            writer.Write(info.ArmorLevels);
        }
    }

    private static readonly PropertyIntComparer PropertyIntComparer = new PropertyIntComparer(16);
    private static readonly PropertyInt64Comparer PropertyInt64Comparer = new PropertyInt64Comparer(8);
    private static readonly PropertyBoolComparer PropertyBoolComparer = new PropertyBoolComparer(8);
    private static readonly PropertyFloatComparer PropertyFloatComparer = new PropertyFloatComparer(8);
    private static readonly PropertyStringComparer PropertyStringComparer = new PropertyStringComparer(8);
    private static readonly PropertyDataIdComparer PropertyDataIdComparer = new PropertyDataIdComparer(8);

    // TODO: generics
    public static void Write(this BinaryWriter writer, Dictionary<PropertyInt, int> _properties)
    {
        PackableHashTable.WriteHeader(writer, _properties.Count, PropertyIntComparer.NumBuckets);

        var properties = new SortedDictionary<PropertyInt, int>(_properties, PropertyIntComparer);

        foreach (var kvp in properties)
        {
            writer.Write((uint)kvp.Key);
            writer.Write(kvp.Value);
        }
    }

    public static void Write(this BinaryWriter writer, Dictionary<PropertyInt64, long> _properties)
    {
        PackableHashTable.WriteHeader(writer, _properties.Count, PropertyInt64Comparer.NumBuckets);

        var properties = new SortedDictionary<PropertyInt64, long>(_properties, PropertyInt64Comparer);

        foreach (var kvp in properties)
        {
            writer.Write((uint)kvp.Key);
            writer.Write(kvp.Value);
        }
    }

    public static void Write(this BinaryWriter writer, Dictionary<PropertyBool, bool> _properties)
    {
        PackableHashTable.WriteHeader(writer, _properties.Count, PropertyBoolComparer.NumBuckets);

        var properties = new SortedDictionary<PropertyBool, bool>(_properties, PropertyBoolComparer);

        foreach (var kvp in properties)
        {
            writer.Write((uint)kvp.Key);
            writer.Write(Convert.ToUInt32(kvp.Value));
        }
    }

    public static void Write(this BinaryWriter writer, Dictionary<PropertyFloat, double> _properties)
    {
        PackableHashTable.WriteHeader(writer, _properties.Count, PropertyFloatComparer.NumBuckets);

        var properties = new SortedDictionary<PropertyFloat, double>(_properties, PropertyFloatComparer);

        foreach (var kvp in properties)
        {
            writer.Write((uint)kvp.Key);
            writer.Write(kvp.Value);
        }
    }

    public static void Write(this BinaryWriter writer, Dictionary<PropertyString, string> _properties)
    {
        PackableHashTable.WriteHeader(writer, _properties.Count, PropertyStringComparer.NumBuckets);

        var properties = new SortedDictionary<PropertyString, string>(_properties, PropertyStringComparer);

        foreach (var kvp in properties)
        {
            writer.Write((uint)kvp.Key);
            writer.WriteString16L(kvp.Value);
        }
    }

    public static void Write(this BinaryWriter writer, Dictionary<PropertyDataId, uint> _properties)
    {
        PackableHashTable.WriteHeader(writer, _properties.Count, PropertyDataIdComparer.NumBuckets);

        var properties = new SortedDictionary<PropertyDataId, uint>(_properties, PropertyDataIdComparer);

        foreach (var kvp in properties)
        {
            writer.Write((uint)kvp.Key);
            writer.Write(kvp.Value);
        }
    }
}
