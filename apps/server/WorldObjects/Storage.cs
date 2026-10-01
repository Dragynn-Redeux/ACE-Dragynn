using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Managers;
using Biota = ACE.Entity.Models.Biota;

namespace ACE.Server.WorldObjects;

public class Storage : Container
{
    public static readonly List<Storage> BankChests = [];

    // Per chest: two players can have two different bank chests open at the same time.
    private Player _bankUser;

    /// <summary>
    /// True once the viewer's bank items have been loaded from the database into this chest.
    /// The load is asynchronous, so the chest is briefly open and empty after Open().
    /// </summary>
    public bool BankInventoryLoaded { get; private set; }

    public const string BankCommandsHint =
        "Type /bank to see what else I can do: deposit, withdraw and sort your items by kind, search your bank, combine salvage, "
        + "count your pyreals, and inscribe your packs to say what goes in them.";

    /// <summary>
    /// The popup the bank shows the first time it is opened (and /bank intro): the basics, and "deposit" packs, which
    /// work from the packs a player carries and which nothing else in game would tell them about.
    /// The client's popup is a fixed size and doesn't scroll: about 18 lines of 58 characters show, the rest is cut off.
    /// This is about 14; /bank has the rest.
    /// </summary>
    public const string BankIntro =
        "Welcome to your bank\n\n"
        + "It's shared by every character on your account. Type /bank for commands that deposit, withdraw, sort and search.\n\n"
        + "\"Deposit\" packs\n"
        + "Inscribe a pack you carry with \"deposit\" and use it as a drop box. "
        + "Whenever you open your bank, it offers to bank everything in that pack at once.\n\n"
        + "Inscribe packs with what they hold, like \"weapons\", and /bank files those items into them.\n\n"
        + "/bank intro shows this again.";

    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public Storage(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public Storage(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    public void OnDestroy()
    {
        lock (BankChests)
        {
            BankChests.Remove(this);
        }
    }

    private void SetEphemeralValues()
    {
        SetProperty(PropertyInt.ShowableOnRadar, 1);

        IsLocked = false;

        IsOpen = false;

        BumpVelocity = true;

        Translucency = 0F;

        lock (BankChests)
        {
            BankChests.Add(this);
        }
    }

    public override void Open(Player player)
    {
        player.LastOpenedContainerId = Guid;

        IsOpen = true;

        Viewer = player.Guid.Full;

        _bankUser = player;

        BankInventoryLoaded = false;

        Translucency = 1f;

        PlayParticleEffect(PlayScript.Destroy, Guid);

        player.Session.Network.EnqueueSend(
            new GameEventTell(
                this,
                "I am commanded to serve you by storing your objects.",
                player,
                ChatMessageType.Tell
            )
        );

        // Nothing else in game mentions /bank, so the bank does until the player has used it.
        if (!player.BankCommandsUsed)
        {
            player.Session.Network.EnqueueSend(new GameEventTell(this, BankCommandsHint, player, ChatMessageType.Tell));
        }

        // The first time, a popup explains the basics.
        if (!player.BankIntroSeen)
        {
            player.QuestManager.Stamp(Player.BankIntroSeenQuest);
            player.Session.Network.EnqueueSend(new GameEventPopupString(player.Session, BankIntro));
        }

        DatabaseManager.Shard.GetBankInventoryInParallel(
            Guid.Full,
            player.Account.AccountId,
            true,
            biotas =>
            {
                EnqueueAction(new ActionEventDelegate(() => SortBiotasIntoBank(biotas)));
            }
        );
    }

    private void SortBiotasIntoBank(IEnumerable<ACE.Database.Models.Shard.Biota> biotas)
    {
        var worldObjects = new List<WorldObject>();

        lock (BankChests)
        {
            worldObjects.AddRange(biotas.Select(Factories.WorldObjectFactory.CreateWorldObject));

            // check for GUID in any other banks and remove from inventory if so.
            foreach (var worldObject in worldObjects)
            {
                foreach (var bank in BankChests.ToList())
                {
                    bank.Inventory.Remove(worldObject.Guid);
                }
            }
        }

        SortWorldObjectsIntoBank(worldObjects);

        if (worldObjects.Count > 0)
        {
            _log.Error("[BANKING] Inventory detected without a container to put it into. BankAccountId: {BankId}. Number of objects affected: {Count}.", this.BankAccountId, worldObjects.Count);
        }
    }

    private void SortWorldObjectsIntoBank(List<WorldObject> worldObjects)
    {
        for (var i = worldObjects.Count - 1; i >= 0; i--)
        {
            if ((worldObjects[i].ContainerId ?? 0) == Biota.Id)
            {
                worldObjects[i].ContainerId = Biota.Id;
                worldObjects[i].OwnerId = Biota.Id;

                if (!Inventory.ContainsKey(worldObjects[i].Guid))
                {
                    Inventory[worldObjects[i].Guid] = worldObjects[i];
                }

                worldObjects[i].Container = this;

                worldObjects.RemoveAt(i);
            }
        }

        var mainPackItems = Inventory
            .Values.Where(wo => !wo.UseBackpackSlot)
            .OrderBy(wo => wo.PlacementPosition)
            .ToList();
        for (var i = 0; i < mainPackItems.Count; i++)
        {
            mainPackItems[i].PlacementPosition = i;
        }

        var sidPackItems = Inventory
            .Values.Where(wo => wo.UseBackpackSlot)
            .OrderBy(wo => wo.PlacementPosition)
            .ToList();
        for (var i = 0; i < sidPackItems.Count; i++)
        {
            sidPackItems[i].PlacementPosition = i;
        }

        var sideContainers = Inventory.Values.Where(i => i.WeenieType == WeenieType.Container).ToList();
        foreach (var container in sideContainers)
        {
            var cont = container as Container;
            cont?.SortWorldObjectsIntoInventory(worldObjects);
        }

        EncumbranceVal = 0;
        Value = 0;

        // Packs grow in the bank (BankPackExpansion). Doing it here covers packs banked before the feature,
        // and follows the settings if they change. The CreateObject messages below carry the new sizes.
        foreach (var pack in Inventory.Values.OfType<Container>())
        {
            BankPackExpansion.ApplyInBank(pack);
        }

        BankInventoryLoaded = true;

        SendBankVaultInventory(_bankUser);

        if (_bankUser != null && IsOpen && Viewer == _bankUser.Guid.Full)
        {
            _bankUser.Session.Network.EnqueueSend(new GameEventTell(this, DescribeSpace(_bankUser), _bankUser, ChatMessageType.Tell));

            // offer to empty the viewer's "deposit" packs into the bank
            _bankUser.OfferDepositPacks(this);
        }
    }

    /// <summary>
    /// What the bank says about its space when it opens, e.g. "You're using 212 of your 300 bank slots,
    /// and your packs here have room for 140 more items."
    /// </summary>
    private string DescribeSpace(Player player)
    {
        var (used, capacity, packFree) = player.GetBankSpace(this);
        var free = capacity - used;

        var text = $"You're using {used:N0} of your {capacity:N0} bank slots";

        text += player.GetBankPacks(this).Count > 0
            ? $", and your packs here have room for {packFree:N0} more {(packFree == 1 ? "item" : "items")}."
            : ".";

        if (free <= 0)
        {
            text += " My own slots are full.";
        }
        else if (free * 10 <= capacity)
        {
            text += $" Only {free:N0} {(free == 1 ? "slot is" : "slots are")} left.";
        }

        return text;
    }

    private void SendBankVaultInventory(Player player)
    {
        if (player is null)
        {
            _log.Error("SendBankVaultInventory(Player) - Player is null");
            return;
        }

        if (Inventory is null)
        {
            _log.Error("SendBankVaultInventory(Player) - Inventory is null");
            return;
        }

        // send createobject for all objects in this container's inventory to player
        var itemsToSend = new List<GameMessage>();

        foreach (var item in Inventory.Values.Where(i => i.BankAccountId == player.Account.AccountId))
        {
            if (item.BankAccountId is null)
            {
                _log.Error("SendBankVaultInventory(Player {Player}) - {Item}.BankAccountId is null", player.Name, item.Name);
                continue;
            }

            itemsToSend.Add(new GameMessageCreateObject(item));

            if (item is Container container)
            {
                foreach (var containerItem in container.Inventory.Values)
                {
                    itemsToSend.Add(new GameMessageCreateObject(containerItem));
                }
            }
        }

        player.Session.Network.EnqueueSend(new GameEventViewContents(player.Session, this));

        // send sub-containers, in slot order: the bank window seems to place a pack by when its list arrives
        foreach (var container in Inventory.Values.OfType<Container>().OrderBy(c => c.PlacementPosition ?? int.MaxValue))
        {
            player.Session.Network.EnqueueSend(new GameEventViewContents(player.Session, container));
        }

        player.Session.Network.EnqueueSend(itemsToSend.ToArray());
    }

    public override void Close(Player player)
    {
        if (!IsOpen)
        {
            return;
        }

        Translucency = 0f;

        SaveBiotaToDatabase(false);

        _bankUser?.Session.Network.EnqueueSend(
            new GameEventTell(this, "Please return with more items.", _bankUser, ChatMessageType.Tell)
        );

        PlayParticleEffect(PlayScript.UnHide, Guid);

        FinishClose(_bankUser);

        AccountWealthTracker.Update(_bankUser);

        // var itemsToSend = new List<GameMessage>();
        //
        // foreach (var item in Inventory.Values)
        // {
        //     itemsToSend.Add(new GameMessageDeleteObject(item));
        // }
        //
        // player.Session.Network.EnqueueSend(itemsToSend.ToArray());

        _bankUser = null;

        var landblock = CurrentLandblock;

        if (landblock is null)
        {
            _log.Information("[BANKING] Storage.Close({Player}) - landblock is null", player.Name);
        }

        landblock?.ReloadObject(this);
    }

    /// <summary>
    /// This event is raised when player adds item to storage
    /// </summary>
    protected override void OnAddItem()
    {
        if (Inventory.Count > 0)
        {
            SaveBiotaToDatabase(false);
        }
    }

    /// <summary>
    /// This event is raised when player removes item from storage
    /// </summary>
    protected override void OnRemoveItem(WorldObject removedItem)
    {
        SaveBiotaToDatabase(false);
    }
}
