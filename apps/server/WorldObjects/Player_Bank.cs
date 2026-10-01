using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects.Logging;

namespace ACE.Server.WorldObjects;

/// <summary>
/// What a /bank deposit or /bank sort did, for the message back to the player.
/// </summary>
public sealed class BankReport
{
    /// <summary>Items moved into (deposit) or within (sort) the bank.</summary>
    public int Moved { get; set; }

    /// <summary>Stacks that were emptied into another stack in the bank.</summary>
    public int StacksCombined { get; set; }

    /// <summary>Items that had somewhere better to go, but it was full.</summary>
    public int NoRoom { get; set; }

    /// <summary>Items that can never be banked (attuned).</summary>
    public int Attuned { get; set; }

    /// <summary>Moves the normal inventory checks turned down.</summary>
    public int Failed { get; set; }

    /// <summary>Bank containers whose items were put back in order.</summary>
    public int Reordered { get; set; }

    /// <summary>
    /// How many items went into each bank container, by name, in the order they were first used.
    /// The bank itself has a null name.
    /// </summary>
    public List<(string Name, int Count)> MovedInto { get; } = [];

    /// <summary>
    /// Where the moved items went, e.g. "6 into Weapons Pack, 8 into your bank".
    /// </summary>
    public string DescribeDestinations()
    {
        return string.Join(", ", MovedInto.Select(m => $"{m.Count:N0} into {m.Name ?? "your bank"}"));
    }

