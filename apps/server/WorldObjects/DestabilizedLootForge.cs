using System;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using Serilog;

namespace ACE.Server.WorldObjects;

public static class DestabilizedLootForge
{
    private static readonly ILogger _log = Log.ForContext(typeof(DestabilizedLootForge));

    private const PropertyBool TerminalDestabilizedLockProperty = (PropertyBool)10012;
    private const PropertyInt ForgePassCountProperty = (PropertyInt)10011;
    private const uint RequiredIngredientWcid = 2023154;
    private const int RequiredIngredientAmount = 1;
    private const string RequiredIngredientName = "Pulsing Resonance Fragment";

    private const string ConfirmMessagePlaceholder =
        "Destabilize this item? This cannot be undone. "
        + "The item will become ineligible for further tinkers. "
        + "Requires 1x Pulsing Resonance Fragment from your inventory; it will be consumed on success.";
    private const string SuccessMessagePlaceholder =
        "The forge destabilizes the resonance within your";
    private const string FailureMessagePlaceholder =
        "The forge rejects the destabilization attempt.";
    private const string ExceptionalMessagePlaceholder =
        "Exceptional resonance cascade detected.";
    private const string MissingIngredientMessagePlaceholder =
        "You need 1 Pulsing Resonance Fragment in your inventory to finalize destabilization.";
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
            failureMessage = "That item is already terminally destabilized.";
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

        DebugLog(
            player,
            item,
            $"queue accepted: confirmation enqueued ingredientCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)}"
        );

        player.ConfirmationManager.EnqueueSend(
            new DestabilizeConfirmation(
                player.Guid,
                response =>
                {
                    if (!response)
                    {
                        DebugLog(player, item, "confirmation declined or timed out");
                        player.SendTransientError("Destabilize cancelled.");
                        onResolved?.Invoke();
                        return;
                    }

                    DebugLog(player, item, "confirmation accepted");

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
            failureMessage = "That item is already terminally destabilized.";
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

        DebugLog(
            player,
            item,
            $"immediate finalize accepted: executing without additional confirmation ingredientCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)}"
        );

        ExecuteFinalization(player, item.Guid);
        onResolved?.Invoke();
        return true;
    }

    private static void ExecuteFinalization(Player player, ObjectGuid itemGuid)
    {
        var item = player.FindObject(itemGuid.Full, Player.SearchLocations.MyInventory);
        DebugLog(
            player,
            item,
            $"execute start: itemLookupGuid={itemGuid.Full} ingredientCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)}"
        );

        if (item == null)
        {
            DebugLog(player, null, $"execute aborted: item lookup failed itemGuid={itemGuid.Full}");
            player.SendTransientError("That item is no longer available.");
            return;
        }

        if (IsTerminallyDestabilized(item))
        {
            DebugLog(player, item, "execute aborted: item already terminally destabilized");
            player.SendTransientError("That item is already terminally destabilized.");
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
        DebugLog(
            player,
            item,
            $"ingredient consumed: {RequiredIngredientName} remainingCount={player.GetNumInventoryItemsOfWCID(RequiredIngredientWcid)}"
        );

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

        for (var packageIndex = 0; packageIndex < rollResult.PackageDetails.Count; packageIndex++)
        {
            DebugLog(
                player,
                item,
                $"change {packageIndex + 1}/{rollResult.AppliedPackageCount}: {rollResult.PackageDetails[packageIndex]}"
            );
        }

        var previousForgePassCount = item.GetProperty(ForgePassCountProperty) ?? 1;
        var previousBonded = item.Bonded;
        var previousAllowedWielder = item.AllowedWielder;
        var previousNumTimesTinkered = item.NumTimesTinkered;
        item.SetProperty(TerminalDestabilizedLockProperty, true);
        item.SetProperty(ForgePassCountProperty, previousForgePassCount + 1);
        item.Bonded = BondedStatus.Bonded;
        item.AllowedWielder = player.Guid.Full;
        item.CraftsmanName = player.Name;
        // Reuse existing tinker gate behavior by forcing the item to its max tinker count.
        if (item.Workmanship.HasValue)
        {
            item.NumTimesTinkered = Math.Max((int)Math.Ceiling(item.Workmanship.Value), item.NumTimesTinkered);
        }

        DebugLog(
            player,
            item,
            $"commit complete: terminalLock=true forgePassCount={previousForgePassCount}->{item.GetProperty(ForgePassCountProperty)} bonded={previousBonded}->{item.Bonded} allowedWielder={previousAllowedWielder}->{item.AllowedWielder} tinkers={previousNumTimesTinkered}->{item.NumTimesTinkered} changes={rollResult.AppliedPackageCount} exceptionalExtras={rollResult.ExceptionalExtraPackageCount}"
        );

        player.EnqueueBroadcast(new GameMessageUpdateObject(item));

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"{SuccessMessagePlaceholder} {item.NameWithMaterial}, altering {rollResult.AppliedPackageCount} {(rollResult.AppliedPackageCount == 1 ? "property" : "properties")}.",
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

    private static void DebugLog(Player player, WorldObject item, string message)
    {
        if (!DebugDestabilization)
        {
            return;
        }

        _log.Information("[DEBUG][Destabilize] {Context} {Message}", GetDebugContext(player, item), message);
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
