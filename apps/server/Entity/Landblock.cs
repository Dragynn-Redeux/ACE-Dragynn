using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ACE.Common;
using ACE.Common.Performance;
using ACE.Database;
using ACE.Database.Models.World;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics.Common;
using ACE.Server.WorldObjects;
using Serilog;
using Encounter = ACE.Database.Models.World.Encounter;
using Position = ACE.Entity.Position;
using Quaternion = System.Numerics.Quaternion;
using RegenerationType = ACE.Entity.Enum.RegenerationType;
using WeenieType = ACE.Entity.Enum.WeenieType;

namespace ACE.Server.Entity;

/// <summary>
/// the gist of a landblock is that, generally, everything on it publishes
/// to and subscribes to everything else in the landblock.  x/y in an outdoor
/// landblock goes from 0 to 192.  "indoor" (dungeon) landblocks have no
/// functional limit as players can't freely roam in/out of them
/// </summary>
public class Landblock : IActor
{
    private readonly ILogger _log = Log.ForContext<Landblock>();

    public static float AdjacencyLoadRange { get; } = 96f;
    public static float OutdoorChatRange { get; } = 75f;
    public static float IndoorChatRange { get; } = 25f;
    public static float MaxXY { get; } = 192f;
    public static float MaxObjectRange { get; } = 192f;
    public static float MaxObjectGhostRange { get; } = 250f;

    public LandblockId Id { get; }

    /// <summary>
    /// Flag indicates if this landblock is permanently loaded (for example, towns on high-traffic servers)
    /// </summary>
    public bool Permaload = false;

    /// <summary>
    /// Flag indicates if this landblock has no keep alive objects
    /// </summary>
    public bool HasNoKeepAliveObjects = true;

    /// <summary>
    /// This must be true before a player enters a landblock.
    /// This prevents a player from possibly pasing through a door that hasn't spawned in yet, and other scenarios.
    /// </summary>
    public bool CreateWorldObjectsCompleted { get; private set; }

    private DateTime lastActiveTime;
    public TimeSpan timeDormant;

    /// <summary>
    /// Dormant landblocks suppress Monster AI ticking and physics processing
    /// </summary>
    public bool IsDormant;

    private readonly Dictionary<ObjectGuid, WorldObject> worldObjects = new Dictionary<ObjectGuid, WorldObject>();
    private readonly Dictionary<ObjectGuid, WorldObject> pendingAdditions = new Dictionary<ObjectGuid, WorldObject>();
    private readonly List<ObjectGuid> pendingRemovals = new List<ObjectGuid>();

    // Cache used for Tick efficiency
    private readonly List<Player> players = new List<Player>();
    private readonly LinkedList<Creature> sortedCreaturesByNextTick = new LinkedList<Creature>();
    private readonly LinkedList<WorldObject> sortedWorldObjectsByNextHeartbeat = new LinkedList<WorldObject>();
    private readonly LinkedList<WorldObject> sortedGeneratorsByNextGeneratorUpdate = new LinkedList<WorldObject>();
    private readonly LinkedList<WorldObject> sortedGeneratorsByNextRegeneration = new LinkedList<WorldObject>();

    /// <summary>
    /// This is used to detect and manage cross-landblock group (which is potentially cross-thread) operations.
    /// </summary>
    public LandblockGroup CurrentLandblockGroup { get; internal set; }

    public List<Landblock> Adjacents = new List<Landblock>();

    private readonly ActionQueue actionQueue = new ActionQueue();

    /// <summary>
    /// Landblocks heartbeat every 5 seconds
    /// </summary>
    private static readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(5);

    private DateTime lastHeartBeat = DateTime.MinValue;

    /// <summary>
    /// Landblock items will be saved to the database every 5 minutes
    /// </summary>
    private static readonly TimeSpan databaseSaveInterval = TimeSpan.FromMinutes(5);

    private DateTime lastDatabaseSave = DateTime.MinValue;

    /// <summary>
    /// Landblocks which have been inactive for this many seconds will be dormant
    /// </summary>
    private static readonly TimeSpan dormantInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Landblocks which have been inactive for this many seconds will be unloaded
    /// </summary>
    public static readonly TimeSpan UnloadInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The clientlib backing store landblock
    /// Eventually these classes could be merged, but for now they are separate...
    /// </summary>
    public Physics.Common.Landblock PhysicsLandblock { get; }

    public CellLandblock CellLandblock { get; }
    public LandblockInfo LandblockInfo { get; }

    /// <summary>
    /// The landblock static meshes for
    /// collision detection and physics simulation
    /// </summary>
    public LandblockMesh LandblockMesh { get; private set; }

    public List<ModelMesh> LandObjects { get; private set; }
    public List<ModelMesh> Buildings { get; private set; }
    public List<ModelMesh> WeenieMeshes { get; private set; }
    public List<ModelMesh> Scenery { get; private set; }

    public readonly RateMonitor Monitor5m = new RateMonitor();
    private readonly TimeSpan last5mClearInteval = TimeSpan.FromMinutes(5);
    private DateTime last5mClear;
    public readonly RateMonitor Monitor1h = new RateMonitor();
    private readonly TimeSpan last1hClearInteval = TimeSpan.FromHours(1);
    private DateTime last1hClear;
    private bool monitorsRequireEventStart = true;

    // Used for cumulative ServerPerformanceMonitor event recording
    private readonly Stopwatch stopwatch = new Stopwatch();

    private EnvironChangeType fogColor;

    public EnvironChangeType FogColor
    {
        get
        {
            if (LandblockManager.GlobalFogColor.HasValue)
            {
                return LandblockManager.GlobalFogColor.Value;
            }

            return fogColor;
        }
        set => fogColor = value;
    }

    public List<Player> ActivePlayers
    {
        get
        {
            var PlayerList = new List<Player>();
            PlayerList = players;
            return PlayerList;
        }
    }

    public Fellowship CapstoneFellowship;
    public Dictionary<string, double> CapstonePlayers = new();
    public double CapstoneUptime = 0;

    public double LandblockLootQualityMod = 0.0;
    public double LandblockLethalityMod = 0.0;

    public List<uint> PlayerAccountIds = new List<uint>();

    public Landblock(LandblockId id)
    {
        //log.DebugFormat("Landblock({0:X8})", (id.Raw | 0xFFFF));

        Id = id;

        CellLandblock = DatManager.CellDat.ReadFromDat<CellLandblock>(Id.Raw | 0xFFFF);
        LandblockInfo = DatManager.CellDat.ReadFromDat<LandblockInfo>((uint)Id.Landblock << 16 | 0xFFFE);

        lastActiveTime = DateTime.UtcNow;

        var cellLandblock = DBObj.GetCellLandblock(Id.Raw | 0xFFFF);
        PhysicsLandblock = new Physics.Common.Landblock(cellLandblock);
    }

    public void Init(bool reload = false)
    {
        if (!reload)
        {
            PhysicsLandblock.PostInit();
        }

        Task.Run(() =>
        {
            CreateWorldObjects();

            SpawnDynamicShardObjects();

            SpawnEncounters();
        });

        SetActiveMods();
        //LoadMeshes(objects);
    }

    /// <summary>
    /// Monster Locations, Generators<para />
    /// This will be called from a separate task from our constructor. Use thread safety when interacting with this landblock.
    /// </summary>
    private void CreateWorldObjects()
    {
        var objects = DatabaseManager.World.GetCachedInstancesByLandblock(Id.Landblock);
        var shardObjects = DatabaseManager.Shard.BaseDatabase.GetStaticObjectsByLandblock(Id.Landblock);
        var factoryObjects = WorldObjectFactory.CreateNewWorldObjects(objects, shardObjects);

        actionQueue.EnqueueAction(
            new ActionEventDelegate(() =>
            {
                // for mansion linking
                var houses = new List<House>();

                foreach (var fo in factoryObjects)
                {
                    WorldObject parent = null;
                    if (fo.WeenieType == WeenieType.House)
                    {
                        var house = fo as House;
                        Houses.Add(house);

                        if (fo.HouseType == HouseType.Mansion)
                        {
                            houses.Add(house);
                            house.LinkedHouses.Add(houses[0]);

                            if (houses.Count > 1)
                            {
                                houses[0].LinkedHouses.Add(house);
                                parent = houses[0];
                            }
                        }
                    }

                    AddWorldObject(fo);
                    fo.ActivateLinks(objects, shardObjects, parent);

                    if (fo.PhysicsObj != null)
                    {
                        fo.PhysicsObj.Order = 0;
                    }
                }

                CreateWorldObjectsCompleted = true;

                PhysicsLandblock.SortObjects();
            })
        );

        switch (Id.Landblock)
        {
            case 48543:
                var banks = shardObjects.Count(shardObject => shardObject.WeenieClassId is 1010100);

                _log.Information(
                    "Cragstone - Objects: {Objects}, ShardObjects: {ShardObjects} (banks = {Banks}), FactoryObjects: {FactoryObjects}",
                    objects.Count, shardObjects.Count, banks, factoryObjects.Count);
                break;
            case 59214:
                banks = shardObjects.Count(shardObject => shardObject.WeenieClassId is 1010100);

                _log.Information(
                    "Hebian-To - Objects: {Objects}, ShardObjects: {ShardObjects} (banks = {Banks}), FactoryObjects: {FactoryObjects}",
                    objects.Count, shardObjects.Count, banks, factoryObjects.Count);
                break;
            case 32912:
                banks = shardObjects.Count(shardObject => shardObject.WeenieClassId is 1010100);
                _log.Information(
                    "Zaikhal - Objects: {Objects}, ShardObjects: {ShardObjects} (banks = {Banks}), FactoryObjects: {FactoryObjects}",
                    objects.Count, shardObjects.Count, banks, factoryObjects.Count);
                break;
        }
    }

    /// <summary>
    /// Corpses<para />
    /// This will be called from a separate task from our constructor. Use thread safety when interacting with this landblock.
    /// </summary>
    private void SpawnDynamicShardObjects()
    {
        var dynamics = DatabaseManager.Shard.BaseDatabase.GetDynamicObjectsByLandblock(Id.Landblock);
        var factoryShardObjects = WorldObjectFactory.CreateWorldObjects(dynamics);

        actionQueue.EnqueueAction(
            new ActionEventDelegate(() =>
            {
                foreach (var fso in factoryShardObjects)
                {
                    AddWorldObject(fso);
                }
            })
        );
    }

    private class AvailableCell
    {
        public int CellX;
        public int CellY;
        public ushort TerrainType;

        public AvailableCell(int cellX, int cellY, ushort terrainType)
        {
            CellX = cellX;
            CellY = cellY;
            TerrainType = terrainType;
        }
    }

    private class EncounterInfo
    {
        public int Coords;
        public Encounter Encounter;
        public ushort TerrainType;

        public EncounterInfo(int coords, Encounter encounter, ushort terrainType)
        {
            Coords = coords;
            Encounter = encounter;
            TerrainType = terrainType;
        }
    }

    private ushort getTerrainType(int cellX, int cellY)
    {
        var terrain = PhysicsLandblock.get_terrain(cellX, cellY);
        var terrainType = (ushort)(terrain >> 2 & 0x1F);
        return terrainType;
    }