    /// <summary>
    /// The message for the player after a deposit. what names what was deposited, for when there was nothing.
    /// </summary>
    public string DescribeDeposit(string what)
    {
        var lines = new List<string>();

        if (Moved > 0)
        {
            lines.Add($"Deposited {Plural(Moved, "item")} ({DescribeDestinations()}).");
        }

        if (StacksCombined > 0)
        {
            lines.Add($"Added {Plural(StacksCombined, "stack")} to stacks already in your bank.");
        }

        if (NoRoom > 0)
        {
            lines.Add($"{Plural(NoRoom, "item")} didn't fit: your bank and its packs are full.");
        }

        if (Failed > 0)
        {
            lines.Add($"{Plural(Failed, "item")} couldn't be moved.");
        }

        if (Attuned > 0)
        {
            lines.Add($"{Plural(Attuned, "item")} {(Attuned == 1 ? "is" : "are")} attuned and can't be banked.");
        }

        if (lines.Count == 0)
        {
            lines.Add($"You have no {what} to deposit.");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// What a sort did, a line each: moves, combined stacks, containers put in order, and what couldn't move.
    /// Empty if it changed nothing.
    /// </summary>
    public List<string> DescribeSort()
    {
        var lines = new List<string>();

        if (Moved > 0)
        {
            lines.Add($"Moved {Plural(Moved, "item")} ({DescribeDestinations()}).");
        }

        if (StacksCombined > 0)
        {
            lines.Add($"Combined {Plural(StacksCombined, "stack")}.");
        }

        if (Reordered > 0)
        {
            lines.Add($"Put {Plural(Reordered, "container")} in order.");
        }

        if (NoRoom > 0)
        {
            lines.Add($"{Plural(NoRoom, "item")} belong in a pack that is full.");
        }

        if (Failed > 0)
        {
            lines.Add($"{Plural(Failed, "item")} couldn't be moved.");
        }

        return lines;
    }

    /// <summary>
    /// The line after a deposit or withdrawal that autosort followed, or null if the sort changed nothing.
    /// </summary>
    public static string DescribeAutoSort(BankReport sort)
    {
        var lines = sort?.DescribeSort();

        return lines is { Count: > 0 } ? $"Autosort: {string.Join(" ", lines)}" : null;
    }

    public static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";

    public void CountMove(Container target, Storage bank)
    {
        var name = target == bank ? null : target.Name;

        var index = MovedInto.FindIndex(m => m.Name == name);
        if (index < 0)
        {
            MovedInto.Add((name, 1));
        }
        else
        {
            MovedInto[index] = (name, MovedInto[index].Count + 1);
        }
    }
}

/// <summary>
/// What a /bank withdraw did, for the message back to the player.
/// </summary>
public sealed class WithdrawReport
{
    /// <summary>
    /// What was taken and where it went ("your main pack" or a pack's name), in order.
    /// Amount is how many of a stack, or null for an item that doesn't stack.
    /// </summary>
    public List<(string Into, string Name, int? Amount)> Taken { get; } = [];

    /// <summary>How many were taken, counting each one in a stack.</summary>
    public int Units { get; set; }

    /// <summary>Items left in the bank because no pack you carry had room.</summary>
    public int NoRoom { get; set; }

    /// <summary>Items left in the bank, or part of a stack, because they would have put you over your burden limit.</summary>
    public int TooHeavy { get; set; }

    /// <summary>Moves the normal inventory checks turned down.</summary>
    public int Failed { get; set; }

    public void Add(string into, string name, int? amount)
    {
        // a stack split between topping up and a new stack in the same pack reads as one line
        var last = Taken.Count - 1;
        if (last >= 0 && Taken[last].Into == into && Taken[last].Name == name && amount != null && Taken[last].Amount != null)
        {
            Taken[last] = (into, name, Taken[last].Amount + amount);
            return;
        }

        Taken.Add((into, name, amount));
    }
}

/// <summary>
/// Bulk bank operations for the /bank command.
/// Every move goes through the same checks and code as dragging the item by hand
/// (HandleActionPutItemInContainer_Verify, DoHandleActionPutItemInContainer, DoHandleActionStackableMerge),
/// without the walk-to and pickup animation, so the bank bookkeeping in Player_Inventory_Banking still runs.
/// </summary>
public partial class Player
{
    /// <summary>
    /// A place in the bank an item can go: the bank itself or one of its side packs.
    /// </summary>
    private sealed class BankSpot
    {
        public Container Container { get; init; }
        public bool IsMain { get; init; }
        public BankPackTags Tags { get; init; }
        public int FreeSlots { get; set; }
        public int Order { get; init; }

        /// <summary>
        /// Takes only what it is named, inscribed or specialized for, nothing at the neutral rank
        /// (a carried pack inscribed "keep", for /bank withdraw).
        /// </summary>
        public bool OnlyItsOwn { get; init; }
    }

    // Lower is a better home for an item, see RankBankSpot. Inscribed packs rank from 1 to BankCategories.MaxPackFit - 1.
    private const int RankNamedPack = 0;
    private const int RankSpecialized = BankCategories.MaxPackFit;
    private const int RankNeutral = RankSpecialized + 1;
    private const int RankMisfiled = RankNeutral + 1;

    /// <summary>
    /// Stamped once the player has used a /bank command. Until then, opening a bank tells them about /bank.
    /// As an ACCOUNT_ flag, it is copied to characters made later on the account.
    /// </summary>
    public const string BankCommandsUsedQuest = "ACCOUNT_BankCommandsUsed";

    public bool BankCommandsUsed => QuestManager.HasQuest(BankCommandsUsedQuest);

    /// <summary>
    /// Stamped when the bank has shown its introduction popup (Storage.BankIntro), which it does once, the first time
    /// the bank is opened. As an ACCOUNT_ flag, it is copied to characters made later on the account.
    /// </summary>
    public const string BankIntroSeenQuest = "ACCOUNT_BankIntroSeen";

    public bool BankIntroSeen => QuestManager.HasQuest(BankIntroSeenQuest);

    /// <summary>
    /// Stamped while /bank autosort is on for this character: every deposit and withdrawal is followed by a /bank sort.
    /// </summary>
    public const string BankAutoSortQuest = "BankAutoSort";

    public bool BankAutoSort
    {
        get => QuestManager.HasQuest(BankAutoSortQuest);
        set
        {
            if (value)
            {
                QuestManager.Stamp(BankAutoSortQuest);
            }
            else
            {
                QuestManager.Erase(BankAutoSortQuest);
            }
        }
    }

    /// <summary>
    /// Sorts the whole bank if autosort is on and a deposit or withdrawal just moved something.
    /// Returns what the sort did, or null if it did not run.
    /// </summary>
    public BankReport AutoSortBank(Storage bank, bool anythingMoved)
    {
        return BankAutoSort && anythingMoved ? SortBank(bank, null) : null;
    }

    // Set while a /bank command moves many items, so they go in the bank log as one entry instead of one each.
    private bool _bankBulkOperation;

    /// <summary>
    /// Logs an item moved into or out of the bank by hand (a drag, split, merge, or equipping it straight from the bank).
    /// amount: how many of a stack. sourceRoot and targetRoot: the bank or the player, where it came from and went.
    /// Moves within the bank or within your packs aren't logged.
    /// </summary>
    private void LogBankMove(WorldObject item, int amount, Container sourceRoot, Container targetRoot)
    {
        if (_bankBulkOperation)
        {
            return;
        }

        var intoBank = targetRoot is Storage && sourceRoot is not Storage;
        var outOfBank = sourceRoot is Storage && targetRoot is not Storage;

        if (intoBank || outOfBank)
        {
            BankActivityLog.Record(this, intoBank ? BankActivityLog.Deposited : BankActivityLog.Withdrew, BankActivityLog.DescribeItem(item, amount));
        }
    }

    /// <summary>
    /// True if a pack you carry is inscribed "deposit".
    /// </summary>
    public bool HasDepositPacks => GetDepositSources(true).Count > 0;

    /// <summary>
    /// A safety net for BankPackExpansion: a pack the player carries should be at its own size, since leaving
    /// the bank shrinks it. If one got out grown some other way, it is shrunk here, before the client is sent anything.
    /// One holding more than its own size can't be shrunk without dropping items, so it only shrinks to what it holds
    /// (it can't take more) and is logged.
    /// </summary>
    protected override void OnInitialInventoryLoadCompleted()
    {
        base.OnInitialInventoryLoadCompleted();

        foreach (var pack in Inventory.Values.OfType<Container>())
        {
            if (!BankPackExpansion.Revert(pack))
            {
                continue;
            }

            if (!BankPackExpansion.CanLeaveBank(pack, out var ownCapacity, out var itemCount))
            {
                _log.Warning(
                    "[BANKING] {Player} carries {Pack} (0x{PackGuid:X8}) holding {Count} items, more than its own {OwnCapacity}. Shrunk to {Count}.",
                    Name,
                    pack.Name,
                    pack.Guid.Full,
                    itemCount,
                    ownCapacity
                );
            }
        }
    }

    /// <summary>
    /// A pack that grew in the bank (BankPackExpansion) can only leave it once it holds no more than its own size.
    /// Returns false, and tells the player what to take out, if item is such a pack moving from the bank to anywhere else.
    /// </summary>
    private bool CanTakePackOutOfBank(WorldObject item, Container itemRootOwner, Container container)
    {
        if (item is not Container pack || itemRootOwner is not Storage || container is Storage)
        {
            return true;
        }

        if (BankPackExpansion.CanLeaveBank(pack, out var ownCapacity, out var itemCount))
        {
            return true;
        }

        Session.Network.EnqueueSend(
            new GameEventCommunicationTransientString(
                Session,
                $"Your {pack.Name} holds {itemCount} items. Take out {itemCount - ownCapacity} to carry it; it holds {ownCapacity} outside the bank."
            )
        );
        Session.Network.EnqueueSend(new GameEventInventoryServerSaveFailed(Session, pack.Guid.Full));

        return false;
    }

    /// <summary>
    /// The bank chest this player has open and is standing at, or null.
    /// </summary>
    public Storage GetOpenBank()
    {
        if (CurrentLandblock?.GetObject(LastOpenedContainerId) is not Storage bank)
        {
            return null;
        }

        if (!bank.IsOpen || bank.Viewer != Guid.Full)
        {
            return null;
        }

        if (!CurrentLandblock.WithinUseRadius(this, bank.Guid, out _))
        {
            return null;
        }

        return bank;
    }

    /// <summary>
    /// Moves every item matching filter (null = everything) from the main pack and side packs into the bank.
    /// Packs inscribed "keep" are skipped, and so are the packs themselves. With fromDepositPacksOnly, only the
    /// packs inscribed "deposit" are emptied (the popup when the bank opens, see OfferDepositPacks).
    /// Stackable items first top up stacks already in the bank, then each item goes to the best place with room
    /// (see RankBankSpot): a named pack for it, a bank pack inscribed for it, an untagged specialized pack that takes it,
    /// then the bank, then an untagged pack.
    /// </summary>
    public BankReport DepositToBank(Storage bank, BankCategory? filter, bool fromDepositPacksOnly = false)
    {
        var deposited = new List<string>();
        BankReport report;

        _bankBulkOperation = true;
        try
        {
            report = DepositToBank(bank, filter, fromDepositPacksOnly, deposited);
        }
        finally
        {
            _bankBulkOperation = false;
        }

        BankActivityLog.Record(this, BankActivityLog.Deposited, BankActivityLog.DescribeItems(deposited));

        return report;
    }

    private BankReport DepositToBank(Storage bank, BankCategory? filter, bool fromDepositPacksOnly, List<string> deposited)
    {
        var report = new BankReport();
        var touched = new HashSet<Container>();

        var spots = GetBankSpots(bank);
        var bankStacks = GetBankItems(bank).Where(i => i is Stackable && (i.MaxStackSize ?? 1) > 1).ToList();

        var anythingMoved = false;

        foreach (var item in GetDepositCandidates(GetDepositSources(fromDepositPacksOnly), filter, report))
        {
            var category = BankCategories.Tags(item);
            var amountBefore = item.StackSize ?? 1;

            var mergedAll = TopUpBankStacks(item, bankStacks, touched, out var mergedAny);
            anythingMoved |= mergedAny;

            if (mergedAll)
            {
                report.StacksCombined++;
                deposited.Add(BankActivityLog.DescribeItem(item, amountBefore));
                continue;
            }

            // what topped up bank stacks went in even if the rest stays out
            if (mergedAny)
            {
                deposited.Add(BankActivityLog.DescribeItem(item, amountBefore - (item.StackSize ?? 1)));
            }

            var target = FindBankSpot(spots, item, category, null, out _);
            if (target == null)
            {
                report.NoRoom++;
                continue;
            }

            if (!TryMoveItemInBank(item, target.Container))
            {
                report.Failed++;
                continue;
            }

            if (!mergedAny)
            {
                deposited.Add(BankActivityLog.DescribeItem(item, item.StackSize ?? 1));
            }
            else
            {
                deposited[^1] = BankActivityLog.DescribeItem(item, amountBefore);
            }

            target.FreeSlots--;
            touched.Add(target.Container);
            report.Moved++;
            report.CountMove(target.Container, bank);
            anythingMoved = true;

            if (item is Stackable && (item.StackSize ?? 1) < (item.MaxStackSize ?? 1))
            {
                bankStacks.Add(item);
            }
        }

        if (anythingMoved)
        {
            Session.Network.EnqueueSend(
                new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.EncumbranceVal, EncumbranceVal ?? 0)
            );

            UpdateCoinValue();

            EnqueueBroadcast(new GameMessageSound(Guid, Sound.DropItem));

            SaveBankContainers(bank, touched);
        }

        return report;
    }

    /// <summary>
    /// Called when the bank has opened: if a pack the player carries is inscribed "deposit" and holds anything
    /// that can be banked, asks whether to deposit it all. Yes deposits it as /bank deposit does; no does nothing.
    /// </summary>
    public void OfferDepositPacks(Storage bank)
    {
        var count = GetDepositCandidates(GetDepositSources(true), null, new BankReport()).Count;
        if (count == 0)
        {
            return;
        }

        var confirmation = new Confirmation_Custom(
            Guid,
            () =>
            {
                // the bank may have closed, or the player moved away, while the question was up
                var openBank = GetOpenBank();
                if (openBank == null || !openBank.BankInventoryLoaded || IsBusy)
                {
                    Session.Network.EnqueueSend(
                        new GameMessageSystemChat("Open your bank and stand at it to deposit your \"deposit\" packs.", ChatMessageType.System)
                    );
                    return;
                }

                var report = DepositToBank(openBank, null, fromDepositPacksOnly: true);
                var autoSort = BankReport.DescribeAutoSort(AutoSortBank(openBank, report.Moved + report.StacksCombined > 0));

                var text = autoSort == null ? report.DescribeDeposit("items") : $"{report.DescribeDeposit("items")}\n{autoSort}";
                Session.Network.EnqueueSend(new GameMessageSystemChat(text, ChatMessageType.System));
            }
        );

        // quietly skipped if another question is already up
        ConfirmationManager.EnqueueSend(confirmation, $"Deposit all items in \"Deposit\" packs? ({BankReport.Plural(count, "item")})");
    }

    /// <summary>
    /// Takes items out of the bank (or its packs) into the packs you carry, up to limit counting each one in a stack
    /// (null = all of them). items are bank items, in the order to take them.
    /// A stackable item first tops up stacks you carry. The rest goes to the best of your packs with room, ranked as a
    /// deposit ranks the bank's (see RankBankSpot): a named pack for it, a pack inscribed for it, an untagged specialized
    /// pack that takes it, then your main pack, then an untagged pack. Packs inscribed "deposit" get nothing, and packs
    /// inscribed "keep" only what they are named, inscribed or specialized for.
    /// Nothing is taken past your burden limit (100%, where you start to slow down); a stack is split to take what you can carry.
    /// </summary>
    public WithdrawReport WithdrawFromBank(Storage bank, List<WorldObject> items, int? limit)
    {
        WithdrawReport report;

        _bankBulkOperation = true;
        try
        {
            report = WithdrawItemsFromBank(bank, items, limit);
        }
        finally
        {
            _bankBulkOperation = false;
        }

        var withdrawn = new List<string>();
        foreach (var (_, name, amount) in report.Taken)
        {
            withdrawn.Add(BankActivityLog.DescribeItem(name, amount));
        }

        BankActivityLog.Record(this, BankActivityLog.Withdrew, BankActivityLog.DescribeItems(withdrawn));

        return report;
    }

    private WithdrawReport WithdrawItemsFromBank(Storage bank, List<WorldObject> items, int? limit)
    {
        var report = new WithdrawReport();
        var bankTouched = new HashSet<Container>();
        var carriedTouched = new HashSet<Container>();

        var spots = GetWithdrawSpots();
        var carriedStacks = spots
            .SelectMany(spot => spot.Container.Inventory.Values)
            .Where(i => i is Stackable && (i.StackSize ?? 1) < (i.MaxStackSize ?? 1))
            .ToList();

        var remaining = limit ?? int.MaxValue;
        var coinsTaken = false;

        foreach (var item in items)
        {
            if (remaining <= 0)
            {
                break;
            }

            // gone from the bank since the list was made
            if (item.Container is not Container source || (source != bank && source.Container != bank))
            {
                continue;
            }

            var name = item.NameWithMaterial;
            var isStack = item is Stackable;
            var isCoin = item.WeenieType == WeenieType.Coin;
            var want = Math.Min(item.StackSize ?? 1, remaining);
            var taken = 0;
            var tooHeavy = false;

            // top up stacks you carry
            if (isStack)
            {
                foreach (var target in carriedStacks.ToList())
                {
                    if (taken >= want)
                    {
                        break;
                    }

                    if (!CanStackTogether(item, target))
                    {
                        continue;
                    }

                    var room = (target.MaxStackSize ?? 1) - (target.StackSize ?? 1);
                    if (room <= 0)
                    {
                        carriedStacks.Remove(target);
                        continue;
                    }

                    var targetContainer = target.Container as Container;
                    var amount = Math.Min(Math.Min(room, want - taken), UnitsYouCanCarry(item, targetContainer));
                    if (amount <= 0)
                    {
                        tooHeavy = true;
                        break;
                    }

                    if (!DoHandleActionStackableMerge(item, target, amount))
                    {
                        break;
                    }

                    taken += amount;
                    bankTouched.Add(source);
                    carriedTouched.Add(targetContainer);
                    report.Add(DescribeCarried(targetContainer), name, amount);
                }
            }

            // the rest, into a pack
            var rest = want - taken;
            if (rest > 0 && !tooHeavy)
            {
                var spot = FindBankSpot(spots, item, BankCategories.Tags(item), null, out _);
                var amount = spot == null ? 0 : Math.Min(rest, UnitsYouCanCarry(item, spot.Container));

                WorldObject moved = null;

                if (spot == null)
                {
                    report.NoRoom++;
                }
                else if (amount <= 0)
                {
                    tooHeavy = true;
                }
                else if (amount == (item.StackSize ?? 1))
                {
                    if (TryMoveItemInBank(item, spot.Container))
                    {
                        OnTakenFromBank(item);
                        moved = item;
                    }
                }
                else
                {
                    moved = TrySplitOutOfBank(item, bank, spot.Container, amount);
                }

                if (moved != null)
                {
                    spot.FreeSlots--;
                    taken += amount;
                    bankTouched.Add(source);
                    carriedTouched.Add(spot.Container);
                    report.Add(DescribeCarried(spot.Container), name, isStack ? amount : null);

                    if (isStack && (moved.StackSize ?? 1) < (moved.MaxStackSize ?? 1))
                    {
                        carriedStacks.Add(moved);
                    }

                    // a stack cut short by your burden
                    tooHeavy = amount < rest;
                }
                else if (spot != null && amount > 0)
                {
                    report.Failed++;
                }
            }

            if (tooHeavy)
            {
                report.TooHeavy++;
            }

            report.Units += taken;
            remaining -= taken;
            coinsTaken |= isCoin && taken > 0;
        }

        if (report.Units > 0)
        {
            EndStealth();

            // A move adds an item's full burden; a specialized pack lightens it, which only a recount picks up.
            RecalculateBurden();

            if (coinsTaken)
            {
                UpdateCoinValue();
            }

            EnqueueBroadcast(new GameMessageSound(Guid, Sound.PickUpItem));

            foreach (var container in carriedTouched)
            {
                DeepSave(container);
            }

            SaveBankContainers(bank, bankTouched);
        }

        return report;
    }

    /// <summary>
    /// Where /bank withdraw can put items: your main pack, then your packs in order, except those inscribed "deposit"
    /// (their contents would be offered back to the bank). A pack inscribed "keep" takes only its own (BankSpot.OnlyItsOwn).
    /// </summary>
    private List<BankSpot> GetWithdrawSpots()
    {
        var spots = new List<BankSpot>
        {
            new()
            {
                Container = this,
                IsMain = true,
                FreeSlots = GetFreeInventorySlots(false),
                Order = 0,
            },
        };

        var order = 1;
        foreach (var pack in Inventory.Values.OfType<Container>().OrderBy(p => p.PlacementPosition ?? int.MaxValue))
        {
            var tags = BankCategories.ParseInscription(pack.Inscription);
            if (tags.Deposit)
            {
                continue;
            }

            spots.Add(
                new BankSpot
                {
                    Container = pack,
                    Tags = tags,
                    FreeSlots = pack.GetFreeInventorySlots(false),
                    Order = order++,
                    OnlyItsOwn = tags.Keep,
                }
            );
        }

        return spots;
    }

    private string DescribeCarried(Container container) => container == this ? "your main pack" : container.Name;

    /// <summary>
    /// How many of item (each one in a stack) you can put in container without going over your burden limit
    /// (100%, where you start to slow down). A specialized pack lightens what is in it, as RecalculateBurden counts it.
    /// </summary>
    private int UnitsYouCanCarry(WorldObject item, Container container)
    {
        double unitBurden = item is Stackable
            ? item.StackUnitEncumbrance ?? (item.EncumbranceVal ?? 0) / Math.Max(1, item.StackSize ?? 1)
            : item.EncumbranceVal ?? 0;

        if (container is { MerchandiseItemTypes: not null } specPack)
        {
            unitBurden *= specPack.SpecializedPackBurdenMod ?? 0.5;
        }

        if (unitBurden <= 0)
        {
            return int.MaxValue;
        }

        var available = GetEncumbranceCapacity() - (EncumbranceVal ?? 0);

        return available <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(available / unitBurden));
    }

