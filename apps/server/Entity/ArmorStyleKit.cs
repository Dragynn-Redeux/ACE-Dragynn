using System;
using System.Linq;
using ACE.Database;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Entity;

/// <summary>
/// The Armor Style Kit turns a piece of loot armor into an Armor Style Template, destroying the piece.
/// The template restyles armor of the same weight class as the piece of its style with that armor's coverage:
/// a template from Studded Leather Bracers makes Leather Sleeves look like Studded Leather Sleeves.
/// Armor with the same coverage as the template's piece takes that piece's exact appearance, as a Tailoring Pattern would.
/// Coverage the style has no piece for can't take it, so Koujia can't go on bracers.
/// Helms, gauntlets and boots have no style: their templates carry their exact look, for one other piece of the same kind.
/// </summary>
public static class ArmorStyleKit
{
    private static readonly ILogger _log = Log.ForContext(typeof(ArmorStyleKit));

    public const uint ArmorStyleKitWcid = 1054006;
    public const uint ArmorStyleTemplateWcid = 1054007;

    private const string TemplateSuffix = " Style Template";

    private static readonly Lazy<ArmorStyleCatalogue> catalogue =
        new(
            () =>
                ArmorStyleCatalogue.Build(wcid =>
                {
                    var weenie = DatabaseManager.World.GetCachedWeenie(wcid);
                    return (
                        weenie?.GetProperty(PropertyDataId.ClothingBase),
                        weenie?.GetProperty(PropertyInt.ClothingPriority)
                    );
                })
        );

