using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects;

partial class Player
{
    public double CampfireTimer = 0;

    /// <summary>
    /// A lookup table for MaterialType => Salvage Bag WCIDs
    /// </summary>
    public static Dictionary<int, int> MaterialSalvage = new Dictionary<int, int>()
    {
        { 1, 20983 }, // Ceramic
        { 2, 21067 }, // Porcelain
        { 3, 0 }, // ======= Cloth =======
        { 4, 20987 }, // Linen
        { 5, 20992 }, // Satin
        { 6, 21076 }, // Silk
        { 7, 20994 }, // Velvet
        { 8, 20995 }, // Wool
        { 9, 0 }, // ======= Gems =======
        { 10, 21034 }, // Agate
        { 11, 21035 }, // Amber
        { 12, 21036 }, // Amethyst
        { 13, 21037 }, // Aquamarine
        { 14, 21038 }, // Azurite
        { 15, 21039 }, // Black Garnet
        { 16, 21040 }, // Black Opal
        { 17, 21041 }, // Bloodstone
        { 18, 21043 }, // Carnelian
        { 19, 21044 }, // Citrine
        { 20, 21046 }, // Diamond
        { 21, 21048 }, // Emerald
        { 22, 21049 }, // Fire Opal
        { 23, 21050 }, // Green Garnet
        { 24, 21051 }, // Green Jade
        { 25, 21053 }, // Hematite
        { 26, 21054 }, // Imperial Topaz
        { 27, 21056 }, // Jet
        { 28, 21057 }, // Lapis Lazuli
        { 29, 21058 }, // Lavender Jade
        { 30, 21060 }, // Malachite
        { 31, 21062 }, // Moonstone
        { 32, 21064 }, // Onyx
        { 33, 21065 }, // Opal
        { 34, 21066 }, // Peridot
        { 35, 21069 }, // Red Garnet
        { 36, 21070 }, // Red Jade
        { 37, 21071 }, // Rose Quartz
        { 38, 21072 }, // Ruby
        { 39, 21074 }, // Sapphire
        { 40, 21078 }, // Smokey Quartz
        { 41, 21079 }, // Sunstone
        { 42, 21081 }, // Tiger Eye
        { 43, 21082 }, // Tourmaline
        { 44, 21083 }, // Turquoise
        { 45, 21084 }, // White Jade
        { 46, 21085 }, // White Quartz
        { 47, 21086 }, // White Sapphire
        { 48, 21087 }, // Yellow Garnet
        { 49, 21088 }, // Yellow Topaz
        { 50, 21089 }, // Zircon
        { 51, 21055 }, // Ivory
        { 52, 21059 }, // Leather
        { 53, 20981 }, // Armoredillo Hide
        { 54, 21052 }, // Gromnie Hide
        { 55, 20991 }, // Reedshark Hide
        { 56, 0 }, // ======= Metal =======
        { 57, 21042 }, // Brass
        { 58, 20982 }, // Bronze
        { 59, 21045 }, // Copper
        { 60, 20984 }, // Gold
        { 61, 20986 }, // Iron
        { 62, 21068 }, // Pyreal
        { 63, 21077 }, // Silver
        { 64, 20993 }, // Steel
        { 65, 0 }, // ======= Stone =======
        { 66, 20980 }, // Alabaster
        { 67, 20985 }, // Granite
        { 68, 21061 }, // Marble
        { 69, 21063 }, // Obsidian
        { 70, 21073 }, // Sandstone
        { 71, 21075 }, // Serpentine
        { 72, 0 }, // ======= Wood =======
        { 73, 21047 }, // Ebony
        { 74, 20988 }, // Mahogany
        { 75, 20989 }, // Oak
        { 76, 20990 }, // Pine
        { 77, 21080 }, // Teak
    };

    /// <summary>
    /// Returns the skill with the largest current value (buffed)
    /// </summary>
    public CreatureSkill GetMaxSkill(List<Skill> skills)
    {
        CreatureSkill maxSkill = null;
        foreach (var skill in skills)
        {
            var creatureSkill = GetCreatureSkill(skill);
            if (maxSkill == null || creatureSkill.Current > maxSkill.Current)
            {
                maxSkill = creatureSkill;
            }
        }
        return maxSkill;
    }

    public static List<Skill> TinkeringSkills = new List<Skill>()
    {
        Skill.Tailoring,
        Skill.Blacksmithing,
        Skill.Jewelcrafting,
        Skill.Spellcrafting,
        Skill.Woodworking
    };