    /// <summary>
    /// Splits amount off a stack in the bank (or one of its packs) into container, one you carry, the same way
    /// dragging part of a stack does. Returns the new stack, or null if it couldn't be done.
    /// </summary>
    private WorldObject TrySplitOutOfBank(WorldObject stack, Storage bank, Container container, int amount)
    {
        if (IsBusy || stack is not Stackable || amount <= 0 || amount >= (stack.StackSize ?? 1))
        {
            return null;
        }

        if (stack.Container is not Container stackContainer || !container.CanHoldItemType(stack))
        {
            return null;
        }

        var newStack = WorldObjectFactory.CreateNewWorldObject(stack.WeenieClassId);
        if (newStack == null)
        {
            return null;
        }

        newStack.SetStackSize(amount);
        CopyMutatedStackProperties(stack, newStack);

        var placement = container.Inventory.Values.Count(i => !i.UseBackpackSlot);

        return DoHandleActionStackableSplitToContainer(stack, stackContainer, bank, container, this, newStack, placement, amount)
            ? newStack
            : null;
    }

    /// <summary>
    /// What picking an item up out of the bank by hand does once it has moved (HandleActionPutItemInContainer).
    /// </summary>
    private void OnTakenFromBank(WorldObject item)
    {
        item.EmoteManager.OnPickup(this);
        item.NotifyOfEvent(RegenerationType.PickUp);
        item.BankAccountId = 0;
        item.SaveBiotaToDatabase();

        // it must not stay in another bank chest's copy of this account's items
        List<Storage> chests;
        lock (Storage.BankChests)
        {
            chests = Storage.BankChests.ToList();
        }

        foreach (var chest in chests)
        {
            chest.Inventory.Remove(item.Guid);
        }
    }

