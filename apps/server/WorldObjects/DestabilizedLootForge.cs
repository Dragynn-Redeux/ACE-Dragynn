using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Factories.Tables;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using Serilog;

namespace ACE.Server.WorldObjects;

public static class DestabilizedLootForge
{
    private static readonly ILogger _log = Log.ForContext(typeof(DestabilizedLootForge));

    private const PropertyBool TerminalDestabilizedLockProperty = PropertyBool.TerminalDestabilizedLock;
    private const PropertyInt ForgePassCountProperty = PropertyInt.ForgePassCount;
    private const uint RequiredIngredientWcid = 2023154;
    private const int RequiredIngredientAmount = 1;
    private const string RequiredIngredientName = "Pulsing Resonance Fragment";

    private const string ConfirmMessagePlaceholder =
        "Destabilize this item? This cannot be undone. "
        + "The item will no longer accept further tinkering. "
        + "Requires 1 Pulsing Resonance Fragment from your pack, consumed on success.";
    private const string SuccessMessageTemplate =
        "The forge destabilizes the resonance within your {item}, {summary}";
    private const string DirectSuccessMessageTemplate =
        "{item} is destabilized. {summary}";
    private const string FailureMessagePlaceholder =
        "The forge fails to destabilize the item.";
    private const string ExceptionalMessagePlaceholder =
        "An exceptional resonance cascade surges through the item.";
    private const string MissingIngredientMessagePlaceholder =
        "You need 1 Pulsing Resonance Fragment in your pack to complete the destabilization.";
    private const string LockedAlterationMessage =
        "That item is retained and cannot be altered.";

    public static bool IsTerminallyDestabilized(WorldObject item)
    {
        return item?.GetProperty(TerminalDestabilizedLockProperty) == true;
    }

    private static bool DebugDestabilization => PropertyManager.GetBool("debug_stabilization").Item;

    public static int GetAvailableIngredientCount(Player player)
    {
        return player?.GetNumInventoryItemsOfWCID(RequiredIngredientWcid) ?? 0;
    }

    public static int GetRequiredIngredientCountForItems(int itemCount)
    {
        return Math.Max(0, itemCount) * RequiredIngredientAmount;
    }

    public static string GetRequiredIngredientName()
    {
        return RequiredIngredientName;
    }

    public static bool TryQueueFinalization(Player player, WorldObject item, out string failureMessage, Action onResolved = null)
    {
        failureMessage = null;

        if (player == null || item == null)
        {
            DebugLog(
                player,
                item,
                $"queue rejected: unavailable input playerPresent={player != null} itemPresent={item != null}"
            );
            failureMessage = "That item is unavailable.";
            return false;
        }

        if (IsTerminallyDestabilized(item))
        {
            DebugLog(player, item, "queue rejected: item already terminally destabilized");
            failureMessage = "That item has already been destabilized.";
            return false;
        }

        if (!HasRequiredIngredient(player))
        {
            DebugLog(
                player,
                item,
                $"queue rejected: missing ingredient {RequiredIngredientName} inventoryCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)} required={RequiredIngredientAmount}"
            );
            SendMissingIngredientNotice(player);
            failureMessage = null;
            return false;
        }

        player.ConfirmationManager.EnqueueSend(
            new DestabilizeConfirmation(
                player.Guid,
                response =>
                {
                    if (!response)
                    {
                        player.SendTransientError("Destabilization cancelled.");
                        onResolved?.Invoke();
                        return;
                    }

                    ExecuteFinalization(player, item.Guid);
                    onResolved?.Invoke();
                }
            ),
            ConfirmMessagePlaceholder
        );

        return true;
    }

    public static bool TryFinalizeImmediately(Player player, WorldObject item, out string failureMessage, Action onResolved = null)
    {
        failureMessage = null;

        if (player == null || item == null)
        {
            DebugLog(
                player,
                item,
                $"immediate finalize rejected: unavailable input playerPresent={player != null} itemPresent={item != null}"
            );
            failureMessage = "That item is unavailable.";
            return false;
        }

        if (IsTerminallyDestabilized(item))
        {
            DebugLog(player, item, "immediate finalize rejected: item already terminally destabilized");
            failureMessage = "That item has already been destabilized.";
            return false;
        }

        if (!HasRequiredIngredient(player))
        {
            DebugLog(
                player,
                item,
                $"immediate finalize rejected: missing ingredient {RequiredIngredientName} inventoryCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)} required={RequiredIngredientAmount}"
            );
            SendMissingIngredientNotice(player);
            return false;
        }

        ExecuteFinalization(player, item.Guid);
        onResolved?.Invoke();
        return true;
    }

