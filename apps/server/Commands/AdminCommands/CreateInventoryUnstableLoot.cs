using System;
using System.Globalization;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Commands.Handlers;
using ACE.Server.Factories;
using ACE.Server.Factories.Entity;
using ACE.Server.Factories.Enum;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;

namespace ACE.Server.Commands.AdminCommands;

public class CreateInventoryUnstableLoot
{
    private const int MaxCount = 1000;
    private const int MaxAttemptsPerItem = 25;

    [CommandHandler(
        "ciu",
        AccessLevel.Admin,
        CommandHandlerFlag.RequiresWorld,
        2,
        "Creates unstable loot in your inventory using loot generation by type+tier.",
        "<type> <tier> [count] [qualityMod]"
    )]
    [CommandHandler(
        "createunstable",
        AccessLevel.Admin,
        CommandHandlerFlag.RequiresWorld,
        2,
        "Creates unstable loot in your inventory using loot generation by type+tier.",
        "<type> <tier> [count] [qualityMod]"
    )]
    public static void Handle(Session session, params string[] parameters)
    {
        if (!TryParseType(parameters[0], out var forceItemType, out var forceWeaponType, out var forceArmorType))
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "Invalid type. Valid values: armor, armorrandom, armorstr, armorwarrior, armorcoord, armorrogue, armorcaster, robe, robes, clothing, melee, missile, caster, jewelry, sigil, sigilwarrior, sigilrogue, sigilcaster.",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        if (!int.TryParse(parameters[1], out var tier) || tier < 1 || tier > 8)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat("Tier must be a number between 1 and 8.", ChatMessageType.Broadcast)
            );
            return;
        }

        var count = 1;
        if (parameters.Length > 2)
        {
            if (!int.TryParse(parameters[2], out count) || count < 1)
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat("Count must be a positive number.", ChatMessageType.Broadcast)
                );
                return;
            }

            if (count > MaxCount)
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Count {count} exceeds max {MaxCount}; clamped to {MaxCount}.",
                        ChatMessageType.Broadcast
                    )
                );
                count = MaxCount;
            }
        }

        var qualityMod = 0.0f;
        if (parameters.Length > 3)
        {
            if (!float.TryParse(parameters[3], NumberStyles.Float, CultureInfo.InvariantCulture, out qualityMod))
            {
                session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "qualityMod must be a number (example: 0.5).",
                        ChatMessageType.Broadcast
                    )
                );
                return;
            }
        }

        var clampedQualityMod = Math.Clamp(qualityMod, 0.0f, 2.0f);
        if (Math.Abs(clampedQualityMod - qualityMod) > float.Epsilon)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"qualityMod {qualityMod.ToString(CultureInfo.InvariantCulture)} was out of range and was clamped to {clampedQualityMod.ToString(CultureInfo.InvariantCulture)}.",
                    ChatMessageType.Broadcast
                )
            );
        }

        var created = 0;
        var failed = 0;
        var inventoryFailed = false;

        var context = new LootGenerationContext { UnstableLoot = true };

        for (var i = 0; i < count; i++)
        {
            var success = false;

            for (var attempt = 0; attempt < MaxAttemptsPerItem; attempt++)
            {
                var profile = BuildProfile(tier, clampedQualityMod, forceItemType, forceWeaponType, forceArmorType);
                var item = LootGenerationFactory.CreateRandomLootObjects_New(
                    profile,
                    TreasureItemCategory.MagicItem,
                    context
                );

                if (item == null)
                {
                    failed++;
                    continue;
                }

                if (!(IsEligibleItemType(item.ItemType) || item is SigilTrinket) || item.GetProperty(PropertyBool.IsUnstable) != true)
                {
                    item.Destroy();
                    failed++;
                    continue;
                }

                if (!session.Player.TryCreateInInventoryWithNetworking(item))
                {
                    item.Destroy();
                    inventoryFailed = true;
                    break;
                }

                created++;
                success = true;
                break;
            }

            if (inventoryFailed)
            {
                break;
            }

            if (!success)
            {
                failed++;
            }
        }

        if (inventoryFailed)
        {
            session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "Inventory is full or item could not be added; generation stopped.",
                    ChatMessageType.Broadcast
                )
            );
        }

        session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"CIU complete: created {created}/{count} unstable {parameters[0]} item(s) at tier {tier} (qualityMod={clampedQualityMod.ToString(CultureInfo.InvariantCulture)}, failed rolls={failed}).",
                ChatMessageType.Broadcast
            )
        );

        if (created > 0)
        {
            PlayerManager.BroadcastToAuditChannel(
                session.Player,
                $"{session.Player.Name} used @ciu type={parameters[0]} tier={tier} count={count} qualityMod={clampedQualityMod.ToString(CultureInfo.InvariantCulture)} and created {created} unstable item(s)."
            );
        }
    }

    private static TreasureDeathExtended BuildProfile(
        int tier,
        float qualityMod,
        TreasureItemType_Orig forceItemType,
        TreasureWeaponType forceWeaponType,
        TreasureArmorType forceArmorType)
    {
        return new TreasureDeathExtended
        {
            Tier = tier,
            LootQualityMod = qualityMod,
            ForceTreasureItemType = forceItemType,
            ForceArmorType = forceArmorType,
            ForceWeaponType = forceWeaponType,
            ForceHeritage = TreasureHeritageGroup.Invalid,

            ItemChance = 100,
            ItemMinAmount = 1,
            ItemMaxAmount = 1,
            ItemTreasureTypeSelectionChances = 8,

            MagicItemChance = 100,
            MagicItemMinAmount = 1,
            MagicItemMaxAmount = 1,
            MagicItemTreasureTypeSelectionChances = 8,

            MundaneItemChance = 100,
            MundaneItemMinAmount = 1,
            MundaneItemMaxAmount = 1,
            MundaneItemTypeSelectionChances = 7,

            UnknownChances = 21
        };
    }

    private static bool TryParseType(
        string type,
        out TreasureItemType_Orig forceItemType,
        out TreasureWeaponType forceWeaponType,
        out TreasureArmorType forceArmorType)
    {
        forceItemType = TreasureItemType_Orig.Undef;
        forceWeaponType = TreasureWeaponType.Undef;
        forceArmorType = TreasureArmorType.Undef;

        switch (type.ToLowerInvariant())
        {
            case "armor":
            case "armorrandom":
                forceItemType = TreasureItemType_Orig.Armor;
                return true;

            case "armorstr":
            case "armorwarrior":
                forceItemType = TreasureItemType_Orig.ArmorWarrior;
                return true;

            case "armorcoord":
            case "armorrogue":
                forceItemType = TreasureItemType_Orig.ArmorRogue;
                return true;

            case "armorcaster":
                forceItemType = TreasureItemType_Orig.ArmorCaster;
                return true;

            case "robe":
            case "robes":
                forceItemType = TreasureItemType_Orig.Armor;
                forceArmorType = TreasureArmorType.Cloth;
                return true;

            case "clothing":
                forceItemType = TreasureItemType_Orig.Clothing;
                return true;

            case "melee":
                forceItemType = TreasureItemType_Orig.Weapon;
                forceWeaponType = TreasureWeaponType.MeleeWeapon;
                return true;

            case "missile":
                forceItemType = TreasureItemType_Orig.Weapon;
                forceWeaponType = TreasureWeaponType.MissileWeapon;
                return true;

            case "caster":
                forceItemType = TreasureItemType_Orig.Caster;
                forceWeaponType = TreasureWeaponType.Caster;
                return true;

            case "jewelry":
                forceItemType = TreasureItemType_Orig.Jewelry;
                return true;

            case "sigil":
            case "sigilwarrior":
                forceItemType = TreasureItemType_Orig.SigilTrinketWarrior;
                return true;

            case "sigilrogue":
                forceItemType = TreasureItemType_Orig.SigilTrinketRogue;
                return true;

            case "sigilcaster":
                forceItemType = TreasureItemType_Orig.SigilTrinketCaster;
                return true;

            default:
                return false;
        }
    }

    private static bool IsEligibleItemType(ItemType itemType)
    {
        return itemType == ItemType.Armor
            || itemType == ItemType.MeleeWeapon
            || itemType == ItemType.MissileWeapon
            || itemType == ItemType.Caster
            || itemType == ItemType.Jewelry
            || itemType == ItemType.Clothing;
    }
}