    /// <summary>
    /// Tidies the bank. For a full sort (null filter) partial stacks are combined first.
    /// Then items matching filter move into the packs inscribed for them, items in a pack inscribed for
    /// something else move out, and the bank and its packs are put in order (see ItemSortOrder: by category, then per-type rules, then name).
    /// A sort for one category only reorders the bank and the packs inscribed for that category.
    /// </summary>
    public BankReport SortBank(Storage bank, BankCategory? filter)
    {
        _bankBulkOperation = true;
        try
        {
            return SortBankContents(bank, filter);
        }
        finally
        {
            _bankBulkOperation = false;
        }
    }

    private BankReport SortBankContents(Storage bank, BankCategory? filter)
    {
        var report = new BankReport();
        var touched = new HashSet<Container>();

        if (filter == null)
        {
            report.StacksCombined = CombineBankStacks(bank, touched);
        }

        var spots = GetBankSpots(bank);

        foreach (var spot in spots)
        {
            foreach (var item in GetMyBankItems(spot.Container, bank))
            {
                if (!BankCategories.Matches(item, filter))
                {
                    continue;
                }

                var category = BankCategories.Tags(item);
                var currentRank = RankBankSpot(spot, item, category) ?? RankMisfiled;

                var target = FindBankSpot(spots, item, category, spot, out var bestRankIgnoringRoom);

                // Only a strictly better home is worth a move, so an item sitting in the bank or an
                // untagged pack stays put unless a pack is meant for it.
                var targetRank = target == null ? int.MaxValue : RankBankSpot(target, item, category).Value;
                if (targetRank >= currentRank)
                {
                    if (bestRankIgnoringRoom < currentRank)
                    {
                        report.NoRoom++;
                    }

                    continue;
                }

                if (!TryMoveItemInBank(item, target.Container))
                {
                    report.Failed++;
                    continue;
                }

                target.FreeSlots--;
                spot.FreeSlots++;
                touched.Add(spot.Container);
                touched.Add(target.Container);
                report.Moved++;
                report.CountMove(target.Container, bank);
            }
        }

        foreach (var spot in spots)
        {
            if (filter != null && !spot.IsMain && !BankCategories.CouldCollect(NamedPacks.Collects(spot.Container), filter.Value))
            {
                continue;
            }

            if (ReorderBankContainer(spot.Container, bank))
            {
                touched.Add(spot.Container);
                report.Reordered++;
            }
        }

        SaveBankContainers(bank, touched);

        return report;
    }

