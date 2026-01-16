using System;
using System.Collections.Generic;
using ACE.Common;
using ACE.Database;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

partial class Player
{
    private static readonly Position MarketplaceDrop =
        DatabaseManager.World.GetCachedWeenie("portalmarketplace")?.GetPosition(PositionType.Destination)
        ?? new Position(0x016C01BC, 49.206f, -31.935f, 0.005f, 0, 0, -0.707107f, 0.707107f);

    /// <summary>
    /// Teleports the player to position
    /// </summary>
    /// <param name="positionType">PositionType to be teleported to</param>
    /// <returns>true on success (position is set) false otherwise</returns>
    public bool TeleToPosition(PositionType positionType)
    {
        var position = GetPosition(positionType);

        if (position != null)
        {
            var teleportDest = new Position(position);
            AdjustDungeon(teleportDest);

            Teleport(teleportDest);
            return true;
        }

        return false;
    }

    private static readonly Motion motionLifestoneRecall = new Motion(
        MotionStance.NonCombat,
        MotionCommand.LifestoneRecall
    );

    private static readonly Motion motionHouseRecall = new Motion(MotionStance.NonCombat, MotionCommand.HouseRecall);

    public static float RecallMoveThreshold = 8.0f;
    public static float RecallMoveThresholdSq = RecallMoveThreshold * RecallMoveThreshold;

    public bool TooBusyToRecall
    {
        get => IsBusy || suicideInProgress; // recalls could be started from portal space?
    }