    private bool ToolIsValidUst(uint tool)
    {
        var toolObject = FindObject(tool, SearchLocations.Everywhere, out _, out var rootOwner, out _);

        if (
            toolObject == null
            || toolObject.WeenieClassId != (uint)ACE.Entity.Enum.WeenieClassName.W_TINKERINGTOOL_CLASS
        )
        {
            SendWeenieError(WeenieError.NotASalvageTool);
            return false;
        }
        else if (rootOwner != this)
        {
            SendWeenieError(WeenieError.YouDoNotOwnThatSalvageTool);
            return false;
        }
        else
        {
            return true;
        }
    }

    public void HandleSalvaging(uint tool, List<uint> salvageItems)
    {
        if (!ToolIsValidUst(tool))
        {
            return;
        }

        var salvageBags = new List<WorldObject>();
        var salvageResults = new SalvageResults();

        foreach (var itemGuid in salvageItems)
        {
            var item = GetInventoryItem(itemGuid);
            if (item == null)
            {
                //log.DebugFormat("[CRAFTING] {0}.HandleSalvaging({1:X8}): couldn't find inventory item", Name, itemGuid);
                continue;
            }

            if (item.MaterialType == null)
            {
                _log.Warning($"[CRAFTING] {Name}.HandleSalvaging({item.Name}): no material type");
                continue;
            }

            if (IsTrading && item.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow))
            {
                SendWeenieError(WeenieError.YouCannotSalvageItemsInTrading);
                continue;
            }

            if (item.Workmanship == null || item.Retained)
            {
                continue;
            }

            // random chance of receiving a jewelcrafting gem in salvage, higher based on gem count and tier

            if (item.GemType != null)
            {
                var random = new Random();
                var roll = random.NextDouble();
                var basechance = 0.001 * (double)item.GemCount;
                var tierModifier = 1.0 / Math.Pow(2, (double)item.Tier);

                var gemBonusWithTier = (basechance * tierModifier);

                if (gemBonusWithTier > roll)
                {
                    if (SalvagedGems.ContainsKey((int)item.GemType))
                    {
                        var wcid = SalvagedGems[(int)item.GemType];

                        var wo = WorldObjectFactory.CreateNewWorldObject((uint)wcid);
                        wo.Tier = item.Tier;
                        wo.Workmanship = item.Workmanship;

                        //player.Session.Network.EnqueueSend(new GameMessageSystemChat($"While salvaging the weapon, you recover a {wo.Name}.", ChatMessageType.Craft));

                        TryCreateInInventoryWithNetworking(wo);
                    }
                }

                // generate Bezel Fragments if the salvaged item has sockets
                var jewelSockets = item.JewelSockets ?? 0;

                if (jewelSockets > 0)
                {
                    var wo = WorldObjectFactory.CreateNewWorldObject(1053975);
                    wo.StackSize = jewelSockets;

                    TryCreateInInventoryWithNetworking(wo);

                    var bezelFragmentsString = jewelSockets > 1 ? "Bezel Fragments" : "Bezel Fragment";
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You obtain {jewelSockets} {bezelFragmentsString}.",
                            ChatMessageType.Broadcast
                        )
                    );
                }
            }

            // can any salvagable items be stacked?
            TryConsumeFromInventoryWithNetworking(item);

            AddSalvage(salvageBags, item, salvageResults);

            // can any salvagable items be stacked?
            TryConsumeFromInventoryWithNetworking(item);
        }

        // add salvage bags
        foreach (var salvageBag in salvageBags)
        {
            TryCreateInInventoryWithNetworking(salvageBag);
        }

        // send network messages
        if (!SquelchManager.Squelches.Contains(this, ChatMessageType.Salvaging))
        {
            foreach (var kvp in salvageResults.GetMessages())
            {
                var salvageSkill = kvp.Key;
                var results = kvp.Value;

                foreach (var result in results)
                {
                    var salvResults = new Network.Structure.SalvageResult(result);
                    var materialType = Regex.Replace((salvResults.MaterialType).ToString(), "(?<!^)([A-Z])", " $1");
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat(
                            $"You obtain {salvResults.Units} {materialType} (ws {salvResults.Workmanship.ToString("N2")}).",
                            ChatMessageType.Broadcast
                        )
                    );
                }
            }
        }
    }

    public static Dictionary<int, int> SalvagedGems = new Dictionary<int, int>()
    {
        { 10, 2413 }, // Agate
        { 11, 2426 }, // Amber
        { 12, 2393 }, // Amethyst
        { 13, 2421 }, // Aquamarine
        { 14, 2414 }, // Azurite
        { 15, 2394 }, // Black Garnet
        { 16, 2402 }, // Black Opal
        { 17, 2427 }, // Bloodstone
        { 18, 2428 }, // Carnelian
        { 19, 2429 }, // Citrine
        { 20, 2409 }, // Diamond
        { 21, 2410 }, // Emerald
        { 22, 2403 }, // Fire Opal
        { 23, 2422 }, // Green Garnet
        { 24, 2395 }, // Green Jade
        { 25, 2430 }, // Hematite
        { 26, 2404 }, // Imperial Topaz
        { 27, 2396 }, // Jet
        { 28, 2415 }, // Lapis Lazuli
        { 29, 2405 }, // Lavender Jade
        { 30, 2416 }, // Malachite
        { 31, 2431 }, // Moonstone
        { 32, 2432 }, // Onyx
        { 33, 2423 }, // Opal
        { 34, 2424 }, // Peridot
        { 35, 2397 }, // Red Garnet
        { 36, 2406 }, // Red Jade
        { 37, 2433 }, // Rose Quartz
        { 38, 2411 }, // Ruby
        { 39, 2412 }, // Sapphire
        { 40, 2417 }, // Smokey Quartz
        { 41, 2407 }, // Sunstone
        { 42, 2418 }, // Tiger Eye
        { 43, 2398 }, // Tourmaline
        { 44, 2419 }, // Turquoise
        { 45, 2399 }, // White Jade
        { 46, 2420 }, // White Quartz
        { 47, 2408 }, // White Sapphire
        { 48, 2400 }, // Yellow Garnet
        { 49, 2425 }, // Yellow Topaz
        { 50, 2401 }, // Zircon
    };

    public void AddSalvage(List<WorldObject> salvageBags, WorldObject item, SalvageResults salvageResults)
    {
        var materialType = (MaterialType)item.MaterialType;

        // determine the amount of salvage produced (structure)
        SalvageMessage message = null;
        var amountProduced = GetStructure(item, salvageResults, ref message);

        var remaining = amountProduced;

        while (remaining > 0)
        {
            // get the destination salvage bag

            // if there are no existing salvage bags for this material type,
            // or all of the salvage bags for this material type are full,
            // this will create a new salvage bag, and adds it to salvageBags

            var salvageBag = GetSalvageBag(materialType, salvageBags);

            var added = TryAddSalvage(salvageBag, item, remaining);
            remaining -= added;

            // https://asheron.fandom.com/wiki/Salvaging/Value_Pre2013

            // increase value of salvage bag - salvage skill is a factor,
            // if bags aren't being combined here
            var valueFactor = (float)added / amountProduced;

            var addedValue = (int)Math.Round((item.Value ?? 0) * valueFactor);

            salvageBag.Value = Math.Min((salvageBag.Value ?? 0) + addedValue, 75000);

            // a bit different here, since ACE handles overages
            if (message != null)
            {
                message.Workmanship += item.ItemWorkmanship ?? 0.0f;
                message.NumItemsInMaterial++;
            }
        }
    }

    public int TryAddSalvage(WorldObject salvageBag, WorldObject item, int tryAmount)
    {
        var maxStructure = salvageBag.MaxStructure ?? 1000;
        var structure = salvageBag.Structure ?? 0;

        var space = maxStructure - structure;

        var amount = Math.Min(tryAmount, space);

        salvageBag.Structure = (ushort)(structure + amount);

        // add workmanship
        var item_numItems = item.StackSize ?? 1;
        var workmanship_bag = salvageBag.ItemWorkmanship ?? 0;
        var workmanship_item = item.ItemWorkmanship ?? 0;

        salvageBag.ItemWorkmanship = workmanship_bag + workmanship_item * item_numItems;

        // increment # of items that went into this salvage bag
        if (item.ItemType == ItemType.TinkeringMaterial)
        {
            item_numItems = item.NumItemsInMaterial ?? 1;

            // handle overflows when combining bags
            if (tryAmount > space)
            {
                var scalar = (float)space / tryAmount;
                var newItems = (int)Math.Ceiling(item_numItems * scalar);
                scalar = (float)newItems / item_numItems;
                var prevNumItems = item_numItems;
                item_numItems = newItems;

                salvageBag.ItemWorkmanship -= (int)Math.Round(workmanship_item * (1.0 - scalar));

                // and for the next bag...
                if (prevNumItems == newItems)
                {
                    newItems--;
                }

                var itemWorkmanship = item.Workmanship;
                item.NumItemsInMaterial -= newItems;
                //item.ItemWorkmanship -= (int)Math.Round(workmanship_item * scalar);
                item.ItemWorkmanship = (int)Math.Round(item.NumItemsInMaterial.Value * (float)itemWorkmanship);
            }
        }
        salvageBag.NumItemsInMaterial = (salvageBag.NumItemsInMaterial ?? 0) + item_numItems;

        salvageBag.Name = $"Salvage ({salvageBag.Structure})";

        if (item.ItemType == ItemType.TinkeringMaterial)
        {
            if (!PropertyManager.GetBool("salvage_handle_overages").Item)
            {
                return tryAmount;
            }
            else
            {
                return amount;
            }
        }
        else
        {
            return amount;
        }
    }

    public int GetStructure(WorldObject salvageItem, SalvageResults salvageResults, ref SalvageMessage message)
    {
        // is this a bag of salvage?
        // if so, return its existing structure

        if (salvageItem.ItemType == ItemType.TinkeringMaterial)
        {
            return salvageItem.Structure.Value;
        }

        // Salvage units returned = 1 + (WK / 2)
        // If WK % 2 is 1, give a 50% chance to round up, else round down
        var addStructure = 1;

        var bonus = (int)Math.Floor((salvageItem.Workmanship ?? 1) * 0.5);
        if ((int)(salvageItem.Workmanship ?? 1) % 2 == 1 && ThreadSafeRandom.Next(0.0f, 1.0f) > 0.5f)
        {
            bonus += 1;
        }
        
        addStructure += bonus;

        message = salvageResults.GetMessage(
            salvageItem.MaterialType ?? ACE.Entity.Enum.MaterialType.Unknown,
            Skill.Salvaging
        );
        message.Amount += (uint)addStructure;

        return addStructure;
    }

    /// <summary>
    /// Calculates the number of units returned from a salvaging operation
    /// </summary>
    /// <param name="skill">The current salvaging or highest trained tinkering skill for the player</param>
    /// <param name="workmanship">The workmanship of the item being salvaged</param>
    /// <param name="numAugs">The AugmentationBonusSalvage for the player</param>
    /// <returns></returns>
    public static int CalcNumUnits(int skill, float workmanship, int numAugs)
    {
        // https://web.archive.org/web/20170130213649/http://www.thejackcat.com/AC/Shopping/Crafts/Salvage_old.htm
        // https://web.archive.org/web/20170130194012/http://www.thejackcat.com/AC/Shopping/Crafts/Salvage.htm

        return 1 + (int)Math.Floor(skill / 194.0f * workmanship * (1.0f + 0.25f * numAugs));
    }

    public WorldObject GetSalvageBag(MaterialType materialType, List<WorldObject> salvageBags)
    {
        // first try finding the first non-filled salvage bag, for this material type
        var existing = salvageBags.FirstOrDefault(i =>
            (i.GetProperty(PropertyInt.MaterialType) ?? 0) == (int)materialType
            && (i.Structure ?? 0) < (i.MaxStructure ?? 0)
        );

        if (existing != null)
        {
            return existing;
        }

        // not found - create a new salvage bag
        var wcid = (uint)MaterialSalvage[(int)materialType];
        var salvageBag = WorldObjectFactory.CreateNewWorldObject(wcid);

        salvageBag.Structure = null; // TODO: fix bugged TOD data for mahogany 20988 / green garnet 21050
        salvageBag.ItemWorkmanship = null;
        salvageBag.NumItemsInMaterial = null;

        salvageBags.Add(salvageBag);

        return salvageBag;
    }

    public static void TryAwardCraftingXp(
        Player player,
        CreatureSkill creatureSkill,
        Skill skill,
        int difficulty,
        int armorSlots = 1,
        float bonusMod = 1.0f
    )
    {
        // check to ensure appropriately difficult craft before granting (is player skill no more than 50 points above relative difficulty)
        if (creatureSkill.Current - difficulty >= 50)
        {
            return;
        }

        // Awarded xp scales based on level of current skill progress (from 50% of current rank awarded per craft, down to 1% at 200 skill).
        var progressPercentage = Math.Min((float)creatureSkill.Current / 200, 1.0f);
        var progressMod = 0.5f * (Math.Max(1.0f - progressPercentage, 0.01f));

        // Awarded xp received a bonus or penalty for relative difficulty of the craft (-100% to +100%).
        var relativeDifficulty = difficulty - creatureSkill.Current;
        var difficultyMod = 1 + Math.Clamp(relativeDifficulty, -50.0f, 50.0f) / 50.0f;

        var xP = (player.GetXPBetweenSkillLevels(creatureSkill.AdvancementClass, creatureSkill.Ranks, creatureSkill.Ranks + 1) ?? 0);
        var totalXp = (uint)(xP * progressMod * difficultyMod * armorSlots * bonusMod);

        player.NoContribSkillXp(player, skill, totalXp, false);

        if (PropertyManager.GetBool("debug_crafting_system").Item)
        {
            Console.WriteLine(
                $"\nAttempt {creatureSkill.Skill} craft:\n" +
                $" Skill: {creatureSkill.Current}, RecipeDiff: {difficulty}\n" +
                $" ProgressPercent: {progressPercentage}, ProgressMod: {progressMod}\n" +
                $" CraftDiff: {relativeDifficulty}, DiffMod: {difficultyMod}\n" +
                $" BonusMod: {bonusMod}\n" +
                $" ToLevelXp: {xP}, TotalXpAward: {totalXp}"
            );
        }
    }
}