    /// <summary>
    /// How many salvage bags in the bank /bank combine would empty into others.
    /// </summary>
    public int CountBankSalvageToCombine(Storage bank)
    {
        return GetBankSalvageGroups(bank).Sum(bags => SalvagePourPlanner.CountEmptied(GetBagUnits(bags), SalvagePourPlanner.Plan(GetBagUnits(bags))));
    }

    /// <summary>
    /// The pyreals in the bank and its packs: coins, and trade notes at face value (their Value, as house payments count them).
    /// </summary>
    public (long Coins, long Notes, int NoteCount) GetBankMoney(Storage bank)
    {
        long coins = 0;
        long notes = 0;
        var noteCount = 0;

        foreach (var (item, _) in GetBankContents(bank))
        {
            if (item.WeenieType == WeenieType.Coin)
            {
                coins += item.Value ?? 0;
            }
            else if (item.IsTradeNote)
            {
                notes += item.Value ?? 0;
                noteCount += item.StackSize ?? 1;
            }
        }

        return (coins, notes, noteCount);
    }

    /// <summary>
    /// Combines salvage bags anywhere in the bank that share a material and workmanship, like /salvage combine
    /// does for the bags you carry: the fullest bags are topped up from the emptiest, and empty bags are removed.
    /// Returns how many bags were emptied.
    /// </summary>
    public int CombineBankSalvage(Storage bank)
    {
        var touched = new HashSet<Container>();
        var emptied = 0;

        foreach (var bags in GetBankSalvageGroups(bank))
        {
            var units = GetBagUnits(bags);
            var plan = SalvagePourPlanner.Plan(units);

            // Pouring that frees no bag would only nudge workmanship around.
            if (SalvagePourPlanner.CountEmptied(units, plan) == 0)
            {
                continue;
            }

            var changed = new HashSet<WorldObject>();

            foreach (var pour in plan)
            {
                var source = bags[pour.Source];
                var target = bags[pour.Target];

                if (Salvage.PourSalvageBag(source, target, pour.Amount) <= 0)
                {
                    continue;
                }

                changed.Add(source);
                changed.Add(target);
            }

            foreach (var bag in changed)
            {
                if (bag.Container is Container container)
                {
                    touched.Add(container);
                }

                if ((bag.Structure ?? 0) > 0)
                {
                    var material = (ACE.Entity.Enum.MaterialType)(bag.GetProperty(PropertyInt.MaterialType) ?? 0);
                    Salvage.RefreshSalvageBagIcon(this, bag, material, (int)(bag.Workmanship ?? 1));
                    Session.Network.EnqueueSend(new GameMessageUpdateObject(bag));
                    continue;
                }

                // Emptied: take it out of the bank (or its pack) and delete it, as a merge that empties a stack does.
                Session.Network.EnqueueSend(new GameMessageInventoryRemoveObject(bag));

                if (bank.TryRemoveFromInventory(bag.Guid, out var removed))
                {
                    removed.Destroy();
                    emptied++;
                }
            }
        }

        SaveBankContainers(bank, touched);

        if (emptied > 0)
        {
            BankActivityLog.Record(this, BankActivityLog.Combined, $"salvage: {BankReport.Plural(emptied, "bag")} poured into others");
        }

        if (emptied > 0 && PropertyManager.GetBool("banking_system_logging").Item)
        {
            _log.Information(
                "(BANKING - SALVAGE COMBINE in BANK)\n PLAYER: {@Player}\n BAGS EMPTIED: {Count}",
                new BankLogPlayer(Name, Account.AccountId),
                emptied
            );
        }

        return emptied;
    }

