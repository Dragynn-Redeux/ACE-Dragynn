using System;
using System.Collections.Generic;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using DamageType = ACE.Entity.Enum.DamageType;
using MotionCommand = ACE.Entity.Enum.MotionCommand;

namespace ACE.Server.WorldObjects;

partial class Jewel : WorldObject
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public Jewel(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public Jewel(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed = false)
    {
        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (!RecipeManager.VerifyUse(player, source, target, true))
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (!target.JewelSockets.HasValue || !HasEmptySockets(target))
        {
            player.Session.Network.EnqueueSend(new GameMessageSystemChat($"The {target.Name} has no empty sockets.", ChatMessageType.Craft));
            player.SendUseDoneEvent();
            return;
        }

        if ((source.JewelMaterialType is null || source.JewelQuality is null) && source.JewelSocket1 is null)
        {
            return;
        }

        // legacy support
        if (source.JewelSocket1 is not null and not "empty")
        {
            var jewelString = source.JewelSocket1.Split('/');

            if (StringToMaterialType.TryGetValue(jewelString[1], out var jewelMaterial))
            {
                source.JewelMaterialType = jewelMaterial;
            }

            if (JewelQualityStringToValue.TryGetValue(jewelString[0], out var jewelQuality))
            {
                source.JewelQuality = jewelQuality;
            }
        }

        if (target.Workmanship < source.JewelQuality)
        {
            var orGreater = source.JewelQuality < 10 ? " or greater" : "";

            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.Name} can only be socketed into an item with a workmanship of {source.JewelQuality}{orGreater}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        if (source.JewelMaterialType is null)
        {
            return;
        }

        // wield restrictions
        var ratingTypes = JewelMaterialToType[source.JewelMaterialType.Value];
        var materialWieldRestrictionMain = RatingToEquipLocations[ratingTypes.PrimaryRating];
        var materialWieldRestrictionAlt = RatingToEquipLocations[ratingTypes.AlternateRating];

        if ((target.ValidLocations & materialWieldRestrictionMain) != target.ValidLocations && (target.ValidLocations & materialWieldRestrictionAlt) != target.ValidLocations)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.Name} can never be slotted into the {target.Name}.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // check for rending damage type matches
        if (MaterialDamage.TryGetValue(source.JewelMaterialType.Value, out var damageType))
        {
            if ((int)target.W_DamageType > 0
                && target.ValidLocations is not EquipMask.HandWear and not EquipMask.FootWear
                && target.W_DamageType != damageType
                && target.W_DamageType != DamageType.SlashPierce)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {source.Name} can never be slotted into a weapon of that damage type.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }
        }

        if (target.ValidLocations is EquipMask.Weapon
            && target.W_DamageType == DamageType.SlashPierce
            && source.JewelMaterialType.Value != ACE.Entity.Enum.MaterialType.BlackGarnet
            && source.JewelMaterialType.Value != ACE.Entity.Enum.MaterialType.ImperialTopaz
        )
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.Name} can never be slotted into a weapon of that damage type.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }


        if (!confirmed)
        {
            if (
                !player.ConfirmationManager.EnqueueSend(
                    new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                    $"Adding {source.Name} to {target.Name}, enhancing its properties.\n\n" +
                    $"Once socketed into an item, this jewel becomes permanently attuned to your character. Items with contained jewels become attuned and will remain so until all jewels are removed.\n\n" +
                    $"Jewels may be unsocketed using an Intricate Carving Tool. There is no skill check or destruction chance.\n\n"
                )
            )
            {
                player.SendUseDoneEvent(WeenieError.ConfirmationInProgress);
            }
            else
            {
                player.SendUseDoneEvent();
            }

            return;
        }

        var actionChain = new ActionChain();

        var animTime = 0.0f;

        player.IsBusy = true;

        if (player.CombatMode != CombatMode.NonCombat)
        {
            var stanceTime = player.SetCombatMode(CombatMode.NonCombat);
            actionChain.AddDelaySeconds(stanceTime);

            animTime += stanceTime;
        }

        animTime += player.EnqueueMotion(actionChain, MotionCommand.ClapHands);

        actionChain.AddAction(
            player,
            () =>
            {
                SocketJewel(source, target);

                player.EnqueueBroadcast(new GameMessageUpdateObject(target));
                player.TryConsumeFromInventoryWithNetworking(source);
            }
        );

        player.EnqueueMotion(actionChain, MotionCommand.Ready);

        actionChain.AddAction(
            player,
            () =>
            {
                player.IsBusy = false;
            }
        );

        actionChain.EnqueueChain();

        player.NextUseTime = DateTime.UtcNow.AddSeconds(animTime);
    }

    private static void SocketJewel(WorldObject jewel, WorldObject target)
    {
        if (jewel.JewelMaterialType is null || jewel.JewelQuality is null)
        {
            return;
        }

        for (var i = 0; i < target.JewelSockets; i++)
        {
            if (target.GetProperty(SocketedJewelDetails[i].JewelSocketMaterialIntId) is not null)
            {
                continue;
            }

            target.SetProperty(SocketedJewelDetails[i].JewelSocketMaterialIntId, (int)jewel.JewelMaterialType.Value);
            target.SetProperty(SocketedJewelDetails[i].JewelSocketQualityIntId, jewel.JewelQuality.Value);

            var jewelAltEquipMask = RatingToEquipLocations[JewelMaterialToType[jewel.JewelMaterialType.Value].AlternateRating];

            if ((jewelAltEquipMask & target.ValidLocations) == target.ValidLocations)
            {
                target.SetProperty(SocketedJewelDetails[i].JewelSocketAlternateEffect, true);
            }
            else
            {
                target.SetProperty(SocketedJewelDetails[i].JewelSocketAlternateEffect, false);
            }

            // if a ring or bracelet, change valid locations to left or right only
            if (target.ValidLocations != null && (int)target.ValidLocations == 786432)
            {
                if (
                    jewel.Name.Contains("Carnelian")
                    || jewel.Name.Contains("Azurite")
                    || jewel.Name.Contains("Tiger Eye")
                )
                {
                    target.ValidLocations = EquipMask.FingerWearRight;
                    target.Use = "This ring can only be worn on the right hand.";
                }
                else
                {
                    target.ValidLocations = EquipMask.FingerWearLeft;
                    target.Use = "This ring can only be worn on the left hand.";
                }
            }
            if (target.ValidLocations != null && (int)target.ValidLocations == 196608)
            {
                if (
                    jewel.Name.Contains("Amethyst")
                    || jewel.Name.Contains("Diamond")
                    || jewel.Name.Contains("Onyx")
                    || jewel.Name.Contains("Zircon")
                )
                {
                    target.ValidLocations = EquipMask.WristWearRight;
                    target.Use = "This bracelet can only be worn on the right wrist.";
                }
                else
                {
                    target.ValidLocations = EquipMask.WristWearLeft;
                    target.Use = "This bracelet can only be worn on the left wrist.";
                }
            }

            target.Attuned = AttunedStatus.Attuned;
            target.Bonded = BondedStatus.Bonded;

            break;
        }
    }

    private static bool HasEmptySockets(WorldObject target)
    {
        for (var i = 0; i < (target.JewelSockets ?? 0); i++)
        {
            if (target.GetProperty(SocketedJewelDetails[i].JewelSocketMaterialIntId) is null)
            {
                return true;
            }
        }

        return false;
    }

    public static void HandleJewelcarving(Player player, WorldObject source, WorldObject target, bool success)
    {
        var jewel = RecipeManager.CreateItem(player, 1053900, 1);

        jewel.IconId = target.IconId;

        ModifyCarvedJewel(player, target, jewel, success);

        player.TryConsumeFromInventoryWithNetworking(target, 1);

        if (!success)
        {

            if (jewel is {JewelSocket1Quality: null})
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("You botch your attempt to carve the gem and the jewel shatters.", ChatMessageType.Craft));
                player.TryConsumeFromInventoryWithNetworking(jewel);
                player.EnqueueBroadcast(new GameMessageUpdateObject(jewel));
            }
            else
            {
                player.Session.Network.EnqueueSend(new GameMessageSystemChat("You botch your attempt to carve the gem and some quality is lost.", ChatMessageType.Craft));
                player.TryCreateInInventoryWithNetworking(jewel);
                player.EnqueueBroadcast(new GameMessageUpdateObject(jewel));
            }
        }
        else
        {
            player.TryCreateInInventoryWithNetworking(jewel);
            player.EnqueueBroadcast(new GameMessageUpdateObject(jewel));
        }
    }

    private static void ModifyCarvedJewel(Player player, WorldObject target, WorldObject jewel, bool success)
    {
        var baseValue = 1;

        if (target.ItemWorkmanship is null)
        {
            _log.Error("ModifyCarvedJewel(Player {Player}, Target {Target}, Jewel {Jewel}) - Target workmanship is null. Defaulting to 1 workmanship.", player.Name, target.Name, jewel.Name);
        }
        else
        {
            baseValue = (int)target.ItemWorkmanship;
        }

        if (target.MaterialType is null)
        {
            _log.Error("ModifyCarvedJewel(Player {Player}, Target {Target}, Jewel {Jewel}) - Target material type is null. Defaulting to 1 workmanship.", player.Name, target.Name, jewel.Name);
            return;
        }

        var materialType = target.MaterialType.Value;

        CalcJewelQuality(player, target, jewel, baseValue, materialType, success);
    }

    // Jewel quality is based on workmanship with a possible range of -2 to +2 depending on carve quality, for a scale of 1-12.
    // certain properties will receive double this bonus in their particular calculations, others half etc. but all fundamentally on same scale
    private static void CalcJewelQuality(
        Player player,
        WorldObject target,
        WorldObject jewel,
        int baseValue,
        MaterialType materialType,
        bool success
    )
    {
        if (target.Workmanship is null)
        {
            _log.Error("CalcJewelQuality() - Target workmanship is null for: {Target} ({Wcid})", target.Name, target.WeenieClassId);
            return;
        }

        var modifiedQuality = baseValue;

        var skill = player.GetCreatureSkill(Skill.Jewelcrafting);
        var difficulty = (int)(target.Workmanship * 20 - 20);

        var jewelRelativeQuality = 1.0f;

        // Difficulty "Failure" Penalty - Rather than destroying jewels, we just lower their quality by 1 for each 10 points of skill below expected, plus 1.
        if (!success)
        {
            if (PropertyManager.GetBool("bypass_crafting_checks").Item == false)
            {
                var difference = Math.Max(difficulty - skill.Current, 0);
                var reductionPenalty = (int)Math.Round((float)difference / 10) + 1;

                modifiedQuality -= reductionPenalty;

                jewelRelativeQuality = (float)modifiedQuality / baseValue;
            }

            Player.TryAwardCraftingXp(player, skill, Skill.Jewelcrafting, (int)difficulty, bonusMod: 3.0f, fail: true);
        }
        else
        {
            Player.TryAwardCraftingXp(player, skill, Skill.Jewelcrafting, (int)difficulty, 1, 3.0f);
        }

        jewel.Value = Convert.ToInt32(target.Value * jewelRelativeQuality);
        jewel.JewelQuality = modifiedQuality;
        jewel.JewelMaterialType = materialType;

        if (JewelQuality.TryGetValue(modifiedQuality, out var qualityName))
        {
            jewel.Name = $"{qualityName} {MaterialTypeToString[materialType]}";
            jewel.IconOverlayId = (uint)(100690995 + jewel.JewelQuality);
        }

        if (GemstoneIconMap.TryGetValue(materialType, out var gemIcon))
        {
            jewel.IconId = gemIcon;
        }

        if (target.MaterialType != null && JewelUiEffect.TryGetValue((MaterialType)target.MaterialType, out var uiEffect))
        {
            jewel.UiEffects = (UiEffects)uiEffect;
        }
    }

    public static void HandleUnsocketing(Player player, WorldObject source, WorldObject target)
    {
        if (player == null)
        {
            return;
        }

        var numSocketedJewels = GetNumberOfSocketedJewels(target);
        var freeSpace = player.GetFreeInventorySlots(true);

        if (freeSpace < numSocketedJewels)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You must free up additional packspace in order to unsocket these gems!",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // if a ring or bracelet, set back to left or right wield and remove added text
        if (target.ValidLocations is EquipMask.FingerWearRight or EquipMask.FingerWearLeft)
        {
            target.ValidLocations = EquipMask.FingerWear;
            target.Use = "";
        }
        if (target.ValidLocations is EquipMask.WristWearRight or EquipMask.WristWearLeft)
        {
            target.ValidLocations = EquipMask.WristWear;
            target.Use = "";
        }

        // cycle through slots, emptying out ones that aren't already (
        for (var i = 0; i < (target.JewelSockets ?? 0); i++)
        {
            if (i == 0 && target.JewelSocket1 is not null)
            {
                LegacyUnsocket(player, target, target.JewelSocket1);
                target.JewelSocket1 = null;
                continue;
            }

            if (i == 1 && target.JewelSocket2 is not null)
            {
                LegacyUnsocket(player, target, target.JewelSocket2);
                target.JewelSocket2 = null;
                continue;
            }

            var currentSocketMaterialTypeId = target.GetProperty(SocketedJewelDetails[i].JewelSocketMaterialIntId);
            var currentSocketQualityLevel = target.GetProperty(SocketedJewelDetails[i].JewelSocketQualityIntId);

            if (currentSocketMaterialTypeId is null or < 1 || currentSocketQualityLevel is null or < 1)
            {
                continue;
            }

            // create jewel for inventory
            var jewel = WorldObjectFactory.CreateNewWorldObject(1053900);

            jewel.JewelMaterialType = (MaterialType)currentSocketMaterialTypeId;

            if (jewel is { JewelMaterialType: null })
            {
                _log.Error("HandleUnsocketing() - MaterialType is null during unsocketing from {Target}", target.Name);
                return;
            }

            jewel.JewelQuality = currentSocketQualityLevel;
            jewel.UiEffects = (UiEffects)JewelUiEffect[jewel.JewelMaterialType.Value];
            jewel.IconId = GemstoneIconMap[jewel.JewelMaterialType.Value];
            jewel.IconOverlayId = (uint)(100690995 + jewel.JewelQuality); // TODO: add work 10 overlay icon

            var qualityString = JewelQuality[currentSocketQualityLevel.Value];
            var materialString = MaterialTypeToString[jewel.JewelMaterialType.Value];
            jewel.Name = $"{qualityString} {materialString}";

            jewel.Attuned = AttunedStatus.Attuned;
            jewel.Bonded = BondedStatus.Bonded;

            player.TryCreateInInventoryWithNetworking(jewel);

            // remove jewel properties from target
            target.RemoveProperty(SocketedJewelDetails[i].JewelSocketMaterialIntId);
            target.RemoveProperty(SocketedJewelDetails[i].JewelSocketQualityIntId);
            target.RemoveProperty(SocketedJewelDetails[i].JewelSocketAlternateEffect);
        }

        target.Attuned = null;
        target.Bonded = null;
        player.EnqueueBroadcast(new GameMessageUpdateObject(target));
    }

    private static void LegacyUnsocket(Player player, WorldObject target, string jewelSocketString)
    {
        if (jewelSocketString.StartsWith("Empty"))
        {
            return;
        }

        var jewel = WorldObjectFactory.CreateNewWorldObject(1053900);

        // 0 - prepended quality, 1 - gemstone type, 2 - appended name, 3 - property type, 4 - amount of property, 5 - original gem
        var socketArray = jewelSocketString.Split('/');

        if (StringToMaterialType.TryGetValue(socketArray[1], out var convertedMaterialType))
        {
            if (JewelUiEffect.TryGetValue(convertedMaterialType, out var uiEffect))
            {
                jewel.UiEffects = (UiEffects)uiEffect;
            }

            if (GemstoneIconMap.TryGetValue(convertedMaterialType, out var gemstoneIcon))
            {
                jewel.IconId = gemstoneIcon;
            }
        }

        if (JewelQualityStringToValue.TryGetValue(socketArray[0], out var qualityLevel))
        {
            jewel.JewelQuality = qualityLevel;
        }

        jewel.JewelMaterialType = convertedMaterialType;
        jewel.Name = $"{socketArray[0]} {socketArray[1]}";

        jewel.Attuned = AttunedStatus.Attuned;
        jewel.Bonded = BondedStatus.Bonded;

        player.TryCreateInInventoryWithNetworking(jewel);

        target.JewelSocket1Material = null;
    }

    private static int GetNumberOfSocketedJewels(WorldObject target)
    {
        var number = 0;

        if (target.JewelSocket1Material is not null)
        {
            number++;
        }

        if (target.JewelSocket2Material is not null)
        {
            number++;
        }

        if (target.JewelSocket3Material is not null)
        {
            number++;
        }

        if (target.JewelSocket4Material is not null)
        {
            number++;
        }

        if (target.JewelSocket5Material is not null)
        {
            number++;
        }

        if (target.JewelSocket6Material is not null)
        {
            number++;
        }

        if (target.JewelSocket7Material is not null)
        {
            number++;
        }

        if (target.JewelSocket8Material is not null)
        {
            number++;
        }

        if (target.JewelSocket9Material is not null)
        {
            number++;
        }

        if (target.JewelSocket10Material is not null)
        {
            number++;
        }

        return number;
    }

    public static readonly List<(PropertyInt JewelSocketMaterialIntId, PropertyInt JewelSocketQualityIntId, PropertyBool JewelSocketAlternateEffect)> SocketedJewelDetails =
    [
        (PropertyInt.JewelSocket1Material, PropertyInt.JewelSocket1Quality, PropertyBool.JewelSocket1AlternateEffect),
        (PropertyInt.JewelSocket2Material, PropertyInt.JewelSocket2Quality, PropertyBool.JewelSocket2AlternateEffect),
        (PropertyInt.JewelSocket3Material, PropertyInt.JewelSocket3Quality, PropertyBool.JewelSocket3AlternateEffect),
        (PropertyInt.JewelSocket4Material, PropertyInt.JewelSocket4Quality, PropertyBool.JewelSocket4AlternateEffect),
        (PropertyInt.JewelSocket5Material, PropertyInt.JewelSocket5Quality, PropertyBool.JewelSocket5AlternateEffect),
        (PropertyInt.JewelSocket6Material, PropertyInt.JewelSocket6Quality, PropertyBool.JewelSocket6AlternateEffect),
        (PropertyInt.JewelSocket7Material, PropertyInt.JewelSocket7Quality, PropertyBool.JewelSocket7AlternateEffect),
        (PropertyInt.JewelSocket8Material, PropertyInt.JewelSocket8Quality, PropertyBool.JewelSocket8AlternateEffect),
        (PropertyInt.JewelSocket9Material, PropertyInt.JewelSocket9Quality, PropertyBool.JewelSocket9AlternateEffect),
        (PropertyInt.JewelSocket10Material, PropertyInt.JewelSocket10Quality, PropertyBool.JewelSocket10AlternateEffect),
    ];
}