    public static bool IsStyleItem(uint wcid)
    {
        return wcid == ArmorStyleKitWcid || wcid == ArmorStyleTemplateWcid;
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target, bool confirmed)
    {
        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (!RecipeManager.VerifyUse(player, source, target, true) || target.Workmanship == null)
        {
            player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
            return;
        }

        if (
            target.ItemType != ItemType.Armor && target.ItemType != ItemType.Clothing
            || target.IsShield
            || target.ArmorWeightClass == null
            || target.ClothingPriority == null
        )
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat("Only armor or clothing can be restyled.", ChatMessageType.Craft)
            );
            player.SendUseDoneEvent();
            return;
        }

        if (source.WeenieClassId == ArmorStyleKitWcid)
        {
            CopyStyle(player, source, target, confirmed);
        }
        else
        {
            ApplyStyle(player, source, target, confirmed);
        }
    }

    private static void CopyStyle(Player player, WorldObject source, WorldObject target, bool confirmed)
    {
        var weightClass = (ArmorWeightClass)target.ArmorWeightClass.Value;

        if (ArmorStyleCatalogue.CoversExtremities(target.ClothingPriority.Value))
        {
            CopyExtremityLook(player, source, target, confirmed, weightClass);
            return;
        }

        var piece = catalogue.Value.Identify(target.WeenieClassId, target.ClothingBase, weightClass);

        if (piece == null)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {target.Name} has no armor style the {source.Name} can copy. A Tailoring Kit can still copy its exact appearance.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var styleName = ArmorStyleCatalogue.StyleName(piece.Style);
        var coverages = string.Join(
            ", ",
            catalogue.Value.Coverages(piece.Style).Select(c => ArmorStyleCatalogue.DescribeTargetType(c))
        );

        if (!confirmed)
        {
            if (
                !player.ConfirmationManager.EnqueueSend(
                    new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                    $"Copy the {styleName} style of the {target.Name}, destroying it in the process? It may be applied to {WeightClassName(weightClass)} {coverages}."
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

        PerformCraft(
            player,
            source,
            target,
            () =>
            {
                var template = WorldObjectFactory.CreateNewWorldObject(ArmorStyleTemplateWcid);

                TailoringKit.RipArmorAppearance(player, source, target, template);
                template.SetProperty(PropertyInt.ArmorStyleTemplateWcid, (int)piece.Wcid);
                template.Name = target.Name + TemplateSuffix;
                template.LongDesc =
                    $"This template carries the {styleName} style of the {target.Name}. It has {template.Structure ?? 0} uses, one per armor slot a piece covers.\n\nIt may be applied to {WeightClassName(weightClass)} {coverages}.\n\nOther armor can't take the {styleName} style. Armor with the same coverage as the {target.Name} takes on its exact appearance.";

                player.TryConsumeFromInventoryWithNetworking(source, 1);
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You create a {styleName} style template from the {target.Name}.",
                        ChatMessageType.Craft
                    )
                );
                player.TryConsumeFromInventoryWithNetworking(target);

                if (!player.TryCreateInInventoryWithNetworking(template))
                {
                    _log.Error(
                        "[ARMOR STYLE] {Player} couldn't receive {Template} after copying the style of {Target}",
                        player.Name,
                        template.Name,
                        target.Name
                    );
                    template.Destroy();
                }
            }
        );
    }

    /// <summary>
    /// Helms, gauntlets and boots have no style, so their templates carry only their exact look,
    /// for one other piece of the same kind and weight class.
    /// </summary>
    private static void CopyExtremityLook(
        Player player,
        WorldObject source,
        WorldObject target,
        bool confirmed,
        ArmorWeightClass weightClass
    )
    {
        var kind = ArmorStyleCatalogue.DescribeTargetType(target.ClothingPriority.Value, withCost: false);

        if (!confirmed)
        {
            if (
                !player.ConfirmationManager.EnqueueSend(
                    new Confirmation_CraftInteration(player.Guid, source.Guid, target.Guid),
                    $"Copy the look of the {target.Name}, destroying it in the process? It may be applied to one {WeightClassName(weightClass)} {kind}."
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

        PerformCraft(
            player,
            source,
            target,
            () =>
            {
                var template = WorldObjectFactory.CreateNewWorldObject(ArmorStyleTemplateWcid);

                TailoringKit.RipArmorAppearance(player, source, target, template);
                template.MaxStructure = 1;
                template.Structure = 1;
                template.Name = target.Name + TemplateSuffix;
                template.LongDesc =
                    $"This template carries the look of the {target.Name}. It has 1 use.\n\nIt may be applied to {WeightClassName(weightClass)} {kind}, giving it the exact appearance of the {target.Name}.";

                player.TryConsumeFromInventoryWithNetworking(source, 1);
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat($"You create a template from the {target.Name}.", ChatMessageType.Craft)
                );
                player.TryConsumeFromInventoryWithNetworking(target);

                if (!player.TryCreateInInventoryWithNetworking(template))
                {
                    _log.Error(
                        "[ARMOR STYLE] {Player} couldn't receive {Template} after copying the look of {Target}",
                        player.Name,
                        template.Name,
                        target.Name
                    );
                    template.Destroy();
                }
            }
        );
    }

    private static void ApplyStyle(Player player, WorldObject source, WorldObject target, bool confirmed)
    {
        if (target.ArmorWeightClass != source.ArmorWeightClass)
        {
            var weightClass = (ArmorWeightClass)(source.ArmorWeightClass ?? 0);
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"The {source.Name} may only be applied to {WeightClassName(weightClass)} armor.",
                    ChatMessageType.Craft
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        var coverage = target.ClothingPriority.Value;
        var sameCoverage = coverage == source.ClothingPriority;
        Weenie pieceWeenie = null;
        string newName;
        int cost;

        if (ArmorStyleCatalogue.CoversExtremities(source.ClothingPriority ?? 0))
        {
            // helm, gauntlet and boot templates carry only their exact look
            if (!sameCoverage)
            {
                var kind = ArmorStyleCatalogue.DescribeTargetType(source.ClothingPriority.Value, withCost: false);
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {source.Name} may only be applied to {kind}.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            newName = LookName(source);
            cost = 1;
        }
        else
        {
            if (ArmorStyleCatalogue.CoversExtremities(coverage))
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        "Helms, gauntlets and boots can only take the look of a template made from another of their kind.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            var templatePiece = catalogue.Value.Get(
                (uint)(source.GetProperty(PropertyInt.ArmorStyleTemplateWcid) ?? 0)
            );
            if (templatePiece == null)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            var piece = sameCoverage ? templatePiece : catalogue.Value.Find(templatePiece, coverage);

            if (piece == null)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"The {ArmorStyleCatalogue.StyleName(templatePiece.Style)} style has no armor that covers the {ArmorStyleCatalogue.DescribeCoverage(coverage)}.",
                        ChatMessageType.Craft
                    )
                );
                player.SendUseDoneEvent();
                return;
            }

            pieceWeenie = DatabaseManager.World.GetCachedWeenie(piece.Wcid);
            if (pieceWeenie == null)
            {
                player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                return;
            }

            newName = pieceWeenie.GetProperty(PropertyString.Name) ?? target.Name;
            cost = ArmorStyleCatalogue.SlotCount(coverage);
        }

        var usesLeft = source.Structure ?? 0;
        if (usesLeft < cost)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"Restyling the {target.Name} needs {Uses(cost)}, but the {source.Name} has only {Uses(usesLeft)} left.",
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
                    $"Restyle the {target.Name} as {newName}? This uses {Uses(cost)} of the {source.Name}'s {usesLeft}."
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

        PerformCraft(
            player,
            source,
            target,
            () =>
            {
                var oldName = target.Name;

                if (sameCoverage)
                {
                    TailoringKit.ApplyPattern(player, source, target);
                }
                else
                {
                    ApplyPiece(source, target, pieceWeenie);
                }

                target.Name = newName;

                player.EnqueueBroadcast(new GameMessageUpdateObject(target));

                var remaining = usesLeft - cost;
                var remainingMsg = remaining > 0 ? $" It has {Uses(remaining)} left." : " It is used up.";
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"You restyle the {oldName} as {newName}.{remainingMsg}",
                        ChatMessageType.Craft
                    )
                );

                if (remaining > 0)
                {
                    player.UpdateProperty(source, PropertyInt.Structure, remaining);
                }
                else
                {
                    player.TryConsumeFromInventoryWithNetworking(source);
                }
            }
        );
    }

    /// <summary>
    /// Gives the target the model of another piece of the template's style, in the template's colors where that piece has them.
    /// </summary>
    private static void ApplyPiece(WorldObject template, WorldObject target, Weenie pieceWeenie)
    {
        var clothingBase = pieceWeenie.GetProperty(PropertyDataId.ClothingBase).Value;
        var clothingTable = DatManager.PortalDat.ReadFromDat<ClothingTable>(clothingBase);
        var iconsByPalette = clothingTable.ClothingSubPalEffects.ToDictionary(e => e.Key, e => e.Value.Icon);

        var (paletteTemplate, icon) = ArmorStyleCatalogue.ChooseColors(
            template.PaletteTemplate,
            pieceWeenie.GetProperty(PropertyInt.PaletteTemplate),
            pieceWeenie.GetProperty(PropertyDataId.Icon) ?? target.IconId,
            iconsByPalette
        );

        target.ClothingBase = clothingBase;
        target.SetupTableId = pieceWeenie.GetProperty(PropertyDataId.Setup) ?? target.SetupTableId;
        target.PaletteBaseId = pieceWeenie.GetProperty(PropertyDataId.PaletteBase);
        if (pieceWeenie.GetProperty(PropertyDataId.PhysicsEffectTable) is { } physicsTable)
        {
            target.PhysicsTableId = physicsTable;
        }

        target.PaletteTemplate = paletteTemplate;
        target.Shade = template.Shade;
        target.IconId = icon;
        target.Dyable = pieceWeenie.GetProperty(PropertyBool.Dyable);

        if (PropertyManager.GetBool("tailoring_intermediate_uieffects").Item)
        {
            target.UiEffects = template.UiEffects;
        }
    }

    /// <summary>
    /// Plays the tailoring animation, then runs the craft if both items are still usable.
    /// </summary>
    private static void PerformCraft(Player player, WorldObject source, WorldObject target, Action craft)
    {
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
                if (!RecipeManager.VerifyUse(player, source, target, true))
                {
                    player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
                    return;
                }

                craft();
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

    /// <summary>
    /// The name of the armor a template was made from, which restyled armor takes when it gets that exact look.
    /// </summary>
    private static string LookName(WorldObject template)
    {
        return template.Name.EndsWith(TemplateSuffix) ? template.Name[..^TemplateSuffix.Length] : template.Name;
    }

    private static string Uses(int count)
    {
        return count == 1 ? "1 use" : $"{count} uses";
    }

    private static string WeightClassName(ArmorWeightClass weightClass)
    {
        return weightClass switch
        {
            ArmorWeightClass.Cloth => "cloth",
            ArmorWeightClass.Light => "light",
            ArmorWeightClass.Heavy => "heavy",
            _ => "unclassed",
        };
    }
}