    /// <summary>
    /// The salvage bags in the bank, in groups that /bank combine may pour together:
    /// the same material and the same workmanship to the nearest whole number, as /salvage combine groups them.
    /// </summary>
    private List<List<WorldObject>> GetBankSalvageGroups(Storage bank)
    {
        return GetBankItems(bank)
            .Where(i => i.WeenieType == WeenieType.Salvage)
            .GroupBy(i => ((int)(i.MaterialType ?? ACE.Entity.Enum.MaterialType.Unknown), (int)Math.Round(i.Workmanship ?? 1)))
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();
    }

    private static List<(int Units, int MaxUnits)> GetBagUnits(List<WorldObject> bags)
    {
        return bags.Select(b => ((int)(b.Structure ?? 0), (int)(b.MaxStructure ?? 1000))).ToList();
    }

    /// <summary>
    /// How full the bank is for this player: its own slots used and in all, and the free slots in their packs in it.
    /// </summary>
    public (int Used, int Capacity, int PackFree) GetBankSpace(Storage bank)
    {
        var used = GetMyBankItems(bank, bank).Count;
        var packFree = GetBankPacks(bank).Sum(p => Math.Max(0, p.GetFreeInventorySlots(false)));

        return (used, bank.ItemCapacity ?? 0, packFree);
    }

    /// <summary>
    /// Every item of yours in the bank and its side packs, not counting the packs themselves.
    /// </summary>
    public List<(WorldObject Item, Container Container)> GetBankContents(Storage bank)
    {
        var contents = new List<(WorldObject, Container)>();

        foreach (var item in GetMyBankItems(bank, bank))
        {
            contents.Add((item, bank));
        }

        foreach (var pack in GetBankPacks(bank))
        {
            foreach (var item in GetMyBankItems(pack, bank))
            {
                contents.Add((item, pack));
            }
        }

        return contents;
    }

    /// <summary>
    /// Your side packs in the bank, in order.
    /// </summary>
    public List<Container> GetBankPacks(Storage bank)
    {
        return bank.Inventory.Values
            .OfType<Container>()
            .Where(p => p.BankAccountId == Account.AccountId)
            .OrderBy(p => p.PlacementPosition ?? int.MaxValue)
            .ToList();
    }

    /// <summary>
    /// The items (not packs) in the bank itself or one of its packs, in slot order.
    /// Like SendBankVaultInventory, only items in the bank itself stamped with this account count;
    /// items inside a pack belong to whoever owns the pack.
    /// </summary>
    private List<WorldObject> GetMyBankItems(Container container, Storage bank)
    {
        return container.Inventory.Values
            .Where(i => !i.UseBackpackSlot)
            .Where(i => container != bank || i.BankAccountId == Account.AccountId)
            .OrderBy(i => i.PlacementPosition ?? int.MaxValue)
            .ToList();
    }

    private List<WorldObject> GetBankItems(Storage bank)
    {
        return GetBankContents(bank).Select(c => c.Item).ToList();
    }

