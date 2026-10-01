using System;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public partial class Chest : Container, Lock
{
    /// <summary>
    /// This is used for things like Mana Forge Chests
    /// </summary>
    public bool ChestRegenOnClose
    {
        get
        {
            if (ChestResetInterval <= 5)
            {
                return true;
            }

            return GetProperty(PropertyBool.ChestRegenOnClose) ?? false;
        }
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.ChestRegenOnClose);
            }
            else
            {
                SetProperty(PropertyBool.ChestRegenOnClose, value);
            }
        }
    }

    /// <summary>
    /// This is used for things like Dirty Old Crate
    /// </summary>
    public bool ChestClearedWhenClosed
    {
        get => GetProperty(PropertyBool.ChestClearedWhenClosed) ?? false;
        set
        {
            if (!value)
            {
                RemoveProperty(PropertyBool.ChestClearedWhenClosed);
            }
            else
            {
                SetProperty(PropertyBool.ChestClearedWhenClosed, value);
            }
        }
    }

    /// <summary>
    /// This is the default setup for resetting chests
    /// </summary>
    public double ChestResetInterval
    {
        get
        {
            var chestResetInterval = ResetInterval ?? Default_ChestResetInterval;

            if (chestResetInterval < 15)
            {
                chestResetInterval = Default_ChestResetInterval;
            }

            return chestResetInterval;
        }
    }

    public virtual double Default_ChestResetInterval => 120;

    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public Chest(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public Chest(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues()
    {
        ContainerCapacity = ContainerCapacity ?? 10;
        ItemCapacity = ItemCapacity ?? 120;

        ActivationResponse |= ActivationResponse.Use; // todo: fix broken data

        CurrentMotionState = motionClosed; // do any chests default to open?

        if (IsLocked)
        {
            DefaultLocked = true;
        }

        if (DefaultLocked) // ignore regen interval, only regen on relock
        {
            NextGeneratorRegenerationTime = double.MaxValue;
        }
    }

    protected static readonly Motion motionOpen = new Motion(MotionStance.NonCombat, MotionCommand.On);
    protected static readonly Motion motionClosed = new Motion(MotionStance.NonCombat, MotionCommand.Off);

    public override ActivationResult CheckUseRequirements(WorldObject activator)
    {
        var baseRequirements = base.CheckUseRequirements(activator);
        if (!baseRequirements.Success)
        {
            return baseRequirements;
        }

        if (!(activator is Player player))
        {
            return new ActivationResult(false);
        }

        if (IsLocked)
        {
            if (PropertyManager.GetBool("fix_chest_missing_inventory_window").Item)
            {
                player.SendTransientError($"The {Name} is locked");
            }

            EnqueueBroadcast(new GameMessageSound(Guid, Sound.OpenFailDueToLock, 1.0f));
            return new ActivationResult(false);
        }

        if (UseLockTimestamp != null && activator.Guid.Full != LastUnlocker)
        {
            var currentTime = Time.GetUnixTime();

            // prevent ninja looting
            if (UseLockTimestamp.Value + PropertyManager.GetDouble("unlocker_window").Item > currentTime)
            {
                player.SendTransientError(InUseMessage);
                return new ActivationResult(false);
            }
        }

        if (IsOpen)
        {
            if (Viewer == player.Guid.Full)
            {
                // current player has this chest open, close it
                Close(player);
            }
            else
            {
                // another player has this chest open -- ensure they are within range
                var currentViewer = CurrentLandblock.GetObject(Viewer) as Player;

                if (currentViewer == null)
                {
                    Close(null); // current viewer not found, close it
                }
                else
                {
                    player.SendTransientError(InUseMessage);
                }
            }

            return new ActivationResult(false);
        }

        // handle a hard prerequisite gate, same semantics as Portal.cs's QuestRestriction check:
        // the player must currently HOLD the quest and be unable to solve it again right now.
        // With no `quest`-table row for the name (the norm for content-tracked quests), CanSolve
        // is always false once a record exists, so this collapses to a clean "must have this
        // quest stamped at all" check - unlike Chest's own Quest/CanSolve path below, a player who
        // has never touched the quest name is rejected outright rather than let through once for
        // free. Content can revoke access by EraseQuest-ing the same name (HasQuest flips back to
        // false), which this immediately honors - unlike Quest/CanSolve, nothing here auto-stamps
        // anything, so pairing it with content-level Stamp/Erase never fights itself.
        //
        // Unlike Portal.cs's read-only use of the same property, Chest consumes it immediately
        // here rather than leaving that to a content-authored Use emote action: EmoteManager's
        // per-object busy gate (ExecuteEmoteSet's `IsBusy && !nested` check) silently drops a
        // queued Use emote set - including any EraseQuest action in it - on a rapid re-click,
        // while ActOnUse()/Open() below is not gated by that at all and still runs every time.
        // A content-authored erase can therefore race a fast double-click: the erase from click 1
        // gets dropped, click 2 passes this same check again, and the chest re-opens with fresh
        // loot from a single grant. Erasing synchronously in this check, before ActOnUse ever
        // runs, closes that race - each activation independently consumes its own grant, with no
        // dependency on the emote queue.
        if (QuestRestriction != null)
        {
            var hasQuest = player.QuestManager.HasQuest(QuestRestriction);
            var canSolve = player.QuestManager.CanSolve(QuestRestriction);

            if (!(hasQuest && !canSolve))
            {
                player.SendTransientError($"You cannot open the {Name}.");
                return new ActivationResult(false);
            }

            player.QuestManager.Erase(QuestRestriction);
        }

        // handle quest requirements
        if (Quest != null)
        {
            if (!player.QuestManager.HasQuest(Quest))
            {
                EmoteManager.OnQuest(player);
            }
            else
            {
                if (player.QuestManager.CanSolve(Quest))
                {
                    EmoteManager.OnQuest(player);
                }
                else
                {
                    player.QuestManager.HandleSolveError(Quest);
                    return new ActivationResult(false);
                }
            }
        }

        return new ActivationResult(true);
    }

    /// <summary>
    /// This is raised by Player.HandleActionUseItem.<para />
    /// The item does not exist in the players possession.<para />
    /// If the item was outside of range, the player will have been commanded to move using DoMoveTo before ActOnUse is called.<para />
    /// When this is called, it should be assumed that the player is within range.
    /// </summary>
    public override void ActOnUse(WorldObject wo)
    {
        if (!(wo is Player player))
        {
            return;
        }

        if (IsPlayerTierChest && player.IsShrouded())
        {
            var baseTier = Tier ?? 1;

            Tier = player.GetPlayerTier(player.Level ?? 1);

            Reset(ResetTimestamp, player);

            Open(player);

            Tier = baseTier;
        }
        else
        {
            // open chest
            Open(player);
        }
    }

    public override void Open(Player player)
    {
        base.Open(player);

        if (!ResetMessagePending && !double.IsPositiveInfinity(ChestResetInterval))
        {
            var resetTimestamp = ResetTimestamp;

            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(ChestResetInterval);
            actionChain.AddAction(this, () => Reset(resetTimestamp));
            actionChain.EnqueueChain();

            ResetMessagePending = true;
        }

        UseLockTimestamp = null;
    }

    public override void Close(Player player)
    {
        Close(player);
    }

    /// <summary>
    /// Called when a chest is closed, or walked away from
    /// </summary>
    public void Close(Player player, bool tryReset = true)
    {
        base.Close(player);

        if (ChestRegenOnClose && tryReset)
        {
            Reset(ResetTimestamp);
        }
    }

    public override void FinishClose(Player player)
    {
        base.FinishClose(player);

        if (ChestClearedWhenClosed && InitCreate > 0)
        {
            if (CurrentCreate == 0)
            {
                FadeOutAndDestroy(); // Chest's complete generated inventory count has been wiped out
            }
            //Destroy(); // Chest's complete generated inventory count has been wiped out
        }
    }

    /// <summary>
    /// Destroys and regenerates this chest's currently spawned treasure using whatever
    /// LootQualityMod is set at the moment of the call. Unlike Reset(), this does not
    /// close the chest or reapply DefaultLocked - callers use this to temporarily bias
    /// loot quality (e.g. the Specialized Thievery lockpick bonus, a key carrying its
    /// own LootQualityMod) right as the chest is being unlocked, before the player has
    /// had a chance to open it - a full Reset() would immediately relock a
    /// DefaultLocked chest out from under them.
    /// </summary>
    public void RegenerateLoot(Player player = null)
    {
        if (!IsGenerator || InitCreate <= 0)
        {
            return;
        }

        ResetGenerator();
        CurrentlyPoweringUp = true;
        Generator_Generate(Tier, player);
    }

    public void Reset(double? resetTimestamp, Player playerOpener = null)
    {
        if (resetTimestamp != ResetTimestamp)
        {
            return; // already cleared by previous reset
        }

        // TODO: if 'ResetInterval' style, do we want to ensure a minimum amount of time for the last viewer?

        // should only be an edge case with reload-landblock
        if (CurrentLandblock == null)
        {
            return;
        }

        var player = CurrentLandblock.GetObject(Viewer) as Player;

        if (IsOpen)
        {
            Close(player, false);
        }

        if (DefaultLocked && !IsLocked)
        {
            IsLocked = true;
            if (!PropertyManager.GetBool("fix_chest_missing_inventory_window").Item)
            {
                EnqueueBroadcast(new GameMessagePublicUpdatePropertyBool(this, PropertyBool.Locked, IsLocked));
            }
        }

        ClearUnmanagedInventory();

        if (IsGenerator)
        {
            ResetGenerator();
            CurrentlyPoweringUp = true;
            if (InitCreate > 0)
            {
                Generator_Generate(Tier, player ?? playerOpener);

            }
        }

        ResetTimestamp = Time.GetUnixTime();
        ResetMessagePending = false;
    }

    protected override float DoOnOpenMotionChanges()
    {
        if (MotionTableId != 0)
        {
            return ExecuteMotion(motionOpen);
        }
        else
        {
            return 0;
        }
    }

    protected override float DoOnCloseMotionChanges()
    {
        if (MotionTableId != 0)
        {
            return ExecuteMotion(motionClosed);
        }
        else
        {
            return 0;
        }
    }

    public string LockCode
    {
        get => GetProperty(PropertyString.LockCode);
        set
        {
            if (value == null)
            {
                RemoveProperty(PropertyString.LockCode);
            }
            else
            {
                SetProperty(PropertyString.LockCode, value);
            }
        }
    }

    /// <summary>
    /// Used for unlocking a chest via lockpick, so contains a skill check
    /// player.Skills[Skill.Lockpick].Current should be sent for the skill check
    /// </summary>
    public UnlockResults Unlock(uint unlockerGuid, uint playerLockpickSkillLvl, ref int difficulty)
    {
        var result = LockHelper.Unlock(this, playerLockpickSkillLvl, ref difficulty);

        if (result == UnlockResults.UnlockSuccess)
        {
            LastUnlocker = unlockerGuid;
            UseLockTimestamp = Time.GetUnixTime();
        }
        return result;
    }

    /// <summary>
    /// Used for unlocking a chest via a key
    /// </summary>
    public UnlockResults Unlock(uint unlockerGuid, Key key, string keyCode = null)
    {
        var result = LockHelper.Unlock(this, key, keyCode);

        if (result == UnlockResults.UnlockSuccess)
        {
            LastUnlocker = unlockerGuid;
            UseLockTimestamp = Time.GetUnixTime();
        }
        return result;
    }
}
