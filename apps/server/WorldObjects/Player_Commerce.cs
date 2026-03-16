using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Market;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using Serilog;

namespace ACE.Server.WorldObjects;

partial class Player
{
    private static readonly Serilog.ILogger MarketSaleLog = Log.ForContext(typeof(Player)).ForContext("Subsystem", "Market");
    // player buying items from vendor

    /// <summary>
    /// Called when player clicks 'Buy Items'
    /// </summary>
    public void HandleActionBuyItem(uint vendorGuid, List<ItemProfile> items)
    {
        if (IsBusy)
        {
            SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        if (IsTrading)
        {
            SendUseDoneEvent(WeenieError.CantDoThatTradeInProgress);
            return;
        }

        var vendor = CurrentLandblock?.GetObject(vendorGuid) as Vendor;

        if (vendor == null)
        {
            SendUseDoneEvent(WeenieError.NoObject);
            return;
        }

        // if this succeeds, it automatically calls player.FinalizeBuyTransaction()
        if (!vendor.BuyItems_ValidateTransaction(items, this))
        {
            Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));
            SendUseDoneEvent();
            return;
        }

        SendUseDoneEvent();
    }

    private const uint coinStackWcid = (uint)ACE.Entity.Enum.WeenieClassName.W_COINSTACK_CLASS;

    /// <summary>
    /// Vendor has validated the transactions and sent a list of items for processing.
    /// </summary>
    public void FinalizeBuyTransaction(
        Vendor vendor,
        List<WorldObject> genericItems,
        List<WorldObject> uniqueItems,
        uint cost
    )
    {
        // transaction has been validated by this point

        var currencyWcid = vendor.AlternateCurrency ?? coinStackWcid;

        // Create items first; if anything fails to add (e.g. encumbrance changed since validation),
        // do not charge the player.
        var allAdded = true;

        foreach (var item in genericItems)
        {
            var service = item.GetProperty(PropertyBool.VendorService) ?? false;

            if (!service)
            {
                // errors shouldn't be possible here, since the items were pre-validated, but just in case...
                if (!TryCreateInInventoryWithNetworking(item))
                {
                    _log.Error(
                        $"[VENDOR] {Name}.FinalizeBuyTransaction({vendor.Name}) - couldn't add {item.Name} ({item.Guid}) to player inventory after validation, this shouldn't happen!"
                    );

                    item.Destroy(); // cleanup for guid manager
                    allAdded = false;
                    break;
                }
                else
                {
                    CheckForQuestStampOnPurchase(item);

                    // trigger pickup emote on the created item (if present)
                    try
                    {
                        item.EmoteManager?.OnPickup(this);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(ex, "[VENDOR] EmoteManager.OnPickup threw for {Item} during FinalizeBuyTransaction", item.Name);
                    }

                    vendor.NumItemsSold++;
                }
            }
            else
            {
                vendor.ApplyService(item, this);
            }
        }

        foreach (var item in uniqueItems)
        {
            // Non-market vendors: the unique item may have been bought/rotted after validation.
            // Re-resolve it from the vendor at finalization time so we don't create a stale in-memory object.
            var itemToCreate = item;

            if (!vendor.IsMarketVendor)
            {
                if (!vendor.UniqueItemsForSale.TryGetValue(item.Guid, out itemToCreate))
                {
                    HandleStaleVendorPurchase(vendor, item.Guid, itemWasAddedToInventory: false);
                    allAdded = false;
                    break;
                }
            }

            // Market vendors: the display item is a per-player clone. If the underlying listing was
            // already purchased/expired, don't add the clone to inventory.
            // Also: market stack listings are displayed as a single entry (StackSize=1) with quantity in the name.
            // On purchase, restore the real stack size and base name from the stored listing snapshot.
            var marketListingIdPreAdd = itemToCreate.GetProperty(PropertyInt.MarketListingId);
            int? marketListingIdSafe = null;
            if (marketListingIdPreAdd.HasValue && marketListingIdPreAdd.Value > 0)
            {
                marketListingIdSafe = marketListingIdPreAdd.Value;
                var listingStillExists = MarketServiceLocator.PlayerMarketRepository.GetListingById(marketListingIdPreAdd.Value);
                if (listingStillExists == null)
                {
                    HandleStaleVendorPurchase(vendor, itemToCreate.Guid, itemWasAddedToInventory: false);
                    allAdded = false;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(listingStillExists.ItemSnapshotJson))
                {
                    var reconstructed = MarketListingSnapshotSerializer.TryRecreateWorldObjectFromSnapshot(listingStillExists.ItemSnapshotJson);
                    if (reconstructed != null)
                    {
                        // Preserve the per-player display GUID (used for session cleanup), but restore the true
                        // stack size and base name (removes the quantity prefix used for vendor display).
                        if (!string.IsNullOrWhiteSpace(reconstructed.Name))
                        {
                            itemToCreate.SetProperty(PropertyString.Name, reconstructed.Name);
                        }

                        if (reconstructed.StackSize is > 1)
                        {
                            itemToCreate.SetStackSize(reconstructed.StackSize.Value);
                        }

                        // Restore original value/unit value from the snapshot so the purchased item doesn't
                        // retain the market display pricing.
                        itemToCreate.Value = reconstructed.Value;

                        var snapUnitValue = reconstructed.GetProperty(PropertyInt.StackUnitValue);
                        if (snapUnitValue.HasValue)
                        {
                            itemToCreate.SetProperty(PropertyInt.StackUnitValue, snapUnitValue.Value);
                        }
                        else
                        {
                            itemToCreate.RemoveProperty(PropertyInt.StackUnitValue);
                        }

                        // Re-tag with listing id for downstream market logic.
                        itemToCreate.SetProperty(PropertyInt.MarketListingId, marketListingIdPreAdd.Value);
                    }
                }

                // Remove MarketListingId before we send/create the item in the player's inventory.
                // `WorldObject.GetNameForClient()` appends a time-remaining suffix when this property is present.
                itemToCreate.RemoveProperty(PropertyInt.MarketListingId);
            }

            if (TryCreateInInventoryWithNetworking(itemToCreate))
            {
                // For market vendors, items are per-player snapshots and are not in UniqueItemsForSale.
                if (!vendor.IsMarketVendor)
                {
                    vendor.UniqueItemsForSale.Remove(itemToCreate.Guid);
                }

                // MARKET HOOK: if this unique is a market listing, mark it sold and create a payout,
                // then restore original value.
                ACE.Database.Models.Shard.PlayerMarketListing listing = null;

                // Prefer direct lookup by listing id when tagged. Note: we may have removed the property
                // before creating the inventory item to prevent name suffix injection.
                if (marketListingIdSafe.HasValue && marketListingIdSafe.Value > 0)
                {
                    listing = MarketServiceLocator.PlayerMarketRepository.GetListingById(marketListingIdSafe.Value);
                }

                // Fallback: for older market items that are not tagged with MarketListingId.
                // Note: display clones may not preserve the original biota id.
                if (listing == null && itemToCreate.Biota != null)
                {
                    listing = MarketServiceLocator.PlayerMarketRepository
                        .GetListingByItemBiotaId((uint)itemToCreate.Biota.Id);
                }

                // Fallback: some items may not have biota; try the purchased object's guid.
                listing ??= MarketServiceLocator.PlayerMarketRepository
                    .GetListingByItemGuid(itemToCreate.Guid.Full);

                if (listing != null)
                {
                    if (!MarketServiceLocator.PlayerMarketRepository.MarkListingSold(listing, this))
                    {
                        // Another player bought this listing between validation and finalization.
                        // Roll back the inventory add because we haven't charged yet.
                        TryRemoveFromInventoryWithNetworking(itemToCreate.Guid, out _, RemoveFromInventoryAction.None);

                        // Extra client-side cleanup: explicitly send a remove message too.
                        Session.Network.EnqueueSend(new GameMessageInventoryRemoveObject(itemToCreate));

                        // Use the shared stale-purchase handler to unwedge the client and refresh the vendor UI.
                        HandleStaleVendorPurchase(vendor, itemToCreate.Guid, itemWasAddedToInventory: false);

                        // Client can end up with a ghost/blank item if it misses a remove or gets out of sync.
                        // Re-send inventory state using the same pattern as login.
                        SendInventoryAndWieldedItems();

                        allAdded = false;
                        break;
                    }

                    var fee = MarketServiceLocator.CalculateSaleFee(listing.ListedPrice);
                    var net = MarketServiceLocator.CalculateNetAfterFee(listing.ListedPrice);

                    try
                    {
                        MarketSaleLog.Information(
                            "[MARKET SALE] Item='{ItemName} ({WCID})' ItemType={ItemType} Price={Price} Seller='{SellerName} ({SellerAccountId})' Buyer='{BuyerName} ({BuyerAccountId})' Vendor='{VendorName} ({VendorGuid})'",
                            itemToCreate.Name,
                            itemToCreate.WeenieClassId,
                            itemToCreate.ItemType,
                            listing.ListedPrice,
                            listing.SellerName,
                            listing.SellerAccountId,
                            Name,
                            Character?.AccountId,
                            vendor?.Name,
                            vendor?.Guid.Full);
                    }
                    catch
                    {
                        // Do not allow logging failures to impact transaction finalization.
                    }

                    var payout = MarketServiceLocator.PlayerMarketRepository.CreatePayout(listing, net);

                    // Track sale/spend history (skip self-purchases).
                    if (listing.SellerAccountId != Character.AccountId)
                    {
                        var tx = MarketServiceLocator.PlayerMarketRepository.CreateTransaction(listing, payout, this);
                        tx.ItemName = itemToCreate.Name;
                        tx.Quantity = itemToCreate.StackSize ?? 1;
                        tx.Price = listing.ListedPrice;
                        tx.FeeAmount = fee;
                        tx.SellerNetAmount = net;
                    }

                    // Notify seller if they are online.
                    Player seller = null;
                    if (listing.SellerCharacterId.HasValue)
                    {
                        seller = PlayerManager.GetOnlinePlayer(new ACE.Entity.ObjectGuid(listing.SellerCharacterId.Value));
                    }

                    if (seller?.Session != null)
                    {
                        var msg = $"[Market] Your market listing sold! {itemToCreate.Name} for {listing.ListedPrice:N0} pyreals. Claim your payout at the Market Broker.";
                        seller.Session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Tell));
                    }

                    // restore original value before saving item again
                    itemToCreate.Value = listing.OriginalValue;

                    // If this was a stackable market listing display item, restore the per-unit value.
                    // On vendor display we temporarily set StackUnitValue to the listing price so the
                    // computed Value shows the full stack amount.
                    var purchasedStackSize = itemToCreate.StackSize ?? 1;
                    if (purchasedStackSize > 1)
                    {
                        var originalUnitValue = listing.OriginalValue / purchasedStackSize;
                        itemToCreate.SetProperty(PropertyInt.StackUnitValue, originalUnitValue);
                        itemToCreate.SetStackSize(purchasedStackSize);
                    }
                    else
                    {
                        // Non-stackables should not retain the market display StackUnitValue (used to show listing price).
                        itemToCreate.RemoveProperty(PropertyInt.StackUnitValue);
                    }

                     // This is vendor/listing metadata; ensure we don't keep it on the purchased item.
                     itemToCreate.RemoveProperty(PropertyInt.MarketListingId);
                }

                // this was only for when the unique item was sold to the vendor,
                // to determine when the item should rot on the vendor. it gets removed now
                itemToCreate.SoldTimestamp = null;

                // Market vendors: remove from the buyer's per-player vendor snapshot so the UI updates.
                vendor.RemoveFromMarketSession(this, itemToCreate.Guid);

                // Market vendors remap salvage (tinkering materials) to Misc for display so it shows in vendor UI.
                // Ensure purchased salvage reverts to its real item type after purchase.
                if (vendor.IsMarketVendor
                    && itemToCreate.WeenieType == WeenieType.Salvage
                    && itemToCreate.ItemType == ItemType.Misc)
                {
                    itemToCreate.ItemType = ItemType.TinkeringMaterial;
                }

                CheckForQuestStampOnPurchase(itemToCreate);

                // trigger pickup emote on the created unique item (if present)
                try
                {
                    item.EmoteManager?.OnPickup(this);
                }
                catch (Exception ex)
                {
                        _log.Warning(ex, "[VENDOR] EmoteManager.OnPickup threw for unique {Item} during FinalizeBuyTransaction", itemToCreate.Name);
                }

                vendor.NumItemsSold++;
            }
            else
            {
                // Another player may have bought/removed this unique between validation and finalization.
                // Treat it as a stale vendor entry, refresh the vendor window, and abort without charging.
                HandleStaleVendorPurchase(vendor, item.Guid, itemWasAddedToInventory: false);
                allAdded = false;
                break;
            }
        }

        if (!allAdded)
        {
            // Best-effort: recalculate limits to send a more accurate failure reason.
            // This can occur if encumbrance/slots changed between validation and finalization.
            var itemsToReceive = new ItemsToReceive(this);

            foreach (var item in genericItems)
            {
                var service = item.GetProperty(PropertyBool.VendorService) ?? false;
                if (!service)
                {
                    itemsToReceive.Add(item.WeenieClassId, item.StackSize ?? 1);
                }
            }

            foreach (var item in uniqueItems)
            {
                itemsToReceive.Add(item.WeenieClassId, item.StackSize ?? 1);
            }

            if (itemsToReceive.PlayerExceedsAvailableBurden)
            {
                SendTransientError("You are too encumbered to buy that!");
            }
            else if (itemsToReceive.PlayerOutOfInventorySlots)
            {
                SendTransientError("You do not have enough pack space to buy that!");
            }
            else if (itemsToReceive.PlayerOutOfContainerSlots)
            {
                SendTransientError("You do not have enough container slots to buy that!");
            }
            else
            {
                SendTransientError("Transaction failed.");
            }

            return;
        }

        SpendCurrency(currencyWcid, cost, true);

        vendor.MoneyIncome += (int)cost;

        Session.Network.EnqueueSend(new GameMessageSound(Guid, Sound.PickUpItem));

        if (PropertyManager.GetBool("player_receive_immediate_save").Item)
        {
            RushNextPlayerSave(5);
        }

        var altCurrencySpent = vendor.AlternateCurrency != null ? cost : 0;

        vendor.ApproachVendor(this, VendorType.Buy, altCurrencySpent);
    }

    private void CheckForQuestStampOnPurchase(WorldObject item)
    {
        // Check if item has a quest stamp property and stamp it
        var questOnPurchase = item.GetProperty(PropertyString.Quest);
        if (!string.IsNullOrWhiteSpace(questOnPurchase))
        {
            QuestManager.Stamp(questOnPurchase);
        }
    }

    // player selling items to vendor

    // whereas most of the logic for buying items is in vendor,
    // most of the logic for selling items is located in player_commerce
    // the functions have similar structure, just in different places
    // there's really no point in there being differences in location,
    // and it might be better to move them all to vendor for consistency.

    /// <summary>
    /// Called when player clicks 'Sell Items'
    /// </summary>
    public void HandleActionSellItem(uint vendorGuid, List<ItemProfile> itemProfiles)
    {
        if (IsBusy)
        {
            SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        var vendor = CurrentLandblock?.GetObject(vendorGuid) as Vendor;

        if (vendor == null)
        {
            SendUseDoneEvent(WeenieError.NoObject);
            return;
        }

        // perform validations on requested sell items,
        // and filter to list of validated items

        // one difference between sell and buy is here.
        // when an itemProfile is invalid in buy, the entire transaction is failed immediately.
        // when an itemProfile is invalid in sell, we just remove the invalid itemProfiles, and continue onwards
        // this might not be the best for safety, and it's a tradeoff between safety and player convenience
        // should we fail the entire transaction (similar to buy), if there are any invalids in the transaction request?

        var sellList = VerifySellItems(itemProfiles, vendor);

        if (sellList.Count == 0)
        {
            Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));
            SendUseDoneEvent();
            return;
        }

        // calculate pyreals to receive
        var payoutCoinAmount = vendor.CalculatePayoutCoinAmount(sellList);

        if (payoutCoinAmount < 0)
        {
            _log.Warning(
                $"[VENDOR] {Name} (0x({Guid}) tried to sell something to {vendor.Name} (0x{vendor.Guid}) resulting in a payout of {payoutCoinAmount} pyreals."
            );

            SendTransientError("Transaction failed.");
            Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));

            SendUseDoneEvent();

            return;
        }

        // verify player has enough pack slots / burden to receive these pyreals
        var itemsToReceive = new ItemsToReceive(this);

        itemsToReceive.Add((uint)ACE.Entity.Enum.WeenieClassName.W_COINSTACK_CLASS, payoutCoinAmount);

        if (itemsToReceive.PlayerExceedsLimits)
        {
            if (itemsToReceive.PlayerExceedsAvailableBurden)
            {
                Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(Session, "You are too encumbered to sell that!")
                );
            }
            else if (itemsToReceive.PlayerOutOfInventorySlots)
            {
                Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(
                        Session,
                        "You do not have enough free pack space to sell that!"
                    )
                );
            }

            Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));
            SendUseDoneEvent(); // WeenieError.FullInventoryLocation?
            return;
        }

        var payoutCoinStacks = CreatePayoutCoinStacks(payoutCoinAmount);

        vendor.MoneyOutflow += payoutCoinAmount;

        // remove sell items from player inventory
        foreach (var item in sellList.Values)
        {
            if (
                TryRemoveFromInventoryWithNetworking(item.Guid, out _, RemoveFromInventoryAction.SellItem)
                || TryDequipObjectWithNetworking(item.Guid, out _, DequipObjectAction.SellItem)
            )
            {
                Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(Session, item, vendor));
            }
            else
            {
                _log.Warning(
                    "[VENDOR] Item 0x{ItemGuid:X8}:{Item} for player {Player} not found in HandleActionSellItem.",
                    item.Guid.Full,
                    item.Name,
                    Name
                ); // This shouldn't happen
            }
        }

        // send the list of items to the vendor
        // for the vendor to determine what to do with each item (resell, destroy)
        vendor.ProcessItemsForPurchase(this, sellList);

        // add coins to player inventory
        foreach (var item in payoutCoinStacks)
        {
            if (!TryCreateInInventoryWithNetworking(item)) // this shouldn't happen because of pre-validations in itemsToReceive
            {
                _log.Warning(
                    "[VENDOR] Payout 0x{ItemGuid:X8}:{Item} for player {Player} failed to add to inventory HandleActionSellItem.",
                    item.Guid.Full,
                    item.Name,
                    Name
                );
                item.Destroy();
            }
        }

        // UpdateCoinValue removed -- already handled in TryCreateInInventoryWithNetworking

        Session.Network.EnqueueSend(new GameMessageSound(Guid, Sound.PickUpItem));

        SendUseDoneEvent();
    }

    /// <summary>
    /// Filters the list of ItemProfiles the player is attempting to sell to the vendor
    /// to the list of verified WorldObjects in the player's inventory w/ validations
    /// </summary>
    private Dictionary<uint, WorldObject> VerifySellItems(List<ItemProfile> sellItems, Vendor vendor)
    {
        var allPossessions = GetAllPossessions().ToDictionary(i => i.Guid.Full, i => i);

        var acceptedItemTypes = (ItemType)(vendor.MerchandiseItemTypes ?? 0);

        var verified = new Dictionary<uint, WorldObject>();

        foreach (var sellItem in sellItems)
        {
            if (!allPossessions.TryGetValue(sellItem.ObjectGuid, out var wo))
            {
                _log.Warning(
                    $"[VENDOR] {Name} tried to sell item {sellItem.ObjectGuid:X8} not in their inventory to {vendor.Name}"
                );
                continue;
            }

            // verify item profile (unique guids, amount)
            if (verified.ContainsKey(wo.Guid.Full))
            {
                _log.Warning($"[VENDOR] {Name} tried to sell duplicate item {wo.Name} ({wo.Guid}) to {vendor.Name}");
                continue;
            }

            if (!sellItem.IsValidAmount)
            {
                _log.Warning(
                    $"[VENDOR] {Name} tried to sell {sellItem.Amount}x {wo.Name} ({wo.Guid}) to {vendor.Name}"
                );
                continue;
            }

            if (sellItem.Amount > (wo.StackSize ?? 1))
            {
                _log.Warning(
                    $"[VENDOR] {Name} tried to sell {sellItem.Amount}x {wo.Name} ({wo.Guid}) to {vendor.Name}, but they only have {wo.StackSize ?? 1}x"
                );
                continue;
            }

            // verify wo / vendor / player properties
            if ((acceptedItemTypes & wo.ItemType) == 0 || !wo.IsSellable || wo.Retained)
            {
                var itemName = (wo.StackSize ?? 1) > 1 ? wo.GetPluralName() : wo.Name;
                Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(Session, $"The {itemName} is unsellable.")
                ); // retail message did not include item name, leaving in that for now.
                continue;
            }

            if (wo.Value < 1)
            {
                var itemName = (wo.StackSize ?? 1) > 1 ? wo.GetPluralName() : wo.Name;
                Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(
                        Session,
                        $"The {itemName} has no value and cannot be sold."
                    )
                ); // retail message did not include item name, leaving in that for now.
                continue;
            }

            if (IsTrading && wo.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow))
            {
                var itemName = (wo.StackSize ?? 1) > 1 ? wo.GetPluralName() : wo.Name;
                Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(
                        Session,
                        $"You cannot sell that! The {itemName} is currently being traded."
                    )
                ); // custom message?
                continue;
            }

            if (wo is Container container && container.Inventory.Count > 0)
            {
                var itemName = (wo.StackSize ?? 1) > 1 ? wo.GetPluralName() : wo.Name;
                Session.Network.EnqueueSend(
                    new GameEventCommunicationTransientString(
                        Session,
                        $"You cannot sell that! The {itemName} must be empty."
                    )
                ); // custom message?
                continue;
            }

            verified.Add(wo.Guid.Full, wo);
        }

        return verified;
    }

    private List<WorldObject> CreatePayoutCoinStacks(int amount)
    {
        var coinStacks = new List<WorldObject>();

        while (amount > 0)
        {
            var currencyStack = WorldObjectFactory.CreateNewWorldObject("coinstack");

            var currentStackAmount = Math.Min(amount, currencyStack.MaxStackSize.Value);

            currencyStack.SetStackSize(currentStackAmount);
            coinStacks.Add(currencyStack);
            amount -= currentStackAmount;
        }
        return coinStacks;
    }

    private void UpdateCoinValue(bool sendUpdateMessageIfChanged = true)
    {
        var coins = 0;

        foreach (var coinStack in GetInventoryItemsOfTypeWeenieType(WeenieType.Coin))
        {
            coins += coinStack.Value ?? 0;
        }

        if (sendUpdateMessageIfChanged && CoinValue == coins)
        {
            sendUpdateMessageIfChanged = false;
        }

        CoinValue = coins;

        if (sendUpdateMessageIfChanged)
        {
            Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.CoinValue, CoinValue ?? 0)
            );
        }
    }

    private List<WorldObject> SpendCurrency(uint currentWcid, uint amount, bool destroy = false)
    {
        if (currentWcid == 0 || amount == 0)
        {
            return null;
        }

        var cost = new List<WorldObject>();

        if (currentWcid == coinStackWcid)
        {
            if (amount > CoinValue)
            {
                return null;
            }
        }
        if (destroy)
        {
            TryConsumeFromInventoryWithNetworking(currentWcid, (int)amount);
        }
        else
        {
            cost = CollectCurrencyStacks(currentWcid, amount);

            foreach (var stack in cost)
            {
                if (!TryRemoveFromInventoryWithNetworking(stack.Guid, out _, RemoveFromInventoryAction.SpendItem))
                {
                    UpdateCoinValue(); // this coinstack was created by spliting up an existing one, and not actually added to the players inventory. The existing stack was already adjusted down but we need to update the player's CoinValue, so we do that now.
                }
            }
        }
        return cost;
    }

    private List<WorldObject> CollectCurrencyStacks(uint currencyWcid, uint amount)
    {
        var currencyStacksCollected = new List<WorldObject>();

        var currencyStacksInInventory = GetInventoryItemsOfWCID(currencyWcid);
        //currencyStacksInInventory = currencyStacksInInventory.OrderBy(o => o.Value).ToList();

        var remaining = (int)amount;

        foreach (var stack in currencyStacksInInventory)
        {
            var amountToRemove = Math.Min(remaining, stack.StackSize ?? 1);

            if (stack.StackSize == amountToRemove)
            {
                currencyStacksCollected.Add(stack);
            }
            else
            {
                // create new stack
                var newStack = WorldObjectFactory.CreateNewWorldObject(currencyWcid);
                newStack.SetStackSize(amountToRemove);
                currencyStacksCollected.Add(newStack);

                var stackToAdjust = FindObject(
                    stack.Guid,
                    SearchLocations.MyInventory,
                    out var foundInContainer,
                    out var rootContainer,
                    out _
                );

                // adjust existing stack
                if (stackToAdjust != null)
                {
                    AdjustStack(stackToAdjust, -amountToRemove, foundInContainer, rootContainer);
                    Session.Network.EnqueueSend(new GameMessageSetStackSize(stackToAdjust));
                    Session.Network.EnqueueSend(
                        new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.EncumbranceVal, EncumbranceVal ?? 0)
                    );
                }
                // UpdateCoinValue removed -- already called upstream
            }

            remaining -= amountToRemove;

            if (remaining <= 0)
            {
                break;
            }
        }
        return currencyStacksCollected;
    }
    private void HandleStaleVendorPurchase(Vendor vendor, ACE.Entity.ObjectGuid itemGuid, bool itemWasAddedToInventory)
    {
        if (itemWasAddedToInventory)
        {
            TryRemoveFromInventoryWithNetworking(itemGuid, out _, RemoveFromInventoryAction.None);

            var newlyCreated = FindObject(itemGuid, SearchLocations.MyInventory, out _, out _, out _);
            newlyCreated?.Destroy();
        }

        if (vendor.IsMarketVendor)
        {
            vendor.RemoveFromMarketSession(this, itemGuid);
        }
        else
        {
            // Ensure the vendor no longer advertises this GUID to any client.
            // If the item was already removed (e.g. purchased by another player), this is a no-op.
            vendor.UniqueItemsForSale.Remove(itemGuid);
        }

        // Unwedge the client transaction first, then refresh the vendor UI.
        Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, Guid.Full));
        vendor.ApproachVendor(this, VendorType.Undef, 0, skipRestock: true);

        SendTransientError("That item is no longer available.");
        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"{vendor.Name} says, \"I'm sorry, that item is no longer available. I've refreshed my stock.\"",
                ChatMessageType.Tell
            )
        );
    }

    // Used by vendor-side validation when the requested item GUID is no longer for sale.
    public void HandleStaleVendorPurchaseByGuid(Vendor vendor, ACE.Entity.ObjectGuid itemGuid)
        => HandleStaleVendorPurchase(vendor, itemGuid, itemWasAddedToInventory: false);
}