    private List<BankSpot> GetBankSpots(Storage bank)
    {
        var spots = new List<BankSpot>
        {
            new()
            {
                Container = bank,
                IsMain = true,
                FreeSlots = bank.GetFreeInventorySlots(false),
                Order = 0,
            },
        };

        var order = 1;
        foreach (var pack in GetBankPacks(bank))
        {
            spots.Add(
                new BankSpot
                {
                    Container = pack,
                    Tags = BankCategories.ParseInscription(pack.Inscription),
                    FreeSlots = pack.GetFreeInventorySlots(false),
                    Order = order++,
                }
            );
        }

        return spots;
    }

    /// <summary>
    /// How good a home spot is for item, lower is better, or null if the item must not go there.
    /// 0: a named pack meant for it (Salvage Crate, Quiver, Component Pouch, Trophy Pack), as /sort fills them first.
    /// 1-8: a pack inscribed for it, the better its PackFit the lower (its types before its category, its tier before
    /// no tier, a pack for just that before a pack that also takes other things).
    /// 9: an untagged specialized pack that takes it.
    /// 10 (neutral): the bank itself, or an untagged pack.
    /// null: a pack inscribed for other things, or a specialized pack that does not take this type,
    /// or a neutral place that takes only its own (BankSpot.OnlyItsOwn).
    /// For anything else a named pack is ranked by its inscription, or as any untagged pack.
    /// The same ranks pick the pack you carry that /bank withdraw puts an item in, your main pack standing in for the bank.
    /// </summary>
    private static int? RankBankSpot(BankSpot spot, WorldObject item, BankCategory tags)
    {
        var rank = RankSpotFor(spot, item, tags);

        return spot.OnlyItsOwn && rank >= RankNeutral ? null : rank;
    }

    private static int? RankSpotFor(BankSpot spot, WorldObject item, BankCategory tags)
    {
        if (!spot.Container.CanHoldItemType(item))
        {
            return null;
        }

        if (spot.IsMain)
        {
            return RankNeutral;
        }

        if (NamedPacks.IsHomeFor(spot.Container, item))
        {
            return RankNamedPack;
        }

        if (spot.Tags.Categories != BankCategory.None)
        {
            var fit = BankCategories.PackFit(spot.Tags.Categories, tags);
            return fit == 0 ? null : RankSpecialized + 1 - fit;
        }

        return (spot.Container.MerchandiseItemTypes ?? 0) != 0 ? RankSpecialized : RankNeutral;
    }

    /// <summary>
    /// The best place with a free slot for item, not counting exclude.
    /// Neutral places are tried bank first, then untagged packs in order.
    /// bestRankIgnoringRoom: the rank of the best place whether or not it has room, int.MaxValue if none.
    /// </summary>
    private static BankSpot FindBankSpot(
        List<BankSpot> spots,
        WorldObject item,
        BankCategory category,
        BankSpot exclude,
        out int bestRankIgnoringRoom
    )
    {
        BankSpot best = null;
        var bestRank = int.MaxValue;
        bestRankIgnoringRoom = int.MaxValue;

        foreach (var spot in spots)
        {
            if (spot == exclude)
            {
                continue;
            }

            var rank = RankBankSpot(spot, item, category);
            if (rank == null)
            {
                continue;
            }

            bestRankIgnoringRoom = Math.Min(bestRankIgnoringRoom, rank.Value);

            if (spot.FreeSlots <= 0)
            {
                continue;
            }

            // spots are in bank-then-pack order, so on a tie the earlier one wins
            if (rank.Value < bestRank)
            {
                best = spot;
                bestRank = rank.Value;
            }
        }

        return best;
    }

    /// <summary>
    /// Where a deposit takes items from: the main pack and every pack not inscribed "keep",
    /// or with fromDepositPacksOnly, just the packs inscribed "deposit".
    /// </summary>
    private List<Container> GetDepositSources(bool fromDepositPacksOnly)
    {
        var packs = Inventory.Values.OfType<Container>().OrderBy(p => p.PlacementPosition ?? int.MaxValue);

        if (fromDepositPacksOnly)
        {
            return packs.Where(p => BankCategories.ParseInscription(p.Inscription).Deposit).ToList();
        }

        var sources = new List<Container> { this };
        sources.AddRange(packs.Where(p => !BankCategories.ParseInscription(p.Inscription).Keep));
        return sources;
    }