    public static bool TryFinalizeDirect(Player player, WorldObject item, WorldObject source, double? variancePercentOverride, out string failureMessage)
    {
        failureMessage = null;

        if (player == null || item == null || source == null)
        {
            failureMessage = "That item is unavailable.";
            return false;
        }

        if (IsTerminallyDestabilized(item))
        {
            failureMessage = "That item has already been destabilized.";
            return false;
        }

        if (!player.TryConsumeFromInventoryWithNetworking(source, 1))
        {
            failureMessage = "That item is unavailable.";
            return false;
        }

        var sourceConsumed = true;
        var rollResult = DestabilizedLootEffects.ApplyDestabilize(item, variancePercentOverride);
        if (!rollResult.Success)
        {
            RestoreConsumedWorldObject(player, source.WeenieClassId, 1, sourceConsumed, source.Name ?? "destabilizer");
            failureMessage = rollResult.FailureReason ?? FailureMessagePlaceholder;
            return false;
        }

        FinalizeItem(player, item, rollResult, DirectSuccessMessageTemplate, source.Name ?? "destabilizer");
        return true;
    }

    private static void ExecuteFinalization(Player player, ObjectGuid itemGuid)
    {
        var item = player.FindObject(itemGuid.Full, Player.SearchLocations.MyInventory);

        if (item == null)
        {
            DebugLog(player, null, $"execute aborted: item lookup failed itemGuid={itemGuid.Full}");
            player.SendTransientError("That item is no longer available.");
            return;
        }

        if (IsTerminallyDestabilized(item))
        {
            DebugLog(player, item, "execute aborted: item already terminally destabilized");
            player.SendTransientError("That item has already been destabilized.");
            return;
        }

        if (!HasRequiredIngredient(player))
        {
            DebugLog(
                player,
                item,
                $"execute aborted: missing ingredient {RequiredIngredientName} inventoryCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)} required={RequiredIngredientAmount}"
            );
            SendMissingIngredientNotice(player);
            return;
        }

        if (!player.TryConsumeFromInventoryWithNetworking(RequiredIngredientWcid, RequiredIngredientAmount))
        {
            DebugLog(
                player,
                item,
                $"execute aborted: ingredient consume failed ingredientCountAfterAttempt={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)}"
            );
            SendMissingIngredientNotice(player);
            return;
        }

        var ingredientConsumed = true;

        var rollResult = DestabilizedLootEffects.ApplyDestabilize(item);
        if (!rollResult.Success)
        {
            DebugLog(
                player,
                item,
                $"execute failed: ApplyDestabilize unsuccessful reason={rollResult.FailureReason ?? FailureMessagePlaceholder}"
            );
            RestoreConsumedIngredient(player, ingredientConsumed);
            player.SendTransientError(rollResult.FailureReason ?? FailureMessagePlaceholder);
            return;
        }

        FinalizeItem(player, item, rollResult, SuccessMessageTemplate, RequiredIngredientName);
    }