    /// <summary>
    /// Spawns the semi-randomized monsters scattered around the outdoors<para />
    /// This will be called from a separate task from our constructor. Use thread safety when interacting with this landblock.
    /// </summary>
    private void SpawnEncounters()
    {
        // get the encounter spawns for this landblock
        var encounters = DatabaseManager.World.GetCachedEncountersByLandblock(Id.Landblock, out var wasCached);

        var generatedEncounterIdList =
            new List<uint>(); // used only when "increase_minimum_encounter_spawn_density" is enabled

        if (PropertyManager.GetBool("increase_minimum_encounter_spawn_density").Item && !wasCached)
        {
            IncreaseMinimumEncounterSpawnDensity(encounters, generatedEncounterIdList);
        }

        foreach (var encounter in encounters)
        {
            var wo = WorldObjectFactory.CreateNewWorldObject(encounter.WeenieClassId);

            if (wo == null)
            {
                continue;
            }

            wo.SetProperty(PropertyBool.IsPseudoRandomGenerator, true);
            wo.GeneratorCooldownInterval = 180;

            if (generatedEncounterIdList.Contains(encounter.Id))
            {
                wo.SetProperty(PropertyFloat.DefaultScale, 0.5f);
                wo.SetProperty(
                    PropertyString.ShortDesc,
                    "Not a permanent encounter.\nAutomatically generated by the increase_minimum_encounter_spawn_density setting.\nDisabling the setting will remove this."
                );
            }
            else
            {
                wo.SetProperty(PropertyFloat.DefaultScale, 1.5f);
            }

            actionQueue.EnqueueAction(
                new ActionEventDelegate(() =>
                {
                    var xPos = Math.Clamp((encounter.CellX * 24.0f) + 12.0f, 0.5f, 191.5f);
                    var yPos = Math.Clamp((encounter.CellY * 24.0f) + 12.0f, 0.5f, 191.5f);

                    var pos = new Physics.Common.Position();
                    pos.ObjCellID = (uint)(Id.Landblock << 16) | 1;
                    pos.Frame = new Physics.Animation.AFrame(new Vector3(xPos, yPos, 0), Quaternion.Identity);
                    pos.adjust_to_outside();

                    pos.Frame.Origin.Z = PhysicsLandblock.GetZ(pos.Frame.Origin);

                    wo.Location = new Position(pos.ObjCellID, pos.Frame.Origin, pos.Frame.Orientation);

                    var sortCell = LScape.get_landcell(pos.ObjCellID) as SortCell;
                    if (sortCell != null && sortCell.has_building())
                    {
                        wo.Destroy();
                        return;
                    }

                    if (PropertyManager.GetBool("increase_minimum_encounter_spawn_density").Item)
                    {
                        // Avoid some less than ideal locations
                        if (
                            !wo.Location.IsWalkable()
                            || PhysicsLandblock.OnRoad(new Vector3(xPos, yPos, pos.Frame.Origin.Z))
                        )
                        {
                            wo.Destroy();
                            return;
                        }
                    }

                    if (PropertyManager.GetBool("override_encounter_spawn_rates").Item)
                    {
                        wo.RegenerationInterval = PropertyManager.GetDouble("encounter_regen_interval").Item;

                        wo.ReinitializeHeartbeats();

                        if (wo.Biota.PropertiesGenerator != null)
                        {
                            // While this may be ugly, it's done for performance reasons.
                            // Common weenie properties are not cloned into the bota on creation. Instead, the biota references simply point to the weenie collections.
                            // The problem here is that we want to update one of those common collection properties. If the biota is referencing the weenie collection,
                            // then we'll end up updating the global weenie (from the cache), instead of just this specific biota.
                            if (wo.Biota.PropertiesGenerator == wo.Weenie.PropertiesGenerator)
                            {
                                wo.Biota.PropertiesGenerator = new List<PropertiesGenerator>(
                                    wo.Weenie.PropertiesGenerator.Count
                                );

                                foreach (var record in wo.Weenie.PropertiesGenerator)
                                {
                                    wo.Biota.PropertiesGenerator.Add(record.Clone());
                                }
                            }

                            foreach (var profile in wo.Biota.PropertiesGenerator)
                            {
                                profile.Delay = (float)PropertyManager.GetDouble("encounter_delay").Item;
                            }
                        }
                    }

                    if (!AddWorldObject(wo))
                    {
                        wo.Destroy();
                    }
                })
            );
        }
    }

    /// <summary>
    /// Loads the meshes for the landblock<para />
    /// This isn't used by ACE, but we still retain it for the following reason:<para />
    /// its useful, concise, high level overview code for everything needed to load landblocks, all their objects, scenery, polygons
    /// without getting into all of the low level methods that acclient uses to do it
    /// </summary>
    private void LoadMeshes(List<LandblockInstance> objects)
    {
        LandblockMesh = new LandblockMesh(Id);
        LoadLandObjects();
        LoadBuildings();
        LoadWeenies(objects);
        LoadScenery();
    }

    /// <summary>
    /// Loads the meshes for the static landblock objects,
    /// also known as obstacles
    /// </summary>
    private void LoadLandObjects()
    {
        LandObjects = new List<ModelMesh>();

        foreach (var obj in LandblockInfo.Objects)
        {
            LandObjects.Add(new ModelMesh(obj.Id, obj.Frame));
        }
    }

    /// <summary>
    /// Loads the meshes for the buildings on the landblock
    /// </summary>
    private void LoadBuildings()
    {
        Buildings = new List<ModelMesh>();

        foreach (var obj in LandblockInfo.Buildings)
        {
            Buildings.Add(new ModelMesh(obj.ModelId, obj.Frame));
        }
    }

    /// <summary>
    /// Loads the meshes for the weenies on the landblock
    /// </summary>
    private void LoadWeenies(List<LandblockInstance> objects)
    {
        WeenieMeshes = new List<ModelMesh>();

        foreach (var obj in objects)
        {
            var weenie = DatabaseManager.World.GetCachedWeenie(obj.WeenieClassId);
            WeenieMeshes.Add(
                new ModelMesh(
                    weenie.GetProperty(PropertyDataId.Setup) ?? 0,
                    new DatLoader.Entity.Frame(
                        new Position(
                            obj.ObjCellId,
                            obj.OriginX,
                            obj.OriginY,
                            obj.OriginZ,
                            obj.AnglesX,
                            obj.AnglesY,
                            obj.AnglesZ,
                            obj.AnglesW
                        )
                    )
                )
            );
        }
    }

    /// <summary>
    /// Loads the meshes for the scenery on the landblock
    /// </summary>
    private void LoadScenery()
    {
        Scenery = Entity.Scenery.Load(this);
    }

    /// <summary>
    /// This should be called before TickLandblockGroupThreadSafeWork() and before Tick()
    /// </summary>
    public void TickPhysics(double portalYearTicks, ConcurrentBag<WorldObject> movedObjects)
    {
        if (IsDormant)
        {
            return;
        }

        Monitor5m.Restart();
        Monitor1h.Restart();
        monitorsRequireEventStart = false;

        ProcessPendingWorldObjectAdditionsAndRemovals();

        foreach (var wo in worldObjects.Values)
        {
            // set to TRUE if object changes landblock
            var landblockUpdate = wo.UpdateObjectPhysics();

            if (landblockUpdate)
            {
                movedObjects.Add(wo);
            }
        }

        Monitor5m.Pause();
        Monitor1h.Pause();
    }

    /// <summary>
    /// This will tick anything that can be multi-threaded safely using LandblockGroups as thread boundaries
    /// This should be called after TickPhysics() and before Tick()
    /// </summary>
    public void TickMultiThreadedWork(double currentUnixTime)
    {
        if (monitorsRequireEventStart)
        {
            Monitor5m.Restart();
            Monitor1h.Restart();
        }
        else
        {
            Monitor5m.Resume();
            Monitor1h.Resume();
        }

        stopwatch.Restart();
        // This will consist of the following work:
        // - this.CreateWorldObjects
        // - this.SpawnDynamicShardObjects
        // - this.SpawnEncounters
        // - Adding items back onto the landblock from failed player movements: Player_Inventory.cs DoHandleActionPutItemInContainer()
        // - Executing trade between two players: Player_Trade.cs FinalizeTrade()
        actionQueue.RunActions();
        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_RunActions,
            stopwatch.Elapsed.TotalSeconds
        );

        ProcessPendingWorldObjectAdditionsAndRemovals();

        // When a WorldObject Ticks, it can end up adding additional WorldObjects to this landblock
        if (!IsDormant)
        {
            stopwatch.Restart();
            while (sortedCreaturesByNextTick.Count > 0) // Monster_Tick()
            {
                var first = sortedCreaturesByNextTick.First.Value;

                // If they wanted to run before or at now
                if (first.NextMonsterTickTime <= currentUnixTime)
                {
                    sortedCreaturesByNextTick.RemoveFirst();
                    first.Monster_Tick(currentUnixTime);
                    sortedCreaturesByNextTick.AddLast(first); // All creatures tick at a fixed interval
                }
                else
                {
                    break;
                }
            }

            ServerPerformanceMonitor.AddToCumulativeEvent(
                ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_Monster_Tick,
                stopwatch.Elapsed.TotalSeconds
            );
        }