    private List<WorldObject> GetDepositCandidates(List<Container> sources, BankCategory? filter, BankReport report)
    {
        var candidates = new List<WorldObject>();

        foreach (var source in sources)
        {
            var items = source.Inventory.Values
                .Where(i => !i.UseBackpackSlot)
                .OrderBy(i => i.PlacementPosition ?? int.MaxValue)
                .ToList();

            foreach (var item in items)
            {
                if (!BankCategories.Matches(item, filter))
                {
                    continue;
                }

                if (item.IsAttunedOrContainsAttuned)
                {
                    report.Attuned++;
                    continue;
                }

                // The same things HandleActionPutItemInContainer_Verify turns down, checked here so a bulk deposit
                // quietly leaves them behind instead of sending an error for each one.
                if (item.Stuck || !item.Guid.IsDynamic())
                {
                    continue;
                }

                if (IsTrading && item.IsBeingTradedOrContainsItemBeingTraded(ItemsInTradeWindow))
                {
                    continue;
                }

                if (item is PetDevice { Pet: not null })
                {
                    continue;
                }

                candidates.Add(item);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Merges item into stacks already in the bank that have room. Returns true if all of it went.
    /// mergedAny: some of it went.
    /// </summary>
    private bool TopUpBankStacks(
        WorldObject item,
        List<WorldObject> bankStacks,
        HashSet<Container> touched,
        out bool mergedAny
    )
    {
        mergedAny = false;

        if (item is not Stackable)
        {
            return false;
        }

        foreach (var target in bankStacks.ToList())
        {
            if (!CanStackTogether(item, target))
            {
                continue;
            }

            var room = (target.MaxStackSize ?? 1) - (target.StackSize ?? 1);
            if (room <= 0)
            {
                bankStacks.Remove(target);
                continue;
            }

            var amount = Math.Min(room, item.StackSize ?? 1);
            var mergesAll = amount == (item.StackSize ?? 1);
            var targetContainer = target.Container as Container;

            if (!DoHandleActionStackableMerge(item, target, amount))
            {
                return false;
            }

            mergedAny = true;

            if (targetContainer != null)
            {
                touched.Add(targetContainer);
            }

            if (mergesAll)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Combines partial stacks of the same thing anywhere in the bank, largest stacks absorbing the smallest,
    /// the same way /sort does for the player's own packs. Returns how many stacks were emptied.
    /// </summary>
    private int CombineBankStacks(Storage bank, HashSet<Container> touched)
    {
        var combined = 0;

        var groups = GetBankItems(bank)
            .Where(i => i is Stackable && (i.MaxStackSize ?? 1) > 1)
            .GroupBy(i => (i.WeenieClassId, i.TrophyQuality, i.SpellDID, i.Spell2, i.BoostValue))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var members = group.OrderByDescending(i => i.StackSize ?? 1).ToList();

            var targetIdx = 0;
            var sourceIdx = members.Count - 1;

            while (targetIdx < sourceIdx)
            {
                var target = members[targetIdx];
                var source = members[sourceIdx];

                var room = (target.MaxStackSize ?? 1) - (target.StackSize ?? 1);
                if (room <= 0)
                {
                    targetIdx++;
                    continue;
                }

                var amount = Math.Min(room, source.StackSize ?? 1);
                var mergesAll = amount == (source.StackSize ?? 1);
                var sourceContainer = source.Container as Container;
                var targetContainer = target.Container as Container;

                if (!DoHandleActionStackableMerge(source, target, amount))
                {
                    break;
                }

                if (sourceContainer != null)
                {
                    touched.Add(sourceContainer);
                }

                if (targetContainer != null)
                {
                    touched.Add(targetContainer);
                }

                if (mergesAll)
                {
                    combined++;
                    sourceIdx--;
                }
            }
        }

        return combined;
    }

    /// <summary>
    /// The same test HandleActionStackableMerge uses to decide whether two stacks may merge.
    /// </summary>
    private static bool CanStackTogether(WorldObject a, WorldObject b)
    {
        return a is Stackable
            && b is Stackable
            && a.Guid != b.Guid
            && a.WeenieClassId == b.WeenieClassId
            && a.TrophyQuality == b.TrophyQuality
            && a.SpellDID == b.SpellDID
            && a.Spell2 == b.Spell2
            && a.BoostValue == b.BoostValue;
    }

    /// <summary>
    /// Moves item into target (the bank or one of its packs), added at the end.
    /// The item can be in the player's packs or already in the bank.
    /// </summary>
    private bool TryMoveItemInBank(WorldObject item, Container target)
    {
        // While busy, _Verify would queue the move as the next pickup instead of doing it now.
        if (IsBusy)
        {
            return false;
        }

        var placement = target.Inventory.Values.Count(i => !i.UseBackpackSlot);

        if (
            !HandleActionPutItemInContainer_Verify(
                item.Guid.Full,
                target.Guid.Full,
                placement,
                out var itemRootOwner,
                out var foundItem,
                out var containerRootOwner,
                out var container,
                out var itemWasEquipped
            )
        )
        {
            return false;
        }

        if (foundItem != item || container != target || itemWasEquipped)
        {
            return false;
        }

        if (!DoHandleActionPutItemInContainer(item, itemRootOwner, false, container, containerRootOwner, placement))
        {
            return false;
        }

        if (itemRootOwner == this)
        {
            item.EmoteManager.OnDrop(this);
        }

        return true;
    }

    /// <summary>
    /// Puts a bank container's items in /bank sort order (ItemSortOrder). Returns false if they already were.
    /// The client is told each item's new slot, the same way /sort reorders a salvage crate.
    /// </summary>
    private bool ReorderBankContainer(Container container, Storage bank)
    {
        var sorted = GetMyBankItems(container, bank)
            .OrderBy(i => i, Comparer<WorldObject>.Create(ItemSortOrder.Compare))
            .ToList();

        var inOrder = true;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].PlacementPosition != i)
            {
                inOrder = false;
                break;
            }
        }

        if (inOrder)
        {
            return false;
        }

        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].PlacementPosition = i;
            Session.Network.EnqueueSend(new GameEventItemServerSaysContainId(Session, sorted[i], container));
        }

        return true;
    }

    /// <summary>
    /// Saves what changed in the bank containers a bulk operation touched. Moves into the bank itself are
    /// saved as they happen; this catches stack sizes and slots changed inside the bank's side packs.
    /// </summary>
    private void SaveBankContainers(Storage bank, HashSet<Container> touched)
    {
        foreach (var container in touched)
        {
            if (container == bank || container.Container == bank)
            {
                DeepSave(container);
            }
        }

        RefreshBankViews(bank, touched);
    }

    /// <summary>
    /// Re-sends the contents lists of the bank and its packs after a bulk operation touched any of them, as opening the bank does.
    /// The client moves an item it dragged itself, but when the server moves one, the bank window adds it to
    /// its new container and keeps showing it in the old one until the bank is reopened. Re-sending a list
    /// replaces it (picking up a pack from the bank already re-sends that pack's list), which clears the copies.
    /// </summary>
    private void RefreshBankViews(Storage bank, HashSet<Container> touched)
    {
        if (touched.Count == 0)
        {
            return;
        }

        // The bank's own list holds its packs, and items move in and out of it, so it is always re-sent.
        Session.Network.EnqueueSend(new GameEventViewContents(Session, bank));

        // The bank window seems to place a pack by when its list arrives: re-sending only the touched packs, in no set
        // order, shuffled the packs. So every pack's list is re-sent, in slot order, as Storage.SendBankVaultInventory does.
        foreach (var pack in GetBankPacks(bank))
        {
            Session.Network.EnqueueSend(new GameEventViewContents(Session, pack));
        }
    }
}
