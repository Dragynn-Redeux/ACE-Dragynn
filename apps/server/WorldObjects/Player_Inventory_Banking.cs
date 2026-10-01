using System.Linq;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Logging;

namespace ACE.Server.WorldObjects;

public partial class Player
{
    /// <summary>
    /// Checks to see if an item was moved to/from a bank.
    /// Sets BankAccountId for items placed in a 'bank-main container' and unsets it from items removed.
    /// Flags containers added to a bank as BankContainers.
    /// Sends detailed logs about each item movement.
    /// </summary>
    /// <param name="item">The item being moved.</param>
    /// <param name="targetContainer">The container the item is being moved to.</param>
    /// <param name="sourceContainer">The container the item was moved from.</param>
    /// <param name="targetContainerRootOwner">The root owner container of the container the item moved to. (Player or bank-main container)</param>
    /// <param name="sourceContainerRootOwner">The root owner container of the container the item moved from. (Player or bank-main container)</param>
    private void CheckForBankMoveItem(
        WorldObject item,
        Container targetContainer,
        Container sourceContainer,
        Container targetContainerRootOwner,
        Container sourceContainerRootOwner
    )
    {
        // If targetContainerRootOwner is null, it is likely a bank-main container
        // Set targetContainerRootOwner to the targetContainer (bank-main container)
        targetContainerRootOwner ??= targetContainer;

        // If sourceContainer is null, the item was likely moved from an equipped slot
        // Set sourceContainer to the sourceContainerRootOwner (Player container)
        sourceContainer ??= sourceContainerRootOwner;

        if (
            targetContainerRootOwner is not { WeenieType: WeenieType.Storage }
            && sourceContainerRootOwner is not { WeenieType: WeenieType.Storage }
        )
        {
            return;
        }

        LogBankMove(item, item.StackSize ?? 1, sourceContainerRootOwner, targetContainerRootOwner);

        var bankLogPlayer = new BankLogPlayer(Name, Account.AccountId);
        var bankLogItem = new BankLogItem(item.Name, item.Guid.Full, item.StackSize, item.PlacementPosition);
        var bankLogSourceContainer = new BankLogContainer(sourceContainer.Name, sourceContainer.Guid.Full);
        var bankLogTargetContainer = new BankLogContainer(targetContainer.Name, targetContainer.Guid.Full);

        // Move an item INTO a BANK-MAIN container. Set a BankAccountId on the item. DeepSave bank.
        if (
            targetContainer is { WeenieType: WeenieType.Storage }
            && sourceContainer is not { WeenieType: WeenieType.Storage }
        )
        {
            if (item is Container itemAsContainer)
            {
                itemAsContainer.IsBankSideContainer = true;

                // plain packs and Trophy Packs hold more in the bank; the client learns the new size from the object
                if (BankPackExpansion.ApplyInBank(itemAsContainer))
                {
                    Session.Network.EnqueueSend(new GameMessageUpdateObject(itemAsContainer));
                }
            }

            item.BankAccountId = Account.AccountId;

            DeepSave(sourceContainer);
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                if (sourceContainer.IsBankSideContainer)
                {
                    bankLogTargetContainer.BankPack = true;
                    bankLogSourceContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - MOVE from BANK-SIDE to BANK-MAIN)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                        bankLogPlayer,
                        bankLogItem,
                        bankLogSourceContainer,
                        bankLogTargetContainer
                    );
                }
                else
                {
                    bankLogTargetContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - MOVE from PLAYER to BANK-MAIN)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                        bankLogPlayer,
                        bankLogItem,
                        bankLogSourceContainer,
                        bankLogTargetContainer
                    );
                }

                VerifyMovedItemPosition(targetContainer, sourceContainer, item);
            }
        }
        // Move an item WITHIN a BANK-MAIN container.
        else if (
            targetContainer is { WeenieType: WeenieType.Storage }
            && sourceContainer is { WeenieType: WeenieType.Storage }
        )
        {
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MOVE within BANK-MAIN)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                    bankLogPlayer,
                    bankLogItem,
                    bankLogSourceContainer,
                    bankLogTargetContainer
                );

                VerifyMovedItemPosition(targetContainer, sourceContainer, item);
            }
        }
        // Move an item OUT of a BANK-MAIN container. Remove the BankAccountId on the item.
        else if (
            targetContainer is not { WeenieType: WeenieType.Storage }
            && sourceContainer is { WeenieType: WeenieType.Storage }
        )
        {
            if (item is Container itemContainer)
            {
                itemContainer.IsBankSideContainer = false;

                // back to its own size (HandleActionPutItemInContainer_Verify refused it if it held more)
                if (BankPackExpansion.Revert(itemContainer))
                {
                    Session.Network.EnqueueSend(new GameMessageUpdateObject(itemContainer));
                }
            }

            item.BankAccountId = 0;

            DeepSave(sourceContainer);
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                if (targetContainer.IsBankSideContainer)
                {
                    bankLogTargetContainer.BankPack = true;
                    bankLogSourceContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - MOVE from BANK-MAIN to BANK-SIDE)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                        bankLogPlayer,
                        bankLogItem,
                        bankLogSourceContainer,
                        bankLogTargetContainer
                    );
                }
                else
                {
                    bankLogSourceContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - MOVE from BANK-MAIN to PLAYER)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                        bankLogPlayer,
                        bankLogItem,
                        bankLogSourceContainer,
                        bankLogTargetContainer
                    );
                }

                VerifyMovedItemPosition(targetContainer, sourceContainer, item);
            }
        }
        // Logging Only - From PLAYER to BANK-SIDE
        else if (
            targetContainer.IsBankSideContainer && sourceContainerRootOwner is not { WeenieType: WeenieType.Storage }
        )
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MOVE from PLAYER to BANK-SIDE)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                    bankLogPlayer,
                    bankLogItem,
                    bankLogSourceContainer,
                    bankLogTargetContainer
                );

                VerifyMovedItemPosition(targetContainer, sourceContainer, item);
            }
        }
        // Logging Only - From BANK-SIDE to PLAYER
        else if (!targetContainer.IsBankSideContainer && sourceContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MOVE from BANK-SIDE to PLAYER)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                    bankLogPlayer,
                    bankLogItem,
                    bankLogSourceContainer,
                    bankLogTargetContainer
                );

                VerifyMovedItemPosition(targetContainer, sourceContainer, item);
            }
        }
        // Logging Only - From BANK-SIDE to BANK-SIDE
        else if (targetContainer.IsBankSideContainer && sourceContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MOVE from BANK-SIDE to BANK-SIDE)\n PLAYER: {@Player}\n MOVED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}\n TO CONTAINER: {@TargetContainer}",
                    bankLogPlayer,
                    bankLogItem,
                    bankLogSourceContainer,
                    bankLogTargetContainer
                );

                VerifyMovedItemPosition(targetContainer, sourceContainer, item);
            }
        }
    }

    /// <summary>
    /// Checks to see if an item was equipped directly from a bank-main container.
    /// Unsets the BankAccountId of the item.
    /// Sends detailed logs about each item movement.
    /// </summary>
    /// <param name="item">The item being equipped.</param>
    /// <param name="sourceContainer">The container the item is being moved to.</param>
    private void CheckForBankMoveToEquip(WorldObject item, Container sourceContainer)
    {
        if (sourceContainer is not { WeenieType: WeenieType.Storage} and not {IsBankSideContainer: true })
        {
            return;
        }

        LogBankMove(item, item.StackSize ?? 1, sourceContainer as Storage ?? sourceContainer.Container as Storage, this);

        var bankLogPlayer = new BankLogPlayer(Name, Account.AccountId);
        var bankLogItem = new BankLogItem(item.Name, item.Guid.Full, item.StackSize, item.PlacementPosition);
        var bankLogSourceContainer = new BankLogContainer(sourceContainer.Name, sourceContainer.Guid.Full);

        if (sourceContainer is { WeenieType: WeenieType.Storage })
        {
            item.BankAccountId = 0;

            DeepSave(item);
            DeepSave(sourceContainer);

            // foreach (var wo in sourceContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }
        }

        if (PropertyManager.GetBool("banking_system_logging").Item)
        {
            if (sourceContainer.IsBankSideContainer)
            {
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - EQUIP from BANK-SIDE)\n PLAYER: {@Player}\n EQUIPPED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}",
                    bankLogPlayer,
                    bankLogItem,
                    bankLogSourceContainer
                );
            }
            else
            {
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - EQUIP from BANK-MAIN)\n PLAYER: {@Player}\n EQUIPPED ITEM: {@Item}\n FROM CONTAINER: {@SourceContainer}",
                    bankLogPlayer,
                    bankLogItem,
                    bankLogSourceContainer
                );
            }
        }
    }

    /// <summary>
    /// Checks to see if a stack was split to/from a bank.
    /// Sets BankAccountId for a new stack placed in a 'bank-main container' and unsets it from new stacks removed.
    /// Sends detailed logs about each item movement.
    /// </summary>
    /// <param name="sourceStack">The original stack.</param>
    /// <param name="newStack">The newly created stack.</param>
    /// <param name="targetContainer">The container the new stack is being moved to.</param>
    /// <param name="sourceContainer">The container the source stack is in.</param>
    /// <param name="targetContainerRootOwner">The root owner container of the container the new stack moved to. (Player or bank-main container)</param>
    /// <param name="sourceContainerRootOwner">The root owner container of the container the source stack is in. (Player or bank-main container)</param>
    private void CheckForBankSplitStack(
        WorldObject sourceStack,
        WorldObject newStack,
        Container targetContainer,
        Container sourceContainer,
        Container targetContainerRootOwner,
        Container sourceContainerRootOwner
    )
    {
        // If targetContainerRootOwner is null, it is likely a bank-main container
        // Set targetContainerRootOwner to the targetContainer (bank-main container)
        targetContainerRootOwner ??= targetContainer;

        // If sourceContainer is null, the source stack was likely split from an equipped slot (ammo?)
        // Set sourceContainer to the sourceContainerRootOwner (Player container)
        sourceContainer ??= sourceContainerRootOwner;

        if (
            targetContainerRootOwner is not { WeenieType: WeenieType.Storage }
            && sourceContainerRootOwner is not { WeenieType: WeenieType.Storage }
        )
        {
            return;
        }

        LogBankMove(newStack, newStack.StackSize ?? 1, sourceContainerRootOwner, targetContainerRootOwner);

        var bankLogPlayer = new BankLogPlayer(Name, Account.AccountId);
        var bankLogSourceStack = new BankLogItem(
            sourceStack.Name,
            sourceStack.Guid.Full,
            sourceStack.StackSize,
            sourceStack.PlacementPosition
        );
        var bankLogNewStack = new BankLogItem(
            newStack.Name,
            newStack.Guid.Full,
            newStack.StackSize,
            newStack.PlacementPosition
        );
        var bankLogSourceContainer = new BankLogContainer(sourceContainer.Name, sourceContainer.Guid.Full);
        var bankLogTargetContainer = new BankLogContainer(targetContainer.Name, targetContainer.Guid.Full);

        // SPLIT a stack INTO BANK-MAIN. Set a BankAccountId on the new stack.
        if (
            targetContainer is { WeenieType: WeenieType.Storage }
            && sourceContainer is not { WeenieType: WeenieType.Storage }
        )
        {
            newStack.BankAccountId = Account.AccountId;

            DeepSave(sourceContainer);
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                if (sourceContainer.IsBankSideContainer)
                {
                    bankLogTargetContainer.BankPack = true;
                    bankLogSourceContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - SPLIT from BANK-SIDE to BANK-MAIN)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                        bankLogPlayer,
                        bankLogSourceStack,
                        bankLogSourceContainer,
                        bankLogNewStack,
                        bankLogTargetContainer
                    );
                }
                else
                {
                    bankLogTargetContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - SPLIT from PLAYER from BANK-MAIN)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                        bankLogPlayer,
                        bankLogSourceStack,
                        bankLogSourceContainer,
                        bankLogNewStack,
                        bankLogTargetContainer
                    );
                }

                VerifySplitStackPosition(targetContainer, sourceContainer, newStack, sourceStack);
            }
        }
        // Split a stack OUT of BANK-MAIN. Unset the BankAccountId on the new stack.
        else if (
            sourceContainer is { WeenieType: WeenieType.Storage }
            && targetContainer is not { WeenieType: WeenieType.Storage }
        )
        {
            newStack.BankAccountId = 0;

            DeepSave(sourceContainer);
            DeepSave(targetContainer);

            // foreach (var wo in sourceContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                if (targetContainer.IsBankSideContainer)
                {
                    bankLogTargetContainer.BankPack = true;
                    bankLogSourceContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - SPLIT from BANK-MAIN to BANK-SIDE )\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                        bankLogPlayer,
                        bankLogSourceStack,
                        bankLogSourceContainer,
                        bankLogNewStack,
                        bankLogTargetContainer
                    );
                }
                else
                {
                    bankLogSourceContainer.BankPack = true;
                    _log.Information(
                        "(BANKING - SPLIT from BANK-MAIN to PLAYER )\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                        bankLogPlayer,
                        bankLogSourceStack,
                        bankLogSourceContainer,
                        bankLogNewStack,
                        bankLogTargetContainer
                    );
                }

                VerifySplitStackPosition(targetContainer, sourceContainer, newStack, sourceStack);
            }
        }
        // Split a stack WITHIN BANK-MAIN. Set a BankAccountId on the new stack.
        else if (
            targetContainer is { WeenieType: WeenieType.Storage }
            && sourceContainer is { WeenieType: WeenieType.Storage }
        )
        {
            newStack.BankAccountId = Account.AccountId;

            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - SPLIT within BANK-MAIN)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogNewStack,
                    bankLogTargetContainer
                );

                VerifySplitStackPosition(targetContainer, sourceContainer, newStack, sourceStack);
            }
        }
        // Logging Only - Split a stack OUT of PLAYER to BANK-SIDE
        else if (targetContainer.IsBankSideContainer && !sourceContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                _log.Information(
                    "(BANKING - Split from PLAYER to BANK-SIDE)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogNewStack,
                    bankLogTargetContainer
                );

                VerifySplitStackPosition(targetContainer, sourceContainer, newStack, sourceStack);
            }
        }
        // Logging Only - Split a stack OUT of BANK-SIDE to PLAYER
        else if (!targetContainer.IsBankSideContainer && sourceContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - Split from BANK-SIDE to PLAYER)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogNewStack,
                    bankLogTargetContainer
                );

                VerifySplitStackPosition(targetContainer, sourceContainer, newStack, sourceStack);
            }
        }
        // Logging Only - Split a stack OUT of BANK-SIDE to BANK-SIDE
        else if (targetContainer.IsBankSideContainer && sourceContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - Split from BANK-SIDE to BANK-SIDE)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO NEW STACK: {@NewStack}\n IN CONTAINER: {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogNewStack,
                    bankLogTargetContainer
                );

                VerifySplitStackPosition(targetContainer, sourceContainer, newStack, sourceStack);
            }
        }
    }

    /// <summary>
    /// Checks to see if a stack is going to merge with another stack after moving to/from a bank.
    /// BankAccountId does not need to be set.
    /// Sends detailed logs about each item movement.
    /// </summary>
    /// <param name="sourceStack">The original stack.</param>
    /// <param name="targetStack">The newly merged stack.</param>
    /// <param name="sourceContainer">The container the source stack is in.</param>
    /// <param name="targetContainer">The container the target stack is in.</param>
    /// <param name="sourceStackRootOwner">The root owner container of the container the source stack is in. (Player or bank-main container)</param>
    /// <param name="targetStackRootOwner">The root owner container of the container the target stack is in. (Player or bank-main container)</param>
    private void CheckForBankSplitAndMerge(
        WorldObject sourceStack,
        WorldObject targetStack,
        int amount,
        Container sourceContainer,
        Container targetContainer,
        Container sourceStackRootOwner,
        Container targetStackRootOwner
    )
    {
        // If targetStackRootOwner is null, it is likely a bank-main container
        // Set targetStackRootOwner to the targetContainer (bank-main container)
        targetStackRootOwner ??= targetContainer;

        // If sourceContainer is null, the stack was likely split from an equipped slot (ammo?)
        // Set sourceContainer to the sourceStackRootOwner (Player container)
        sourceContainer ??= sourceStackRootOwner;

        var bankLogPlayer = new BankLogPlayer(Name, Account.AccountId);
        var bankLogSourceStack = new BankLogItem(
            sourceStack.Name,
            sourceStack.Guid.Full,
            sourceStack.StackSize,
            sourceStack.PlacementPosition
        );
        var bankLogTargetStack = new BankLogItem(
            targetStack.Name,
            targetStack.Guid.Full,
            targetStack.StackSize,
            targetStack.PlacementPosition
        );
        var bankLogSourceContainer = new BankLogContainer(sourceContainer?.Name, sourceContainer?.Guid.Full);
        var bankLogTargetContainer = new BankLogContainer(targetContainer?.Name, targetContainer?.Guid.Full);

        if (
            targetStackRootOwner is not { WeenieType: WeenieType.Storage }
            && sourceStackRootOwner is not { WeenieType: WeenieType.Storage }
        )
        {
            return;
        }

        LogBankMove(sourceStack, amount, sourceStackRootOwner, targetStackRootOwner);

        //  MERGE stack from PLAYER to BANK-MAIN
        if (
            targetContainer is { WeenieType: WeenieType.Storage }
            && sourceStackRootOwner is not { WeenieType: WeenieType.Storage }
        )
        {
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE from PLAYER to BANK-MAIN)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }

        //  MERGE stack from BANK-MAIN to PLAYER
        if (
            targetStackRootOwner is not { WeenieType: WeenieType.Storage }
            && sourceContainer is { WeenieType: WeenieType.Storage }
        )
        {
            DeepSave(sourceContainer);

            // foreach (var wo in sourceContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE from BANK-MAIN to PLAYER)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }

        //  MERGE stack from BANK-MAIN to BANK-MAIN
        if (
            targetContainer is { WeenieType: WeenieType.Storage }
            && sourceContainer is { WeenieType: WeenieType.Storage }
        )
        {
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE from BANK-MAIN to BANK-MAIN)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }
        // MERGE stack from BANK-MAIN to BANK-SIDE
        else if (targetContainer != null && sourceContainer is { WeenieType: WeenieType.Storage } && targetContainer.IsBankSideContainer)
        {
            DeepSave(sourceContainer);
            DeepSave(targetContainer);

            // foreach (var wo in sourceContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE BANK-MAIN to BANK-SIDE)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }
        // MERGE stack from BANK-SIDE to BANK-MAIN
        else if (sourceContainer is { IsBankSideContainer: true } && targetContainer is { WeenieType: WeenieType.Storage })
        {
            DeepSave(sourceContainer);
            DeepSave(targetContainer);

            // foreach (var wo in targetContainer.Inventory.Values)
            // {
            //     DeepSave(wo);
            // }

            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE BANK-SIDE to BANK-MAIN)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }
        // Logging Only - MERGE stack from PLAYER to BANK-SIDE
        else if (targetContainer != null && sourceStackRootOwner is not { WeenieType: WeenieType.Storage } && targetContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE PLAYER to BANK-SIDE)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }
        // Logging Only - MERGE stack from BANK-SIDE to PLAYER
        else if (sourceContainer is { IsBankSideContainer: true } && targetStackRootOwner is not { WeenieType: WeenieType.Storage })
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE BANK-SIDE to PLAYER)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }
        // Logging Only - MERGE stack from BANK-SIDE to BANK-SIDE
        else if (targetContainer != null && sourceContainer is { IsBankSideContainer: true } && targetContainer.IsBankSideContainer)
        {
            if (PropertyManager.GetBool("banking_system_logging").Item)
            {
                bankLogTargetContainer.BankPack = true;
                bankLogSourceContainer.BankPack = true;
                _log.Information(
                    "(BANKING - MERGE BANK-SIDE to BANK-SIDE)\n PLAYER: {@Player}\n SPLIT STACK: {@Stack}\n FROM CONTAINER: {@PrevContainer}\n TO MERGE WITH ANOTHER STACK: {@NewStack}\n IN CONTAINER {@NewContainer}",
                    bankLogPlayer,
                    bankLogSourceStack,
                    bankLogSourceContainer,
                    bankLogTargetStack,
                    bankLogTargetContainer
                );

                VerifyMergeStackPosition(targetContainer, sourceContainer, targetStack);
            }
        }
    }

    private void VerifyMovedItemPosition(Container targetContainer, Container sourceContainer, WorldObject wo)
    {
        if (wo is null || sourceContainer is null || targetContainer is null)
        {
            _log.Error("[BANKING] Null object detected during banking transaction. " +
                       "Item: {Item}, SourceContainer: {SourceContainer}, TargetContainer: {TargetContainer}",
                wo is null, sourceContainer is null, targetContainer is null);

            return;
        }

        foreach (var worldObject in targetContainer.Inventory.Where(worldObject => worldObject.Value.Guid == wo.Guid))
        {
            if (worldObject.Value.PlacementPosition != wo.PlacementPosition)
            {
                _log.Error("[BANKING] Placement position mismatch for '{WorldObject}'. " +
                           "Object Placement Position: {ObjectPlacementPosition}, " +
                           "Container's Placement Position: {ContainerPlacementPosition}",
                    wo.Name, wo.PlacementPosition, worldObject.Value.PlacementPosition);
            }
            else
            {
                _log.Information("[BANKING] '{Item}' placement position matches '{Container}' placement position record.", wo.Name, targetContainer.Name);
            }
        }

        if (sourceContainer != targetContainer)
        {
            foreach (var worldObject in sourceContainer.Inventory.Where(worldObject => worldObject.Value.Guid == wo.Guid))
            {
                _log.Error(
                    "[BANKING] Bank object '{WorldObject}' has not properly moved from container '{SourceContainer}'" +
                    " to container '{TargetContainer}'", wo.Name, sourceContainer.Name, targetContainer.Name);
            }
        }
    }

    private void VerifySplitStackPosition(Container targetContainer, Container sourceContainer, WorldObject newStack, WorldObject sourceStack)
    {
        if (newStack is null || sourceStack is null || sourceContainer is null || targetContainer is null)
        {
            _log.Error("[BANKING] Null object detected during banking transaction. " +
                       "NewStack: {NewStack}, SourceStack: {SourceStack}, SourceContainer: {SourceContainer}, TargetContainer: {TargetContainer}",
                newStack is null, sourceStack is null, sourceContainer is null, targetContainer is null);

            return;
        }

        foreach (var worldObject in targetContainer.Inventory.Where(worldObject => worldObject.Value.Guid == newStack.Guid))
        {
            if (worldObject.Value.PlacementPosition != newStack.PlacementPosition)
            {
                _log.Error("[BANKING] Placement position mismatch for '{WorldObject}'. " +
                           "NewStack Placement Position: {ObjectPlacementPosition}, " +
                           "Container's Placement Position: {ContainerPlacementPosition}",
                    newStack.Name, newStack.PlacementPosition, worldObject.Value.PlacementPosition);
            }
            else
            {
                _log.Information("[BANKING] '{NewStack}' placement position matches '{Container}' placement position record.", newStack.Name, targetContainer.Name);
            }
        }

        if (sourceContainer != targetContainer)
        {
            foreach (var worldObject in sourceContainer.Inventory.Where(worldObject => worldObject.Value.Guid == newStack.Guid))
            {
                _log.Error(
                    "[BANKING] Bank newStack '{NewStack}' has not properly moved from container '{SourceContainer}'" +
                    " to container '{TargetContainer}'", newStack.Name, sourceContainer.Name, targetContainer.Name);
            }
        }
    }

    private void VerifyMergeStackPosition(Container targetContainer, Container sourceContainer, WorldObject mergedStack)
    {
        if (mergedStack is null || sourceContainer is null || targetContainer is null)
        {
            _log.Error("[BANKING] Null object detected during banking transaction. " +
                       "MergedStack: {MergedStack}, SourceContainer: {SourceContainer}, TargetContainer: {TargetContainer}",
                mergedStack is null, sourceContainer is null, targetContainer is null);

            return;
        }

        foreach (var worldObject in targetContainer.Inventory.Where(worldObject => worldObject.Value.Guid == mergedStack.Guid))
        {
            if (worldObject.Value.PlacementPosition != mergedStack.PlacementPosition)
            {
                _log.Error("[BANKING] Placement position mismatch for '{WorldObject}'. " +
                           "MergedStack Placement Position: {ObjectPlacementPosition}, " +
                           "Container's Placement Position: {ContainerPlacementPosition}",
                    mergedStack.Name, mergedStack.PlacementPosition, worldObject.Value.PlacementPosition);
            }
            else
            {
                _log.Information("[BANKING] '{MergedStack}' placement position matches '{Container}' placement position record.", mergedStack.Name, targetContainer.Name);
            }
        }

        if (sourceContainer != targetContainer)
        {
            foreach (var worldObject in sourceContainer.Inventory.Where(worldObject => worldObject.Value.Guid == mergedStack.Guid))
            {
                _log.Error(
                    "[BANKING] Bank MergedStack '{MergedStack}' has not properly moved from container '{SourceContainer}'" +
                    " to container '{TargetContainer}'", mergedStack.Name, sourceContainer.Name, targetContainer.Name);
            }
        }
    }
}