    public void HandleActionTeleToHouse()
    {
        if (IsOlthoiPlayer)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.OlthoiCanOnlyRecallToLifestone));
            return;
        }

        if (PKTimerActive)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (RecallsDisabled)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ExitTrainingAcademyToUseCommand));
            return;
        }

        if (TooBusyToRecall)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YoureTooBusy));
            return;
        }

        var house = House ?? GetAccountHouse();

        if (house == null)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouMustOwnHouseToUseCommand));
            return;
        }

        if (CombatMode != CombatMode.NonCombat)
        {
            // this should be handled by a different thing, probably a function that forces player into peacemode
            var updateCombatMode = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.CombatMode,
                (int)CombatMode.NonCombat
            );
            SetCombatMode(CombatMode.NonCombat);
            Session.Network.EnqueueSend(updateCombatMode);
        }

        EnqueueBroadcast(
            new GameMessageSystemChat($"{Name} is recalling home.", ChatMessageType.Recall),
            LocalBroadcastRange,
            ChatMessageType.Recall
        );

        SendMotionAsCommands(MotionCommand.HouseRecall, MotionStance.NonCombat);

        var startPos = new Position(Location);

        // Wait for animation
        var actionChain = new ActionChain();

        // Then do teleport
        var animLength = DatManager
            .PortalDat.ReadFromDat<MotionTable>(MotionTableId)
            .GetAnimationLength(MotionCommand.HouseRecall);
        actionChain.AddDelaySeconds(animLength);
        IsBusy = true;
        actionChain.AddAction(
            this,
            () =>
            {
                IsBusy = false;
                var endPos = new Position(Location);
                if (startPos.SquaredDistanceTo(endPos) > RecallMoveThresholdSq)
                {
                    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouHaveMovedTooFar));
                    return;
                }
                Teleport(house.SlumLord.Location);
            }
        );

        actionChain.EnqueueChain();
    }

    /// <summary>
    /// Handles teleporting a player to the lifestone (/ls or /lifestone command)
    /// </summary>
    public void HandleActionTeleToLifestone()
    {
        if (PKTimerActive)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (RecallsDisabled)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ExitTrainingAcademyToUseCommand));
            return;
        }

        if (TooBusyToRecall)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YoureTooBusy));
            return;
        }

        if (Sanctuary == null)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "Your spirit has not been attuned to a sanctuary location.",
                    ChatMessageType.Broadcast
                )
            );
            return;
        }

        // FIXME(ddevec): I should probably make a better interface for this
        UpdateVital(Mana, Mana.Current / 2);

        if (CombatMode != CombatMode.NonCombat)
        {
            // this should be handled by a different thing, probably a function that forces player into peacemode
            var updateCombatMode = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.CombatMode,
                (int)CombatMode.NonCombat
            );
            SetCombatMode(CombatMode.NonCombat);
            Session.Network.EnqueueSend(updateCombatMode);
        }

        EnqueueBroadcast(
            new GameMessageSystemChat($"{Name} is recalling to the lifestone.", ChatMessageType.Recall),
            LocalBroadcastRange,
            ChatMessageType.Recall
        );

        SendMotionAsCommands(MotionCommand.LifestoneRecall, MotionStance.NonCombat);

        var startPos = new Position(Location);

        // Wait for animation
        var lifestoneChain = new ActionChain();

        // Then do teleport
        IsBusy = true;
        lifestoneChain.AddDelaySeconds(
            DatManager
                .PortalDat.ReadFromDat<MotionTable>(MotionTableId)
                .GetAnimationLength(MotionCommand.LifestoneRecall)
        );
        lifestoneChain.AddAction(
            this,
            () =>
            {
                IsBusy = false;
                var endPos = new Position(Location);
                if (startPos.SquaredDistanceTo(endPos) > RecallMoveThresholdSq)
                {
                    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouHaveMovedTooFar));
                    return;
                }

                Teleport(Sanctuary);
            }
        );

        lifestoneChain.EnqueueChain();
    }

    private static readonly Motion motionMarketplaceRecall = new Motion(
        MotionStance.NonCombat,
        MotionCommand.MarketplaceRecall
    );

    public void HandleActionTeleToMarketPlace() { }

    private static readonly Motion motionAllegianceHometownRecall = new Motion(
        MotionStance.NonCombat,
        MotionCommand.AllegianceHometownRecall
    );

    public void HandleActionRecallAllegianceHometown()
    {
        //Console.WriteLine($"{Name}.HandleActionRecallAllegianceHometown()");

        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The /ah command is not available on this server.",
                ChatMessageType.Broadcast
            )
        );

        return;

        if (IsOlthoiPlayer)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.OlthoiCanOnlyRecallToLifestone));
            return;
        }

        if (PKTimerActive)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (RecallsDisabled)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ExitTrainingAcademyToUseCommand));
            return;
        }

        if (TooBusyToRecall)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YoureTooBusy));
            return;
        }

        // check if player is in an allegiance
        if (!VerifyRecallAllegianceHometown())
        {
            return;
        }

        if (CombatMode != CombatMode.NonCombat)
        {
            // this should be handled by a different thing, probably a function that forces player into peacemode
            var updateCombatMode = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.CombatMode,
                (int)CombatMode.NonCombat
            );
            SetCombatMode(CombatMode.NonCombat);
            Session.Network.EnqueueSend(updateCombatMode);
        }

        EnqueueBroadcast(
            new GameMessageSystemChat($"{Name} is going to the Allegiance hometown.", ChatMessageType.Recall),
            LocalBroadcastRange,
            ChatMessageType.Recall
        );

        SendMotionAsCommands(MotionCommand.AllegianceHometownRecall, MotionStance.NonCombat);

        var startPos = new Position(Location);

        // Wait for animation
        var actionChain = new ActionChain();

        // Then do teleport
        IsBusy = true;
        var animLength = DatManager
            .PortalDat.ReadFromDat<MotionTable>(MotionTableId)
            .GetAnimationLength(MotionCommand.AllegianceHometownRecall);
        actionChain.AddDelaySeconds(animLength);
        actionChain.AddAction(
            this,
            () =>
            {
                IsBusy = false;
                var endPos = new Position(Location);
                if (startPos.SquaredDistanceTo(endPos) > RecallMoveThresholdSq)
                {
                    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouHaveMovedTooFar));
                    return;
                }

                // re-verify
                if (!VerifyRecallAllegianceHometown())
                {
                    return;
                }

                Teleport(Allegiance.Sanctuary);
            }
        );

        actionChain.EnqueueChain();
    }

    private bool VerifyRecallAllegianceHometown()
    {
        if (Allegiance == null)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouAreNotInAllegiance));
            return false;
        }

        if (Allegiance.Sanctuary == null)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YourAllegianceDoesNotHaveHometown)
            );
            return false;
        }

        return true;
    }

    /// <summary>
    /// Recalls you to your allegiance's Mansion or Villa
    /// </summary>
    public void HandleActionTeleToMansion()
    {
        //Console.WriteLine($"{Name}.HandleActionTeleToMansion()");

        if (IsOlthoiPlayer)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.OlthoiCanOnlyRecallToLifestone));
            return;
        }

        if (PKTimerActive)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (RecallsDisabled)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ExitTrainingAcademyToUseCommand));
            return;
        }

        if (TooBusyToRecall)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YoureTooBusy));
            return;
        }

        var allegianceHouse = VerifyTeleToMansion();

        if (allegianceHouse == null)
        {
            return;
        }

        if (CombatMode != CombatMode.NonCombat)
        {
            // this should be handled by a different thing, probably a function that forces player into peacemode
            var updateCombatMode = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.CombatMode,
                (int)CombatMode.NonCombat
            );
            SetCombatMode(CombatMode.NonCombat);
            Session.Network.EnqueueSend(updateCombatMode);
        }

        EnqueueBroadcast(
            new GameMessageSystemChat($"{Name} is recalling to the Allegiance housing.", ChatMessageType.Recall),
            LocalBroadcastRange,
            ChatMessageType.Recall
        );

        SendMotionAsCommands(MotionCommand.HouseRecall, MotionStance.NonCombat);

        var startPos = new Position(Location);

        // Wait for animation
        var actionChain = new ActionChain();

        // Then do teleport
        var animLength = DatManager
            .PortalDat.ReadFromDat<MotionTable>(MotionTableId)
            .GetAnimationLength(MotionCommand.HouseRecall);
        actionChain.AddDelaySeconds(animLength);

        IsBusy = true;
        actionChain.AddAction(
            this,
            () =>
            {
                IsBusy = false;
                var endPos = new Position(Location);
                if (startPos.SquaredDistanceTo(endPos) > RecallMoveThresholdSq)
                {
                    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouHaveMovedTooFar));
                    return;
                }

                // re-verify
                allegianceHouse = VerifyTeleToMansion();

                if (allegianceHouse == null)
                {
                    return;
                }

                Teleport(allegianceHouse.SlumLord.Location);
            }
        );

        actionChain.EnqueueChain();
    }

    private House VerifyTeleToMansion()
    {
        // check if player is in an allegiance
        if (Allegiance == null)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouAreNotInAllegiance));
            return null;
        }

        var allegianceHouse = Allegiance.GetHouse();

        if (allegianceHouse == null)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YourMonarchDoesNotOwnAMansionOrVilla)
            );
            return null;
        }

        if (allegianceHouse.HouseType < HouseType.Villa)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YourMonarchsHouseIsNotAMansionOrVilla)
            );
            return null;
        }

        // ensure allegiance housing has allegiance permissions enabled
        if (allegianceHouse.MonarchId == null)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YourMonarchHasClosedTheMansion));
            return null;
        }

        return allegianceHouse;
    }

    private static readonly Motion motionPkArenaRecall = new Motion(
        MotionStance.NonCombat,
        MotionCommand.PKArenaRecall
    );

    private static List<Position> pkArenaLocs = new List<Position>()
    {
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpkarenanew1")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00660117, 30, -50, 0.005f, 0, 0, 0.000000f, 1.000000f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpkarenanew2")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00660106, 10, 0, 0.005f, 0, 0, -0.947071f, 0.321023f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpkarenanew3")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00660103, 30, -30, 0.005f, 0, 0, -0.699713f, 0.714424f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpkarenanew4")?.GetPosition(PositionType.Destination)
                ?? new Position(0x0066011E, 50, 0, 0.005f, 0, 0, -0.961021f, -0.276474f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpkarenanew5")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00660127, 60, -30, 0.005f, 0, 0, 0.681639f, 0.731689f)
        ),
    };

    public void HandleActionTeleToPkArena()
    {
        //Console.WriteLine($"{Name}.HandleActionTeleToPkArena()");

        if (PlayerKillerStatus != PlayerKillerStatus.PK)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.OnlyPKsMayUseCommand));
            return;
        }

        if (PKTimerActive)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (RecallsDisabled)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ExitTrainingAcademyToUseCommand));
            return;
        }

        if (TooBusyToRecall)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YoureTooBusy));
            return;
        }

        if (CombatMode != CombatMode.NonCombat)
        {
            // this should be handled by a different thing, probably a function that forces player into peacemode
            var updateCombatMode = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.CombatMode,
                (int)CombatMode.NonCombat
            );
            SetCombatMode(CombatMode.NonCombat);
            Session.Network.EnqueueSend(updateCombatMode);
        }

        EnqueueBroadcast(
            new GameMessageSystemChat($"{Name} is going to the PK Arena.", ChatMessageType.Recall),
            LocalBroadcastRange,
            ChatMessageType.Recall
        );

        SendMotionAsCommands(MotionCommand.PKArenaRecall, MotionStance.NonCombat);

        var startPos = new Position(Location);

        // Wait for animation
        var actionChain = new ActionChain();

        // Then do teleport
        var animLength = DatManager
            .PortalDat.ReadFromDat<MotionTable>(MotionTableId)
            .GetAnimationLength(MotionCommand.PKArenaRecall);
        actionChain.AddDelaySeconds(animLength);

        IsBusy = true;
        actionChain.AddAction(
            this,
            () =>
            {
                IsBusy = false;
                var endPos = new Position(Location);
                if (startPos.SquaredDistanceTo(endPos) > RecallMoveThresholdSq)
                {
                    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouHaveMovedTooFar));
                    return;
                }

                var rng = ThreadSafeRandom.Next(0, pkArenaLocs.Count - 1);
                var loc = pkArenaLocs[rng];

                Teleport(loc);
            }
        );

        actionChain.EnqueueChain();
    }

    private static List<Position> pklArenaLocs = new List<Position>()
    {
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpklarenanew1")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00670117, 30, -50, 0.005f, 0, 0, 0.000000f, 1.000000f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpklarenanew2")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00670106, 10, 0, 0.005f, 0, 0, -0.947071f, 0.321023f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpklarenanew3")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00670103, 30, -30, 0.005f, 0, 0, -0.699713f, 0.714424f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpklarenanew4")?.GetPosition(PositionType.Destination)
                ?? new Position(0x0067011E, 50, 0, 0.005f, 0, 0, -0.961021f, -0.276474f)
        ),
        new Position(
            DatabaseManager.World.GetCachedWeenie("portalpklarenanew5")?.GetPosition(PositionType.Destination)
                ?? new Position(0x00670127, 60, -30, 0.005f, 0, 0, 0.681639f, 0.731689f)
        ),
    };

    public void HandleActionTeleToPklArena()
    {
        //Console.WriteLine($"{Name}.HandleActionTeleToPkLiteArena()");

        if (IsOlthoiPlayer)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.OlthoiCanOnlyRecallToLifestone));
            return;
        }

        if (PlayerKillerStatus != PlayerKillerStatus.PKLite)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.OnlyPKLiteMayUseCommand));
            return;
        }

        if (PKTimerActive)
        {
            Session.Network.EnqueueSend(
                new GameEventWeenieError(Session, WeenieError.YouHaveBeenInPKBattleTooRecently)
            );
            return;
        }

        if (RecallsDisabled)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ExitTrainingAcademyToUseCommand));
            return;
        }

        if (TooBusyToRecall)
        {
            Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YoureTooBusy));
            return;
        }

        if (CombatMode != CombatMode.NonCombat)
        {
            // this should be handled by a different thing, probably a function that forces player into peacemode
            var updateCombatMode = new GameMessagePrivateUpdatePropertyInt(
                this,
                PropertyInt.CombatMode,
                (int)CombatMode.NonCombat
            );
            SetCombatMode(CombatMode.NonCombat);
            Session.Network.EnqueueSend(updateCombatMode);
        }

        EnqueueBroadcast(
            new GameMessageSystemChat($"{Name} is going to the PKL Arena.", ChatMessageType.Recall),
            LocalBroadcastRange,
            ChatMessageType.Recall
        );

        SendMotionAsCommands(MotionCommand.PKArenaRecall, MotionStance.NonCombat);

        var startPos = new Position(Location);

        // Wait for animation
        var actionChain = new ActionChain();

        // Then do teleport
        var animLength = DatManager
            .PortalDat.ReadFromDat<MotionTable>(MotionTableId)
            .GetAnimationLength(MotionCommand.PKArenaRecall);
        actionChain.AddDelaySeconds(animLength);

        IsBusy = true;
        actionChain.AddAction(
            this,
            () =>
            {
                IsBusy = false;
                var endPos = new Position(Location);
                if (startPos.SquaredDistanceTo(endPos) > RecallMoveThresholdSq)
                {
                    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.YouHaveMovedTooFar));
                    return;
                }

                var rng = ThreadSafeRandom.Next(0, pklArenaLocs.Count - 1);
                var loc = pklArenaLocs[rng];

                Teleport(loc);
            }
        );

        actionChain.EnqueueChain();
    }

    public void SendMotionAsCommands(MotionCommand motionCommand, MotionStance motionStance)
    {
        if (FastTick)
        {
            var actionChain = new ActionChain();
            EnqueueMotionAction(actionChain, new List<MotionCommand>() { motionCommand }, 1.0f, motionStance);
            actionChain.EnqueueChain();
        }
        else
        {
            var motion = new Motion(motionStance, MotionCommand.Ready);
            motion.MotionState.AddCommand(this, motionCommand);
            EnqueueBroadcastMotion(motion);
        }
    }

    public DateTime LastTeleportTime;

    /// <summary>
    /// This is not thread-safe. Consider using WorldManager.ThreadSafeTeleport() instead if you're calling this from a multi-threaded subsection.
    /// </summary>
    public void Teleport(Position _newPosition, bool fromPortal = false)
    {
        var newPosition = new Position(_newPosition);
        //newPosition.PositionZ += 0.005f;
        newPosition.PositionZ += 0.005f * (ObjScale ?? 1.0f);

        //Console.WriteLine($"{Name}.Teleport() - Sending to {newPosition.ToLOCString()}");

        // Check currentFogColor set for player. If LandblockManager.GlobalFogColor is set, don't bother checking, dungeons didn't clear like this on retail worlds.
        // if not clear, reset to clear before portaling in case portaling to dungeon (no current way to fast check unloaded landblock for IsDungeon or current FogColor)
        // client doesn't respond to any change inside dungeons, and only queues for change if in dungeon, executing change upon next teleport
        // so if we delay teleport long enough to ensure clear arrives before teleport, we don't get fog carrying over into dungeon.

        if (
            currentFogColor.HasValue
            && currentFogColor != EnvironChangeType.Clear
            && !LandblockManager.GlobalFogColor.HasValue
        )
        {
            var delayTelport = new ActionChain();
            delayTelport.AddAction(this, () => ClearFogColor());
            delayTelport.AddDelaySeconds(1);
            delayTelport.AddAction(this, () => WorldManager.ThreadSafeTeleport(this, _newPosition));

            delayTelport.EnqueueChain();

            return;
        }

        Teleporting = true;
        LastTeleportTime = DateTime.UtcNow;
        LastTeleportStartTimestamp = Time.GetUnixTime();

        EndStealth();

        if (fromPortal)
        {
            LastPortalTeleportTimestamp = LastTeleportStartTimestamp;
        }

        Session.Network.EnqueueSend(new GameMessagePlayerTeleport(this));

        // load quickly, but player can load into landblock before server is finished loading

        // send a "fake" update position to get the client to start loading asap,
        // also might fix some decal bugs
        var prevLoc = Location;
        Location = newPosition;
        SendUpdatePosition();
        Location = prevLoc;

        DoTeleportPhysicsStateChanges();

        // force out of hotspots
        PhysicsObj.report_collision_end(true);

        if (UnderLifestoneProtection)
        {
            LifestoneProtectionDispel();
        }

        HandlePreTeleportVisibility(newPosition);

        UpdatePlayerPosition(new Position(newPosition), true);
    }

    public void DoPreTeleportHide()
    {
        if (Teleporting)
        {
            return;
        }

        PlayParticleEffect(PlayScript.Hide, Guid);
    }

    public void DoTeleportPhysicsStateChanges()
    {
        var broadcastUpdate = false;

        var oldHidden = Hidden.Value;
        var oldIgnore = IgnoreCollisions.Value;
        var oldReport = ReportCollisions.Value;

        Hidden = true;
        IgnoreCollisions = true;
        ReportCollisions = false;

        if (Hidden != oldHidden || IgnoreCollisions != oldIgnore || ReportCollisions != oldReport)
        {
            broadcastUpdate = true;
        }

        if (broadcastUpdate)
        {
            EnqueueBroadcastPhysicsState();
        }
    }

    /// <summary>
    /// Prevent message spam
    /// </summary>
    public double? LastPortalTeleportTimestampError;

    public void OnTeleportComplete()
    {
        if (CurrentLandblock != null && !CurrentLandblock.CreateWorldObjectsCompleted)
        {
            // If the critical landblock resources haven't been loaded yet, we keep the player in the pink bubble state
            // We'll check periodically to see when it's safe to let them materialize in
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(0.1);
            actionChain.AddAction(this, OnTeleportComplete);
            actionChain.EnqueueChain();
            return;
        }

        // set materialize physics state
        // this takes the player from pink bubbles -> fully materialized
        if (CloakStatus != CloakStatus.On)
        {
            ReportCollisions = true;
        }

        IgnoreCollisions = false;
        Hidden = false;
        Teleporting = false;

        Location = PhysicsObj.Position.ACEPosition(); // Update our location to wherever the physics says we ended up. This takes care of slightly invalid destination locations that both the server and client physics will autocorrect.
        SnapPos = Location;

        CheckMonsters();
        CheckHouse();

        EnqueueBroadcastPhysicsState();

        // hijacking this for both start/end on portal teleport
        if (LastTeleportStartTimestamp == LastPortalTeleportTimestamp)
        {
            LastPortalTeleportTimestamp = Time.GetUnixTime();
        }
    }

    public void SendTeleportedViaMagicMessage(WorldObject itemCaster, Spell spell)
    {
        if (itemCaster == null || itemCaster is Gem)
        {
            Session.Network.EnqueueSend(new GameMessageSystemChat($"You have been teleported.", ChatMessageType.Magic));
        }
        else if (
            this != itemCaster
            && !(itemCaster is Gem)
            && !(itemCaster is Switch)
            && !(itemCaster.GetProperty(PropertyBool.NpcInteractsSilently) ?? false)
        )
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"{itemCaster.Name} teleports you with {spell.Name}.", ChatMessageType.Magic)
            );
        }
        //else if (itemCaster is Gem)
        //    Session.Network.EnqueueSend(new GameEventWeenieError(Session, WeenieError.ITeleported));
    }

    public void NotifyLandblocks()
    {
        // the original implementations of this were done on landblock heartbeat,
        // with checks for players in the current landblock, as well as adjacent outdoor landblocks

        // for performance reasons, this is being reimplemented in the reverse manner,
        // with players notifying landblocks of their activity

        // notify current landblock of player activity
        if (CurrentLandblock != null)
        {
            CurrentLandblock?.SetActive();
        }
    }

    public const float RunFactor = 1.5f;

    /// <summary>
    /// Returns the amount of time for player to rotate by the # of degrees
    /// from the input angle, using the omega speed from its MotionTable
    /// </summary>
    public override float GetRotateDelay(float angle)
    {
        return base.GetRotateDelay(angle) / RunFactor;
    }

    /// <summary>
    /// A list of landblocks the player cannot relog directly into
    ///
    /// If a regular player logs out in one of these landblocks,
    /// they will be transported back to the lifestone when they log back in.
    /// </summary>
    public static HashSet<ushort> NoLog_Landblocks = new HashSet<ushort>()
    {
        // https://asheron.fandom.com/wiki/Special:Search?query=Lifestone+on+Relog%3A+Yes+
        // https://docs.google.com/spreadsheets/d/122xOw3IKCezaTDjC_hggWSVzYJ_9M_zUUtGEXkwNXfs/edit#gid=846612575

        0x0000, // Null Landblock
        0x0002, // Viamontian Garrison
        0x0007, // Town Network
        0x0056, // Augmentation Realm Main Level
        0x005F, // Tanada House of Pancakes (Seasonal)
        0x0067, // PKL Arena
        0x006D, // Augmentation Realm Upper Level
        0x007D, // Augmentation Realm Lower Level
        0x00AB, // Derethian Combat Arena
        0x00AC, // Derethian Combat Arena
        0x00C3, // Blighted Putrid Moarsman Tunnels
        0x00D7, // Jester's Prison
        0x00EA, // Mhoire Armory
        0x015D, // Mountain Cavern
        0x027F, // East Fork Dam Hive
        0x03A7, // Mount Elyrii Hive
        0x5764, // Oubliette of Mhoire Castle
        0x634C, // Tainted Grotto
        0x6544, // Greater Battle Dungeon
        0x6651, // Hoshino Tower
        0x7E04, // Thug Hideout
        0x8A04, // Night Club (Seasonal Anniversary)
        0x8B04, // Frozen Wight Lair
        0x9EE5, // Northwatch Castle Black Market
        0xB5F0, // Aerfalle's Sanctum
        0xF92F, // Freebooter Keep Black Market
        0x00B0, // Colosseum Arena One
        0x00B1, // Colosseum Arena Two
        0x00B2, // Colosseum Arena Three
        0x00B3, // Colosseum Arena Four
        0x00B4, // Colosseum Arena Five
        0x00B6, // Colosseum Arena Mini-Bosses
        0x5960, // Gauntlet Arena One (Celestial Hand)
        0x5961, // Gauntlet Arena Two (Celestial Hand)
        0x5962, // Gauntlet Arena One (Eldritch Web)
        0x5963, // Gauntlet Arena Two (Eldritch Web)
        0x5964, // Gauntlet Arena One (Radiant Blood)
        0x5965, // Gauntlet Arena Two (Radiant Blood)
    };

    /// <summary>
    /// Called when a player first logs in
    /// </summary>
    public static void HandleNoLogLandblock(Biota biota, out bool playerWasMovedFromNoLogLandblock)
    {
        playerWasMovedFromNoLogLandblock = false;

        if (biota.WeenieType == WeenieType.Sentinel || biota.WeenieType == WeenieType.Admin)
        {
            return;
        }

        if (!biota.PropertiesPosition.TryGetValue(PositionType.Location, out var location))
        {
            return;
        }

        var landblock = (ushort)(location.ObjCellId >> 16);

        if (!NoLog_Landblocks.Contains(landblock))
        {
            return;
        }

        if (!biota.PropertiesPosition.TryGetValue(PositionType.Sanctuary, out var lifestone))
        {
            return;
        }

        location.ObjCellId = lifestone.ObjCellId;
        location.PositionX = lifestone.PositionX;
        location.PositionY = lifestone.PositionY;
        location.PositionZ = lifestone.PositionZ;
        location.RotationX = lifestone.RotationX;
        location.RotationY = lifestone.RotationY;
        location.RotationZ = lifestone.RotationZ;
        location.RotationW = lifestone.RotationW;

        playerWasMovedFromNoLogLandblock = true;

        return;
    }

    public static void HandleCapstoneLandblockLogin(Session session, Player player)
    {
        var landblockId = new LandblockId(player.Location.Landblock << 16 | 0xFFFF);

        if (!Landblock.CapstoneTeleportLocations.ContainsKey(landblockId))
        {
            return;
        }

        session.Player.Location = new Position(session.Player.Sanctuary);
    }

    public static Dictionary<int, string> DungeonList = new Dictionary<int, string>
    {
        { 3, "Niffis Fighting Pits" },
        { 4, "Northern Power Forge" },
        { 5, "Southern Power Forge" },
        { 6, "Western Power Forge" },
        { 7, "Night Club" },
        { 8, "Defiled Temple Lower Wing" },
        { 9, "Defiled Temple Upper Wing" },
        { 10, "Defiled Temple Asylum" },
        { 11, "Banderling Shrine" },
        { 12, "Weakened Vault Sewers" },
        { 13, "Secured Vault Sewers" },
        { 14, "Reinforced Vault Sewers" },
        { 15, "Fortified Vault Sewers" },
        { 16, "War Room" },
        { 17, "Weakened Royal Vault" },
        { 18, "Secured Royal Vault" },
        { 19, "Reinforced Royal Vault" },
        { 20, "Fortified Royal Vault" },
        { 21, "Banished Assembly" },
        { 22, "Ravaged Cathedral" },
        { 23, "Vile Sanctuary" },
        { 35, "Sezzherei's Lair" },
        { 36, "Mausoleum of Bitterness" },
        { 37, "Mausoleum of Anger" },
        { 38, "Mausoleum of Cruelty" },
        { 39, "Accursed Mausoleum of Slaughter" },
        { 40, "Unholy Mausoleum of Slaughter" },
        { 242, "Qin Xikit's Hidden Crown" },
        { 256, "Murk Warrens" },
        { 257, "Murk Warrens" },
        { 258, "Murk Warrens" },
        { 259, "Black Spawn Den" },
        { 260, "Black Spawn Den" },
        { 261, "Black Spawn Den" },
        { 262, "Setab's Barracks" },
        { 263, "Nor's Folly" },
        { 264, "Cursed Swamp" },
        { 265, "Dungeon of Corpses" },
        { 266, "Black Dominion" },
        { 267, "Asuger Temple" },
        { 268, "Mysterious Tunnels" },
        { 269, "AC Orange Room" },
        { 270, "AC Purple Room" },
        { 271, "AC Red Room" },
        { 272, "Nexus" },
        { 273, "AC Storage" },
        { 275, "Amiantos Bethel" },
        { 277, "Wedding Hall" },
        { 278, "Tenkarrdun Foundry" },
        { 279, "Serac Vault" },
        { 281, "Amperehelion Vault" },
        { 282, "Incunabula Vault" },
        { 283, "Jahannan Vault" },
        { 284, "Mountain Fortress" },
        { 285, "Al-Arqas Meeting Hall" },
        { 286, "Al-Jalima Meeting Hall" },
        { 287, "Arwic Meeting Hall" },
        { 288, "Baishi Meeting Hall" },
        { 289, "Cragstone Meeting Hall" },
        { 290, "Eastham Meeting Hall" },
        { 291, "Glenden Wood Meeting Hall" },
        { 292, "Hebian-to Meeting Hall" },
        { 293, "Holtburg Meeting Hall" },
        { 294, "Khayyaban Meeting Hall" },
        { 295, "Lin Meeting Hall" },
        { 296, "Lytelthorpe Meeting Hall" },
        { 297, "Mayoi Meeting Hall" },
        { 298, "Nanto Meeting Hall" },
        { 299, "Qalaba'r Meeting Hall" },
        { 300, "Rithwic Meeting Hall" },
        { 301, "Samsur Meeting Hall" },
        { 302, "Sawato Meeting Hall" },
        { 303, "Shoushi Meeting Hall" },
        { 304, "Tou-Tou Meeting Hall" },
        { 305, "Tufa Meeting Hall" },
        { 306, "Uziz Meeting Hall" },
        { 307, "Yanshi Meeting Hall" },
        { 308, "Yaraq Meeting Hall" },
        { 309, "Zaikhal Meeting Hall" },
        { 310, "Empyrean Foundry" },
        { 311, "Artifex Vault" },
        { 312, "Lost City of Frore" },
        { 313, "Mage Academy" },
        { 314, "Night Club" },
        { 315, "Folthid Cellar" },
        { 316, "Underground Forest" },
        { 317, "Forbidden Crypts" },
        { 318, "Burial Temple" },
        { 319, "Saadia's Retreat" },
        { 320, "Virindi Fort" },
        { 321, "Enkindled Souls" },
        { 322, "Recovered Temple" },
        { 323, "Phyntos Menace" },
        { 324, "Darkened Halls" },
        { 325, "Bone Lair" },
        { 326, "Mount Lethe Magma Tubes" },
        { 327, "Mount Naipenset Caverns" },
        { 328, "A Small Ruin" },
        { 329, "Thieves Galleries" },
        { 330, "Mysterious Cave" },
        { 331, "Winthura's Garden" },
        { 332, "Nevius Passage" },
        { 333, "Damp Caverns" },
        { 334, "Smugglers Hideaway" },
        { 335, "Dungeon Maggreth" },
        { 336, "Filos' Doom" },
        { 337, "Forgotten Temple" },
        { 338, "Ruined Cave Outpost" },
        { 339, "Shreth Hive" },
        { 340, "Stone Cathedral" },
        { 341, "Musansayn's Vaults" },
        { 342, "Sea Temple Catacombs" },
        { 343, "Under-Cove Crypt" },
        { 344, "Lytaway" },
        { 345, "Abandoned Shops" },
        { 346, "Dry Well" },
        { 347, "Unfinished Temple" },
        { 348, "Nanto Rat Lair" },
        { 349, "Mountain Cavern" },
        { 350, "Impious Temple" },
        { 352, "Fort Tununska" },
        { 353, "Empyrean Garrison" },
        { 354, "Cave of Alabree" },
        { 355, "Holtburg Redoubt" },
        { 356, "Lost Distillery" },
        { 357, "Old Warehouse" },
        { 358, "Deserted Ruin" },
        { 359, "Desert Ruin" },
        { 360, "Water Temple" },
        { 361, "Mattekar Cave" },
        { 362, "Guardian Crypt" },
        { 363, "Creepy Chambers" },
        { 364, "The Marketplace of Dereth" },
        { 373, "Muggy Font" },
        { 374, "Humid Font" },
        { 375, "Mossy Cave" },
        { 376, "Dark Mosswart Halls" },
        { 377, "Umbral Hall" },
        { 378, "Banderling Hovel" },
        { 379, "Watery Grotto" },
        { 381, "Hidden Entrance" },
        { 382, "Underway" },
        { 383, "Halls" },
        { 384, "Tunnels" },
        { 385, "Dungeon Nye" },
        { 386, "Desert Mine" },
        { 387, "Dungeon Binar" },
        { 388, "Dungeon Mei" },
        { 389, "Daiklos Dungeon" },
        { 390, "Sclavus Keep" },
        { 391, "A Ruin" },
        { 392, "Mines of Despair" },
        { 393, "Shoushi's Revenge" },
        { 394, "Advocate Dungeon" },
        { 395, "A Cave" },
        { 396, "Crater Lair" },
        { 397, "Olthoi Tunnels" },
        { 399, "Carved Cave" },
        { 400, "Old Mine" },
        { 401, "Mountain Sewer" },
        { 402, "Mountain Halls" },
        { 403, "Knath Lair" },
        { 405, "Xi Ru's Chapel" },
        { 406, "Xi Ru's Crypt" },
        { 408, "Steamy Font" },
        { 410, "Crater Caves Dungeon" },
        { 411, "Lightless Catacombs" },
        { 412, "Swamp Temple" },
        { 413, "Simple Tower" },
        { 414, "Drudge Hideout" },
        { 415, "Tumerok Post" },
        { 416, "Tumerok Dungeon" },
        { 417, "Tumerok Base" },
        { 418, "Swamp Temple" },
        { 419, "The Pit Dungeon" },
        { 420, "Mite Tunnels" },
        { 421, "Inner Dungeon" },
        { 422, "Disaster Maze" },
        { 423, "Crater Pathway" },
        { 424, "Abandoned Arena" },
        { 425, "Ancient Lighthouse" },
        { 426, "Lair of Death" },
        { 427, "Forking Trail" },
        { 428, "Adventurer's Haven" },
        { 429, "Dungeon Gallery Tower" },
        { 430, "Colier Mine" },
        { 431, "Lakeside Lair" },
        { 432, "Seaside Lair" },
        { 433, "Lost Garden Ruins" },
        { 434, "Web Maze" },
        { 435, "Dungeon Fern" },
        { 436, "Golem Burial Ground" },
        { 437, "Rocky Crypt" },
        { 438, "Dungeon Muddy" },
        { 439, "Braid Mansion Ruin" },
        { 440, "A Ruin" },
        { 441, "A Ruin" },
        { 442, "Dungeon of Tatters" },
        { 443, "Thief Trail" },
        { 444, "Tumerok Chamber" },
        { 445, "Cave" },
        { 446, "Tumerok Fortress" },
        { 447, "Tumerok Outpost" },
        { 448, "Small Complex" },
        { 449, "Tumerok Mine" },
        { 450, "Witshire Dungeon" },
        { 451, "Trialos" },
        { 452, "Thasali" },
        { 453, "Swamp Ruin" },
        { 454, "Hunter's Leap" },
        { 455, "Lugian Outpost" },
        { 456, "Lugian Post" },
        { 457, "Abandoned Mine" },
        { 458, "Hebian-to Sewers" },
        { 460, "Halls of the Helm" },
        { 461, "Rithwic Crypt" },
        { 462, "Halls of the Lost Light" },
        { 463, "Eastham Sewer" },
        { 464, "Yanshi Tunnel" },
        { 465, "Mayoi Shrine" },
        { 466, "Mountain Keep" },
        { 467, "Zabool Tower Base" },
        { 468, "Bellig Tower Base" },
        { 469, "Syliph Tower" },
        { 470, "Alfreth Dungeon" },
        { 471, "Sylsfear Dungeon" },
        { 472, "A Drudge Nest" },
        { 473, "A Red Rat Lair" },
        { 474, "A Ruin" },
        { 475, "Moss Chamber" },
        { 476, "A Drudge Nest" },
        { 477, "A Rat Nest" },
        { 478, "A Ruin" },
        { 479, "A Mosswart Nest" },
        { 480, "Banderling Ruin" },
        { 481, "Thieves' Den" },
        { 482, "Bandit Castle Prison" },
        { 483, "Glenden Wood Dungeon" },
        { 484, "North Glenden Prison" },
        { 485, "Green Mire Grave" },
        { 486, "Small Icecave" },
        { 487, "A Small Cave" },
        { 488, "Dungeon Manor" },
        { 489, "Underground City" },
        { 490, "Old Talisman Dungeon" },
        { 492, "Defiled Temple Middle Wing" },
        { 493, "Dungeon of Shadows" },
        { 495, "Arwic Mines" },
        { 497, "Cave" },
        { 499, "Tomb of The Dead" },
        { 500, "Trial 1" },
        { 501, "Aerfalle Keep" },
        { 502, "Holtburg Dungeon" },
        { 503, "Shoushi Grotto" },
        { 504, "Mite Maze" },
        { 505, "Accursed Halls" },
        { 506, "Crypt of Ashen Tears" },
        { 507, "Yaraq Tunnels" },
        { 508, "ReedShark Lair" },
        { 509, "Trothyr's Rest" },
        { 626, "Ogham Dungeon" },
        { 636, "Ancient Empyrean Grotto" },
        { 637, "Lair of the Eviscerators" },
        { 638, "Martinate Holding" },
        { 639, "East Fork Dam Hive" },
        { 642, "An Olthoi Soldier Nest" },
        { 643, "The Dark Lair" },
        { 644, "Abandoned Tumerok Site" },
        { 645, "Wasteland Hive" },
        { 646, "Tumerok Cave" },
        { 648, "Royal Hive" },
        { 649, "Tiny Hive" },
        { 650, "Small Hive" },
        { 651, "Simple Hive" },
        { 652, "Shallow Hive" },
        { 653, "New Hive" },
        { 654, "Palenqual's Caverns" },
        { 655, "Swamp Gardens" },
        { 656, "Stable Rift" },
        { 657, "Singularity Bore" },
        { 658, "Tumerok Cavern" },
        { 659, "Habitat Tower" },
        { 660, "Director's Chambers" },
        { 661, "Inculcation Cells" },
        { 662, "Southern Black Claw Outpost" },
        { 663, "Northern Black Claw Outpost" },
        { 664, "Land Bridge Staging Complex" },
        { 665, "Farmer's Garden" },
        { 666, "The Envoy's Chamber" },
        { 667, "Gredaline Consulate" },
        { 668, "Linvak Tukal Entryway" },
        { 669, "Small Mnemosyne Collection Site" },
        { 670, "Aerbax Laboratory" },
        { 671, "Aerbax Haven" },
        { 672, "Sand Shallow" },
        { 673, "Panopticon" },
        { 674, "Trial 2" },
        { 675, "Moss Chamber" },
        { 676, "South Tumerok Vanguard Outpost" },
        { 677, "North Tumerok Vanguard Outpost" },
        { 678, "Trial 3" },
        { 679, "Trial 4" },
        { 680, "Trial 5" },
        { 681, "Hieromancers' Halls" },
        { 682, "Desert March" },
        { 683, "Strange Tunnel" },
        { 684, "Upper Empyrean Mausoleum" },
        { 685, "Empyrean Cloister" },
        { 686, "Chakron Gate" },
        { 687, "Upper Chakron Flux" },
        { 688, "Sepulcher of the Hopeslayer" },
        { 689, "Upper Shade Stronghold" },
        { 690, "Upper Heart of Darkness" },
        { 691, "Moars Laboratory" },
        { 692, "A Mosswart Hideout" },
        { 693, "Moarsmen Spawning Grounds" },
        { 694, "Idol Spawning Grounds" },
        { 695, "Catacombs of Ithaenc" },
        { 696, "Rumuba's Hidey-Hole" },
        { 698, "Moars" },
        { 699, "Jungle Shadows" },
        { 700, "Mosswart Nest" },
        { 701, "Small Ruin" },
        { 702, "Small Temple" },
        { 703, "Small Temple" },
        { 704, "Small Temple" },
        { 705, "Mud Cave" },
        { 706, "Moarsmen Muck" },
        { 707, "Slithis Pit" },
        { 708, "Moarsmen Hideout" },
        { 709, "Treacherous Tunnels" },
        { 710, "Lightless Tunnels" },
        { 711, "Tumideon Fortress" },
        { 712, "Banderling Conquest Dungeon" },
        { 713, "Sotiris" },
        { 714, "Mosswart Maze" },
        { 715, "The Floating City" },
        { 716, "The Floating City" },
        { 717, "The Floating City" },
        { 718, "The Floating City" },
        { 719, "The Floating City" },
        { 720, "The Floating City" },
        { 721, "The Floating City" },
        { 722, "Uninhabited Area" },
        { 727, "Shendolain" },
        { 730, "Fenmalain" },
        { 731, "Fenmalain Vestibule" },
        { 732, "Caulnalain Vestibule" },
        { 735, "Caulnalain" },
        { 736, "Shendolain Vestibule" },
        { 737, "Golem Sanctum" },
        { 738, "Arena" },
        { 739, "Arena" },
        { 741, "Krau Li's Labyrinth" },
        { 742, "Catacombs of the Forgotten" },
        { 743, "Lugian Excavations" },
        { 744, "Lugian Quarry" },
        { 745, "Lugian Mines" },
        { 749, "Aerlinthe Lower Reservoir" },
        { 750, "Aerlinthe Reservoir" },
        { 751, "Soul-Fearing Vestry Dungeon" },
        { 752, "Hills Citadel" },
        { 753, "Ridge Citadel" },
        { 754, "Wilderness Citadel" },
        { 755, "Halls of Metos" },
        { 756, "Halls of Metos" },
        { 757, "Halls of Metos" },
        { 758, "Sclavus Cathedral" },
        { 759, "Sclavus Cathedral" },
        { 760, "Burun Cathedral" },
        { 761, "AC Room of Cheese" },
        { 762, "AC Blue Room" },
        { 763, "Olthoi Horde Nest" },
        { 764, "Olthoi Horde Nest" },
        { 765, "Olthoi Horde Nest" },
        { 924, "Hollow Lair near Lytelthorpe" },
        { 926, "Seat of the New Singularity" },
        { 927, "Singular Obsidian Repository" },
        { 928, "Singular Chorizite Repository" },
        { 929, "Singular Pyreal Repository" },
        { 930, "Southern Infiltrator Keep" },
        { 931, "Northern Infiltrator Keep" },
        { 932, "The Asteliary" },
        { 934, "North Fork Dam Hive" },
        { 935, "Mount Elyrii Hive" }
    };
}