        stopwatch.Restart();
        while (sortedGeneratorsByNextGeneratorUpdate.Count > 0)
        {
            var first = sortedGeneratorsByNextGeneratorUpdate.First.Value;

            // If they wanted to run before or at now
            if (first.NextGeneratorUpdateTime <= currentUnixTime)
            {
                sortedGeneratorsByNextGeneratorUpdate.RemoveFirst();
                first.GeneratorUpdate(currentUnixTime);
                //InsertWorldObjectIntoSortedGeneratorUpdateList(first);
                sortedGeneratorsByNextGeneratorUpdate.AddLast(first);
            }
            else
            {
                break;
            }
        }

        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_GeneratorUpdate,
            stopwatch.Elapsed.TotalSeconds
        );

        stopwatch.Restart();
        while (sortedGeneratorsByNextRegeneration.Count > 0) // GeneratorRegeneration()
        {
            var first = sortedGeneratorsByNextRegeneration.First.Value;

            //Console.WriteLine($"{first.Name}.Landblock_Tick_GeneratorRegeneration({currentUnixTime})");

            // If they wanted to run before or at now
            if (first.NextGeneratorRegenerationTime <= currentUnixTime)
            {
                sortedGeneratorsByNextRegeneration.RemoveFirst();
                first.GeneratorRegeneration(currentUnixTime);
                InsertWorldObjectIntoSortedGeneratorRegenerationList(
                    first); // Generators can have regenerations at different intervals
            }
            else
            {
                break;
            }
        }

        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_GeneratorRegeneration,
            stopwatch.Elapsed.TotalSeconds
        );

        // Heartbeat
        stopwatch.Restart();
        if (lastHeartBeat + heartbeatInterval <= DateTime.UtcNow)
        {
            var thisHeartBeat = DateTime.UtcNow;

            ProcessPendingWorldObjectAdditionsAndRemovals();

            // Decay world objects
            if (lastHeartBeat != DateTime.MinValue)
            {
                foreach (var wo in worldObjects.Values)
                {
                    if (wo.IsDecayable())
                    {
                        wo.Decay(thisHeartBeat - lastHeartBeat);
                    }
                }
            }

            if (CapstoneTeleportLocations.Keys.Contains(Id))
            {
                CapstoneUptime += 5;
            }

            foreach (var player in players)
            {
                if (player.PatronAccountId != null)
                {
                    if (PlayerAccountIds.Contains((uint)player.PatronAccountId))
                    {
                        player.WithPatron = true;
                    }
                    else
                    {
                        player.WithPatron = false;
                    }
                }
            }

            if (!Permaload && HasNoKeepAliveObjects)
            {
                if (lastActiveTime + dormantInterval < thisHeartBeat)
                {
                    if (!IsDormant)
                    {
                        var spellProjectiles = worldObjects.Values.Where(i => i is SpellProjectile).ToList();
                        foreach (var spellProjectile in spellProjectiles)
                        {
                            spellProjectile.PhysicsObj.set_active(false);
                            spellProjectile.Destroy();
                        }
                    }

                    IsDormant = true;
                }

                if (lastActiveTime + UnloadInterval < thisHeartBeat)
                {
                    LandblockManager.AddToDestructionQueue(this);
                }
            }

            //log.Info($"Landblock {Id.ToString()}.Tick({currentUnixTime}).Landblock_Tick_Heartbeat: thisHeartBeat: {thisHeartBeat.ToString()} | lastHeartBeat: {lastHeartBeat.ToString()} | worldObjects.Count: {worldObjects.Count()}");
            lastHeartBeat = thisHeartBeat;

            timeDormant = lastHeartBeat - lastActiveTime;
        }

        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_Heartbeat,
            stopwatch.Elapsed.TotalSeconds
        );

        // Database Save
        stopwatch.Restart();
        if (lastDatabaseSave + databaseSaveInterval <= DateTime.UtcNow)
        {
            ProcessPendingWorldObjectAdditionsAndRemovals();

            SaveDB();
            lastDatabaseSave = DateTime.UtcNow;
        }

        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_Database_Save,
            stopwatch.Elapsed.TotalSeconds
        );

        Monitor5m.Pause();
        Monitor1h.Pause();
    }

    /// <summary>
    /// This will tick everything that should be done single threaded on the main ACE World thread
    /// This should be called after TickPhysics() and after Tick()
    /// </summary>
    public void TickSingleThreadedWork(double currentUnixTime)
    {
        if (monitorsRequireEventStart)
        {
            Monitor5m.Restart();
            Monitor1h.Restart();
        }
        else
        {
            Monitor5m.Resume();
            Monitor1h.Resume();
        }

        ProcessPendingWorldObjectAdditionsAndRemovals();

        stopwatch.Restart();
        foreach (var player in players)
        {
            player.Player_Tick(currentUnixTime);
        }

        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_Player_Tick,
            stopwatch.Elapsed.TotalSeconds
        );

        stopwatch.Restart();
        while (sortedWorldObjectsByNextHeartbeat.Count > 0) // Heartbeat()
        {
            var first = sortedWorldObjectsByNextHeartbeat.First.Value;

            // If they wanted to run before or at now
            if (first.NextHeartbeatTime <= currentUnixTime)
            {
                sortedWorldObjectsByNextHeartbeat.RemoveFirst();
                first.Heartbeat(currentUnixTime);
                InsertWorldObjectIntoSortedHeartbeatList(
                    first); // WorldObjects can have heartbeats at different intervals
            }
            else
            {
                break;
            }
        }

        ServerPerformanceMonitor.AddToCumulativeEvent(
            ServerPerformanceMonitor.CumulativeEventHistoryType.Landblock_Tick_WorldObject_Heartbeat,
            stopwatch.Elapsed.TotalSeconds
        );

        Monitor5m.RegisterEventEnd();
        Monitor1h.RegisterEventEnd();
        monitorsRequireEventStart = true;

        if (DateTime.UtcNow - last5mClear >= last5mClearInteval)
        {
            Monitor5m.ClearEventHistory();
            last5mClear = DateTime.UtcNow;
        }

        if (DateTime.UtcNow - last1hClear >= last1hClearInteval)
        {
            Monitor1h.ClearEventHistory();
            last1hClear = DateTime.UtcNow;
        }
    }

    private void ProcessPendingWorldObjectAdditionsAndRemovals()
    {
        if (pendingAdditions.Count > 0)
        {
            foreach (var kvp in pendingAdditions)
            {
                worldObjects[kvp.Key] = kvp.Value;

                if (kvp.Value is Player player)
                {
                    players.Add(player);
                    PlayerAccountIds.Add(player.Account.AccountId);
                }
                else if (kvp.Value is Creature creature)
                {
                    sortedCreaturesByNextTick.AddLast(creature);
                }

                InsertWorldObjectIntoSortedHeartbeatList(kvp.Value);
                InsertWorldObjectIntoSortedGeneratorUpdateList(kvp.Value);
                InsertWorldObjectIntoSortedGeneratorRegenerationList(kvp.Value);

                if (kvp.Value.WeenieClassId == 80007) // Landblock KeepAlive weenie (ACE custom)
                {
                    HasNoKeepAliveObjects = false;
                }
            }

            pendingAdditions.Clear();
        }

        if (pendingRemovals.Count > 0)
        {
            foreach (var objectGuid in pendingRemovals)
            {
                if (worldObjects.Remove(objectGuid, out var wo))
                {
                    if (wo is Player player)
                    {
                        players.Remove(player);
                        PlayerAccountIds.Remove(player.Account.AccountId);
                    }
                    else if (wo is Creature creature)
                    {
                        sortedCreaturesByNextTick.Remove(creature);
                    }

                    sortedWorldObjectsByNextHeartbeat.Remove(wo);
                    sortedGeneratorsByNextGeneratorUpdate.Remove(wo);
                    sortedGeneratorsByNextRegeneration.Remove(wo);

                    if (wo.WeenieClassId == 80007) // Landblock KeepAlive weenie (ACE custom)
                    {
                        var keepAliveObject = worldObjects.Values.FirstOrDefault(w => w.WeenieClassId == 80007);

                        if (keepAliveObject == null)
                        {
                            HasNoKeepAliveObjects = true;
                        }
                    }
                }
            }

            pendingRemovals.Clear();
        }
    }

    private void InsertWorldObjectIntoSortedHeartbeatList(WorldObject worldObject)
    {
        // If you want to add checks to exclude certain object types from heartbeating, you would do it here
        if (worldObject.NextHeartbeatTime == double.MaxValue)
        {
            return;
        }

        if (sortedWorldObjectsByNextHeartbeat.Count == 0)
        {
            sortedWorldObjectsByNextHeartbeat.AddFirst(worldObject);
            return;
        }

        if (sortedWorldObjectsByNextHeartbeat.Last.Value.NextHeartbeatTime <= worldObject.NextHeartbeatTime)
        {
            sortedWorldObjectsByNextHeartbeat.AddLast(worldObject);
            return;
        }

        var currentNode = sortedWorldObjectsByNextHeartbeat.First;

        while (currentNode != null)
        {
            if (worldObject.NextHeartbeatTime <= currentNode.Value.NextHeartbeatTime)
            {
                sortedWorldObjectsByNextHeartbeat.AddBefore(currentNode, worldObject);
                return;
            }

            currentNode = currentNode.Next;
        }

        sortedWorldObjectsByNextHeartbeat.AddLast(worldObject); // This line really shouldn't be hit
    }

    private void InsertWorldObjectIntoSortedGeneratorUpdateList(WorldObject worldObject)
    {
        // If you want to add checks to exclude certain object types from heartbeating, you would do it here
        if (worldObject.NextGeneratorUpdateTime == double.MaxValue)
        {
            return;
        }

        if (sortedGeneratorsByNextGeneratorUpdate.Count == 0)
        {
            sortedGeneratorsByNextGeneratorUpdate.AddFirst(worldObject);
            return;
        }

        if (
            sortedGeneratorsByNextGeneratorUpdate.Last.Value.NextGeneratorUpdateTime
            <= worldObject.NextGeneratorUpdateTime
        )
        {
            sortedGeneratorsByNextGeneratorUpdate.AddLast(worldObject);
            return;
        }

        var currentNode = sortedGeneratorsByNextGeneratorUpdate.First;

        while (currentNode != null)
        {
            if (worldObject.NextGeneratorUpdateTime <= currentNode.Value.NextGeneratorUpdateTime)
            {
                sortedGeneratorsByNextGeneratorUpdate.AddBefore(currentNode, worldObject);
                return;
            }

            currentNode = currentNode.Next;
        }

        sortedGeneratorsByNextGeneratorUpdate.AddLast(worldObject); // This line really shouldn't be hit
    }

    private void InsertWorldObjectIntoSortedGeneratorRegenerationList(WorldObject worldObject)
    {
        // If you want to add checks to exclude certain object types from heartbeating, you would do it here
        if (worldObject.NextGeneratorRegenerationTime == double.MaxValue)
        {
            return;
        }

        if (sortedGeneratorsByNextRegeneration.Count == 0)
        {
            sortedGeneratorsByNextRegeneration.AddFirst(worldObject);
            return;
        }

        if (
            sortedGeneratorsByNextRegeneration.Last.Value.NextGeneratorRegenerationTime
            <= worldObject.NextGeneratorRegenerationTime
        )
        {
            sortedGeneratorsByNextRegeneration.AddLast(worldObject);
            return;
        }

        var currentNode = sortedGeneratorsByNextRegeneration.First;

        while (currentNode != null)
        {
            if (worldObject.NextGeneratorRegenerationTime <= currentNode.Value.NextGeneratorRegenerationTime)
            {
                sortedGeneratorsByNextRegeneration.AddBefore(currentNode, worldObject);
                return;
            }

            currentNode = currentNode.Next;
        }

        sortedGeneratorsByNextRegeneration.AddLast(worldObject); // This line really shouldn't be hit
    }

    public void ResortWorldObjectIntoSortedGeneratorRegenerationList(WorldObject worldObject)
    {
        if (sortedGeneratorsByNextRegeneration.Contains(worldObject))
        {
            sortedGeneratorsByNextRegeneration.Remove(worldObject);
            InsertWorldObjectIntoSortedGeneratorRegenerationList(worldObject);
        }
    }

    public void EnqueueAction(IAction action)
    {
        actionQueue.EnqueueAction(action);
    }

    /// <summary>
    /// This will fail if the wo doesn't have a valid location.
    /// </summary>
    public bool AddWorldObject(WorldObject wo)
    {
        if (wo.Location == null)
        {
            _log.Debug(
                "Landblock 0x{LandblockId} failed to add 0x{BiotaId:X8} {BiotaName}. Invalid Location",
                Id,
                wo.Biota.Id,
                wo.Name
            );
            return false;
        }

        return AddWorldObjectInternal(wo);
    }

    public void AddWorldObjectForPhysics(WorldObject wo)
    {
        AddWorldObjectInternal(wo);
    }

    private bool AddWorldObjectInternal(WorldObject wo)
    {
        if (LandblockManager.CurrentlyTickingLandblockGroupsMultiThreaded)
        {
            if (
                CurrentLandblockGroup != null
                && CurrentLandblockGroup != LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value
            )
            {
                // Prevent possible multi-threaded crash
                // The following scenario can happen rarely in ACE, all in the same call stack with no ActionQueue usage:
                // Moster successfully lands an attack on a player that procs a cloak spell
                // The code goes through and does the LaunchSpellProjectiles() which adds the spell projectiles to (presumably) the players landblock
                // For some unknown reason, the LandblockGroup/Thread where the monster exists seems to be a different LandblockGroup/Thread where the spells are added to.
                // Maybe there's a player death race condition? Maybe it's a teleport race condition? I dunno.
                // Because this happens so rarely, and, it only seems to affect cloak projectiles, and, cloak projectiles are pretty benign, we simply don't add the object, and only log it as a warning.
                if (wo.WeenieType == WeenieType.ProjectileSpell)
                {
                    _log.Warning(
                        "Landblock 0x{LandblockId} entered AddWorldObjectInternal in a cross-thread operation for a ProjectileSpell. This is normally not an issue unless it's happening more than once an hour.",
                        Id
                    );
                    return false;
                }

                _log.Error("Landblock 0x{LandblockId} entered AddWorldObjectInternal in a cross-thread operation.", Id);
                _log.Error(
                    "Landblock 0x{LandblockId} CurrentLandblockGroup: {CurrentLandblockGroup}",
                    Id,
                    CurrentLandblockGroup
                );
                _log.Error(
                    "LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value: {CurrentMultiThreadedTickingLandblockGroupValue}",
                    LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value
                );

                _log.Error(
                    "wo: 0x{WorldObjectGuid}:{WorldObjectName} [{WorldObjectWeenieClassId} - {WorldObjectWeenieType}], previous landblock 0x{WorldObjectLandblockId}",
                    wo.Guid,
                    wo.Name,
                    wo.WeenieClassId,
                    wo.WeenieType,
                    wo.CurrentLandblock?.Id
                );

                if (wo.WeenieType == WeenieType.ProjectileSpell)
                {
                    if (wo.ProjectileSource != null)
                    {
                        _log.Error(
                            $"wo.ProjectileSource: 0x{wo.ProjectileSource?.Guid}:{wo.ProjectileSource?.Name}, position: {wo.ProjectileSource?.Location}"
                        );
                    }

                    if (wo.ProjectileTarget != null)
                    {
                        _log.Error(
                            $"wo.ProjectileTarget: 0x{wo.ProjectileTarget?.Guid}:{wo.ProjectileTarget?.Name}, position: {wo.ProjectileTarget?.Location}"
                        );
                    }
                }

                _log.Error(System.Environment.StackTrace);

                _log.Error("PLEASE REPORT THIS TO THE ACE DEV TEAM !!!");

                // This may still crash...
            }
        }

        wo.CurrentLandblock = this;

        if (wo.PhysicsObj == null)
        {
            wo.InitPhysicsObj();
        }
        else
        {
            wo.PhysicsObj.set_object_guid(wo.Guid); // re-add to ServerObjectManager
        }

        if (wo.PhysicsObj.CurCell == null)
        {
            var success = wo.AddPhysicsObj();
            if (!success)
            {
                wo.CurrentLandblock = null;

                if (wo.Generator != null)
                {
                    _log.Debug(
                        "AddWorldObjectInternal: couldn't spawn 0x{WorldObjectGuid}:{WorldObjectName} [{WorldObjectWeenieClassId} - {WorldObjectWeenieType}] at {WorldObjectLocation} from generator {WorldObjectGeneratorWeenieClassId} - 0x{WorldObjectGeneratorGuid}:{WorldObjectGeneratorName}",
                        wo.Guid,
                        wo.Name,
                        wo.WeenieClassId,
                        wo.WeenieType,
                        wo.Location.ToLOCString(),
                        wo.Generator.WeenieClassId,
                        wo.Generator.Guid,
                        wo.Generator.Name
                    );
                    wo.NotifyOfEvent(RegenerationType
                        .PickUp); // Notify generator the generated object is effectively destroyed, use Pickup to catch both cases.
                }
                else if
                    (wo.IsGenerator) // Some generators will fail random spawns if they're circumference spans over water or cliff edges
                {
                    _log.Debug(
                        "AddWorldObjectInternal: couldn't spawn generator 0x{WorldObjectGuid}:{WorldObjectName} [{WorldObjectWeenieClassId} - {WorldObjectWeenieType}] at {WorldObjectLocation}",
                        wo.Guid,
                        wo.Name,
                        wo.WeenieClassId,
                        wo.WeenieType,
                        wo.Location.ToLOCString()
                    );
                }
                else if (wo.ProjectileTarget == null && !(wo is SpellProjectile))
                {
                    _log.Warning(
                        "AddWorldObjectInternal: couldn't spawn 0x{WorldObjectGuid}:{WorldObjectName} [{WorldObjectWeenieClassId} - {WorldObjectWeenieType}] at {WorldObjectLocation}",
                        wo.Guid,
                        wo.Name,
                        wo.WeenieClassId,
                        wo.WeenieType,
                        wo.Location.ToLOCString()
                    );
                }

                return false;
            }
        }

        if (!worldObjects.ContainsKey(wo.Guid))
        {
            pendingAdditions[wo.Guid] = wo;
        }
        else
        {
            pendingRemovals.Remove(wo.Guid);
        }

        // broadcast to nearby players
        wo.NotifyPlayers();

        if (wo is Player player)
        {
            player.SetFogColor(FogColor);
        }

        if (wo is Corpse && wo.Level.HasValue)
        {
            var corpseLimit = PropertyManager.GetLong("corpse_spam_limit").Item;
            var corpseList = worldObjects
                .Values.Union(pendingAdditions.Values)
                .Where(w => w is Corpse && w.Level.HasValue && w.VictimId == wo.VictimId)
                .OrderBy(w => w.CreationTimestamp);

            if (corpseList.Count() > corpseLimit)
            {
                var corpse = GetObject(corpseList.First(w => w.TimeToRot > Corpse.EmptyDecayTime).Guid);

                if (corpse != null)
                {
                    _log.Warning(
                        "[CORPSE] Landblock.AddWorldObjectInternal(): {WorldObjectName} (0x{WorldObjectGuid}) exceeds the per player limit of {CorpseLimit} corpses for 0x{LandblockId:X4}. Adjusting TimeToRot for oldest {CorpseName} (0x{CorpseGuid}), CreationTimestamp: {CorpseCreationTimestamp} ({CorpseCreationTimestampLocalTime::yyyy-MM-dd HH:mm:ss}), to Corpse.EmptyDecayTime({CorpseEmptyDecayTime}).",
                        wo.Name,
                        wo.Guid,
                        corpseLimit,
                        Id.Landblock,
                        corpse.Name,
                        corpse.Guid,
                        corpse.CreationTimestamp,
                        Time.GetDateTimeFromTimestamp(corpse.CreationTimestamp ?? 0).ToLocalTime(),
                        Corpse.EmptyDecayTime
                    );
                    corpse.TimeToRot = Corpse.EmptyDecayTime;
                }
            }
        }

        if (wo is Creature creature and not Player)
        {
            creature.SetLethalityModFromDungeonMod();
            creature.SetHealthFromDungeonMod();
            creature.SetSkillsFromDungeonMod();
        }

        return true;
    }

    public void RemoveWorldObject(
        ObjectGuid objectId,
        bool adjacencyMove = false,
        bool fromPickup = false,
        bool showError = true
    )
    {
        RemoveWorldObjectInternal(objectId, adjacencyMove, fromPickup, showError);
    }

    /// <summary>
    /// Should only be called by physics/relocation engines -- not from player
    /// </summary>
    /// <param name="objectId">The object ID to be removed from the current landblock</param>
    /// <param name="adjacencyMove">Flag indicates if object is moving to an adjacent landblock</param>
    public void RemoveWorldObjectForPhysics(ObjectGuid objectId, bool adjacencyMove = false)
    {
        RemoveWorldObjectInternal(objectId, adjacencyMove);
    }

    private void RemoveWorldObjectInternal(
        ObjectGuid objectId,
        bool adjacencyMove = false,
        bool fromPickup = false,
        bool showError = true
    )
    {
        if (LandblockManager.CurrentlyTickingLandblockGroupsMultiThreaded)
        {
            if (
                CurrentLandblockGroup != null
                && CurrentLandblockGroup != LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value
            )
            {
                _log.Error($"Landblock 0x{Id} entered RemoveWorldObjectInternal in a cross-thread operation.");
                _log.Error($"Landblock 0x{Id} CurrentLandblockGroup: {CurrentLandblockGroup}");
                _log.Error(
                    $"LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value: {LandblockManager.CurrentMultiThreadedTickingLandblockGroup.Value}"
                );

                _log.Error($"objectId: 0x{objectId}");

                _log.Error(System.Environment.StackTrace);

                _log.Error("PLEASE REPORT THIS TO THE ACE DEV TEAM !!!");

                // This may still crash...
            }
        }

        if (worldObjects.TryGetValue(objectId, out var wo))
        {
            pendingRemovals.Add(objectId);
        }
        else if (!pendingAdditions.Remove(objectId, out wo))
        {
            if (showError)
            {
                _log.Warning($"RemoveWorldObjectInternal: Couldn't find {objectId.Full:X8}");
            }

            return;
        }

        wo.CurrentLandblock = null;

        // Weenies can come with a default of 0 (Instant Rot) or -1 (Never Rot). If they still have that value, we want to retain it.
        // We also want to make sure fromPickup is true so that we're not clearing out TimeToRot on server shutdown (unloads all landblocks and removed all objects).
        if (fromPickup && wo.TimeToRot.HasValue && wo.TimeToRot != 0 && wo.TimeToRot != -1)
        {
            wo.TimeToRot = null;
        }

        if (!adjacencyMove)
        {
            // really remove it - send message to client to remove object
            wo.EnqueueActionBroadcast(p => p.RemoveTrackedObject(wo, fromPickup));

            wo.PhysicsObj.DestroyObject();
        }
    }

    public void EmitSignal(WorldObject emitter, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        foreach (var wo in worldObjects.Values.Where(w => w.HearLocalSignals).ToList())
        {
            if (emitter == wo)
            {
                continue;
            }

            if (emitter.IsWithinUseRadiusOf(wo, wo.HearLocalSignalsRadius))
            {
                //Console.WriteLine($"{wo.Name}.EmoteManager.OnLocalSignal({emitter.Name}, {message})");
                wo.EmoteManager.OnLocalSignal(emitter, message);
            }
        }
    }

    /// <summary>
    /// Check to see if we are close enough to interact.   Adds a fudge factor of 1.5f
    /// </summary>
    public bool WithinUseRadius(Player player, ObjectGuid targetGuid, out bool validTargetGuid, float? useRadius = null)
    {
        var target = GetObject(targetGuid);

        validTargetGuid = target != null;

        if (target != null)
        {
            return player.IsWithinUseRadiusOf(target, useRadius);
        }

        return false;
    }

    /// <summary>
    /// Returns landblock objects with physics initialized
    /// </summary>
    public ICollection<WorldObject> GetWorldObjectsForPhysicsHandling()
    {
        // If a missile is destroyed when it runs it's UpdateObjectPhysics(), it will remove itself from the landblock, thus, modifying the worldObjects collection.

        ProcessPendingWorldObjectAdditionsAndRemovals();

        return worldObjects.Values;
    }

    public List<WorldObject> GetAllWorldObjectsForDiagnostics()
    {
        // We do not ProcessPending here, and we return ToList() to avoid cross-thread issues.
        // This can happen if we "loadalllandblocks" and do a "serverstatus".
        return worldObjects.Values.ToList();
    }

    public WorldObject GetObject(uint objectId)
    {
        return GetObject(new ObjectGuid(objectId));
    }

    /// <summary>
    /// This will return null if the object was not found in the current or adjacent landblocks.
    /// </summary>
    public WorldObject GetObject(ObjectGuid guid, bool searchAdjacents = true)
    {
        if (pendingRemovals.Contains(guid))
        {
            return null;
        }

        if (worldObjects.TryGetValue(guid, out var worldObject) || pendingAdditions.TryGetValue(guid, out worldObject))
        {
            return worldObject;
        }

        if (searchAdjacents)
        {
            foreach (var lb in Adjacents)
            {
                if (lb != null)
                {
                    var wo = lb.GetObject(guid, false);

                    if (wo != null)
                    {
                        return wo;
                    }
                }
            }
        }

        return null;
    }

    public WorldObject GetWieldedObject(uint objectGuid, bool searchAdjacents = true)
    {
        return GetWieldedObject(new ObjectGuid(objectGuid), searchAdjacents); // todo fix
    }

    /// <summary>
    /// Searches this landblock (and possibly adjacents) for an ObjectGuid wielded by a creature
    /// </summary>
    public WorldObject GetWieldedObject(ObjectGuid guid, bool searchAdjacents = true)
    {
        // search creature wielded items in current landblock
        var creatures = worldObjects.Values.OfType<Creature>();
        foreach (var creature in creatures)
        {
            var wieldedItem = creature.GetEquippedItem(guid);
            if (wieldedItem != null)
            {
                if ((wieldedItem.CurrentWieldedLocation & EquipMask.Selectable) != 0)
                {
                    return wieldedItem;
                }

                return null;
            }
        }

        // try searching adjacent landblocks if not found
        if (searchAdjacents)
        {
            foreach (var adjacent in Adjacents)
            {
                if (adjacent == null)
                {
                    continue;
                }

                var wieldedItem = adjacent.GetWieldedObject(guid, false);
                if (wieldedItem != null)
                {
                    return wieldedItem;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Sets a landblock to active state, with the current time as the LastActiveTime
    /// </summary>
    /// <param name="isAdjacent">Public calls to this function should always set isAdjacent to false</param>
    public void SetActive(bool isAdjacent = false)
    {
        lastActiveTime = DateTime.UtcNow;
        IsDormant = false;

        if (isAdjacent || PhysicsLandblock == null || PhysicsLandblock.IsDungeon)
        {
            return;
        }

        // for outdoor landblocks, recursively call 1 iteration to set adjacents to active
        foreach (var landblock in Adjacents)
        {
            if (landblock != null)
            {
                landblock.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Handles the cleanup process for a landblock
    /// This method is called by LandblockManager
    /// </summary>
    public void Unload()
    {
        var landblockID = Id.Raw | 0xFFFF;

        //log.DebugFormat("Landblock.Unload({0:X8})", landblockID);

        ProcessPendingWorldObjectAdditionsAndRemovals();

        SaveDB();

        // remove all objects
        foreach (var wo in worldObjects.ToList())
        {
            if (!wo.Value.BiotaOriginatedFromOrHasBeenSavedToDatabase())
            {
                wo.Value.Destroy(false, true);
            }
            else
            {
                RemoveWorldObjectInternal(wo.Key);
            }
        }

        // clear capstone fellowships
        CapstoneFellowship = null;
        LandblockLethalityMod = 0.0;

        ProcessPendingWorldObjectAdditionsAndRemovals();

        actionQueue.Clear();

        // remove physics landblock
        LScape.unload_landblock(landblockID);

        PhysicsLandblock.release_shadow_objs();
    }

    public void DestroyAllNonPlayerObjects()
    {
        ProcessPendingWorldObjectAdditionsAndRemovals();

        SaveDB();

        // remove all objects
        foreach (var wo in worldObjects.Where(i => !(i.Value is Player)).ToList())
        {
            if (!wo.Value.BiotaOriginatedFromOrHasBeenSavedToDatabase())
            {
                wo.Value.Destroy(false);
            }
            else
            {
                RemoveWorldObjectInternal(wo.Key);
            }
        }

        ProcessPendingWorldObjectAdditionsAndRemovals();

        actionQueue.Clear();
    }

    public void ReloadObject(WorldObject wo)
    {
        ProcessPendingWorldObjectAdditionsAndRemovals();
        SaveDB();

        if (!wo.BiotaOriginatedFromOrHasBeenSavedToDatabase())
        {
            wo.Destroy(false);
        }
        else
        {
            RemoveWorldObjectInternal(wo.Guid, true);
        }

        ProcessPendingWorldObjectAdditionsAndRemovals();
        actionQueue.Clear();

        DatabaseManager.World.ClearCachedInstancesByLandblock(Id.Landblock);

        Task.Run(() =>
        {
            CreateWorldObjects();

            SpawnDynamicShardObjects();
        });
    }

    private void SaveDB()
    {
        var biotas = new Collection<(Biota biota, ReaderWriterLockSlim rwLock)>();

        foreach (var wo in worldObjects.Values)
        {
            if (wo.IsStaticThatShouldPersistToShard() || wo.IsDynamicThatShouldPersistToShard())
            {
                AddWorldObjectToBiotasSaveCollection(wo, biotas);
            }
        }

        DatabaseManager.Shard.SaveBiotasInParallel(biotas, null);
    }

    private void AddWorldObjectToBiotasSaveCollection(
        WorldObject wo,
        Collection<(Biota biota, ReaderWriterLockSlim rwLock)> biotas
    )
    {
        if (wo.ChangesDetected)
        {
            wo.SaveBiotaToDatabase(false);
            biotas.Add((wo.Biota, wo.BiotaDatabaseLock));
        }

        if (wo is Container container)
        {
            foreach (var item in container.Inventory.Values)
            {
                AddWorldObjectToBiotasSaveCollection(item, biotas);
            }
        }
    }

    /// <summary>
    /// This is only used for very specific instances, such as broadcasting player deaths to the destination lifestone block
    /// This is a rarely used method to broadcast network messages to all of the players within a landblock,
    /// and possibly the adjacent landblocks.
    /// </summary>
    public void EnqueueBroadcast(
        ICollection<Player> excludeList,
        bool adjacents,
        Position pos = null,
        float? maxRangeSq = null,
        params GameMessage[] msgs
    )
    {
        var players = worldObjects.Values.OfType<Player>();

        // for landblock death broadcasts:
        // exclude players that have already been broadcast to within range of the death
        if (excludeList != null)
        {
            players = players.Except(excludeList);
        }

        // broadcast messages to player in this landblock
        foreach (var player in players)
        {
            if (pos != null && maxRangeSq != null)
            {
                var distSq = player.Location.SquaredDistanceTo(pos);
                if (distSq > maxRangeSq)
                {
                    continue;
                }
            }

            player.Session.Network.EnqueueSend(msgs);
        }

        // if applicable, iterate into adjacent landblocks
        if (adjacents)
        {
            foreach (var adjacent in this.Adjacents.Where(adj => adj != null))
            {
                adjacent.EnqueueBroadcast(excludeList, false, pos, maxRangeSq, msgs);
            }
        }
    }

    private bool? isDungeon;

    /// <summary>
    /// Returns TRUE if this landblock is a dungeon,
    /// with no traversable overworld
    /// </summary>
    public bool IsDungeon
    {
        get
        {
            // return cached value
            if (isDungeon != null)
            {
                return isDungeon.Value;
            }

            // hack for NW island
            // did a worldwide analysis for adding watercells into the formula,
            // but they are inconsistently defined for some of the edges of map unfortunately
            if (Id.LandblockX < 0x08 && Id.LandblockY > 0xF8)
            {
                isDungeon = false;
                return isDungeon.Value;
            }

            // a dungeon landblock is determined by:
            // - all heights being 0
            // - having at least 1 EnvCell (0x100+)
            // - contains no buildings
            foreach (var height in CellLandblock.Height)
            {
                if (height != 0)
                {
                    isDungeon = false;
                    return isDungeon.Value;
                }
            }

            isDungeon =
                LandblockInfo != null
                && LandblockInfo.NumCells > 0
                && LandblockInfo.Buildings != null
                && LandblockInfo.Buildings.Count == 0;
            return isDungeon.Value;
        }
    }

    private bool? hasDungeon;

    /// <summary>
    /// Returns TRUE if this landblock contains a dungeon
    //
    /// If a landblock contains both a dungeon + traversable overworld,
    /// this field will return TRUE, whereas IsDungeon will return FALSE
    ///
    /// This property should only be used in very specific scenarios,
    /// such as determining if a landblock contains a mansion basement
    /// </summary>
    public bool HasDungeon
    {
        get
        {
            // return cached value
            if (hasDungeon != null)
            {
                return hasDungeon.Value;
            }

            hasDungeon =
                LandblockInfo != null
                && LandblockInfo.NumCells > 0
                && LandblockInfo.Buildings != null
                && LandblockInfo.Buildings.Count == 0;
            return hasDungeon.Value;
        }
    }

    public List<House> Houses = new List<House>();

    public void SetFogColor(EnvironChangeType environChangeType)
    {
        if (environChangeType.IsFog())
        {
            FogColor = environChangeType;

            foreach (var lb in Adjacents)
            {
                lb.FogColor = environChangeType;
            }

            foreach (var player in players)
            {
                player.SetFogColor(FogColor);
            }
        }
    }

    public void SendEnvironSound(EnvironChangeType environChangeType)
    {
        if (environChangeType.IsSound())
        {
            SendEnvironChange(environChangeType);

            foreach (var lb in Adjacents)
            {
                lb.SendEnvironChange(environChangeType);
            }
        }
    }

    public void SendEnvironChange(EnvironChangeType environChangeType)
    {
        foreach (var player in players)
        {
            player.SendEnvironChange(environChangeType);
        }
    }

    public void SendCurrentEnviron()
    {
        foreach (var player in players)
        {
            if (FogColor.IsFog())
            {
                player.SetFogColor(FogColor);
            }
            else
            {
                player.SendEnvironChange(FogColor);
            }
        }
    }

    public void DoEnvironChange(EnvironChangeType environChangeType)
    {
        if (environChangeType.IsFog())
        {
            SetFogColor(environChangeType);
        }
        else
        {
            SendEnvironSound(environChangeType);
        }
    }

    public static void AssignCapstoneDungeon(Player player, string dungeonName)
    {
        var dungeonLandblocks = CapstoneDungeonLists(dungeonName);

        if (dungeonLandblocks == null)
        {
            return;
        }

        if (player.Fellowship == null)
        {
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    $"You must be in a fellowship to enter this dungeon.",
                    ChatMessageType.Broadcast
                )
            );

            return;
        }

        var fellowship = player.Fellowship;

        if (fellowship.CapstoneDungeon.HasValue && dungeonLandblocks.Contains((LandblockId)fellowship.CapstoneDungeon))
        {
            var landblock = LandblockManager.GetLandblock((LandblockId)fellowship.CapstoneDungeon, false);

            CapstoneTeleport(player, landblock);
        }
        else
        {
            FindOpenInstanceFellowship(player, dungeonLandblocks, dungeonName);
        }
    }

    private static void FindOpenInstanceFellowship(
        Player player,
        List<LandblockId> dungeonLandblocks,
        string dungeonName
    )
    {
        var fellowship = player.Fellowship;

        foreach (var landblockId in dungeonLandblocks)
        {
            var landblock = LandblockManager.GetLandblock(landblockId, false);

            if (landblock.CapstoneFellowship != null)
            {
                continue;
            }

            landblock.CapstoneFellowship = fellowship;
            fellowship.CapstoneDungeon = landblockId;

            landblock.SetLandblockMods(fellowship, dungeonName);

            CapstoneTeleport(player, landblock);
            return;
        }

        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                $"There are no free instances of this dungeon available at the moment.",
                ChatMessageType.Broadcast
            )
        );

        WorldManager.ThreadSafeTeleport(player, player.Sanctuary);
    }

    private void SetLandblockMods(Fellowship fellowship, string dungeonName)
    {
        LandblockLootQualityMod = 0.0;

        var playerLeaderGuid = fellowship.FellowshipLeaderGuid;

        // must be fellowship leader
        if (!fellowship.GetFellowshipMembers().TryGetValue(playerLeaderGuid, out var playerLeader))
        {
            return;
        }

        if (dungeonName is "Lugian Mines2" or "Beyond the Mines")
        {
            var previousLandblock = LandblockManager.GetLandblock(GetPartOneDungeon(Id), false);

            var previousLandblockMods = previousLandblock.LandblockMods;
            var previousLandblockLootQuality = previousLandblock.LandblockLootQualityMod;

            foreach (var kvp in previousLandblockMods)
            {
                LandblockMods[kvp.Key] = kvp.Value;
            }

            LandblockLootQualityMod = previousLandblockLootQuality;

            return;
        }
        else
        {
            var leaderLandblockModSpells = new List<PropertiesEnchantmentRegistry>();

            foreach (var landblockMod in LandblockMods)
            {
                leaderLandblockModSpells.Add(
                    playerLeader.EnchantmentManager.GetEnchantment((uint)landblockMod.Value.SpellId));
            }

            if (leaderLandblockModSpells.Count == 0)
            {
                return;
            }

            var distinctLandblockMoSpellsActive = leaderLandblockModSpells.Distinct();

            foreach (var landblockModSpell in distinctLandblockMoSpellsActive)
            {
                if (landblockModSpell is null)
                {
                    continue;
                }

                LandblockModsSpellToName.TryGetValue(landblockModSpell.SpellId, out var modName);

                if (modName is null)
                {
                    continue;
                }

                var modInfo = LandblockMods[modName];
                modInfo.Active = true;

                LandblockMods[modName] = modInfo;

                LandblockLootQualityMod += LandblockMods[modName].LootQualityBonus;

                playerLeader.EnchantmentManager.Dispel(landblockModSpell);
            }
        }

        foreach (var fellowshipMember in playerLeader.Fellowship.GetFellowshipMembers())
        {
            var fellowPlayer = fellowshipMember.Value;

            if (fellowPlayer is null)
            {
                continue;
            }

            const int baseBonus = 25;

            fellowPlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $"Dungeon instance activated by '{playerLeader.Name}' with the following effects:\n" +
                $" -Base Bonus: {baseBonus}%",
                ChatMessageType.Broadcast
            ));

            var completionBonus = QuestManager.GetCapstonesCompleted(fellowPlayer) * 2;

            fellowPlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $" -Completion Bonus: +{completionBonus}%",
                ChatMessageType.Broadcast
            ));

            foreach (var activeLandblockMod in LandblockMods)
            {
                if (!activeLandblockMod.Value.Active)
                {
                    continue;
                }

                fellowPlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(
                    $" -{activeLandblockMod.Key}: +{activeLandblockMod.Value.LootQualityBonus * 100}%",
                    ChatMessageType.Broadcast
                ));
            }

            var totalBonus = baseBonus + completionBonus + Math.Round(LandblockLootQualityMod * 100);
            var diminishedRoll = (float)(1 - Math.Exp(-1 * totalBonus / 100));

            fellowPlayer.Session.Network.EnqueueSend(new GameMessageSystemChat(
                $" -Total Loot Quality Bonus: +{totalBonus}%",
                ChatMessageType.Broadcast
            ));
        }
    }

    public Dictionary<string, (bool Active, int SpellId, double LootQualityBonus)> LandblockMods { get; private set; }

    private void SetActiveMods()
    {
        LandblockMods = new Dictionary<string, (bool Active, int SpellId, double LootQualityBonus)>
        {
            { "Lethality 50%", (false, 6416, 0.05) },
            { "Lethality 100%", (false, 6417, 0.1) },
            { "Lethality 150%", (false, 6418, 0.15) },
            { "Lethality 200%", (false, 6419, 0.2) },
            { "Lethality 250%", (false, 6420, 0.25) },
            { "Lethality 300%", (false, 6421, 0.3) },
            { "Lethality 350%", (false, 6422, 0.35) },
            { "Lethality 400%", (false, 6423, 0.4) },
            { "Lethality 450%", (false, 6424, 0.45) },
            { "Lethality 500%", (false, 6425, 0.5) },
            { "Titans", (false, 6426, 0.05) },
            { "Drained", (false, 6427, 0.05) },
            { "Explosive", (false, 6428, 0.05) },
            { "Skilled", (false, 6429, 0.05) },
            { "Fester", (false, 6430, 0.05) },
            { "Enraged", (false, 6431, 0.05) },
            { "Inspired", (false, 6432, 0.05) },
        };
    }

    private static readonly Dictionary<int, string> LandblockModsSpellToName = new()
    {
        {6416, "Lethality 50%"},
        {6417, "Lethality 100%"},
        {6418, "Lethality 150%"},
        {6419, "Lethality 200%"},
        {6420, "Lethality 250%"},
        {6421, "Lethality 300%"},
        {6422, "Lethality 350%"},
        {6423, "Lethality 400%"},
        {6424, "Lethality 450%"},
        {6425, "Lethality 500%"},
        {6426, "Titans"},
        {6427, "Drained"},
        {6428, "Explosive"},
        {6429, "Skilled"},
        {6430, "Fester"},
        {6431, "Enraged"},
        {6432, "Inspired"},
    };

    public static void CapstoneTeleport(Player player, Landblock landblock)
    {
        player.CapstoneDungeon = landblock.Id;

        if (!CapstoneTeleportLocations.TryGetValue(landblock.Id, out var destination))
        {
            return;
        }

        WorldObject.AdjustDungeon(destination);
        WorldManager.ThreadSafeTeleport(player, destination);
    }

    public static List<LandblockId> CapstoneDungeonLists(string dungeonName)
    {
        uint[] dungeonLandblocks;

        switch (dungeonName)
        {
            //T0
            case "Glenden Wood Dungeon":
                dungeonLandblocks = [0x01E3, 0x13FE, 0x13FD, 0x13FC, 0x13FB, 0x13FA];
                break;
            case "Green Mire Grave":
                dungeonLandblocks = [0x01E5, 0x09FE, 0x09FD, 0x09FC, 0x09FB, 0x09FA];
                break;
            case "Sand Shallow":
                dungeonLandblocks = [0x02A0, 0x0EFE, 0x0EFD, 0x0EFC, 0x0EFB, 0x0EFA];
                break;
            // T1
            case "Manse of Panderlou":
                dungeonLandblocks = [0x01ED, 0x14FE, 0x14FD, 0x14FC, 0x14FB, 0x14FA];
                break;
            case "Smugglers Hideaway":
                dungeonLandblocks = [0x014E, 0x0AFE, 0x0AFD, 0x0AFC, 0x0AFB, 0x0AFA];
                break;
            case "Halls of the Helm":
                dungeonLandblocks = [0x01CC, 0x0FFE, 0x0FFD, 0x0FFC, 0x0FFB, 0x0FFA];
                break;
            // T2
            case "Colier Mine":
                dungeonLandblocks = [0x01AE, 0x18FE, 0x18FD, 0x18FC, 0x18FB, 0x18FA];
                break;
            case "Empyrean Garrison":
                dungeonLandblocks = [0x0161, 0x0BFE, 0x0BFD, 0x0BFC, 0x0BFB, 0x0BFA];
                break;
            case "Grievous Vault":
                dungeonLandblocks = [0x0189, 0x10FE, 0x10FD, 0x10FC, 0x10FB, 0x10FA];
                break;
            // T3
            case "Folthid Cellar":
                dungeonLandblocks = [0x013B, 0x19FE, 0x19FD, 0x19FC, 0x19FB, 0x19FA];
                break;
            case "Mines of Despair":
                dungeonLandblocks = [0x0188, 0x0CFE, 0x0CFD, 0x0CFC, 0x0CFB, 0x0CFA];
                break;
            case "Beyond the Mines":
                dungeonLandblocks = [0x02AB, 0x0DFE, 0x0DFD, 0x0DFC, 0x0DFB, 0x0DFA];
                break;
            case "Gredaline Consulate":
                dungeonLandblocks = [0x029B, 0x11FE, 0x11FD, 0x11FC, 0x11FB, 0x11FA];
                break;
            // T4
            case "Mage Academy":
                dungeonLandblocks = [0x0139, 0x15FE, 0x15FD, 0x15FC, 0x15FB, 0x15FA];
                break;
            case "Lugian Mines":
                dungeonLandblocks = [0x02E9, 0x16FE, 0x16FD, 0x16FC, 0x16FB, 0x16FA];
                break;
            case "Lugian Mines2":
                dungeonLandblocks = [0x02E7, 0x17FE, 0x17FD, 0x17FC, 0x17FB, 0x17FA];
                break;
            case "Mountain Fortress":
                dungeonLandblocks = [0x011C, 0x12FE, 0x12FD, 0x12FC, 0x12FB, 0x12FA];
                break;
            default:
                return null;
        }

        if (dungeonLandblocks.Length > 0)
        {
            var landblockIds = new List<LandblockId> { };
            foreach (var id in dungeonLandblocks)
            {
                var landblockId = new LandblockId(id << 16 | 0xFFFF);
                landblockIds.Add(landblockId);
            }
            return landblockIds;
        }
        return null;
    }

    public static LandblockId GetPartOneDungeon(LandblockId dungeonId)
    {
        if (CapstoneDungeonLists("Lugian Mines2").Contains(dungeonId))
        {
            var index = CapstoneDungeonLists("Lugian Mines2").IndexOf(dungeonId);

            return CapstoneDungeonLists("Lugian Mines")[index];
        }
        else
        {
            var index = CapstoneDungeonLists("Beyond the Mines").IndexOf(dungeonId);

            return CapstoneDungeonLists("Mines of Despair")[index];
        }
    }


    public static readonly Dictionary<LandblockId, Position> CapstoneTeleportLocations = new()
    {
        // Glenden Wood Dungeon
        { new LandblockId(0x01E3 << 16 | 0xFFFF), new Position(0x01E303B9, 159.847f, -165.469f, 6.005f, 0, 0, 0, 1) },
        { new LandblockId(0x13FE << 16 | 0xFFFF), new Position(0x13FE30B9, 159.847f, -165.469f, 6.005f, 0, 0, 0, 1) },
        { new LandblockId(0x13FD << 16 | 0xFFFF), new Position(0x13FD30B9, 159.847f, -165.469f, 6.005f, 0, 0, 0, 1) },
        { new LandblockId(0x13FC << 16 | 0xFFFF), new Position(0x13FC30B9, 159.847f, -165.469f, 6.005f, 0, 0, 0, 1) },
        { new LandblockId(0x13FB << 16 | 0xFFFF), new Position(0x13FB30B9, 159.847f, -165.469f, 6.005f, 0, 0, 0, 1) },
        { new LandblockId(0x13FA << 16 | 0xFFFF), new Position(0x13FA30B9, 159.847f, -165.469f, 6.005f, 0, 0, 0, 1) },
        // Green Mire Grave   0x09FE020F [80.487862 -79.122993 0.005000] -0.000000 0.000000 0.000000 1.000000
        { new LandblockId(0x01E5 << 16 | 0xFFFF), new Position(0x01E5020F, 80.1099f, -80.136284f, 0.005f, 0, 0, 1, 0) },
        { new LandblockId(0x09FE << 16 | 0xFFFF), new Position(0x09FE020F, 80.1099f, -80.136284f, 0.005f, 0, 0, 1, 0) },
        { new LandblockId(0x09FD << 16 | 0xFFFF), new Position(0x09FD020F, 80.1099f, -80.136284f, 0.005f, 0, 0, 1, 0) },
        { new LandblockId(0x09FC << 16 | 0xFFFF), new Position(0x09FC020F, 80.1099f, -80.136284f, 0.005f, 0, 0, 1, 0) },
        { new LandblockId(0x09FB << 16 | 0xFFFF), new Position(0x09FB020F, 80.1099f, -80.136284f, 0.005f, 0, 0, 1, 0) },
        { new LandblockId(0x09FA << 16 | 0xFFFF), new Position(0x09FA020F, 80.1099f, -80.136284f, 0.005f, 0, 0, 1, 0) },
        // Sand Shallow 0x02A0, 0x0EFE, 0x0EFD, 0x0EFC, 0x0EFB, 0x0EFA  0x2A002F5, 290, -340, 0, 1, 0, 0, 0
        { new LandblockId(0x02A0 << 16 | 0xFFFF), new Position(0x02A002F5, 290f, -340f, 0.005f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0EFE << 16 | 0xFFFF), new Position(0x0EFE02F5, 290f, -340f, 0.005f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0EFD << 16 | 0xFFFF), new Position(0x0EFD02F5, 290f, -340f, 0.005f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0EFC << 16 | 0xFFFF), new Position(0x0EFC02F5, 290f, -340f, 0.005f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0EFB << 16 | 0xFFFF), new Position(0x0EFB02F5, 290f, -340f, 0.005f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0EFA << 16 | 0xFFFF), new Position(0x0EFA02F5, 290f, -340f, 0.005f, 0f, 0f, 0f, 1f) },
        // Manse of Panderlou 0x01ED, 0x14FE, 0x14FD, 0x14FC, 0x14FB, 0x14FA
        { new LandblockId(0x01ED << 16 | 0xFFFF), new Position(0x01ED0310, 120.1193f, -3.697473f, 12.004999f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x14FE << 16 | 0xFFFF), new Position(0x14FE0310, 120.1193f, -3.697473f, 12.004999f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x14FD << 16 | 0xFFFF), new Position(0x14FD0310, 120.1193f, -3.697473f, 12.004999f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x14FC << 16 | 0xFFFF), new Position(0x14FC0310, 120.1193f, -3.697473f, 12.004999f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x14FB << 16 | 0xFFFF), new Position(0x14FB0310, 120.1193f, -3.697473f, 12.004999f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x14FA << 16 | 0xFFFF), new Position(0x14FA0310, 120.1193f, -3.697473f, 12.004999f, 0f, 0f, -1f, 0f) },
        // Smuggler's Hideaway  0x014E, 0x0AFE, 0x0AFD, 0x0AFC, 0x0AFB, 0x0AFA
        { new LandblockId(0x014E << 16 | 0xFFFF), new Position(0x014E025C, 190f, -10f, 0f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0AFE << 16 | 0xFFFF), new Position(0x0AFE025C, 190f, -10f, 0f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0AFD << 16 | 0xFFFF), new Position(0x0AFD025C, 190f, -10f, 0f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0AFC << 16 | 0xFFFF), new Position(0x0AFC025C, 190f, -10f, 0f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0AFB << 16 | 0xFFFF), new Position(0x0AFB025C, 190f, -10f, 0f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0AFA << 16 | 0xFFFF), new Position(0x0AFA025C, 190f, -10f, 0f, 0f, 0f, -1f, 0f) },
        // Halls of the Helm 0x01CC, 0x0FFE, 0x0FFD, 0x0FFC, 0x0FFB, 0x0FFA
        { new LandblockId(0x01CC << 16 | 0xFFFF), new Position(0x01CC01E5, 70.5f, -71f, 12f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0FFE << 16 | 0xFFFF), new Position(0x0FFE01E5, 70.5f, -71f, 12f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0FFD << 16 | 0xFFFF), new Position(0x0FFD01E5, 70.5f, -71f, 12f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0FFC << 16 | 0xFFFF), new Position(0x0FFC01E5, 70.5f, -71f, 12f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0FFB << 16 | 0xFFFF), new Position(0x0FFB01E5, 70.5f, -71f, 12f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x0FFA << 16 | 0xFFFF), new Position(0x0FFA01E5, 70.5f, -71f, 12f, 0f, 0f, -1f, 0f) },
        // Colier Mine 0x01AE, 0x18FE, 0x18FD, 0x18FC, 0x18FB, 0x18FA
        { new LandblockId(0x01AE << 16 | 0xFFFF), new Position(0x01AE032C, 76.59f, -97.36f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x18FE << 16 | 0xFFFF), new Position(0x18FE032C, 76.59f, -97.36f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x18FD << 16 | 0xFFFF), new Position(0x18FD032C, 76.59f, -97.36f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x18FC << 16 | 0xFFFF), new Position(0x18FC032C, 76.59f, -97.36f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x18FB << 16 | 0xFFFF), new Position(0x18FB032C, 76.59f, -97.36f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x18FA << 16 | 0xFFFF), new Position(0x18FA032C, 76.59f, -97.36f, 0f, 0f, 0f, 0f, 1f) },
        // Empyrean Garrison  0x0161, 0x0BFE, 0x0BFD, 0x0BFC, 0x0BFB, 0x0BFA
        { new LandblockId(0x0161 << 16 | 0xFFFF), new Position(0x01610264, 80f, -80f, 0f, 0f, 0f, -0.707107f, -0.707107f) },
        { new LandblockId(0x0BFE << 16 | 0xFFFF), new Position(0x0BFE0264, 80f, -80f, 0f, 0f, 0f, -0.707107f, -0.707107f) },
        { new LandblockId(0x0BFD << 16 | 0xFFFF), new Position(0x0BFD0264, 80f, -80f, 0f, 0f, 0f, -0.707107f, -0.707107f) },
        { new LandblockId(0x0BFC << 16 | 0xFFFF), new Position(0x0BFC0264, 80f, -80f, 0f, 0f, 0f, -0.707107f, -0.707107f) },
        { new LandblockId(0x0BFB << 16 | 0xFFFF), new Position(0x0BFB0264, 80f, -80f, 0f, 0f, 0f, -0.707107f, -0.707107f) },
        { new LandblockId(0x0BFA << 16 | 0xFFFF), new Position(0x0BFA0264, 80f, -80f, 0f, 0f, 0f, -0.707107f, -0.707107f) },
        // Grievous Vault 0x0189, 0x10FE, 0x10FD, 0x10FC, 0x10FB, 0x10FA
        { new LandblockId(0x0189 << 16 | 0xFFFF), new Position(0x01890321, 139.792f, -66.582f, 6.005f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x10FE << 16 | 0xFFFF), new Position(0x10FE0321, 139.792f, -66.582f, 6.005f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x10FD << 16 | 0xFFFF), new Position(0x10FD0321, 139.792f, -66.582f, 6.005f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x10FC << 16 | 0xFFFF), new Position(0x10FC0321, 139.792f, -66.582f, 6.005f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x10FB << 16 | 0xFFFF), new Position(0x10FB0321, 139.792f, -66.582f, 6.005f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x10FA << 16 | 0xFFFF), new Position(0x10FA0321, 139.792f, -66.582f, 6.005f, 0f, 0f, -1f, 0f) },
        // Folthid Cellar 0x013B, 0x19FE, 0x19FD, 0x19FC, 0x19FB, 0x19FA
        { new LandblockId(0x013B << 16 | 0xFFFF), new Position(0x013B0336, 62.5684F, -109.99126F, 0.005F, 0, 0, -0.707107f, 0.707107f) },
        { new LandblockId(0x19FE << 16 | 0xFFFF), new Position(0x19FE0336, 62.5684F, -109.99126F, 0.005F, 0, 0, -0.707107f, 0.707107f) },
        { new LandblockId(0x19FD << 16 | 0xFFFF), new Position(0x19FD0336, 62.5684F, -109.99126F, 0.005F, 0, 0, -0.707107f, 0.707107f) },
        { new LandblockId(0x19FC << 16 | 0xFFFF), new Position(0x19FC0336, 62.5684F, -109.99126F, 0.005F, 0, 0, -0.707107f, 0.707107f) },
        { new LandblockId(0x19FB << 16 | 0xFFFF), new Position(0x19FB0336, 62.5684F, -109.99126F, 0.005F, 0, 0, -0.707107f, 0.707107f) },
        { new LandblockId(0x19FA << 16 | 0xFFFF), new Position(0x19FA0336, 62.5684F, -109.99126F, 0.005F, 0, 0, -0.707107f, 0.707107f) },
        // Mines of Despair 0x0188, 0x0CFE, 0x0CFD, 0x0CFC, 0x0CFB, 0x0CFA
        { new LandblockId(0x0188 << 16 | 0xFFFF), new Position(0x01880307, 30f, -70f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0CFE << 16 | 0xFFFF), new Position(0x0CFE0307, 30f, -70f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0CFD << 16 | 0xFFFF), new Position(0x0CFD0307, 30f, -70f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0CFC << 16 | 0xFFFF), new Position(0x0CFC0307, 30f, -70f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0CFB << 16 | 0xFFFF), new Position(0x0CFB0307, 30f, -70f, 0f, 0f, 0f, 0f, 1f) },
        { new LandblockId(0x0CFA << 16 | 0xFFFF), new Position(0x0CFA0307, 30f, -70f, 0f, 0f, 0f, 0f, 1f) },
        // Beyond the Mines of Despair  0x02AB, 0x0DFE, 0x0DFD, 0x0DFC, 0x0DFB, 0x0DFA
        { new LandblockId(0x02AB << 16 | 0xFFFF), new Position(0x02AB0160, 110f, -10f, 0f, 0f, 0f, -0.707107f, 0.707107f) },
        { new LandblockId(0x0DFE << 16 | 0xFFFF), new Position(0x0DFE0160, 110f, -10f, 0f, 0f, 0f, -0.707107f, 0.707107f) },
        { new LandblockId(0x0DFD << 16 | 0xFFFF), new Position(0x0DFD0160, 110f, -10f, 0f, 0f, 0f, -0.707107f, 0.707107f) },
        { new LandblockId(0x0DFC << 16 | 0xFFFF), new Position(0x0DFC0160, 110f, -10f, 0f, 0f, 0f, -0.707107f, 0.707107f) },
        { new LandblockId(0x0DFB << 16 | 0xFFFF), new Position(0x0DFB0160, 110f, -10f, 0f, 0f, 0f, -0.707107f, 0.707107f) },
        { new LandblockId(0x0DFA << 16 | 0xFFFF), new Position(0x0DFA0160, 110f, -10f, 0f, 0f, 0f, -0.707107f, 0.707107f) },
        // Gredaline Consulate   0x029B, 0x11FE, 0x11FD, 0x11FC, 0x11FB, 0x11FA
        { new LandblockId(0x029B << 16 | 0xFFFF), new Position(0x029B0317, 279.8409f, -292.3339f, 6.005f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x11FE << 16 | 0xFFFF), new Position(0x11FE0317, 279.8409f, -292.3339f, 6.005f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x11FD << 16 | 0xFFFF), new Position(0x11FD0317, 279.8409f, -292.3339f, 6.005f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x11FC << 16 | 0xFFFF), new Position(0x11FC0317, 279.8409f, -292.3339f, 6.005f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x11FB << 16 | 0xFFFF), new Position(0x11FB0317, 279.8409f, -292.3339f, 6.005f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x11FA << 16 | 0xFFFF), new Position(0x11FA0317, 279.8409f, -292.3339f, 6.005f, 0f, 0f, 1f, 0f) },
        // Mage Academy 0x0139, 0x15FE, 0x15FD, 0x15FC, 0x15FB, 0x15FA
        { new LandblockId(0x0139 << 16 | 0xFFFF), new Position(0x01390396, 40f, -60f, 6f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x15FE << 16 | 0xFFFF), new Position(0x15FE0396, 40f, -60f, 6f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x15FD << 16 | 0xFFFF), new Position(0x15FD0396, 40f, -60f, 6f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x15FC << 16 | 0xFFFF), new Position(0x15FC0396, 40f, -60f, 6f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x15FB << 16 | 0xFFFF), new Position(0x15FB0396, 40f, -60f, 6f, 0f, 0f, -1f, 0f) },
        { new LandblockId(0x15FA << 16 | 0xFFFF), new Position(0x15FA0396, 40f, -60f, 6f, 0f, 0f, -1f, 0f) },
        // Lugian Mines 0x02E9, 0x16FE, 0x16FD, 0x16FC, 0x16FB, 0x16FA
        { new LandblockId(0x02E9 << 16 | 0xFFFF), new Position(0x02E9010E, 70.0493f, -480.134f, -11.995f, 0f, 0f, -0.701483f, 0.712686f) },
        { new LandblockId(0x16FE << 16 | 0xFFFF), new Position(0x16FE010E, 70.0493f, -480.134f, -11.995f, 0f, 0f, -0.701483f, 0.712686f) },
        { new LandblockId(0x16FD << 16 | 0xFFFF), new Position(0x16FD010E, 70.0493f, -480.134f, -11.995f, 0f, 0f, -0.701483f, 0.712686f) },
        { new LandblockId(0x16FC << 16 | 0xFFFF), new Position(0x16FC010E, 70.0493f, -480.134f, -11.995f, 0f, 0f, -0.701483f, 0.712686f) },
        { new LandblockId(0x16FB << 16 | 0xFFFF), new Position(0x16FB010E, 70.0493f, -480.134f, -11.995f, 0f, 0f, -0.701483f, 0.712686f) },
        { new LandblockId(0x16FA << 16 | 0xFFFF), new Position(0x16FA010E, 70.0493f, -480.134f, -11.995f, 0f, 0f, -0.701483f, 0.712686f) },
        // Deeper into Lugian Mines 0x02E7, 0x17FE, 0x17FD, 0x17FC, 0x17FB, 0x17FA
        { new LandblockId(0x02E7 << 16 | 0xFFFF), new Position(0x02E70282, 509.92538f, -13.700772f, 12.004999f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x17FE << 16 | 0xFFFF), new Position(0x17FE0282, 509.92538f, -13.700772f, 12.004999f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x17FD << 16 | 0xFFFF), new Position(0x17FD0282, 509.92538f, -13.700772f, 12.004999f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x17FC << 16 | 0xFFFF), new Position(0x17FC0282, 509.92538f, -13.700772f, 12.004999f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x17FB << 16 | 0xFFFF), new Position(0x17FB0282, 509.92538f, -13.700772f, 12.004999f, 0f, 0f, 1f, 0f) },
        { new LandblockId(0x17FA << 16 | 0xFFFF), new Position(0x17FA0282, 509.92538f, -13.700772f, 12.004999f, 0f, 0f, 1f, 0f) },
        // Mountain Fortress 0x011C, 0x12FE, 0x12FD, 0x12FC, 0x12FB, 0x12FA
        { new LandblockId(0x011C << 16 | 0xFFFF), new Position(0x011C030F, 100.572f, -160.084f, 0.005f, 0f, 0f, 0.711837f, 0.702345f) },
        { new LandblockId(0x12FE << 16 | 0xFFFF), new Position(0x12FE030F, 100.572f, -160.084f, 0.005f, 0f, 0f, 0.711837f, 0.702345f) },
        { new LandblockId(0x12FD << 16 | 0xFFFF), new Position(0x12FD030F, 100.572f, -160.084f, 0.005f, 0f, 0f, 0.711837f, 0.702345f) },
        { new LandblockId(0x12FC << 16 | 0xFFFF), new Position(0x12FC030F, 100.572f, -160.084f, 0.005f, 0f, 0f, 0.711837f, 0.702345f) },
        { new LandblockId(0x12FB << 16 | 0xFFFF), new Position(0x12FB030F, 100.572f, -160.084f, 0.005f, 0f, 0f, 0.711837f, 0.702345f) },
        { new LandblockId(0x12FA << 16 | 0xFFFF), new Position(0x12FA030F, 100.572f, -160.084f, 0.005f, 0f, 0f, 0.711837f, 0.702345f) },
    };

    public bool IsFellowshipRequired()
    {
        return FellowshipRequiredLandblocksIds.Contains(Id.Landblock);
    }

    private static List<uint> FellowshipRequiredLandblocksIds =
        [
            0x01E3, 0x13FE, 0x13FD, 0x13FC, 0x13FB, 0x13FA, // "Glenden Wood Dungeon"
            0x01E5, 0x09FE, 0x09FD, 0x09FC, 0x09FB, 0x09FA, // "Green Mire Grave"
            0x02A0, 0x0EFE, 0x0EFD, 0x0EFC, 0x0EFB, 0x0EFA, // "Sand Shallow"
            0x01ED, 0x14FE, 0x14FD, 0x14FC, 0x14FB, 0x14FA, // "Manse of Panderlou"
            0x014E, 0x0AFE, 0x0AFD, 0x0AFC, 0x0AFB, 0x0AFA, // "Smugglers Hideaway"
            0x01CC, 0x0FFE, 0x0FFD, 0x0FFC, 0x0FFB, 0x0FFA, // "Halls of the Helm"
            0x01AE, 0x18FE, 0x18FD, 0x18FC, 0x18FB, 0x18FA, // "Colier Mine"
            0x0161, 0x0BFE, 0x0BFD, 0x0BFC, 0x0BFB, 0x0BFA, // "Empyrean Garrison"
            0x0189, 0x10FE, 0x10FD, 0x10FC, 0x10FB, 0x10FA, // "Grievous Vault"
            0x013B, 0x19FE, 0x19FD, 0x19FC, 0x19FB, 0x19FA, // "Folthid Cellar"
            0x0188, 0x0CFE, 0x0CFD, 0x0CFC, 0x0CFB, 0x0CFA, // "Mines of Despair"
            0x02AB, 0x0DFE, 0x0DFD, 0x0DFC, 0x0DFB, 0x0DFA, // "Beyond the Mines"
            0x029B, 0x11FE, 0x11FD, 0x11FC, 0x11FB, 0x11FA, // "Gredaline Consulate"
            0x0139, 0x15FE, 0x15FD, 0x15FC, 0x15FB, 0x15FA, // "Mage Academy"
            0x02E9, 0x16FE, 0x16FD, 0x16FC, 0x16FB, 0x16FA, // "Lugian Mines"
            0x02E7, 0x17FE, 0x17FD, 0x17FC, 0x17FB, 0x17FA, // "Lugian Mines2"
            0x011C, 0x12FE, 0x12FD, 0x12FC, 0x12FB, 0x12FA // "Mountain Fortress"
        ];

    private void IncreaseMinimumEncounterSpawnDensity(List<Encounter> encounters, List<uint> generatedEncounterIdList)
    {
        if (encounters.Count > 0)
        {
            // Landscape spawn density multiplier
            // The maximum amount of encounters that will fit in a landblock is 64.
            int newCount;
            var minmumSpawnDensity = PropertyManager.GetLong("landblock_minimum_spawn_density").Item;

            if (encounters.Count < minmumSpawnDensity)
            {
                newCount = (int)minmumSpawnDensity;
            }
            else
            {
                newCount = encounters.Count;
            }

            if (newCount != encounters.Count)
            {
                var encountersToDuplicate = new Dictionary<int, EncounterInfo>();
                var terrainTypeMap = new Dictionary<ushort, List<AvailableCell>>();

                foreach (var encounter in encounters)
                {
                    var coords = encounter.CellX << 16 | encounter.CellY;
                    encountersToDuplicate.Add(
                        coords,
                        new EncounterInfo(coords, encounter, getTerrainType(encounter.CellX, encounter.CellY))
                    );
                }

                for (var cellX = 0; cellX < LandDefs.BlockSide; cellX++)
                {
                    for (var cellY = 0; cellY < LandDefs.BlockSide; cellY++)
                    {
                        var coords = cellX << 16 | cellY;

                        if (!encountersToDuplicate.ContainsKey(coords)) // Only add cells that do not yet contain encounters.
                        {
                            var terrainType = getTerrainType(cellX, cellY);

                            if (terrainTypeMap.TryGetValue(terrainType, out var entry))
                            {
                                entry.Add(new AvailableCell(cellX, cellY, terrainType));
                            }
                            else
                            {
                                terrainTypeMap.Add(
                                    terrainType,
                                    new List<AvailableCell>() { new AvailableCell(cellX, cellY, terrainType) }
                                );
                            }
                        }
                    }
                }

                while (encounters.Count < newCount && encountersToDuplicate.Count > 0)
                {
                    var sourceEncounter = encountersToDuplicate
                        .ElementAt(ThreadSafeRandom.Next(0, encountersToDuplicate.Count - 1))
                        .Value;
                    if (terrainTypeMap.TryGetValue(sourceEncounter.TerrainType, out var availableCells))
                    {
                        var newEncounterCell = availableCells[ThreadSafeRandom.Next(0, availableCells.Count - 1)];

                        var newEncounter = new Encounter();
                        newEncounter.WeenieClassId = sourceEncounter.Encounter.WeenieClassId;
                        newEncounter.Landblock = sourceEncounter.Encounter.Landblock;
                        newEncounter.LastModified = sourceEncounter.Encounter.LastModified;
                        newEncounter.CellX = newEncounterCell.CellX;
                        newEncounter.CellY = newEncounterCell.CellY;

                        generatedEncounterIdList.Add(newEncounter.Id);
                        encounters.Add(newEncounter);
                        availableCells.Remove(newEncounterCell);
                        if (availableCells.Count == 0)
                        {
                            terrainTypeMap.Remove(sourceEncounter.TerrainType);
                            encountersToDuplicate = encountersToDuplicate
                                .Where(i => i.Value.TerrainType != sourceEncounter.TerrainType)
                                .ToDictionary(i => i.Key, i => i.Value);
                        }
                    }
                    else
                    {
                        // This should never happen.
                        terrainTypeMap.Remove(sourceEncounter.TerrainType);
                        encountersToDuplicate = encountersToDuplicate
                            .Where(i => i.Value.TerrainType != sourceEncounter.TerrainType)
                            .ToDictionary(i => i.Key, i => i.Value);
                    }
                }
            }
        }
    }

    public double GetLandblockLethalityMod()
    {
        return true switch
        {
            _ when LandblockMods["Lethality 500%"].Active => 5.0,
            _ when LandblockMods["Lethality 450%"].Active => 4.5,
            _ when LandblockMods["Lethality 400%"].Active => 4.0,
            _ when LandblockMods["Lethality 350%"].Active => 3.5,
            _ when LandblockMods["Lethality 300%"].Active => 3.0,
            _ when LandblockMods["Lethality 250%"].Active => 2.5,
            _ when LandblockMods["Lethality 200%"].Active => 2.0,
            _ when LandblockMods["Lethality 150%"].Active => 1.5,
            _ when LandblockMods["Lethality 100%"].Active => 1.0,
            _ when LandblockMods["Lethality 50%"].Active => 0.5,
            _ => 0.0
        };
    }
}