    private static bool HasRequiredIngredient(Player player)
    {
        return player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid) >= RequiredIngredientAmount;
    }

    private static void SendMissingIngredientNotice(Player player)
    {
        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                MissingIngredientMessagePlaceholder,
                ChatMessageType.Craft
            )
        );
    }

    private static void RestoreConsumedIngredient(Player player, bool ingredientConsumed)
    {
        if (!ingredientConsumed)
        {
            return;
        }

        var ingredient = WorldObjectFactory.CreateNewWorldObject(RequiredIngredientWcid);
        if (ingredient == null)
        {
            DebugLog(player, null, $"ingredient restore failed: could not create {RequiredIngredientName} weenie={RequiredIngredientWcid}");
            return;
        }

        ingredient.StackSize = RequiredIngredientAmount;
        player.TryCreateInInventoryWithNetworking(ingredient);
        DebugLog(
            player,
            null,
            $"ingredient restored: {RequiredIngredientName} amount={RequiredIngredientAmount} inventoryCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)}"
        );
    }

    private static void RestoreConsumedWorldObject(Player player, uint weenieClassId, int amount, bool itemConsumed, string itemName)
    {
        if (!itemConsumed)
        {
            return;
        }

        var restored = WorldObjectFactory.CreateNewWorldObject(weenieClassId);
        if (restored == null)
        {
            DebugLog(player, null, $"item restore failed: could not create {itemName} weenie={weenieClassId}");
            return;
        }

        restored.StackSize = amount;
        player.TryCreateInInventoryWithNetworking(restored);
        DebugLog(player, null, $"item restored: {itemName} amount={amount} weenie={weenieClassId}");
    }

    private static void FinalizeItem(Player player, WorldObject item, DestabilizedRollResult rollResult, string successMessageTemplate, string costName)
    {
        var previousStage = ForgeStageDisplay.GetStage(item);
        var previousForgePassCount = item.GetProperty(ForgePassCountProperty) ?? 1;
        item.SetProperty(TerminalDestabilizedLockProperty, true);
        item.SetProperty(ForgePassCountProperty, previousForgePassCount + 1);
        ForgeStageDisplay.ApplyStageOverlay(item);
        item.Bonded = BondedStatus.Bonded;
        item.AllowedWielder = player.Guid.Full;
        item.CraftsmanName = player.Name;

        DebugLog(player, item, BuildAdminDestabilizationSuccessLog(player, item, previousStage, ForgeStageDisplay.GetStage(item), rollResult, costName));

        player.EnqueueBroadcast(new GameMessageUpdateObject(item));

        var alterationSummary = $"altering {rollResult.AppliedPackageCount} {(rollResult.AppliedPackageCount == 1 ? "property" : "properties")}.";
        var successMessage = successMessageTemplate
            .Replace("{item}", item.NameWithMaterial)
            .Replace("{summary}", alterationSummary);

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                successMessage,
                ChatMessageType.Broadcast
            )
        );

        foreach (var packageDetail in rollResult.PackageDetails)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    packageDetail,
                    ChatMessageType.Broadcast
                )
            );
        }

        if (rollResult.ExceptionalExtraPackageCount > 0)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"{ExceptionalMessagePlaceholder} (+{rollResult.ExceptionalExtraPackageCount} extra package(s)).",
                    ChatMessageType.Broadcast
                )
            );
        }
    }

    public static bool TryBlockFurtherAlteration(Player player, WorldObject target)
    {
        if (!IsTerminallyDestabilized(target))
        {
            return false;
        }

        DebugLog(player, target, "alteration blocked: item is terminally destabilized");

        player.Session.Network.EnqueueSend(new GameMessageSystemChat(LockedAlterationMessage, ChatMessageType.Craft));
        player.SendUseDoneEvent(WeenieError.YouDoNotPassCraftingRequirements);
        return true;
    }

    public static void RecalculateArcaneLore(WorldObject item)
    {
        if (item == null)
        {
            return;
        }

        item.ItemDifficulty = CalculateArcaneLore(item);
    }

    private static int CalculateArcaneLore(WorldObject item)
    {
        var numSpells = 0;
        var increasedDifficulty = 0.0f;

        if (item.Biota.PropertiesSpellBook != null)
        {
            const int Minor = 0;
            const int Major = 1;
            const int Epic = 2;
            const int Legendary = 3;

            foreach (SpellId spellId in item.Biota.PropertiesSpellBook.Keys)
            {
                numSpells++;

                var cantripLevels = SpellLevelProgression.GetSpellLevels(spellId);
                if (cantripLevels == null)
                {
                    continue;
                }

                var cantripLevel = cantripLevels.IndexOf(spellId);
                if (cantripLevel < 0)
                {
                    continue;
                }

                if (cantripLevel == Minor)
                {
                    increasedDifficulty += 5;
                }
                else if (cantripLevel == Major)
                {
                    increasedDifficulty += 10;
                }
                else if (cantripLevel == Epic)
                {
                    increasedDifficulty += 15;
                }
                else if (cantripLevel == Legendary)
                {
                    increasedDifficulty += 20;
                }
            }
        }

        var tier = Math.Max((item.Tier ?? 1) - 1, 0);

        if (item.ProcSpell != null)
        {
            numSpells++;
            increasedDifficulty += Math.Max(5 * tier, 5);
        }

        var armorSlots = item.ArmorSlots ?? 1;
        var spellsPerSlot = (float)numSpells / armorSlots;

        if (spellsPerSlot <= 1 && item.ProcSpell == null)
        {
            return 0;
        }

        var baseDifficulty = ActivationDifficultyPerTier(tier);
        return baseDifficulty + (int)(increasedDifficulty / armorSlots);
    }

    private static int ActivationDifficultyPerTier(int tier)
    {
        switch (tier)
        {
            case 1:
                return 75;
            case 2:
                return 175;
            case 3:
                return 225;
            case 4:
                return 275;
            case 5:
                return 325;
            case 6:
                return 375;
            case 7:
                return 425;
            default:
                return 50;
        }
    }

    private static void DebugLog(Player player, WorldObject item, string message)
    {
        if (!DebugDestabilization)
        {
            return;
        }

        _log.Information("[DEBUG][Destabilize] {Context} {Message}", GetDebugContext(player, item), message);
    }

    private static string BuildAdminDestabilizationSuccessLog(
        Player player,
        WorldObject item,
        ForgeStage beforeStage,
        ForgeStage afterStage,
        DestabilizedRollResult rollResult,
        string costName)
    {
        var itemGuid = $"0x{item.Guid.Full:X8}";
        var changeSummary = BuildAdminPackageSummary(rollResult.PackageDetails, rollResult.AppliedPackageCount);
        var exceptionalSegment = rollResult.ExceptionalExtraPackageCount > 0
            ? $" exceptionalExtras={rollResult.ExceptionalExtraPackageCount}"
            : string.Empty;

        return $"[ForgeAdmin] destabilize success, {player.Name} lvl={player.Level ?? 1}. item={item.Name} ({itemGuid}). {beforeStage}->{afterStage} cost={costName} changes={changeSummary}{exceptionalSegment}";
    }

    private static string BuildAdminPackageSummary(System.Collections.Generic.IReadOnlyList<string> packageDetails, int appliedPackageCount)
    {
        var meaningfulDetails = new System.Collections.Generic.List<string>();

        foreach (var packageDetail in packageDetails)
        {
            if (!IsMeaningfulAdminPackageDetail(packageDetail))
            {
                continue;
            }

            meaningfulDetails.Add(packageDetail.TrimEnd('.'));
        }

        if (meaningfulDetails.Count == 0)
        {
            return $"{appliedPackageCount} package(s)";
        }

        return string.Join("; ", meaningfulDetails);
    }

    private static bool IsMeaningfulAdminPackageDetail(string packageDetail)
    {
        if (string.IsNullOrWhiteSpace(packageDetail))
        {
            return false;
        }

        return !packageDetail.Contains("(+0.00)", StringComparison.Ordinal)
            && !packageDetail.Contains("(-0.00)", StringComparison.Ordinal)
            && !packageDetail.Contains("(+0.00%)", StringComparison.Ordinal)
            && !packageDetail.Contains("(-0.00%)", StringComparison.Ordinal);
    }

    private static string GetDebugContext(Player player, WorldObject item)
    {
        var playerName = player?.Name ?? "<null>";
        var playerGuid = player?.Guid.Full.ToString() ?? "<null>";
        var itemName = item?.Name ?? "<null>";
        var itemGuid = item?.Guid.Full.ToString() ?? "<null>";
        var forgePassCount = item?.GetProperty(ForgePassCountProperty)?.ToString() ?? "<null>";
        return $"player={playerName}({playerGuid}) item={itemName}({itemGuid}) forgePass={forgePassCount}";
    }

    private sealed class DestabilizeConfirmation : Confirmation
    {
        private readonly Action<bool> _action;

        public DestabilizeConfirmation(ObjectGuid playerGuid, Action<bool> action)
            : base(playerGuid, ConfirmationType.Yes_No)
        {
            _action = action;
        }

        public override void ProcessConfirmation(bool response, bool timeout = false)
        {
            var player = Player;
            if (player == null)
            {
                return;
            }

            _action(!timeout && response);
        }
    }
}
