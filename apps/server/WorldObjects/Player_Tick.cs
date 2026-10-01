using System;
using System.Collections.Generic;
using System.Numerics;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network.Enum;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Network.Motion;
using ACE.Server.Network.Sequence;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.Structure;
using ACE.Server.Physics;
using ACE.Server.Physics.Common;

namespace ACE.Server.WorldObjects;

partial class Player
{
    private readonly ActionQueue actionQueue = new ActionQueue();

    /// <inheritdoc/>
    public override bool NeedsPhysicsUpdate => true;

    private int initialAge;
    private DateTime initialAgeTime;

    private const double ageUpdateInterval = 7;
    private double nextAgeUpdateTime;

    private double houseRentWarnTimestamp;
    private const double houseRentWarnInterval = 3600;

    private double nextRoadCheckTime;
    private const double RoadCheckInterval = 0.5;
    private double roadGraceEndTime;
    private const double RoadGracePeriod = 5.0;
    public static readonly uint RoadSpeedBuffSpellId = (uint)SpellId.RoadRunBuff;

    public void Player_Tick(double currentUnixTime)
    {
        if (CharacterSaveFailed)
        {
            // Boot the player as their Character object is not saving properly
            if (!IsLoggingOut)
            {
                _log.Error(
                    $"{Session.Player.Name} | 0x{Guid} | Account: {Account.AccountName} - disconnected for CharacterSaveFailed"
                );
                //Session.SendCharacterError(CharacterError.AccountLogin); // forces client to error screen
                Session.Terminate(
                    SessionTerminationReason.CharacterSaveFailed,
                    new GameMessageCharacterError(CharacterError.AccountLogin)
                );
                //Session.LogOffPlayer(true);
                CharacterSaveFailed = false;
            }
            return;
        }

        if (BiotaSaveFailed)
        {
            // Boot the player as their Biota object is not saving properly
            if (!IsLoggingOut)
            {
                _log.Error(
                    $"{Session.Player.Name} | 0x{Guid} | Account: {Account.AccountName} - disconnected for BiotaSaveFailed"
                );
                //Session.SendCharacterError(CharacterError.AccountLogin); // forces client to error screen
                Session.Terminate(
                    SessionTerminationReason.BiotaSaveFailed,
                    new GameMessageCharacterError(CharacterError.AccountLogin)
                );
                //Session.LogOffPlayer(true);
                BiotaSaveFailed = false;
            }
            return;
        }

        actionQueue.RunActions();
       
        TryFlushPendingTownMessage(currentUnixTime);

        if (nextAgeUpdateTime <= currentUnixTime)
        {
            nextAgeUpdateTime = currentUnixTime + ageUpdateInterval;

            if (initialAgeTime == DateTime.MinValue)
            {
                initialAge = Age ?? 1;
                initialAgeTime = DateTime.UtcNow;
            }

            Age = initialAge + (int)(DateTime.UtcNow - initialAgeTime).TotalSeconds;

            // In retail, this is sent every 7 seconds. If you adjust ageUpdateInterval from 7, you'll need to re-add logic to send this every 7s (if you want to match retail)
            Session.Network.EnqueueSend(new GameMessagePrivateUpdatePropertyInt(this, PropertyInt.Age, Age ?? 1));
        }

        if (FellowVitalUpdate && Fellowship != null)
        {
            Fellowship.OnVitalUpdate(this);
            FellowVitalUpdate = false;
        }

        CheckRoadSpeedBuff(currentUnixTime);

        UpdateStealthDetectionLevel(currentUnixTime);

        if (House != null && PropertyManager.GetBool("house_rent_enabled").Item)
        {
            if (houseRentWarnTimestamp > 0 && currentUnixTime > houseRentWarnTimestamp)
            {
                HouseManager.GetHouse(
                    House.Guid.Full,
                    (house) =>
                    {
                        if (house != null && house.HouseStatus == HouseStatus.Active && !house.SlumLord.IsRentPaid())
                        {
                            Session.Network.EnqueueSend(
                                new GameMessageSystemChat(
                                    $"Warning!  You have not paid your maintenance costs for the last {(house.IsApartment ? "90" : "30")} day maintenance period.  Please pay these costs by this deadline or you will lose your house, and all your items within it.",
                                    ChatMessageType.Broadcast
                                )
                            );
                        }
                    }
                );

                houseRentWarnTimestamp = Time.GetFutureUnixTime(houseRentWarnInterval);
            }
            else if (houseRentWarnTimestamp == 0)
            {
                houseRentWarnTimestamp = Time.GetFutureUnixTime(houseRentWarnInterval);
            }
        }
    }

    /// <summary>
    /// Checks whether the player is standing on a road texture and applies or removes the road speed buff accordingly.
    /// </summary>
    private void TryFlushPendingTownMessage(double currentUnixTime)
{
    if (string.IsNullOrEmpty(PendingTownAttunementQuestName))
    {
        return;
    }

    if (currentUnixTime < PendingTownAttunementDueTime)
    {
        return;
    }

    QuestManager.FlushPendingTownMessage();
}
    private void CheckRoadSpeedBuff(double currentUnixTime)
    {
        if (!PropertyManager.GetBool("road_speed_buff").Item)
        {
            RemoveRoadSpeedBuff();
            return;
        }

        if (currentUnixTime < nextRoadCheckTime)
        {
            return;
        }

        nextRoadCheckTime = currentUnixTime + RoadCheckInterval;

        // Road textures only exist on outdoor landcells (cell ID lower 16 bits < 0x100)
        if ((PhysicsObj?.Position.ObjCellID & 0xFFFF) >= 0x100)
        {
            RemoveRoadSpeedBuff();
            return;
        }

        var physLandblock = LScape.get_landblock(PhysicsObj.Position.ObjCellID, InstanceId);
        if (physLandblock == null)
        {
            RemoveRoadSpeedBuff();
            return;
        }

        var isOnRoad = physLandblock.OnRoad(PhysicsObj.Position.Frame.Origin)
            && !Town.IsTownLandblock((Location.Landblock << 16) | 0xFFFF);
        var hasRoadBuff = EnchantmentManager.HasSpell(RoadSpeedBuffSpellId);

        if (isOnRoad)
        {
            roadGraceEndTime = 0;

            if (!hasRoadBuff)
            {
                var spell = new Spell(RoadSpeedBuffSpellId);
                if (spell.NotFound)
                {
                    return;
                }

                var addResult = EnchantmentManager.Add(spell, this, null);
                if (addResult.Enchantment != null)
                {
                    Session.Network.EnqueueSend(new GameEventMagicUpdateEnchantment(Session, new Enchantment(this, addResult.Enchantment)));
                    HandleSpellHooks(spell);
                }
            }
        }
        else if (hasRoadBuff)
        {
            if (roadGraceEndTime == 0)
            {
                roadGraceEndTime = currentUnixTime + RoadGracePeriod;
            }
            else if (currentUnixTime >= roadGraceEndTime)
            {
                roadGraceEndTime = 0;
                RemoveRoadSpeedBuff();
            }
        }
    }

