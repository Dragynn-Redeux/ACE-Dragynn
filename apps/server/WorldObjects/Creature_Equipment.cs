using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    public bool EquippedObjectsLoaded { get; private set; }

    /// <summary>
    /// Use EquipObject() and DequipObject() to manipulate this dictionary..<para />
    /// Do not manipulate this dictionary directly.
    /// </summary>
    public Dictionary<ObjectGuid, WorldObject> EquippedObjects { get; } = new Dictionary<ObjectGuid, WorldObject>();

    /// <summary>
    /// The only time this should be used is to populate EquippedObjects from the ctor.
    /// </summary>
    protected void AddBiotasToEquippedObjects(IEnumerable<ACE.Database.Models.Shard.Biota> wieldedItems)
    {
        foreach (var biota in wieldedItems)
        {
            var worldObject = WorldObjectFactory.CreateWorldObject(biota);
            EquippedObjects[worldObject.Guid] = worldObject;

            AddItemToEquippedItemsRatingCache(worldObject);
            AddItemToEquippedItemsSkillModCache(worldObject);

            if (worldObject.WeenieType == WeenieType.Ammunition || worldObject.WeenieType == WeenieType.Missile)
            {
                EncumbranceVal += (int)Math.Ceiling((worldObject.EncumbranceVal ?? 0) / 2.0f);
            }
            else
            {
                EncumbranceVal += (worldObject.EncumbranceVal ?? 0);
            }
        }

        EquippedObjectsLoaded = true;

        SetChildren();
    }

    public bool WieldedLocationIsAvailable(WorldObject item, EquipMask wieldedLocation)
    {
        // filtering to just armor here, or else trinkets and dual wielding breaks
        // update: cannot repro the break anymore?
        //var existing = this is Player ? GetEquippedClothingArmor(item.ClothingPriority ?? 0) : GetEquippedItems(item, wieldedLocation);
        var existing = GetEquippedItems(item, wieldedLocation);

        // TODO: handle overlap from MeleeWeapon / MissileWeapon / Held

        return existing.Count == 0;
    }

    public bool HasEquippedItem(ObjectGuid objectGuid)
    {
        return EquippedObjects.ContainsKey(objectGuid);
    }

    /// <summary>
    /// Get Wielded Item. Returns null if not found.
    /// </summary>
    public WorldObject GetEquippedItem(uint objectGuid)
    {
        return EquippedObjects.TryGetValue(new ObjectGuid(objectGuid), out var item) ? item : null; // todo fix this so it doesn't instantiate a new ObjectGuid
    }

    /// <summary>
    /// Get Wielded Item. Returns null if not found.
    /// </summary>
    public WorldObject GetEquippedItem(ObjectGuid objectGuid)
    {
        return EquippedObjects.TryGetValue(objectGuid, out var item) ? item : null;
    }

    /// <summary>
    /// Returns a list of equipped clothing/armor with any coverage overlap
    /// </summary>
    public List<WorldObject> GetEquippedClothingArmor(CoverageMask coverageMask)
    {
        return EquippedObjects
            .Values.Where(i => i.ClothingPriority != null && (i.ClothingPriority & coverageMask) != 0)
            .ToList();
    }

    /// <summary>
    /// Returns a list of equipped items with any overlap with input locations
    /// </summary>
    public List<WorldObject> GetEquippedItems(WorldObject item, EquipMask wieldedLocation)
    {
        if (IsWeaponSlot(wieldedLocation))
        {
            // TODO: change to coalesced CurrentWieldedLocation
            GetPlacementLocation(item, wieldedLocation, out var placement, out var parentLocation);
            return EquippedObjects
                .Values.Where(i =>
                    i.ParentLocation != null
                    && i.ParentLocation == parentLocation
                    && i.CurrentWieldedLocation != EquipMask.MissileAmmo
                )
                .ToList();
        }

        if (item is Clothing)
        {
            return GetEquippedClothingArmor(item.ClothingPriority ?? 0);
        }
        else
        {
            return EquippedObjects
                .Values.Where(i =>
                    i.CurrentWieldedLocation != null && (i.CurrentWieldedLocation & wieldedLocation) != 0
                )
                .ToList();
        }
    }

    /// <summary>
    /// Returns the currently equipped primary weapon
    /// </summary>
    public WorldObject GetEquippedWeapon(bool forceMainHand = false)
    {
        var meleeWeapon = GetEquippedMeleeWeapon(forceMainHand);
        return meleeWeapon ?? GetEquippedMissileWeapon() ?? GetEquippedMainHand();
    }

    /// <summary>
    /// Returns the current equipped active melee weapon
    /// This will normally be the primary melee weapon, but if dual wielding, this will be the weapon for the next attack
    /// </summary>
    public WorldObject GetEquippedMeleeWeapon(bool forceMainHand = false)
    {
        if (!IsDualWieldAttack || DualWieldAlternate || forceMainHand)
        {
            return EquippedObjects.Values.FirstOrDefault(e =>
                e.ParentLocation == ACE.Entity.Enum.ParentLocation.RightHand
                && (
                    e.CurrentWieldedLocation == EquipMask.MeleeWeapon || e.CurrentWieldedLocation == EquipMask.TwoHanded
                )
            );
        }

        return GetDualWieldWeapon();
    }

    /// <summary>
    /// Returns the currently equipped secondary weapon
    /// </summary>
    public WorldObject GetDualWieldWeapon()
    {
        return EquippedObjects.Values.FirstOrDefault(e => !e.IsShield && e.CurrentWieldedLocation == EquipMask.Shield);
    }

    /// <summary>
    /// Returns the currently equipped wand
    /// </summary>
    public WorldObject GetEquippedWand()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.Held);
    }

    /// <summary>
    /// Returns the currently equipped missile weapon
    /// This can be either a missile launcher (bow, crossbow, atlatl) or stackable thrown weapons directly in the main hand slot
    /// </summary>
    public WorldObject GetEquippedMissileWeapon()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.MissileWeapon);
    }

    /// <summary>
    /// Returns the currently equipped missile launcher
    /// </summary>
    public WorldObject GetEquippedMissileLauncher()
    {
        return EquippedObjects.Values.FirstOrDefault(e =>
            e.CurrentWieldedLocation == EquipMask.MissileWeapon && e is MissileLauncher
        );
    }

    /// <summary>
    /// Returns the current equipped weapon in main hand
    /// </summary>
    public WorldObject GetEquippedMainHand()
    {
        return GetEquippedMeleeWeapon(true) ?? GetEquippedMissileWeapon() ?? GetEquippedWand();
    }

    /// <summary>
    /// Returns either a shield, an off-hand weapon, or null
    /// </summary>
    public WorldObject GetEquippedOffHand()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.Shield);
    }

    /// <summary>
    /// Returns the currently equipped shield
    /// </summary>
    public WorldObject GetEquippedShield()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.IsShield && e.CurrentWieldedLocation == EquipMask.Shield);
    }

    /// <summary>
    /// Returns the currently equipped missile ammo
    /// </summary>
    /// <returns></returns>
    public WorldObject GetEquippedAmmo()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.MissileAmmo);
    }

    public WorldObject GetEquippedTrinket()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.TrinketOne);
    }

    public List<SigilTrinket> GetEquippedSigilTrinkets()
    {
        var trinketsBlue =
            EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.SigilOne)
            as SigilTrinket;
        var trinketsYellow =
            EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.SigilTwo)
            as SigilTrinket;
        var trinketsRed =
            EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.SigilThree)
            as SigilTrinket;

        var equippedTrinkets = new List<SigilTrinket>();

        if (trinketsBlue != null)
        {
            equippedTrinkets.Add(trinketsBlue);
        }

        if (trinketsYellow != null)
        {
            equippedTrinkets.Add(trinketsYellow);
        }

        if (trinketsRed != null)
        {
            equippedTrinkets.Add(trinketsRed);
        }

        return equippedTrinkets;
    }

    protected SigilTrinket GetEquippedSigilTrinketOfType(SigilTrinketColor sigilTrinketColor)
    {
        var equippedTrinkets = GetEquippedSigilTrinkets();

        return equippedTrinkets.ToList().FirstOrDefault(trinket => trinket?.SigilTrinketColor != null && trinket.SigilTrinketColor == (int)sigilTrinketColor);
    }

    protected List<SigilTrinket> GetHeldSigilTrinketsBlue()
    {
        const uint empoweredScarabBlueLifeWcid = 1050250;
        const uint empoweredScarabBlueWarWcid = 1050251;

        var empoweredScarabsBlueLife = GetInventoryItemsOfWCID(empoweredScarabBlueLifeWcid).ConvertAll(x => (SigilTrinket)x);
        var empoweredScarabsBlueWar = GetInventoryItemsOfWCID(empoweredScarabBlueWarWcid).ConvertAll(x => (SigilTrinket)x);

        var heldTrinketsBlue = new List<SigilTrinket>();
        heldTrinketsBlue.AddRange(empoweredScarabsBlueLife);
        heldTrinketsBlue.AddRange(empoweredScarabsBlueWar);

        return heldTrinketsBlue;
    }

    protected List<SigilTrinket> GetHeldSigilTrinketsYellow()
    {
        const uint empoweredScarabYellowLifeWcid = 1050252;
        const uint empoweredScarabYellowWarWcid = 1050253;

        var empoweredScarabsYellowLife = GetInventoryItemsOfWCID(empoweredScarabYellowLifeWcid).ConvertAll(x => (SigilTrinket)x);
        var empoweredScarabsYellowWar = GetInventoryItemsOfWCID(empoweredScarabYellowWarWcid).ConvertAll(x => (SigilTrinket)x);

        var heldTrinketsYellow = new List<SigilTrinket>();
        heldTrinketsYellow.AddRange(empoweredScarabsYellowLife);
        heldTrinketsYellow.AddRange(empoweredScarabsYellowWar);

        return heldTrinketsYellow;
    }

    protected List<SigilTrinket> GetHeldSigilTrinketsRed()
    {
        const uint empoweredScarabRedLifeWcid = 1050254;
        const uint empoweredScarabRedWarWcid = 1050255;

        var empoweredScarabsRedLife = GetInventoryItemsOfWCID(empoweredScarabRedLifeWcid).ConvertAll(x => (SigilTrinket)x);
        var empoweredScarabsRedWar = GetInventoryItemsOfWCID(empoweredScarabRedWarWcid).ConvertAll(x => (SigilTrinket)x);

        var heldTrinketsRed = new List<SigilTrinket>();
        heldTrinketsRed.AddRange(empoweredScarabsRedLife);
        heldTrinketsRed.AddRange(empoweredScarabsRedWar);

        return heldTrinketsRed;
    }

    public CombatFocus GetEquippedCombatFocus()
    {
        return EquippedObjects.Values.FirstOrDefault(e => e.CurrentWieldedLocation == EquipMask.TrinketOne)
            as CombatFocus;
    }

    /// <summary>
    /// Returns the ammo slot item for bows / atlatls,
    /// or the missile weapon for thrown weapons
    /// </summary>
    public WorldObject GetMissileAmmo()
    {
        var weapon = GetEquippedMissileWeapon();

        if (weapon != null && weapon.IsAmmoLauncher)
        {
            return GetEquippedAmmo();
        }

        return weapon;
    }

    /// <summary>
    /// This is initialized the first time an item is equipped that has a rating. If it is null, there are no equipped items with ratings.
    /// </summary>
    private Dictionary<PropertyInt, int> equippedItemsRatingCache;

    private void AddItemToEquippedItemsRatingCache(WorldObject wo)
    {
        if (
            (wo.GearDamage ?? 0) == 0
            && (wo.GearDamageResist ?? 0) == 0
            && (wo.GearCrit ?? 0) == 0
            && (wo.GearCritDamage ?? 0) == 0
            && (wo.GearCritDamageResist ?? 0) == 0
            && (wo.GearHealingBoost ?? 0) == 0
            && (wo.GearMaxHealth ?? 0) == 0
            && (wo.GearPKDamageRating ?? 0) == 0
            && (wo.GearPKDamageResistRating ?? 0) == 0
            && (wo.WardLevel ?? 0) == 0
            && (wo.GearStrength ?? 0) == 0
            && (wo.GearEndurance ?? 0) == 0
            && (wo.GearQuickness ?? 0) == 0
            && (wo.GearFocus ?? 0) == 0
            && (wo.GearSelf ?? 0) == 0
            && (wo.GearThreatGain ?? 0) == 0
            && (wo.GearThreatReduction ?? 0) == 0
            && (wo.GearElementalWard ?? 0) == 0
            && (wo.GearPhysicalWard ?? 0) == 0
            && (wo.GearMagicFind ?? 0) == 0
            && (wo.GearBlock ?? 0) == 0
            && (wo.GearItemManaUsage ?? 0) == 0
            && (wo.GearLifesteal ?? 0) == 0
            && (wo.GearSelfHarm ?? 0) == 0
            && (wo.GearThorns ?? 0) == 0
            && (wo.GearVitalsTransfer ?? 0) == 0
            && (wo.GearRedFury ?? 0) == 0
            && (wo.GearYellowFury ?? 0) == 0
            && (wo.GearBlueFury ?? 0) == 0
            && (wo.GearSelflessness ?? 0) == 0
            && (wo.GearVipersStrike ?? 0) == 0
            && (wo.GearFamiliarity ?? 0) == 0
            && (wo.GearBravado ?? 0) == 0
            && (wo.GearHealthToStamina ?? 0) == 0
            && (wo.GearHealthToMana ?? 0) == 0
            && (wo.GearExperienceGain ?? 0) == 0
            && (wo.GearManasteal ?? 0) == 0
            && (wo.GearBludgeon ?? 0) == 0
            && (wo.GearPierce ?? 0) == 0
            && (wo.GearSlash ?? 0) == 0
            && (wo.GearFire ?? 0) == 0
            && (wo.GearFrost ?? 0) == 0
            && (wo.GearAcid ?? 0) == 0
            && (wo.GearLightning ?? 0) == 0
            && (wo.GearHealBubble ?? 0) == 0
            && (wo.GearCompBurn ?? 0) == 0
            && (wo.GearPyrealFind ?? 0) == 0
            && (wo.GearNullification ?? 0) == 0
            && (wo.GearWardPen ?? 0) == 0
            && (wo.GearStaminasteal ?? 0) == 0
            && (wo.GearHardenedDefense ?? 0) == 0
            && (wo.GearReprisal ?? 0) == 0
            && (wo.GearElementalist ?? 0) == 0
            && (wo.GearToughness ?? 0) == 0
            && (wo.GearResistance ?? 0) == 0
            && (wo.GearSlashBane ?? 0) == 0
            && (wo.GearBludgeonBane ?? 0) == 0
            && (wo.GearPierceBane ?? 0) == 0
            && (wo.GearAcidBane ?? 0) == 0
            && (wo.GearFireBane ?? 0) == 0
            && (wo.GearFrostBane ?? 0) == 0
            && (wo.GearLightningBane ?? 0) == 0
        )
        {
            return;
        }

        if (equippedItemsRatingCache == null)
        {
            equippedItemsRatingCache = new Dictionary<PropertyInt, int>
            {
                { PropertyInt.GearDamage, 0 },
                { PropertyInt.GearDamageResist, 0 },
                { PropertyInt.GearCrit, 0 },
                { PropertyInt.GearCritResist, 0 },
                { PropertyInt.GearCritDamage, 0 },
                { PropertyInt.GearCritDamageResist, 0 },
                { PropertyInt.GearHealingBoost, 0 },
                { PropertyInt.GearMaxHealth, 0 },
                { PropertyInt.GearPKDamageRating, 0 },
                { PropertyInt.GearPKDamageResistRating, 0 },
                { PropertyInt.WardLevel, 0 },
                { PropertyInt.GearStrength, 0 },
                { PropertyInt.GearEndurance, 0 },
                { PropertyInt.GearCoordination, 0 },
                { PropertyInt.GearQuickness, 0 },
                { PropertyInt.GearFocus, 0 },
                { PropertyInt.GearSelf, 0 },
                { PropertyInt.GearMaxStamina, 0 },
                { PropertyInt.GearMaxMana, 0 },
                { PropertyInt.GearThreatGain, 0 },
                { PropertyInt.GearThreatReduction, 0 },
                { PropertyInt.GearElementalWard, 0 },
                { PropertyInt.GearPhysicalWard, 0 },
                { PropertyInt.GearMagicFind, 0 },
                { PropertyInt.GearBlock, 0 },
                { PropertyInt.GearItemManaUsage, 0 },
                { PropertyInt.GearLifesteal, 0 },
                { PropertyInt.GearSelfHarm, 0 },
                { PropertyInt.GearThorns, 0 },
                { PropertyInt.GearVitalsTransfer, 0 },
                { PropertyInt.GearRedFury, 0 },
                { PropertyInt.GearYellowFury, 0 },
                { PropertyInt.GearBlueFury, 0 },
                { PropertyInt.GearSelflessness, 0 },
                { PropertyInt.GearVipersStrike, 0 },
                { PropertyInt.GearFamiliarity, 0 },
                { PropertyInt.GearBravado, 0 },
                { PropertyInt.GearHealthToStamina, 0 },
                { PropertyInt.GearHealthToMana, 0 },
                { PropertyInt.GearExperienceGain, 0 },
                { PropertyInt.GearManasteal, 0 },
                { PropertyInt.GearBludgeon, 0 },
                { PropertyInt.GearPierce, 0 },
                { PropertyInt.GearSlash, 0 },
                { PropertyInt.GearFire, 0 },
                { PropertyInt.GearFrost, 0 },
                { PropertyInt.GearAcid, 0 },
                { PropertyInt.GearLightning, 0 },
                { PropertyInt.GearHealBubble, 0 },
                { PropertyInt.GearCompBurn, 0 },
                { PropertyInt.GearPyrealFind, 0 },
                { PropertyInt.GearNullification, 0 },
                { PropertyInt.GearWardPen, 0 },
                { PropertyInt.GearStaminasteal, 0 },
                { PropertyInt.GearHardenedDefense, 0 },
                { PropertyInt.GearReprisal, 0 },
                { PropertyInt.GearElementalist, 0 },
                { PropertyInt.GearToughness, 0 },
                { PropertyInt.GearResistance, 0 },
                { PropertyInt.GearSlashBane, 0 },
                { PropertyInt.GearBludgeonBane, 0 },
                { PropertyInt.GearPierceBane, 0 },
                { PropertyInt.GearAcidBane, 0 },
                { PropertyInt.GearFireBane, 0 },
                { PropertyInt.GearFrostBane, 0 },
                { PropertyInt.GearLightningBane, 0 }
            };
        }

        equippedItemsRatingCache[PropertyInt.GearDamage] += (wo.GearDamage ?? 0);
        equippedItemsRatingCache[PropertyInt.GearDamageResist] += (wo.GearDamageResist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCrit] += (wo.GearCrit ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCritResist] += (wo.GearCritDamageResist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCritDamage] += (wo.GearCritDamage ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCritDamageResist] += (wo.GearCritDamageResist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealingBoost] += (wo.GearHealingBoost ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMaxHealth] += (wo.GearMaxHealth ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPKDamageRating] += (wo.GearPKDamageRating ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPKDamageResistRating] += (wo.GearPKDamageResistRating ?? 0);
        equippedItemsRatingCache[PropertyInt.WardLevel] += (wo.WardLevel ?? 0);
        equippedItemsRatingCache[PropertyInt.GearStrength] += (wo.GearStrength ?? 0);
        equippedItemsRatingCache[PropertyInt.GearEndurance] += (wo.GearEndurance ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCoordination] += (wo.GearCoordination ?? 0);
        equippedItemsRatingCache[PropertyInt.GearQuickness] += (wo.GearQuickness ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFocus] += (wo.GearFocus ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSelf] += (wo.GearSelf ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMaxStamina] += (wo.GearMaxStamina ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMaxMana] += (wo.GearMaxMana ?? 0);
        equippedItemsRatingCache[PropertyInt.GearThreatGain] += (wo.GearThreatGain ?? 0);
        equippedItemsRatingCache[PropertyInt.GearThreatReduction] += (wo.GearThreatReduction ?? 0);
        equippedItemsRatingCache[PropertyInt.GearElementalWard] += (wo.GearElementalWard ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPhysicalWard] += (wo.GearPhysicalWard ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMagicFind] += (wo.GearMagicFind ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBlock] += (wo.GearBlock ?? 0);
        equippedItemsRatingCache[PropertyInt.GearItemManaUsage] += (wo.GearItemManaUsage ?? 0);
        equippedItemsRatingCache[PropertyInt.GearLifesteal] += (wo.GearLifesteal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSelfHarm] += (wo.GearSelfHarm ?? 0);
        equippedItemsRatingCache[PropertyInt.GearThorns] += (wo.GearThorns ?? 0);
        equippedItemsRatingCache[PropertyInt.GearVitalsTransfer] += (wo.GearVitalsTransfer ?? 0);
        equippedItemsRatingCache[PropertyInt.GearRedFury] += (wo.GearRedFury ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSelflessness] += (wo.GearSelflessness ?? 0);
        equippedItemsRatingCache[PropertyInt.GearVipersStrike] += (wo.GearVipersStrike ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFamiliarity] += (wo.GearFamiliarity ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBravado] += (wo.GearBravado ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealthToStamina] += (wo.GearHealthToStamina ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealthToMana] += (wo.GearHealthToMana ?? 0);
        equippedItemsRatingCache[PropertyInt.GearExperienceGain] += (wo.GearExperienceGain ?? 0);
        equippedItemsRatingCache[PropertyInt.GearManasteal] += (wo.GearManasteal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBludgeon] += (wo.GearBludgeon ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPierce] += (wo.GearPierce ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSlash] += (wo.GearSlash ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFire] += (wo.GearFire ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFrost] += (wo.GearFrost ?? 0);
        equippedItemsRatingCache[PropertyInt.GearAcid] += (wo.GearAcid ?? 0);
        equippedItemsRatingCache[PropertyInt.GearLightning] += (wo.GearLightning ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealBubble] += (wo.GearHealBubble ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCompBurn] += (wo.GearCompBurn ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPyrealFind] += (wo.GearPyrealFind ?? 0);
        equippedItemsRatingCache[PropertyInt.GearNullification] += (wo.GearNullification ?? 0);
        equippedItemsRatingCache[PropertyInt.GearWardPen] += (wo.GearWardPen ?? 0);
        equippedItemsRatingCache[PropertyInt.GearStaminasteal] += (wo.GearStaminasteal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHardenedDefense] += (wo.GearHardenedDefense ?? 0);
        equippedItemsRatingCache[PropertyInt.GearReprisal] += (wo.GearReprisal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearElementalist] += (wo.GearElementalist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearToughness] -= (wo.GearToughness ?? 0);
        equippedItemsRatingCache[PropertyInt.GearResistance] -= (wo.GearResistance ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSlashBane] -= (wo.GearSlashBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBludgeonBane] -= (wo.GearBludgeonBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPierceBane] -= (wo.GearPierceBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearAcidBane] -= (wo.GearAcidBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFireBane] -= (wo.GearFireBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFrostBane] -= (wo.GearFrostBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearLightningBane] -= (wo.GearLightningBane ?? 0);
    }

    private void RemoveItemFromEquippedItemsRatingCache(WorldObject wo)
    {
        if (equippedItemsRatingCache == null)
        {
            return;
        }

        equippedItemsRatingCache[PropertyInt.GearDamage] -= (wo.GearDamage ?? 0);
        equippedItemsRatingCache[PropertyInt.GearDamageResist] -= (wo.GearDamageResist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCrit] -= (wo.GearCrit ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCritResist] -= (wo.GearCritDamageResist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCritDamage] -= (wo.GearCritDamage ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCritDamageResist] -= (wo.GearCritDamageResist ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealingBoost] -= (wo.GearHealingBoost ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMaxHealth] -= (wo.GearMaxHealth ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPKDamageRating] -= (wo.GearPKDamageRating ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPKDamageResistRating] -= (wo.GearPKDamageResistRating ?? 0);
        equippedItemsRatingCache[PropertyInt.WardLevel] -= (wo.WardLevel ?? 0);
        equippedItemsRatingCache[PropertyInt.GearStrength] -= (wo.GearStrength ?? 0);
        equippedItemsRatingCache[PropertyInt.GearEndurance] -= (wo.GearEndurance ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCoordination] -= (wo.GearCoordination ?? 0);
        equippedItemsRatingCache[PropertyInt.GearQuickness] -= (wo.GearQuickness ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFocus] -= (wo.GearFocus ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSelf] -= (wo.GearSelf ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMaxStamina] -= (wo.GearMaxStamina ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMaxMana] -= (wo.GearMaxMana ?? 0);
        equippedItemsRatingCache[PropertyInt.GearThreatGain] -= (wo.GearThreatGain ?? 0);
        equippedItemsRatingCache[PropertyInt.GearThreatReduction] -= (wo.GearThreatReduction ?? 0);
        equippedItemsRatingCache[PropertyInt.GearElementalWard] -= (wo.GearElementalWard ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPhysicalWard] -= (wo.GearPhysicalWard ?? 0);
        equippedItemsRatingCache[PropertyInt.GearMagicFind] -= (wo.GearMagicFind ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBlock] -= (wo.GearBlock ?? 0);
        equippedItemsRatingCache[PropertyInt.GearItemManaUsage] -= (wo.GearItemManaUsage ?? 0);
        equippedItemsRatingCache[PropertyInt.GearLifesteal] -= (wo.GearLifesteal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSelfHarm] -= (wo.GearSelfHarm ?? 0);
        equippedItemsRatingCache[PropertyInt.GearThorns] -= (wo.GearThorns ?? 0);
        equippedItemsRatingCache[PropertyInt.GearVitalsTransfer] -= (wo.GearVitalsTransfer ?? 0);
        equippedItemsRatingCache[PropertyInt.GearRedFury] -= (wo.GearRedFury ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSelflessness] -= (wo.GearSelflessness ?? 0);
        equippedItemsRatingCache[PropertyInt.GearVipersStrike] -= (wo.GearVipersStrike ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFamiliarity] -= (wo.GearFamiliarity ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBravado] -= (wo.GearBravado ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealthToStamina] -= (wo.GearHealthToStamina ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealthToMana] -= (wo.GearHealthToMana ?? 0);
        equippedItemsRatingCache[PropertyInt.GearExperienceGain] -= (wo.GearExperienceGain ?? 0);
        equippedItemsRatingCache[PropertyInt.GearManasteal] -= (wo.GearManasteal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBludgeon] -= (wo.GearBludgeon ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPierce] -= (wo.GearPierce ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSlash] -= (wo.GearSlash ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFire] -= (wo.GearFire ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFrost] -= (wo.GearFrost ?? 0);
        equippedItemsRatingCache[PropertyInt.GearAcid] -= (wo.GearAcid ?? 0);
        equippedItemsRatingCache[PropertyInt.GearLightning] -= (wo.GearLightning ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHealBubble] -= (wo.GearHealBubble ?? 0);
        equippedItemsRatingCache[PropertyInt.GearCompBurn] -= (wo.GearCompBurn ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPyrealFind] -= (wo.GearPyrealFind ?? 0);
        equippedItemsRatingCache[PropertyInt.GearNullification] -= (wo.GearNullification ?? 0);
        equippedItemsRatingCache[PropertyInt.GearWardPen] -= (wo.GearWardPen ?? 0);
        equippedItemsRatingCache[PropertyInt.GearStaminasteal] -= (wo.GearStaminasteal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearHardenedDefense] -= (wo.GearHardenedDefense ?? 0);
        equippedItemsRatingCache[PropertyInt.GearReprisal] -= (wo.GearReprisal ?? 0);
        equippedItemsRatingCache[PropertyInt.GearToughness] -= (wo.GearToughness ?? 0);
        equippedItemsRatingCache[PropertyInt.GearResistance] -= (wo.GearResistance ?? 0);
        equippedItemsRatingCache[PropertyInt.GearSlashBane] -= (wo.GearSlashBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearBludgeonBane] -= (wo.GearBludgeonBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearPierceBane] -= (wo.GearPierceBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearAcidBane] -= (wo.GearAcidBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFireBane] -= (wo.GearFireBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearFrostBane] -= (wo.GearFrostBane ?? 0);
        equippedItemsRatingCache[PropertyInt.GearLightningBane] -= (wo.GearLightningBane ?? 0);
    }

    public int GetEquippedItemsRatingSum(PropertyInt rating)
    {
        if (equippedItemsRatingCache == null)
        {
            return 0;
        }

        if (equippedItemsRatingCache.TryGetValue(rating, out var value))
        {
            return value;
        }

        _log.Error($"Creature_Equipment.GetEquippedItemsRatingsSum() does not support {rating}");
        return 0;
    }

    public int GetEquippedAndActivatedItemRatingSum(PropertyInt rating)
    {
        if (this is not Player)
        {
            return 0;
        }

        //Console.WriteLine("\nGetEquippedAndActivatedItemRatingSum()");

        var ratingAmount = 0;

        foreach (var item in EquippedObjects.Values)
        {
            if (item.SpecialPropertiesRequireMana && item.ItemCurMana == 0)
            {
                continue;
            }

            var properties = GetProperties(item);

            foreach (var property in properties)
            {
                if (property.Key == rating && property.Value != null)
                {
                    ratingAmount += property.Value.Value;
                }
            }

            ratingAmount += GetRatingFromSocketedJewels(rating, item);

        }

        //Console.WriteLine($"Total Rating ({rating}): {ratingAmount}");
        return ratingAmount;
    }

    protected static int GetRatingFromSocketedJewels(PropertyInt rating, WorldObject item)
    {
        var jewelRating = 0;

        for (var i = 0; i < (item.JewelSockets ?? 0); i++)
        {
            var jewelMaterialType = item.GetProperty(Jewel.SocketedJewelDetails[i].JewelSocketMaterialIntId);
            var jewelQuality = item.GetProperty(Jewel.SocketedJewelDetails[i].JewelSocketQualityIntId);
            var itemLocation = item.ValidLocations;

            if (jewelMaterialType is null || jewelQuality is null || itemLocation is null)
            {
                continue;
            }

            if (!Jewel.JewelTypeToMaterial.TryGetValue(rating, out var materialType) || materialType != (MaterialType)jewelMaterialType)
            {
                continue;
            }

            jewelRating += GetRatingFromJewel(rating, (EquipMask)itemLocation, (MaterialType)jewelMaterialType, (int)jewelQuality);
        }

        //Console.WriteLine($" -Rating ({rating}) from item ({item.Name}): {jewelRating}");
        return jewelRating;
    }

    private static int GetRatingFromJewel(PropertyInt rating, EquipMask equipMask, MaterialType jewelMaterialType, int jewelQuality)
    {
        // Check if the rating's material type matches the jewel's material type
        if (!Jewel.JewelTypeToMaterial.TryGetValue(rating, out var value) && value != jewelMaterialType)
        {
            return 0;
        }

        // Check if the item's equip location can be used with the jewel's equip mask
        if ((Jewel.MaterialValidLocations[jewelMaterialType] & equipMask) != equipMask)
        {
            return 0;
        }

        // If the item is an armor, check if the rating's type matches the jewel's alternate rating type
        if ((EquipMask.Armor & equipMask) == equipMask && Jewel.JewelMaterialToType[jewelMaterialType].AlternateRating != rating)
        {
            return 0;
        }

        // If item is not an armor, check if the rating's type matches the jewels primary rating
        if ((EquipMask.Armor & equipMask) != equipMask && Jewel.JewelMaterialToType[jewelMaterialType].PrimaryRating != rating)
        {
            return 0;
        }

        return jewelQuality;
    }

    public int GetEquippedItemsWardSum(PropertyInt wardLevel)
    {
        if (equippedItemsRatingCache == null)
        {
            return 0;
        }

        if (equippedItemsRatingCache.TryGetValue(wardLevel, out var value))
        {
            return value;
        }

        _log.Error($"Creature_Equipment.GetEquippedItemsWardSum() does not support {wardLevel}");
        return 0;
    }

    /// <summary>
    /// This is initialized the first time an item is equipped that has a skill mod. If it is null, there are no equipped items with skill mods.
    /// </summary>
    private Dictionary<PropertyFloat, double> equippedItemsSkillModCache;

    private void AddItemToEquippedItemsSkillModCache(WorldObject wo)
    {
        if (
            (wo.ArmorHealthRegenMod ?? 0) == 0
            && (wo.ArmorStaminaRegenMod ?? 0) == 0
            && (wo.ArmorManaRegenMod ?? 0) == 0
            && (wo.ArmorAttackMod ?? 0) == 0
            && (wo.ArmorPhysicalDefMod ?? 0) == 0
            && (wo.ArmorMissileDefMod ?? 0) == 0
            && (wo.ArmorMagicDefMod ?? 0) == 0
            && (wo.ArmorRunMod ?? 0) == 0
            && (wo.ArmorTwohandedCombatMod ?? 0) == 0
            && (wo.ArmorDualWieldMod ?? 0) == 0
            && (wo.ArmorThieveryMod ?? 0) == 0
            && (wo.ArmorPerceptionMod ?? 0) == 0
            && (wo.ArmorShieldMod ?? 0) == 0
            && (wo.ArmorDeceptionMod ?? 0) == 0
            && (wo.ArmorWarMagicMod ?? 0) == 0
            && (wo.ArmorLifeMagicMod ?? 0) == 0
            && (wo.WeaponWarMagicMod ?? 0) == 0
            && (wo.WeaponLifeMagicMod ?? 0) == 0
            && (wo.WeaponRestorationSpellsMod ?? 0) == 0
            && (wo.ArmorHealthMod ?? 0) == 0
            && (wo.ArmorStaminaMod ?? 0) == 0
            && (wo.ArmorManaMod ?? 0) == 0
            && (wo.ArmorResourcePenalty ?? 0) == 0
        )
        {
            return;
        }

        if (equippedItemsSkillModCache == null)
        {
            equippedItemsSkillModCache = new Dictionary<PropertyFloat, double>
            {
                { PropertyFloat.ArmorHealthRegenMod, 0 },
                { PropertyFloat.ArmorStaminaRegenMod, 0 },
                { PropertyFloat.ArmorManaRegenMod, 0 },
                { PropertyFloat.ArmorAttackMod, 0 },
                { PropertyFloat.ArmorPhysicalDefMod, 0 },
                { PropertyFloat.ArmorMissileDefMod, 0 },
                { PropertyFloat.ArmorMagicDefMod, 0 },
                { PropertyFloat.ArmorRunMod, 0 },
                { PropertyFloat.ArmorDualWieldMod, 0 },
                { PropertyFloat.ArmorTwohandedCombatMod, 0 },
                { PropertyFloat.ArmorThieveryMod, 0 },
                { PropertyFloat.ArmorPerceptionMod, 0 },
                { PropertyFloat.ArmorDeceptionMod, 0 },
                { PropertyFloat.ArmorShieldMod, 0 },
                { PropertyFloat.ArmorWarMagicMod, 0 },
                { PropertyFloat.ArmorLifeMagicMod, 0 },
                { PropertyFloat.WeaponWarMagicMod, 0 },
                { PropertyFloat.WeaponLifeMagicMod, 0 },
                { PropertyFloat.WeaponRestorationSpellsMod, 0 },
                { PropertyFloat.ArmorHealthMod, 0 },
                { PropertyFloat.ArmorStaminaMod, 0 },
                { PropertyFloat.ArmorManaMod, 0 },
                { PropertyFloat.ArmorResourcePenalty, 0 },
            };
        }

        equippedItemsSkillModCache[PropertyFloat.ArmorHealthRegenMod] += (wo.ArmorHealthRegenMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorStaminaRegenMod] += (wo.ArmorStaminaRegenMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorManaRegenMod] += (wo.ArmorManaRegenMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorAttackMod] += (wo.ArmorAttackMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorPhysicalDefMod] += (wo.ArmorPhysicalDefMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorMissileDefMod] += (wo.ArmorMissileDefMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorMagicDefMod] += (wo.ArmorMagicDefMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorRunMod] += (wo.ArmorRunMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorDualWieldMod] += (wo.ArmorDualWieldMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorTwohandedCombatMod] += (wo.ArmorTwohandedCombatMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorThieveryMod] += (wo.ArmorThieveryMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorPerceptionMod] += (wo.ArmorPerceptionMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorDeceptionMod] += (wo.ArmorDeceptionMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorShieldMod] += (wo.ArmorShieldMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorWarMagicMod] += (wo.ArmorWarMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorLifeMagicMod] += (wo.ArmorLifeMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.WeaponWarMagicMod] += (wo.WeaponWarMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.WeaponLifeMagicMod] += (wo.WeaponLifeMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.WeaponRestorationSpellsMod] += (wo.WeaponRestorationSpellsMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorHealthMod] += (wo.ArmorHealthMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorStaminaMod] += (wo.ArmorStaminaMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorManaMod] += (wo.ArmorManaMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorResourcePenalty] += (wo.ArmorResourcePenalty ?? 0);
    }

    private void RemoveItemFromEquippedItemsSkillModCache(WorldObject wo)
    {
        if (equippedItemsSkillModCache == null)
        {
            return;
        }

        equippedItemsSkillModCache[PropertyFloat.ArmorHealthRegenMod] -= (wo.ArmorHealthRegenMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorStaminaRegenMod] -= (wo.ArmorStaminaRegenMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorManaRegenMod] -= (wo.ArmorManaRegenMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorAttackMod] -= (wo.ArmorAttackMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorPhysicalDefMod] -= (wo.ArmorPhysicalDefMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorMissileDefMod] -= (wo.ArmorMissileDefMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorMagicDefMod] -= (wo.ArmorMagicDefMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorRunMod] -= (wo.ArmorRunMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorDualWieldMod] -= (wo.ArmorDualWieldMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorTwohandedCombatMod] -= (wo.ArmorTwohandedCombatMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorThieveryMod] -= (wo.ArmorThieveryMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorPerceptionMod] -= (wo.ArmorPerceptionMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorDeceptionMod] -= (wo.ArmorDeceptionMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorShieldMod] -= (wo.ArmorShieldMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorWarMagicMod] -= (wo.ArmorWarMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorLifeMagicMod] -= (wo.ArmorLifeMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.WeaponWarMagicMod] -= (wo.WeaponWarMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.WeaponLifeMagicMod] -= (wo.WeaponLifeMagicMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.WeaponRestorationSpellsMod] -= (wo.WeaponRestorationSpellsMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorHealthMod] -= (wo.ArmorHealthMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorStaminaMod] -= (wo.ArmorStaminaMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorManaMod] -= (wo.ArmorManaMod ?? 0);
        equippedItemsSkillModCache[PropertyFloat.ArmorResourcePenalty] -= (wo.ArmorResourcePenalty ?? 0);
    }

    public double GetEquippedItemsSkillModSum(PropertyFloat skillMod)
    {
        if (equippedItemsSkillModCache == null)
        {
            return 0;
        }

        if (equippedItemsSkillModCache.TryGetValue(skillMod, out var value))
        {
            return value;
        }
        _log.Error($"Creature_Equipment.GetEquippedItemsSkillModSum() does not support {skillMod}");
        return 0;
    }

    /// <summary>
    /// Try to wield an object for non-player creatures
    /// </summary>
    /// <returns></returns>
    public bool TryWieldObject(WorldObject worldObject, EquipMask wieldedLocation)
    {
        // check wield requirements?
        if (!TryEquipObject(worldObject, wieldedLocation))
        {
            return false;
        }

        // enqueue to ensure parent object has spawned,
        // and spell fx are visible
        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(0.1);
        actionChain.AddAction(this, () => TryActivateItemSpells(worldObject));
        actionChain.EnqueueChain();

        return true;
    }

    /// <summary>
    /// Tries to activate item spells for a non-player creature
    /// </summary>
    private void TryActivateItemSpells(WorldObject item)
    {
        if (!Attackable)
        {
            return;
        }

        // check activation requirements?
        foreach (var spell in item.Biota.GetKnownSpellsIds(BiotaDatabaseLock))
        {
            CreateItemSpell(item, (uint)spell);
        }
    }

    /// <summary>
    /// This will set the CurrentWieldedLocation property to wieldedLocation and the Wielder property to this guid and will add it to the EquippedObjects dictionary.<para />
    /// It will also increase the EncumbranceVal and Value.
    /// </summary>
    public bool TryEquipObject(WorldObject worldObject, EquipMask wieldedLocation)
    {
        // todo: verify wielded location is valid location
        if (!WieldedLocationIsAvailable(worldObject, wieldedLocation))
        {
            return false;
        }

        worldObject.CurrentWieldedLocation = wieldedLocation;
        worldObject.WielderId = Biota.Id;
        worldObject.Wielder = this;

        EquippedObjects[worldObject.Guid] = worldObject;

        AddItemToEquippedItemsRatingCache(worldObject);
        AddItemToEquippedItemsSkillModCache(worldObject);

        if (this is Player player)
        {
            //player.AuditItemSpells();

            UpdateArmorModBuffs();
        }

        if (UseArchetypeSystem is true && Damage is not null)
        {
            MutateWeaponForArchetype(worldObject, Damage.Value);
        }

        if (worldObject.IsAmmoLauncher)
        {
            CheckForAutoEquipAmmo(worldObject);
        }

        EncumbranceVal += (worldObject.EncumbranceVal ?? 0);
        Value += (worldObject.Value ?? 0);

        TrySetChild(worldObject);

        worldObject.OnWield(this);

        return true;
    }

    private void CheckForAutoEquipAmmo(WorldObject missileLauncher)
    {
        if (this is not Player player || missileLauncher.W_DamageType is DamageType.Undef)
        {
            return;
        }

        var sideContainers = Inventory
            .Values.Where(i => i.WeenieType == WeenieType.Container)
            .Select(i => i as Container)
            .OrderBy(i => i.PlacementPosition)
            .ToList();

        foreach (var container in sideContainers)
        {
            if (container.Name.Contains("Quiver"))
            {
                var element = missileLauncher.W_DamageType;

                var matchingElementAmmo = new List<WorldObject>();
                foreach (var ammo in container.Inventory)
                {
                    if (ammo.Value.W_DamageType == element && ammo.Value.AmmoType == missileLauncher.AmmoType)
                    {
                        matchingElementAmmo.Add(ammo.Value);
                    }
                }

                if (matchingElementAmmo.Count == 0)
                {
                    return;
                }

                var currentEquippedAmmo = GetEquippedAmmo();
                var highestDamageMatchingAmmo = currentEquippedAmmo?.W_DamageType == element ? currentEquippedAmmo : matchingElementAmmo[0];

                foreach (var ammo in matchingElementAmmo)
                {
                    if (highestDamageMatchingAmmo.Damage < ammo.Damage)
                    {
                        highestDamageMatchingAmmo = ammo;
                    }
                }

                if (currentEquippedAmmo is null || highestDamageMatchingAmmo != currentEquippedAmmo)
                {
                    var actionChain = new ActionChain();

                    if (currentEquippedAmmo is not null)
                    {
                        actionChain.AddAction(player, () => player.HandleActionPutItemInContainer(currentEquippedAmmo.Guid.Full, player.Guid.Full));
                        actionChain.AddDelaySeconds(0.1);
                        actionChain.AddAction(player, () => player.HandleActionGetAndWieldItem(highestDamageMatchingAmmo.Guid.Full, EquipMask.MissileAmmo));
                        actionChain.AddDelaySeconds(0.1);
                        actionChain.AddAction(player, () => player.HandleActionPutItemInContainer(currentEquippedAmmo.Guid.Full, container.Guid.Full));
                    }
                    else
                    {
                        actionChain.AddAction(player, () => player.HandleActionGetAndWieldItem(highestDamageMatchingAmmo.Guid.Full, EquipMask.MissileAmmo));
                    }

                    actionChain.EnqueueChain();

                    return;
                }
            }
        }
    }

    protected bool TryWieldObjectWithBroadcasting(WorldObject worldObject, EquipMask wieldedLocation)
    {
        // check wield requirements?
        if (!TryEquipObjectWithBroadcasting(worldObject, wieldedLocation))
        {
            return false;
        }

        TryActivateItemSpells(worldObject);

        return true;
    }

    /// <summary>
    /// This will set the CurrentWieldedLocation property to wieldedLocation and the Wielder property to this guid and will add it to the EquippedObjects dictionary.<para />
    /// It will also increase the EncumbranceVal and Value.
    /// </summary>
    protected bool TryEquipObjectWithBroadcasting(WorldObject worldObject, EquipMask wieldedLocation)
    {
        if (!TryEquipObject(worldObject, wieldedLocation))
        {
            return false;
        }

        if (IsInChildLocation(worldObject)) // Is this equipped item visible to others?
        {
            EnqueueBroadcast(false, new GameMessageSound(Guid, Sound.WieldObject));
        }

        if (worldObject.ParentLocation != null)
        {
            EnqueueBroadcast(new GameMessageParentEvent(this, worldObject));
        }

        EnqueueBroadcast(new GameMessageObjDescEvent(this));

        // Notify viewers in the area that we've equipped the item
        EnqueueActionBroadcast(p => p.TrackEquippedObject(this, worldObject));

        return true;
    }

    /// <summary>
    /// This will remove the Wielder and CurrentWieldedLocation properties on the item and will remove it from the EquippedObjects dictionary.<para />
    /// It does not add it to inventory as you could be unwielding to the ground or a chest.<para />
    /// It will also decrease the EncumbranceVal and Value.
    /// </summary>
    public bool TryDequipObject(ObjectGuid objectGuid, out WorldObject worldObject, out EquipMask wieldedLocation)
    {
        if (!EquippedObjects.Remove(objectGuid, out worldObject))
        {
            wieldedLocation = 0;
            return false;
        }

        RemoveItemFromEquippedItemsRatingCache(worldObject);
        RemoveItemFromEquippedItemsSkillModCache(worldObject);

        //Console.WriteLine($"{Name} - Unequip: {GetArmorRunMod() + 1}");
        if (this is Player player)
        {
            //player.AuditItemSpells();

            UpdateArmorModBuffs();
        }
        wieldedLocation = worldObject.CurrentWieldedLocation ?? EquipMask.None;

        worldObject.RemoveProperty(PropertyInt.CurrentWieldedLocation);
        worldObject.RemoveProperty(PropertyInstanceId.Wielder);
        worldObject.Wielder = null;

        worldObject.OnSpellsDeactivated();

        if ((worldObject.WeenieType == WeenieType.Ammunition || worldObject.WeenieType == WeenieType.Missile))
        {
            EncumbranceVal -= (int)Math.Ceiling((worldObject.EncumbranceVal ?? 0) / 2.0f);
        }
        else
        {
            EncumbranceVal -= (worldObject.EncumbranceVal ?? 0);
        }

        Value -= (worldObject.Value ?? 0);

        ClearChild(worldObject);

        var wo = worldObject;
        Children.Remove(Children.Find(s => s.Guid == wo.Guid.Full));

        worldObject.OnUnWield(this);

        return true;
    }

    /// <summary>
    /// Called by non-player creatures to unwield an item,
    /// removing any spells casted by the item
    /// </summary>
    public bool TryUnwieldObjectWithBroadcasting(
        ObjectGuid objectGuid,
        out WorldObject worldObject,
        out EquipMask wieldedLocation,
        bool droppingToLandscape = false
    )
    {
        if (!TryDequipObjectWithBroadcasting(objectGuid, out worldObject, out wieldedLocation, droppingToLandscape))
        {
            return false;
        }

        // remove item spells
        foreach (var spell in worldObject.Biota.GetKnownSpellsIds(BiotaDatabaseLock))
        {
            RemoveItemSpell(worldObject, (uint)spell, true);
        }

        return true;
    }

    /// <summary>
    /// This will remove the Wielder and CurrentWieldedLocation properties on the item and will remove it from the EquippedObjects dictionary.<para />
    /// It does not add it to inventory as you could be unwielding to the ground or a chest.<para />
    /// It will also decrease the EncumbranceVal and Value.
    /// </summary>
    protected bool TryDequipObjectWithBroadcasting(
        ObjectGuid objectGuid,
        out WorldObject worldObject,
        out EquipMask wieldedLocation,
        bool droppingToLandscape = false
    )
    {
        if (!TryDequipObject(objectGuid, out worldObject, out wieldedLocation))
        {
            return false;
        }

        if ((wieldedLocation & EquipMask.Selectable) != 0) // Is this equipped item visible to others?
        {
            EnqueueBroadcast(false, new GameMessageSound(Guid, Sound.UnwieldObject));
        }

        EnqueueBroadcast(new GameMessageObjDescEvent(this));

        // handle combat focus dequip
        var combatFocus = worldObject as CombatFocus;
        if (combatFocus != null)
        {
            combatFocus.OnDequip(this as Player);
        }

        // handle mana scarabs
        var manaScarab = worldObject as SigilTrinket;
        if (manaScarab != null)
        {
            manaScarab.OnDequip(this as Player);
        }

        // If item has any spells, remove them from the registry on unequip
        if (worldObject.Biota.PropertiesSpellBook != null)
        {
            foreach (var spell in worldObject.Biota.PropertiesSpellBook)
            {
                if (worldObject.HasProcSpell((uint)spell.Key))
                {
                    continue;
                }

                RemoveItemSpell(worldObject, (uint)spell.Key, true);
            }
        }

        if (!droppingToLandscape)
        {
            // This should only be called if the object is going to the private storage, not when dropped on the landscape
            var wo = worldObject;
            EnqueueActionBroadcast(p => p.RemoveTrackedEquippedObject(this, wo));
        }

        return true;
    }

    protected bool IsInChildLocation(WorldObject item)
    {
        if (item.CurrentWieldedLocation == null)
        {
            return false;
        }

        if (((EquipMask)item.CurrentWieldedLocation & EquipMask.Selectable) != 0)
        {
            return true;
        }

        if (((EquipMask)item.CurrentWieldedLocation & EquipMask.MissileAmmo) != 0)
        {
            var wielder = item.Wielder;

            if (wielder != null && wielder is Creature creature)
            {
                var weapon = creature.GetEquippedMissileWeapon();

                if (weapon == null)
                {
                    return false;
                }

                if (creature.CombatMode == CombatMode.Missile && weapon.WeenieType == WeenieType.MissileLauncher)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// This method sets properties needed for items that will be child items.<para />
    /// Items here are only items equipped in the hands.<para />
    /// This deals with the orientation and positioning for visual appearance of the child items held by the parent.<para />
    /// If the item isn't in a valid child state (CurrentWieldedLocation), the child properties will be cleared. (Placement, ParentLocation, Location).
    /// </summary>
    private bool TrySetChild(WorldObject item)
    {
        if (!IsInChildLocation(item))
        {
            ClearChild(item);
            return false;
        }

        GetPlacementLocation(item, item.CurrentWieldedLocation ?? 0, out var placement, out var parentLocation);

        Children.Add(new HeldItem(item.Guid.Full, (int)parentLocation, (EquipMask)item.CurrentWieldedLocation));

        item.Placement = placement;
        item.ParentLocation = parentLocation;
        item.Location = Location;

        return true;
    }

    private static void GetPlacementLocation(
        WorldObject item,
        EquipMask wieldedLocation,
        out Placement placement,
        out ParentLocation parentLocation
    )
    {
        switch (wieldedLocation)
        {
            case EquipMask.MeleeWeapon:
            case EquipMask.Held:
            case EquipMask.TwoHanded:
                placement = ACE.Entity.Enum.Placement.RightHandCombat;
                parentLocation = ACE.Entity.Enum.ParentLocation.RightHand;
                break;

            case EquipMask.Shield:
                if (item.ItemType == ItemType.Armor)
                {
                    placement = ACE.Entity.Enum.Placement.Shield;
                    parentLocation = ACE.Entity.Enum.ParentLocation.Shield;
                }
                else
                {
                    placement = ACE.Entity.Enum.Placement.RightHandNonCombat;
                    parentLocation = ACE.Entity.Enum.ParentLocation.LeftWeapon;
                }
                break;

            case EquipMask.MissileWeapon:
                if (item.DefaultCombatStyle == CombatStyle.Bow || item.DefaultCombatStyle == CombatStyle.Crossbow)
                {
                    placement = ACE.Entity.Enum.Placement.LeftHand;
                    parentLocation = ACE.Entity.Enum.ParentLocation.LeftHand;
                }
                else
                {
                    placement = ACE.Entity.Enum.Placement.RightHandCombat;
                    parentLocation = ACE.Entity.Enum.ParentLocation.RightHand;
                }
                break;

            default:
                placement = ACE.Entity.Enum.Placement.Default;
                parentLocation = ACE.Entity.Enum.ParentLocation.None;
                break;
        }
    }

    private static bool IsWeaponSlot(EquipMask equipMask)
    {
        switch (equipMask)
        {
            case EquipMask.MeleeWeapon:
            case EquipMask.Held:
            case EquipMask.TwoHanded:
            case EquipMask.Shield:
            case EquipMask.MissileWeapon:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// This clears the child properties:<para />
    /// Placement = ACE.Entity.Enum.Placement.Resting<para />
    /// ParentLocation = null<para />
    /// Location = null
    /// </summary>
    protected void ClearChild(WorldObject item)
    {
        item.Placement = ACE.Entity.Enum.Placement.Resting;
        item.ParentLocation = null;
        item.Location = null;
    }

    /// <summary>
    /// Removes an existing object from Children if exists,
    /// and resets to new Child position
    /// </summary>
    public void ResetChild(WorldObject item)
    {
        Children.Remove(Children.Find(s => s.Guid == item.Guid.Full));
        TrySetChild(item);
    }

    /// <summary>
    /// This is called prior to SendSelf to load up the child list for wielded items that are held in a hand.
    /// </summary>
    private void SetChildren()
    {
        Children.Clear();

        foreach (var item in EquippedObjects.Values)
        {
            if (item.CurrentWieldedLocation != null)
            {
                TrySetChild(item);
            }
        }
    }

    public void GenerateWieldList()
    {
        if (Biota.PropertiesCreateList == null)
        {
            return;
        }

        var wielded = Biota.PropertiesCreateList.Where(i => (i.DestinationType & DestinationType.Wield) != 0).ToList();

        var items = CreateListSelect(wielded);

        foreach (var item in items)
        {
            var wo = WorldObjectFactory.CreateNewWorldObject(item, Tier ?? 1);

            if (wo == null)
            {
                continue;
            }

            //if (wo.ValidLocations == null || (ItemCapacity ?? 0) > 0)
            {
                if (!TryAddToInventory(wo))
                {
                    wo.Destroy();
                }
            }
            //else
            //TryWieldObject(wo, (EquipMask)wo.ValidLocations);
        }
    }

    public static List<PropertiesCreateList> CreateListSelect(List<PropertiesCreateList> createList)
    {
        var trophy_drop_rate = PropertyManager.GetDouble("trophy_drop_rate").Item;
        if (trophy_drop_rate != 1.0)
        {
            return CreateListSelect(createList, (float)trophy_drop_rate);
        }

        var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
        var totalProbability = 0.0f;
        var rngSelected = false;

        var results = new List<PropertiesCreateList>();

        foreach (var item in createList)
        {
            var destinationType = (DestinationType)item.DestinationType;
            var useRNG = destinationType.HasFlag(DestinationType.Treasure) && item.Shade != 0;

            var shadeOrProbability = item.Shade;

            if (useRNG)
            {
                // handle sets in 0-1 chunks
                if (totalProbability >= 1.0f)
                {
                    totalProbability = 0.0f;
                    rng = ThreadSafeRandom.Next(0.0f, 1.0f);
                    rngSelected = false;
                }

                var probability = shadeOrProbability;

                totalProbability += probability;

                if (rngSelected || rng >= totalProbability)
                {
                    continue;
                }

                rngSelected = true;
            }

            results.Add(item);
        }

        return results;
    }

    public static List<PropertiesCreateList> CreateListSelect(List<PropertiesCreateList> _createList, float dropRateMod)
    {
        var createList = new CreateList(_createList);
        CreateListSetModifier modifier = null;

        var rng = ThreadSafeRandom.Next(0.0f, 1.0f);
        var totalProbability = 0.0f;
        var rngSelected = false;

        var results = new List<PropertiesCreateList>();

        for (var i = 0; i < _createList.Count; i++)
        {
            var item = _createList[i];

            var destinationType = (DestinationType)item.DestinationType;
            var useRNG = destinationType.HasFlag(DestinationType.Treasure) && item.Shade != 0;

            var shadeOrProbability = item.Shade;

            if (useRNG)
            {
                // handle sets in 0-1 chunks
                if (totalProbability == 0.0f || totalProbability >= 1.0f)
                {
                    totalProbability = 0.0f;
                    rng = ThreadSafeRandom.Next(0.0f, 1.0f);
                    rngSelected = false;

                    modifier = createList.GetSetModifier(i, dropRateMod);
                }

                if (modifier != null)
                {
                    var probability = (float)
                        Math.Round(shadeOrProbability * (item.WeenieClassId != 0 ? modifier.TrophyMod : modifier.NoneMod), 4);

                    totalProbability += probability;
                }

                //Console.WriteLine($"Modifier: {modifier}, Prob: {probability}, TotalProb: {totalProbability}");
                if (rngSelected || rng >= totalProbability)
                {
                    continue;
                }

                rngSelected = true;
            }

            results.Add(item);
        }

        return results;
    }

    public uint? WieldedTreasureType
    {
        get => GetProperty(PropertyDataId.WieldedTreasureType);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyDataId.WieldedTreasureType);
            }
            else
            {
                SetProperty(PropertyDataId.WieldedTreasureType, value.Value);
            }
        }
    }

    public List<TreasureWielded> WieldedTreasure
    {
        get
        {
            if (WieldedTreasureType.HasValue)
            {
                return DatabaseManager.World.GetCachedWieldedTreasure(WieldedTreasureType.Value);
            }

            return null;
        }
    }

    public void GenerateWieldedTreasure()
    {
        if (WieldedTreasure == null)
        {
            return;
        }

        //var table = new TreasureWieldedTable(WieldedTreasure);

        var wieldedTreasure = GenerateWieldedTreasureSets(WieldedTreasure);

        if (wieldedTreasure == null)
        {
            return;
        }

        foreach (var item in wieldedTreasure)
        {
            //if (item.ValidLocations == null || (ItemCapacity ?? 0) > 0)
            {
                if (!TryAddToInventory(item))
                {
                    item.Destroy();
                }
            }
            //else
            //TryWieldObject(item, (EquipMask)item.ValidLocations);
        }
    }

    public uint? InventoryTreasureType
    {
        get => GetProperty(PropertyDataId.InventoryTreasureType);
        set
        {
            if (!value.HasValue)
            {
                RemoveProperty(PropertyDataId.InventoryTreasureType);
            }
            else
            {
                SetProperty(PropertyDataId.InventoryTreasureType, value.Value);
            }
        }
    }

    public void GenerateInventoryTreasure()
    {
        if (InventoryTreasureType == null || InventoryTreasureType.Value <= 0)
        {
            return;
        }

        // based on property name found in older data, this property was only found 5 weenies (entirely contained in Focusing Stone quest)
        // guessing that the value might have possibly allowed for either Death or Wielded treasure, but technically it might have only been the former.
        // so for now, coded for checking both types.
        // Although the property's name seemingly was removed, either it's value was still used in code OR its value was moved into DeathTreasureType/CreateList
        // because pcaps for these 5 objects do show similar, if not exact, results on corpses.

        var treasureDeath = DatabaseManager.World.GetCachedDeathTreasure(InventoryTreasureType.Value);
        var treasureWielded = DatabaseManager.World.GetCachedWieldedTreasure(InventoryTreasureType.Value);

        var treasure = new List<WorldObject>();
        if (treasureDeath != null)
        {
            treasure = LootGenerationFactory.CreateRandomLootObjects(treasureDeath);
        }
        else if (treasureWielded != null)
        {
            treasure = GenerateWieldedTreasureSets(treasureWielded);
        }

        foreach (var item in treasure)
        {
            item.DestinationType = DestinationType.Treasure;
            // add this flag so item can move over to corpse upon death
            // (ACE logic: it is likely all inventory of a creature was moved over without reservation (bonded rules enforced), but ACE is slightly different in how it handles it for net same result)

            if (!TryAddToInventory(item))
            {
                item.Destroy();
            }
        }
    }

    protected void UpdateArmorModBuffs()
    {
        var player = this as Player;

        var enchantments = Biota
            .PropertiesEnchantmentRegistry.Clone(BiotaDatabaseLock)
            .Where(i => i.Duration == -1 && i.SpellId != (int)SpellId.Vitae)
            .ToList();

        // ARMOR RUN MOD
        if (GetArmorRunMod() > 0.01 && this is Player)
        {
            var spell = new Server.Entity.Spell(SpellId.OntheRun);
            var addResult = EnchantmentManager.Add(spell, null, null, true);
            addResult.Enchantment.StatModValue = (float)(GetArmorRunMod() ?? 0) + 1;

            if (player != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(this, addResult.Enchantment))
                );
                player.HandleRunRateUpdate(spell);
            }
        }
        else
        {
            foreach (var enchantment in enchantments)
            {
                var spellId = (uint)SpellId.OntheRun;
                if (enchantment.SpellId == spellId)
                {
                    EnchantmentManager.Dispel(enchantment);
                    player.HandleRunRateUpdate(new Server.Entity.Spell(SpellId.OntheRun));
                }
            }
        }
        // ARMOR HEALTH MOD
        if (GetArmorHealthMod() >= 0.01 && this is Player)
        {
            var spell = new Server.Entity.Spell(SpellId.Ardence);
            var addResult = EnchantmentManager.Add(spell, null, null, true);
            addResult.Enchantment.StatModValue = (float)(GetArmorHealthMod() ?? 0) + 1;

            if (player != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(this, addResult.Enchantment))
                );
                player.HandleMaxVitalUpdate(spell);
            }
        }
        else
        {
            foreach (var enchantment in enchantments)
            {
                var spellId = (uint)SpellId.Ardence;
                if (enchantment.SpellId == spellId)
                {
                    EnchantmentManager.Dispel(enchantment);
                    player.HandleMaxVitalUpdate(new Server.Entity.Spell(SpellId.Ardence));
                }
            }
        }
        // ARMOR STAMINA MOD
        if (GetArmorStaminaMod() >= 0.01 && this is Player)
        {
            var spell = new Server.Entity.Spell(SpellId.Vim);
            var addResult = EnchantmentManager.Add(spell, null, null, true);
            addResult.Enchantment.StatModValue = (float)(GetArmorStaminaMod() ?? 0) + 1;

            if (player != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(this, addResult.Enchantment))
                );
                player.HandleMaxVitalUpdate(spell);
            }
        }
        else
        {
            foreach (var enchantment in enchantments)
            {
                var spellId = (uint)SpellId.Vim;
                if (enchantment.SpellId == spellId)
                {
                    EnchantmentManager.Dispel(enchantment);
                    player.HandleMaxVitalUpdate(new Server.Entity.Spell(SpellId.Vim));
                }
            }
        }
        // ARMOR MANA MOD
        if (GetArmorManaMod() >= 0.01 && this is Player)
        {
            var spell = new Server.Entity.Spell(SpellId.Volition);
            var addResult = EnchantmentManager.Add(spell, null, null, true);

            addResult.Enchantment.StatModValue = (float)(GetArmorManaMod() ?? 0) + 1;

            if (player != null)
            {
                player.Session.Network.EnqueueSend(
                    new GameEventMagicUpdateEnchantment(player.Session, new Enchantment(this, addResult.Enchantment))
                );
                player.HandleMaxVitalUpdate(spell);
            }
        }
        else
        {
            foreach (var enchantment in enchantments)
            {
                var spellId = (uint)SpellId.Volition;
                if (enchantment.SpellId == spellId)
                {
                    EnchantmentManager.Dispel(enchantment);
                    player.HandleMaxVitalUpdate(new Server.Entity.Spell(SpellId.Volition));
                }
            }
        }
    }

    private void MutateWeaponForArchetype(WorldObject worldObject, int avgDamage)
    {
        if (worldObject is { WeenieType: WeenieType.MeleeWeapon })
        {
            var variance = worldObject.DamageVariance ?? 1.0f;
            var baseDamage = Math.Max((int)(avgDamage / (1.0f - (variance / 2))), 1);

            worldObject.SetProperty(PropertyInt.Damage, baseDamage);
        }

        if (worldObject is { WeenieType: WeenieType.Missile })
        {
            var variance = worldObject.DamageVariance ?? 1.0f;
            var baseDamage = Math.Max((int)(avgDamage / (1.0f - (variance / 2))), 1);

            worldObject.SetProperty(PropertyInt.Damage, baseDamage);
        }

        if (worldObject is { WeenieType: WeenieType.MissileLauncher })
        {
            var ammo = GetEquippedAmmo();
            if (ammo is null)
            {
                return;
            }

            var variance = ammo.DamageVariance ?? 1.0f;
            var baseDamage = Math.Max((int)(avgDamage / (1.0f - (variance / 2))), 1);

            ammo.SetProperty(PropertyInt.Damage, baseDamage);
        }
    }
}