    /// <summary>
    /// Checks whether the player is on a snow terrain tile above elevation 100 and applies Frigid damage.
    /// Damage = (PositionZ - 100), reduced by GearFrigidProtectionMod (% reduction) then GearFrigidProtection (flat reduction).
    /// Called every heartbeat (~5 seconds).
    /// </summary>
    private void CheckFrigidDamage()
    {
        if (!PropertyManager.GetBool("frigid_damage").Item)
        {
            return;
        }

        if (IsDead || Teleporting)
        {
            return;
        }

        if (Location.PositionZ <= 200f)
        {
            return;
        }

        // Snow textures only exist on outdoor landcells (cell ID lower 16 bits < 0x100)
        if ((PhysicsObj?.Position.ObjCellID & 0xFFFF) >= 0x100)
        {
            return;
        }

        var physLandblock = LScape.get_landblock(PhysicsObj.Position.ObjCellID, InstanceId);
        if (physLandblock == null || !physLandblock.NearSnow(PhysicsObj.Position.Frame.Origin, 50f))
        {
            return;
        }

        // _log.Information(
        //     "[FRIGID] {Name} is near snow/ice terrain (within 50 units) at cell {CellID:X8}, elevation {Elevation:F1}",
        //     Name,
        //     PhysicsObj.Position.ObjCellID,
        //     Location.PositionZ
        // );

        var rawDamage = (float)Location.PositionZ - 200f;

        // Outside the Northern Esper Mountains, Frigid damage is only 1/10th strength.
        var inEsperMountains = EsperMountainsZone.Contains(Location);
        var worldX = Location.LandblockX * 192f + Location.PositionX;
        var worldY = Location.LandblockY * 192f + Location.PositionY;

        //_log.Information(
        //    "[FRIGID ZONE] {Name} zone check — inEsperMountains: {InZone}, mapCoords: {MapCoords}, worldX: {WorldX:F1}, worldY: {WorldY:F1}, rawDamage before modifier: {RawDamage:F1}",
        //    Name,
        //    inEsperMountains,
        //    Location.GetMapCoordStr() ?? "unknown",
        //    worldX,
        //    worldY,
        //    rawDamage
        //);

        if (!inEsperMountains)
        {
            rawDamage *= 0.1f;
            //_log.Information(
            //    "[FRIGID ZONE] {Name} is outside Esper Mountains — rawDamage reduced to {RawDamage:F1}",
            //    Name,
            //    rawDamage
            //);
        }

        // Apply percentage reduction from equipped gear
        var modReduction = GetEquippedItemsSkillModSum(PropertyFloat.GearFrigidProtectionMod);
        rawDamage *= (float)Math.Max(0.0, 1.0 - modReduction);

        // Apply cold resistance from active spells, natural resistance, and augmentations
        var coldResistMod = GetResistanceMod(DamageType.Cold, null, null);
        rawDamage *= coldResistMod;

        // Apply flat reduction from equipped gear (after % reduction)
        var flatReduction = GetEquippedItemsRatingSum(PropertyInt.GearFrigidProtection);
        rawDamage -= flatReduction;

        if (rawDamage <= 0)
        {
            return;
        }

        var damage = (int)Math.Ceiling(rawDamage);

        //_log.Information(
        //    "[FRIGID] {Name} took {Damage} Frigid damage at elevation {Elevation:F1} (modReduction: {ModReduction:P1}, flatReduction: {FlatReduction})",
        //    Name,
        //    damage,
        //    Location.PositionZ,
        //    modReduction,
        //    flatReduction
        //);

        UpdateVitalDelta(Health, -damage);

        Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"The frigid air bites at you for {damage} points of cold damage!",
                ChatMessageType.Broadcast
            )
        );

        if (Health.Current == 0)
        {
            OnDeath(null, DamageType.Cold);
            Die();
        }
    }

    private void RemoveRoadSpeedBuff()
    {
        var roadBuff = EnchantmentManager.GetEnchantment(RoadSpeedBuffSpellId);
        if (roadBuff == null)
        {
            return;
        }

        EnchantmentManager.Remove(roadBuff, sound: false);
        HandleRunRateUpdate();
    }

    private static readonly TimeSpan MaximumTeleportTime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Called every ~5 seconds for Players
    /// </summary>
    public override void Heartbeat(double currentUnixTime)
    {
        NotifyLandblocks();

        ManaConsumersTick();

        HandleTargetVitals();

        HandleBuildUpEffects();

        CheckFrigidDamage();

        LifestoneProtectionTick();

        PK_DeathTick();

        GagsTick();

        PhysicsObj.ObjMaint.DestroyObjects();

        // Check if we're due for our periodic SavePlayer
        if (LastRequestedDatabaseSave == DateTime.MinValue)
        {
            LastRequestedDatabaseSave = DateTime.UtcNow;
        }

        if (LastRequestedDatabaseSave.AddSeconds(PlayerSaveIntervalSecs) <= DateTime.UtcNow)
        {
            SavePlayerToDatabase();
        }

        if (
            Teleporting
            && DateTime.UtcNow > Time.GetDateTimeFromTimestamp(LastTeleportStartTimestamp ?? 0).Add(MaximumTeleportTime)
        )
        {
            if (Session != null)
            {
                Session.LogOffPlayer(true);
            }
            else
            {
                LogOut();
            }
        }

        base.Heartbeat(currentUnixTime);
        ResonanceManager.Zones?.TryHandlePlayer(this, currentUnixTime);
    }

    public static float MaxSpeed = 50;
    public static float MaxSpeedSq = MaxSpeed * MaxSpeed;

    public static bool DebugPlayerMoveToStatePhysics { get; set; } = false;

    /// <summary>
    /// Flag indicates if player is doing full physics simulation
    /// </summary>
    public bool FastTick => IsPKType;

    /// <summary>
    /// For advanced spellcasting / players glitching around during powersliding,
    /// the reason for this retail bug is from 2 different functions for player movement
    ///
    /// The client's self-player uses DoMotion/StopMotion
    /// The server and other players on the client use apply_raw_movement
    ///
    /// When a 3+ button powerslide is performed, this bugs out apply_raw_movement,
    /// and causes the player to spin in place. With DoMotion/StopMotion, it performs a powerslide.
    ///
    /// With this option enabled (retail defaults to false), the player's position on the server
    /// will match up closely with the player's client during powerslides.
    ///
    /// Since the client uses apply_raw_movement to simulate the movement of nearby players,
    /// the other players will still glitch around on screen, even with this option enabled.
    ///
    /// If you wish for the positions of other players to be less glitchy, the 'MoveToState_UpdatePosition_Threshold'
    /// can be lowered to achieve that
    /// </summary>

    public void OnMoveToState(MoveToState moveToState)
    {
        if (!FastTick)
        {
            return;
        }

        if (DebugPlayerMoveToStatePhysics)
        {
            Console.WriteLine(moveToState.RawMotionState);
        }

        if (RecordCast.Enabled)
        {
            RecordCast.OnMoveToState(moveToState);
        }

        if (!PhysicsObj.IsMovingOrAnimating)
        {
            PhysicsObj.UpdateTime = PhysicsTimer.CurrentTime;
        }

        if (!PropertyManager.GetBool("client_movement_formula").Item || moveToState.StandingLongJump)
        {
            OnMoveToState_ServerMethod(moveToState);
        }
        else
        {
            OnMoveToState_ClientMethod(moveToState);
        }

        if (MagicState.IsCasting && MagicState.PendingTurnRelease && moveToState.RawMotionState.TurnCommand == 0)
        {
            OnTurnRelease();
        }
    }

    public void OnMoveToState_ClientMethod(MoveToState moveToState)
    {
        var rawState = moveToState.RawMotionState;
        var prevState = LastMoveToState?.RawMotionState ?? RawMotionState.None;

        var mvp = new Physics.Animation.MovementParameters();
        mvp.HoldKeyToApply = rawState.CurrentHoldKey;

        if (!PhysicsObj.IsMovingOrAnimating)
        {
            PhysicsObj.UpdateTime = PhysicsTimer.CurrentTime;
        }

        // ForwardCommand
        if (rawState.ForwardCommand != MotionCommand.Invalid)
        {
            // press new key
            if (prevState.ForwardCommand == MotionCommand.Invalid)
            {
                PhysicsObj.DoMotion((uint)MotionCommand.Ready, mvp);
                PhysicsObj.DoMotion((uint)rawState.ForwardCommand, mvp);
            }
            // press alternate key
            else if (prevState.ForwardCommand != rawState.ForwardCommand)
            {
                PhysicsObj.DoMotion((uint)rawState.ForwardCommand, mvp);
            }
        }
        else if (prevState.ForwardCommand != MotionCommand.Invalid)
        {
            // release key
            PhysicsObj.StopMotion((uint)prevState.ForwardCommand, mvp, true);
        }

        // StrafeCommand
        if (rawState.SidestepCommand != MotionCommand.Invalid)
        {
            // press new key
            if (prevState.SidestepCommand == MotionCommand.Invalid)
            {
                PhysicsObj.DoMotion((uint)rawState.SidestepCommand, mvp);
            }
            // press alternate key
            else if (prevState.SidestepCommand != rawState.SidestepCommand)
            {
                PhysicsObj.DoMotion((uint)rawState.SidestepCommand, mvp);
            }
        }
        else if (prevState.SidestepCommand != MotionCommand.Invalid)
        {
            // release key
            PhysicsObj.StopMotion((uint)prevState.SidestepCommand, mvp, true);
        }

        // TurnCommand
        if (rawState.TurnCommand != MotionCommand.Invalid)
        {
            // press new key
            if (prevState.TurnCommand == MotionCommand.Invalid)
            {
                PhysicsObj.DoMotion((uint)rawState.TurnCommand, mvp);
            }
            // press alternate key
            else if (prevState.TurnCommand != rawState.TurnCommand)
            {
                PhysicsObj.DoMotion((uint)rawState.TurnCommand, mvp);
            }
        }
        else if (prevState.TurnCommand != MotionCommand.Invalid)
        {
            // release key
            PhysicsObj.StopMotion((uint)prevState.TurnCommand, mvp, true);
        }
    }

    public void OnMoveToState_ServerMethod(MoveToState moveToState)
    {
        var minterp = PhysicsObj.get_minterp();
        minterp.RawState.SetState(moveToState.RawMotionState);

        if (moveToState.StandingLongJump)
        {
            minterp.RawState.ForwardCommand = (uint)MotionCommand.Ready;
            minterp.RawState.SideStepCommand = 0;
        }

        var allowJump = minterp.motion_allows_jump(minterp.InterpretedState.ForwardCommand) == WeenieError.None;

        //PhysicsObj.cancel_moveto();

        minterp.apply_raw_movement(true, allowJump);
    }

    public bool InUpdate;

    public override bool UpdateObjectPhysics()
    {
        try
        {
            stopwatch.Restart();

            var landblockUpdate = false;

            InUpdate = true;

            // update position through physics engine
            if (RequestedLocation != null)
            {
                landblockUpdate = UpdatePlayerPosition(RequestedLocation);
                RequestedLocation = null;
            }

            if (FastTick && PhysicsObj.IsMovingOrAnimating || PhysicsObj.Velocity != Vector3.Zero)
            {
                UpdatePlayerPhysics();

                if (MoveToParams?.Callback != null && !PhysicsObj.IsMovingOrAnimating)
                {
                    HandleMoveToCallback();
                }
            }

            InUpdate = false;

            return landblockUpdate;
        }
        finally
        {
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            ServerPerformanceMonitor.AddToCumulativeEvent(
                ServerPerformanceMonitor.CumulativeEventHistoryType.Player_Tick_UpdateObjectPhysics,
                elapsedSeconds
            );
            if (elapsedSeconds >= 1) // Yea, that ain't good....
            {
                _log.Warning(
                    $"[PERFORMANCE][PHYSICS] {Guid}:{Name} took {(elapsedSeconds * 1000):N1} ms to process UpdateObjectPhysics() at loc: {Location}"
                );
            }
            else if (elapsedSeconds >= 0.010)
            {
                _log.Debug(
                    "[PERFORMANCE][PHYSICS] {Guid}:{Name} took {ElapsedTimeMs):N1} ms to process UpdateObjectPhysics() at loc: {Location}",
                    Guid,
                    Name,
                    (elapsedSeconds * 1000),
                    Location
                );
            }
        }
    }

    public void UpdatePlayerPhysics()
    {
        if (DebugPlayerMoveToStatePhysics)
        {
            Console.WriteLine($"{Name}.UpdatePlayerPhysics({PhysicsObj.PartArray.Sequence.CurrAnim.Value.Anim.ID:X8})");
        }

        //Console.WriteLine($"{PhysicsObj.Position.Frame.Origin}");
        //Console.WriteLine($"{PhysicsObj.Position.Frame.get_heading()}");

        PhysicsObj.update_object();

        // sync ace position?
        Location.Rotation = PhysicsObj.Position.Frame.Orientation;

        if (!FastTick)
        {
            return;
        }

        // ensure PKLogout position is synced up for other players
        if (PKLogout)
        {
            EnqueueBroadcast(
                new GameMessageUpdateMotion(this, new Motion(MotionStance.NonCombat, MotionCommand.Ready))
            );
            PhysicsObj.StopCompletely(true);

            if (!PhysicsObj.IsMovingOrAnimating)
            {
                SyncLocation();
                EnqueueBroadcast(new GameMessageUpdatePosition(this));
            }
        }

        // this fixes some differences between client movement (DoMotion/StopMotion) and server movement (apply_raw_movement)
        //
        // scenario: start casting a self-spell, and then immediately start holding the run forward key during the windup
        // on client: player will start running forward after the cast has completed
        // on server: player will stand still

        // this block of code can improve the sync between these 2 methods,
        // however there are some bugs that originate in acclient that cannot be resolved on the server
        // for example, equip a wand, and then start running forward in non-combat mode. switch to magic combat mode, and then release forward during the stance swap
        // the client will never send a 'client released forward' MoveToState in this scenario unfortunately.
        // because of this, it's better to have the 'client blip forward' bug without it, than to have the client invisibly running forward on the server.
        // commenting out this block because of this...

        /*if (!PhysicsObj.IsMovingOrAnimating && LastMoveToState != null)
        {
            // apply latest MoveToState, if applicable
            //if ((LastMoveToState.RawMotionState.Flags & (RawMotionFlags.ForwardCommand | RawMotionFlags.SideStepCommand | RawMotionFlags.TurnCommand)) != 0)
            if ((LastMoveToState.RawMotionState.Flags & RawMotionFlags.ForwardCommand) != 0 && LastMoveToState.RawMotionState.ForwardHoldKey == HoldKey.Invalid)
            {
                if (DebugPlayerMoveToStatePhysics)
                    Console.WriteLine("Re-applying movement: " + LastMoveToState.RawMotionState.Flags);

                OnMoveToState(LastMoveToState);

                // re-broadcast MoveToState to other clients only
                EnqueueBroadcast(false, new GameMessageUpdateMotion(this, CurrentMovementData));
            }
            LastMoveToState = null;
        }*/

        if (MagicState.IsCasting && MagicState.PendingTurnRelease)
        {
            CheckTurn();
        }
    }

    /// <summary>
    /// The maximum rate UpdatePosition packets from MoveToState will be broadcast for each player
    /// AutonomousPosition still always broadcasts UpdatePosition
    ///
    /// The default value (1 second) was estimated from this retail video:
    /// https://youtu.be/o5lp7hWhtWQ?t=112
    ///
    /// If you wish for players to glitch around less during powerslides, lower this value
    /// </summary>
    public static TimeSpan MoveToState_UpdatePosition_Threshold = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Used by physics engine to actually update a player position
    /// Automatically notifies clients of updated position
    /// </summary>
    /// <param name="newPosition">The new position being requested, before verification through physics engine</param>
    /// <returns>TRUE if object moves to a different landblock</returns>
    public bool UpdatePlayerPosition(ACE.Entity.Position newPosition, bool forceUpdate = false)
    {
        //Console.WriteLine($"{Name}.UpdatePlayerPhysics({newPosition}, {forceUpdate}, {Teleporting})");
        var verifyContact = false;

        // possible bug: while teleporting, client can still send AutoPos packets from old landblock
        if (Teleporting && !forceUpdate)
        {
            return false;
        }

        // pre-validate movement
        if (!ValidateMovement(newPosition))
        {
            _log.Error(
                $"{Name}.UpdatePlayerPosition() - movement pre-validation failed from {Location} to {newPosition}"
            );
            return false;
        }

        try
        {
            if (!forceUpdate) // This is needed beacuse this function might be called recursively
            {
                stopwatch.Restart();
            }

            var success = true;

            // anti-blink: set when the closed-door scan runs for this move, so the on-door-plane candidate it
            // computes can be committed once (and only if) the move is actually accepted further down
            var doorCheckRan = false;
            uint onPlaneCandidate = 0;

            if (PhysicsObj != null)
            {
                var distSq = Location.SquaredDistanceTo(newPosition);

                // A player who has been moved to another instance is still in a cell of the instance they left until the physics update runs.
                // That has to happen even if they are moving to exactly the place they are at.
                var inWrongInstance = PhysicsObj.CurCell != null && PhysicsObj.CurCell.Instance != InstanceId;

                if (distSq > PhysicsGlobals.EpsilonSq || inWrongInstance)
                {
                    /*var p = new Physics.Common.Position(newPosition);
                    var dist = PhysicsObj.Position.Distance(p);
                    Console.WriteLine($"Dist: {dist}");*/

                    if (newPosition.Landblock == 0x18A && Location.Landblock != 0x18A)
                    {
                        _log.Information($"{Name} is getting swanky");
                    }

                    if (!Teleporting)
                    {
                        var blockDist = PhysicsObj.GetBlockDist(Location.Cell, newPosition.Cell);

                        // verify movement
                        if (distSq > MaxSpeedSq && blockDist > 1)
                        {
                            //Session.Network.EnqueueSend(new GameMessageSystemChat("Movement error", ChatMessageType.Broadcast));
                            _log.Warning(
                                $"MOVEMENT SPEED: {Name} trying to move from {Location} to {newPosition}, speed: {Math.Sqrt(distSq)}"
                            );
                            return false;
                        }

                        // verify z-pos
                        if (
                            blockDist == 0
                            && LastGroundPos != null
                            && newPosition.PositionZ - LastGroundPos.PositionZ > 10
                            && DateTime.UtcNow - LastJumpTime > TimeSpan.FromSeconds(1)
                            && GetCreatureSkill(Skill.Jump).Current < 1000
                        )
                        {
                            verifyContact = true;
                        }

                        // verify closed doors - a client plugin can delete a door from its own world and walk
                        // through it. Player movement is client-authoritative (Location is assigned from
                        // newPosition below, and update_object_server force-sets RequestPos over the transition
                        // result), so this is the only place the server compares the requested path against door
                        // state it owns. Cloaked staff are exempt.
                        if (
                            PropertyManager.GetBool("anti_blink_door_detection").Item
                            && CloakStatus != CloakStatus.On
                        )
                        {
                            var blockingDoor = CheckDoorCollision(Location, newPosition, out onPlaneCandidate);
                            doorCheckRan = true;

                            if (blockingDoor != null)
                            {
                                HandleBlinkDetection(blockingDoor, newPosition);
                                return false;
                            }
                        }
                    }

                    var curCell = LScape.get_landcell(newPosition.Cell, InstanceId);
                    if (curCell != null)
                    {
                        //if (PhysicsObj.CurCell == null || curCell.ID != PhysicsObj.CurCell.ID)
                        //PhysicsObj.change_cell_server(curCell);

                        PhysicsObj.set_request_pos(
                            newPosition.Pos,
                            newPosition.Rotation,
                            curCell,
                            Location.LandblockId.Raw
                        );
                        if (FastTick)
                        {
                            success = PhysicsObj.update_object_server_new();
                        }
                        else
                        {
                            success = PhysicsObj.update_object_server();
                        }

                        if (PhysicsObj.CurCell == null && curCell.ID >> 16 != 0x18A)
                        {
                            PhysicsObj.CurCell = curCell;
                        }

                        if (verifyContact && IsJumping)
                        {
                            var blockDist = PhysicsObj.GetBlockDist(newPosition.Cell, LastGroundPos.Cell);

                            if (blockDist <= 1)
                            {
                                _log.Warning(
                                    $"z-pos hacking detected for {Name}, lastGroundPos: {LastGroundPos.ToLOCString()} - requestPos: {newPosition.ToLOCString()}"
                                );
                                Location = new ACE.Entity.Position(LastGroundPos);
                                Sequences.GetNextSequence(SequenceType.ObjectForcePosition);
                                SendUpdatePosition();
                                return false;
                            }
                        }

                        CheckMonsters();
                    }
                    else if (!InstanceManager.CanEnter(InstanceId, newPosition.LandblockId))
                    {
                        // There is nothing there. The client walks by itself and knows the whole world, so it has gone past the edge of what
                        // this instance is made of (or, in the persistent world, into a landblock that only exists in instances). Physics stops
                        // at the edge, so the client has to be put back, the same way as when its height is not possible. If the position were
                        // let through, the client would be somewhere the server does not have, it would throw away everything it was shown
                        // (the server would still think it has it, and would not send it again), and the player would keep going.
                        Sequences.GetNextSequence(SequenceType.ObjectForcePosition);
                        SendUpdatePosition();
                        return false;
                    }
                }
                else
                {
                    PhysicsObj.Position.Frame.Orientation = newPosition.Rotation;
                }
            }

            // double update path: landblock physics update -> updateplayerphysics() -> update_object_server() -> Teleport() -> updateplayerphysics() -> return to end of original branch
            if (Teleporting && !forceUpdate)
            {
                return true;
            }

            if (!success)
            {
                return false;
            }

            var landblockUpdate = Location.Cell >> 16 != newPosition.Cell >> 16;

            Location = newPosition;

            // newPosition is only committed to Location here, past every later rejection path above (the z-hack
            // rubber-band, the instance-edge put-back, and `if (!success) return false;`) - so the on-door-plane
            // candidate CheckDoorCollision computed for it is only now safe to commit.
            if (doorCheckRan)
            {
                CommitOnDoorPlaneRecord(onPlaneCandidate);
            }

            if (RecordCast.Enabled)
            {
                RecordCast.Log($"CurPos: {Location.ToLOCString()}");
            }

            if (
                RequestedLocationBroadcast
                || DateTime.UtcNow - LastUpdatePosition >= MoveToState_UpdatePosition_Threshold
            )
            {
                SendUpdatePosition();
            }
            else
            {
                Session.Network.EnqueueSend(new GameMessageUpdatePosition(this));
            }

            if (!InUpdate)
            {
                LandblockManager.RelocateObjectForPhysics(this, true);
            }

            // an instance can have a margin that players are turned back from, and has to know where they last were inside it
            if (InstanceId != LScape.PersistentInstance && !Teleporting)
            {
                InstanceManager.OnPlayerMoved(this);
            }

            return landblockUpdate;
        }
        finally
        {
            if (!forceUpdate) // This is needed beacuse this function might be called recursively
            {
                var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                ServerPerformanceMonitor.AddToCumulativeEvent(
                    ServerPerformanceMonitor.CumulativeEventHistoryType.Player_Tick_UpdateObjectPhysics,
                    elapsedSeconds
                );
                if (elapsedSeconds >= 0.100) // Yea, that ain't good....
                {
                    _log.Warning(
                        $"[PERFORMANCE][PHYSICS] {Guid}:{Name} took {(elapsedSeconds * 1000):N1} ms to process UpdatePlayerPosition() at loc: {Location}"
                    );
                }
                else if (elapsedSeconds >= 0.010)
                {
                    _log.Debug(
                        "[PERFORMANCE][PHYSICS] {Guid}:{Name} took {ElapsedTimeMs:N1} ms to process UpdatePlayerPosition() at loc: {Location}",
                        Guid,
                        Name,
                        (elapsedSeconds * 1000),
                        Location
                    );
                }
            }
        }
    }

    #region Anti-blink door detection

    // Ported from ACE-DreamWeave (https://github.com/Awful-Waffle-Rofl/ACE-DreamWeave, AGPL-3.0). Differences from
    // the original: the pure geometry lives in AntiBlinkGeometry (Player's static initializers need the world
    // database, so tests cannot touch Player statics), instances are compared through WorldObject.InstanceId
    // (Daralet does not carry an instance on Position), and Daralet's extra instance-edge rejection path in
    // UpdatePlayerPosition is covered by the same commit-after-accept ordering as the others.

    /// <summary>
    /// Rate limit for the player-facing notice and the audit record, so a client that keeps re-sending a
    /// rejected position cannot spam either. The rubber-band itself is never rate limited.
    /// </summary>
    private DateTime lastBlinkNoticeTime = DateTime.MinValue;

    private static readonly TimeSpan BlinkNoticeCooldown = TimeSpan.FromSeconds(5);

    /// <summary>Door whose plane the last ACCEPTED position sat on (within DoorPlaneTolerance), 0 if none.</summary>
    private uint onDoorPlaneGuid;

    /// <summary>Unix time the player first arrived on that door's plane. Compared against Door.CloseTimestamp.</summary>
    private double onDoorPlaneSince;

    /// <summary>
    /// Returns the closed door the requested movement passes through, or null if it passes through none.
    /// Also computes the on-door-plane candidate for the position movement is TARGETING, via
    /// <paramref name="onPlaneCandidateGuid"/> (0 if none) - it does NOT write onDoorPlaneGuid/onDoorPlaneSince
    /// itself, because this position is not yet accepted: UpdatePlayerPosition still has later rejection paths
    /// after this call returns null (the z-hack rubber-band under verifyContact/IsJumping, the instance-edge
    /// put-back, and `if (!success) return false;` after update_object_server), any of which would otherwise let
    /// a rejected position stamp a stale timestamp that could later satisfy IsLegitimatelyOnDoorPlane for a door
    /// the player never actually reached. The caller commits the candidate only once the move sticks, via
    /// CommitOnDoorPlaneRecord. This scan only runs while anti_blink_door_detection is on, so the record goes
    /// stale while the flag is off - toggling it back on while someone stands in a doorway enforces against them
    /// once, which is acceptable.
    /// </summary>
    private Door CheckDoorCollision(
        ACE.Entity.Position oldPosition,
        ACE.Entity.Position newPosition,
        out uint onPlaneCandidateGuid
    )
    {
        onPlaneCandidateGuid = 0;

        if (CurrentLandblock == null || oldPosition == null || newPosition == null || PhysicsObj?.ObjMaint == null)
        {
            return null;
        }

        var debug = PropertyManager.GetBool("anti_blink_debug").Item;
        var doorWidth = (float)PropertyManager.GetDouble("anti_blink_door_width", 3.0).Item;
        var zHeightLimit = (float)PropertyManager.GetDouble("anti_blink_z_height_limit", 2.0).Item;

        var pathStart = AntiBlinkGeometry.GetGlobalPos(oldPosition);
        var pathEnd = AntiBlinkGeometry.GetGlobalPos(newPosition);

        var checkedDoors = 0;

        Door blockingDoor = null;

        // the visible set is built from this player's own landblock and cells, so it is already scoped to
        // their instance; the InstanceId comparison below is belt-and-braces against another instance leaking in
        foreach (var obj in PhysicsObj.ObjMaint.GetVisibleObjectsValues())
        {
            if (obj.WeenieObj?.WorldObject is not Door door)
            {
                continue;
            }

            // IsOpen goes false the instant Close() starts, but Ethereal only clears when the animation
            // finishes in FinalizeClose - a player already in the doorway as it swings shut is not blinking
            if (door.IsOpen || door.Ethereal == true)
            {
                continue;
            }

            var doorPos = door.Location;

            if (doorPos == null || door.InstanceId != InstanceId)
            {
                continue;
            }

            // multi-floor guard: a door a storey above or below the lower end of this path is not on it
            if (
                !AntiBlinkGeometry.IsDoorWithinZ(
                    doorPos.PositionZ,
                    oldPosition.PositionZ,
                    newPosition.PositionZ,
                    zHeightLimit
                )
            )
            {
                continue;
            }

            checkedDoors++;

            var doorSegment = AntiBlinkGeometry.GetDoorSegment(doorPos, doorWidth);

            // If the player is ALREADY standing on this door's plane AND got there before it closed - they
            // were in an open doorway when it swung shut - then every move they make crosses it, in both
            // directions, and enforcing it would wedge them there permanently. A player who arrived on the
            // plane after CloseTimestamp got there through a door their client had already deleted.
            if (
                AntiBlinkGeometry.DistanceToSegmentSq(pathStart, doorSegment.Start, doorSegment.End)
                    < AntiBlinkGeometry.DoorPlaneToleranceSq
                && AntiBlinkGeometry.IsLegitimatelyOnDoorPlane(
                    onDoorPlaneGuid,
                    onDoorPlaneSince,
                    door.Guid.Full,
                    door.CloseTimestamp
                )
            )
            {
                if (debug)
                {
                    _log.Information(
                        "[BLINK DEBUG] {PlayerName} already on the plane of '{DoorName}' (0x{DoorGuid:X8}) since before it closed - not enforced",
                        Name,
                        door.Name,
                        door.Guid.Full
                    );
                }

                continue;
            }

            var intersection = AntiBlinkGeometry.GetSegmentIntersection(
                pathStart,
                pathEnd,
                doorSegment.Start,
                doorSegment.End
            );

            if (intersection == null)
            {
                continue;
            }

            if (debug)
            {
                _log.Information(
                    "[BLINK DEBUG] {PlayerName} path crosses '{DoorName}' (0x{DoorGuid:X8}) at ({CrossX:F2}, {CrossY:F2})",
                    Name,
                    door.Name,
                    door.Guid.Full,
                    intersection.Value.X,
                    intersection.Value.Y
                );
            }

            blockingDoor = door;
            break;
        }

        if (blockingDoor == null)
        {
            onPlaneCandidateGuid = FindOnDoorPlaneCandidate(
                pathEnd,
                oldPosition,
                newPosition,
                doorWidth,
                zHeightLimit
            );

            if (debug)
            {
                _log.Information(
                    "[BLINK DEBUG] {PlayerName} - {CheckedDoors} closed doors in range, no crossing, {From} -> {To}",
                    Name,
                    checkedDoors,
                    oldPosition.ToLOCString(),
                    newPosition.ToLOCString()
                );
            }
        }

        return blockingDoor;
    }

    /// <summary>
    /// Finds the guid of the door (0 if none) whose plane the position movement is TARGETING would end up
    /// standing on. Candidates include OPEN doors too (unlike CheckDoorCollision's blocking scan), since a
    /// player can legitimately end up standing on the plane of a door that is currently open. Pure lookup -
    /// does not touch onDoorPlaneGuid/onDoorPlaneSince; see CheckDoorCollision's doc comment for why.
    /// </summary>
    private uint FindOnDoorPlaneCandidate(
        Vector2 pathEnd,
        ACE.Entity.Position oldPosition,
        ACE.Entity.Position newPosition,
        float doorWidth,
        float zHeightLimit
    )
    {
        foreach (var obj in PhysicsObj.ObjMaint.GetVisibleObjectsValues())
        {
            if (obj.WeenieObj?.WorldObject is not Door door)
            {
                continue;
            }

            var doorPos = door.Location;

            if (doorPos == null || door.InstanceId != InstanceId)
            {
                continue;
            }

            if (
                !AntiBlinkGeometry.IsDoorWithinZ(
                    doorPos.PositionZ,
                    oldPosition.PositionZ,
                    newPosition.PositionZ,
                    zHeightLimit
                )
            )
            {
                continue;
            }

            var doorSegment = AntiBlinkGeometry.GetDoorSegment(doorPos, doorWidth);

            if (
                AntiBlinkGeometry.DistanceToSegmentSq(pathEnd, doorSegment.Start, doorSegment.End)
                < AntiBlinkGeometry.DoorPlaneToleranceSq
            )
            {
                return door.Guid.Full;
            }
        }

        return 0;
    }

    /// <summary>
    /// Commits an on-door-plane candidate computed by CheckDoorCollision, once the position it was computed
    /// for has actually been accepted as the player's new Location. Applying NextOnDoorPlaneRecord here -
    /// not inside CheckDoorCollision - is what keeps a later-rejected position from stamping
    /// onDoorPlaneSince for a plane the player never reached.
    /// </summary>
    private void CommitOnDoorPlaneRecord(uint candidateGuid)
    {
        var next = AntiBlinkGeometry.NextOnDoorPlaneRecord(
            onDoorPlaneGuid,
            onDoorPlaneSince,
            candidateGuid,
            Time.GetUnixTime()
        );

        onDoorPlaneGuid = next.Guid;
        onDoorPlaneSince = next.Since;
    }

    /// <summary>
    /// Rubber-bands the player back to their last valid position and records the attempt.
    /// </summary>
    private void HandleBlinkDetection(Door door, ACE.Entity.Position rejectedPosition)
    {
        var now = DateTime.UtcNow;

        if (now - lastBlinkNoticeTime >= BlinkNoticeCooldown)
        {
            lastBlinkNoticeTime = now;

            var doorName = door.Name ?? "unknown door";
            var doorLoc = door.Location?.ToLOCString() ?? "unknown";

            _log.Warning(
                "[BLINK] {PlayerName} (0x{PlayerGuid:X8}, account {AccountName}) crossed closed door '{DoorName}' at {DoorLoc} - rejected {From} -> {To}",
                Name,
                Guid.Full,
                Account?.AccountName,
                doorName,
                doorLoc,
                Location.ToLOCString(),
                rejectedPosition.ToLOCString()
            );

            PlayerManager.BroadcastToAuditChannel(
                null,
                $"[BLINK] {Name} (account {Account?.AccountName}) crossed closed door '{doorName}' at {doorLoc}"
            );

            Session?.Network?.EnqueueSend(
                new GameMessageSystemChat(
                    "Blink detected, relocating to last known valid position",
                    ChatMessageType.Broadcast
                )
            );
        }

        // Location is left exactly as it is, and that IS the rubber-band: the assignment from newPosition
        // happens further down UpdatePlayerPosition, after this check has already returned false, so
        // Location still holds the last position the server accepted. All that is needed is to bump the
        // force-position sequence and tell the client to go back to where the server still has them.
        Sequences.GetNextSequence(SequenceType.ObjectForcePosition);
        SendUpdatePosition();
    }

    #endregion

    private static HashSet<uint> buggedCells = new HashSet<uint>() { 0xD6990112, 0xD599012C };

    public bool ValidateMovement(ACE.Entity.Position newPosition)
    {
        if (CurrentLandblock == null)
        {
            return false;
        }

        if (!Teleporting && Location.Landblock != newPosition.Cell >> 16)
        {
            if ((Location.Cell & 0xFFFF) >= 0x100 && (newPosition.Cell & 0xFFFF) >= 0x100)
            {
                if (!buggedCells.Contains(Location.Cell) || !buggedCells.Contains(newPosition.Cell))
                {
                    return false;
                }
            }

            if (CurrentLandblock.IsDungeon)
            {
                var destBlock = LScape.get_landblock(newPosition.Cell, InstanceId);
                if (destBlock != null && destBlock.IsDungeon)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool SyncLocationWithPhysics()
    {
        if (PhysicsObj.CurCell == null)
        {
            Console.WriteLine($"{Name}.SyncLocationWithPhysics(): CurCell is null!");
            return false;
        }

        var blockcell = PhysicsObj.Position.ObjCellID;
        var pos = PhysicsObj.Position.Frame.Origin;
        var rotate = PhysicsObj.Position.Frame.Orientation;

        var landblockUpdate = blockcell << 16 != CurrentLandblock.Id.Landblock;

        Location = new ACE.Entity.Position(blockcell, pos, rotate);

        return landblockUpdate;
    }

    private bool gagNoticeSent = false;

    public void GagsTick()
    {
        if (IsGagged)
        {
            if (!gagNoticeSent)
            {
                SendGagNotice();
                gagNoticeSent = true;
            }

            // check for gag expiration, if expired, remove gag.
            GagDuration -= CachedHeartbeatInterval;

            if (GagDuration <= 0)
            {
                IsGagged = false;
                GagTimestamp = 0;
                GagDuration = 0;
                SaveBiotaToDatabase();
                SendUngagNotice();
                gagNoticeSent = false;
            }
        }
    }

    /// <summary>
    /// Prepare new action to run on this player
    /// </summary>
    public override void EnqueueAction(IAction action)
    {
        actionQueue.EnqueueAction(action);
    }

    /// <summary>
    /// Called every ~5 secs for equipped mana consuming items
    /// </summary>
    public void ManaConsumersTick()
    {
        if (!EquippedObjectsLoaded)
        {
            return;
        }

        foreach (var item in EquippedObjects.Values)
        {
            if (!item.IsAffecting)
            {
                continue;
            }

            if (CombatMode == CombatMode.NonCombat)
            {
                continue;
            }

            if (item.ItemCurMana == null || item.ItemMaxMana == null || item.ManaRate == null)
            {
                continue;
            }

            var burnRate = -item.ManaRate.Value;

            if (LumAugItemManaUsage != 0)
            {
                burnRate *= GetNegativeRatingMod(LumAugItemManaUsage * 5);
            }

            burnRate *= 1.0f - Jewel.GetJewelEffectMod(this, PropertyInt.GearItemManaUsage);

            item.ItemManaRateAccumulator += (float)(burnRate * CachedHeartbeatInterval);

            if (item.ItemManaRateAccumulator < 1)
            {
                continue;
            }

            var manaToBurn = (int)Math.Floor(item.ItemManaRateAccumulator);

            if (manaToBurn > item.ItemCurMana)
            {
                manaToBurn = item.ItemCurMana.Value;
            }

            item.ItemCurMana -= manaToBurn;

            item.ItemManaRateAccumulator -= manaToBurn;

            if (item.ItemCurMana > 0)
            {
                CheckLowMana(item, burnRate);
            }
            else
            {
                HandleManaDepleted(item);
            }
        }
    }

    private bool CheckLowMana(WorldObject item, double burnRate)
    {
        const int lowManaWarningSeconds = 120;

        var secondsUntilEmpty = item.ItemCurMana / burnRate;

        if (secondsUntilEmpty > lowManaWarningSeconds)
        {
            item.ItemManaDepletionMessage = false;
            return false;
        }
        if (!item.ItemManaDepletionMessage)
        {
            Session.Network.EnqueueSend(
                new GameMessageSystemChat($"Your {item.Name} is low on Mana.", ChatMessageType.Magic)
            );
            item.ItemManaDepletionMessage = true;
        }
        return true;
    }

    private void HandleManaDepleted(WorldObject item)
    {
        var msg = new GameMessageSystemChat($"Your {item.Name} is out of Mana.", ChatMessageType.Magic);
        var sound = new GameMessageSound(Guid, Sound.ItemManaDepleted);
        Session.Network.EnqueueSend(msg, sound);

        // unsure if these messages / sounds were ever sent in retail,
        // or if it just purged the enchantments invisibly
        // doing a delay here to prevent 'SpellExpired' sounds from overlapping with 'ItemManaDepleted'
        var actionChain = new ActionChain();
        actionChain.AddDelaySeconds(2.0f);
        actionChain.AddAction(
            this,
            () =>
            {
                foreach (var spellId in item.Biota.GetKnownSpellsIds(item.BiotaDatabaseLock))
                {
                    RemoveItemSpell(item, (uint)spellId);
                }
            }
        );
        actionChain.EnqueueChain();

        item.OnSpellsDeactivated();
    }

    public override void HandleMotionDone(uint motionID, bool success)
    {
        //Console.WriteLine($"{Name}.HandleMotionDone({(MotionCommand)motionID}, {success})");

        if (!FastTick)
        {
            return;
        }

        if (FoodState.IsChugging)
        {
            HandleMotionDone_UseConsumable(motionID, success);
        }

        if (MagicState.IsCasting)
        {
            HandleMotionDone_Magic(motionID, success);
        }
    }

    /// <summary>
    /// Called on Player heartbeat
    /// Decrement any build up effects that decrease over time
    /// </summary>
    private void HandleBuildUpEffects()
    {
        if (AdrenalineMeter > 0.0f)
        {
            AdrenalineMeter -= 0.05f;

            if (AdrenalineMeter < 0.0f)
            {
                AdrenalineMeter = 0.0f;
            }
        }

        if (ManaChargeMeter > 0.0f)
        {
            ManaChargeMeter -= 0.05f;

            if (ManaChargeMeter < 0.0f)
            {
                ManaChargeMeter = 0.0f;
            }
        }
    }
}
