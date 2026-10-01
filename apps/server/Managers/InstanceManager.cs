using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Managers;

/// <summary>
/// Owns the instance templates and every live instance.<para />
/// An instance is a separate copy of some landblocks (its footprint). Objects, physics, visibility and tick groups
/// are per instance, so two instances of the same landblock never see or touch each other, and nothing in an instance is saved.
/// Instance 0 is the persistent world.<para />
/// The lock in here is only held for short lookups and bookkeeping, never while calling out to something else
/// (LandblockManager calls in here while it holds its own lock, so this must never call back into it while holding this one).
/// </summary>
public static class InstanceManager
{
    private static readonly ILogger _log = Log.ForContext(typeof(InstanceManager));

    private static readonly object sync = new object();

    private static readonly Dictionary<string, InstanceTemplate> templates = new Dictionary<string, InstanceTemplate>(
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly Dictionary<uint, WorldInstance> instances = new Dictionary<uint, WorldInstance>();

    private static uint lastInstanceId;

    /// <summary>
    /// The landblocks that only exist as instances, from the registered templates that say so.<para />
    /// LandblockManager asks about every landblock it is asked to load, so this is read without the lock: it is never changed,
    /// only replaced by a new set whenever a template is registered.
    /// </summary>
    private static volatile HashSet<LandblockId> instanceOnlyLandblocks = new HashSet<LandblockId>();

    /// <summary>
    /// How long after a player has been turned back from the boundary of an instance they can be turned back again.
    /// The turn back is queued, so more position updates can come in before it has happened.
    /// </summary>
    private static readonly TimeSpan SweepBackCooldown = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The current time, replaceable so tests can move it
    /// </summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

    /// <summary>
    /// How long an instance stays open after its last player has left. Set with the instance_empty_timeout_minutes server property.
    /// </summary>
    internal static Func<TimeSpan> EmptyTimeout = () =>
        TimeSpan.FromMinutes(PropertyManager.GetLong("instance_empty_timeout_minutes").Item);

    #region Templates

    /// <summary>
    /// Makes a template known by name. Registering a name again replaces the earlier template, which does not affect instances already made from it.<para />
    /// A template that is instance only has to be registered before the world opens: landblocks the persistent world has loaded already stay loaded.
    /// </summary>
    public static void RegisterTemplate(InstanceTemplate template)
    {
        lock (sync)
        {
            if (templates.ContainsKey(template.Name))
            {
                _log.Warning("[INSTANCE] Replacing the instance template {Template}", template.Name);
            }

            templates[template.Name] = template;

            instanceOnlyLandblocks = new HashSet<LandblockId>(
                templates.Values.Where(t => t.InstanceOnly).SelectMany(t => t.Footprint)
            );
        }

        // not with the lock held: this is LandblockManager's
        if (template.InstanceOnly)
        {
            foreach (var landblockId in template.Footprint)
            {
                if (LandblockManager.IsLoaded(landblockId))
                {
                    _log.Warning(
                        "[INSTANCE] {Template} only exists as an instance, but landblock {Landblock:X4} is already loaded in the persistent world",
                        template.Name,
                        landblockId.Landblock
                    );
                }
            }
        }
    }

    /// <summary>
    /// Reads the islands of instances.json, which is next to the server, and registers them. A mistake in one island is logged
    /// and only loses that island. There is no such file if there are no islands. This is done before the world opens.
    /// </summary>
    public static void LoadTemplates(string path = null)
    {
        path ??= Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "instances.json");

        if (!File.Exists(path))
        {
            _log.Information("[INSTANCE] There is no {Path}, so there are no islands", path);
            return;
        }

        var errors = new List<string>();
        List<InstanceTemplate> loaded;

        try
        {
            loaded = InstanceTemplateConfig.Parse(File.ReadAllText(path), errors);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[INSTANCE] {Path} can't be read, so there are no islands", path);
            return;
        }

        foreach (var error in errors)
        {
            _log.Error("[INSTANCE] {Path}: {Error}", path, error);
        }

        foreach (var template in loaded)
        {
            RegisterTemplate(template);

            _log.Information(
                "[INSTANCE] Island {Template}: {Landblocks} landblocks, {Boundary} of them ring{InstanceOnly}",
                template.Name,
                template.Footprint.Count,
                template.Boundary.Count,
                template.InstanceOnly ? ", instance only" : ""
            );
        }
    }

    public static InstanceTemplate GetTemplate(string name)
    {
        lock (sync)
        {
            templates.TryGetValue(name, out var template);
            return template;
        }
    }

    public static List<InstanceTemplate> GetTemplates()
    {
        lock (sync)
        {
            return templates.Values.OrderBy(t => t.Name).ToList();
        }
    }

    /// <summary>
    /// The registered template that says this landblock only exists in instances, if there is one
    /// </summary>
    public static InstanceTemplate GetInstanceOnlyTemplate(LandblockId landblockId)
    {
        lock (sync)
        {
            return templates.Values.FirstOrDefault(t => t.InstanceOnly && t.Contains(landblockId));
        }
    }

    /// <summary>
    /// Whether a landblock only exists in instances, so that nothing is ever loaded there in the persistent world.
    /// This is asked for every landblock that is loaded, so it is nothing but a read for as long as there are no such landblocks.
    /// </summary>
    public static bool IsInstanceOnly(LandblockId landblockId)
    {
        var landblocks = instanceOnlyLandblocks;

        return landblocks.Count != 0 && landblocks.Contains(landblockId);
    }

    /// <summary>
    /// The persistent world was asked to load a landblock that only exists in instances. That is always a mistake in whatever asked:
    /// something in the world database that points into an island, or a portal that leads there. It is logged once for each landblock.
    /// </summary>
    internal static void ReportInstanceOnlyLoad(LandblockId landblockId)
    {
        lock (reportedInstanceOnlyLoads)
        {
            if (!reportedInstanceOnlyLoads.Add(landblockId))
            {
                return;
            }
        }

        _log.Error(
            "[INSTANCE] Something asked the persistent world for landblock {Landblock:X4}, which only exists in instances ({Template}). It was refused.{StackTrace}",
            landblockId.Landblock,
            GetInstanceOnlyTemplate(landblockId)?.Name,
            Environment.NewLine + Environment.StackTrace
        );
    }

    private static readonly HashSet<LandblockId> reportedInstanceOnlyLoads = new HashSet<LandblockId>();

    #endregion

    #region Instances

    /// <summary>
    /// Makes a new instance and loads its landblocks. They stay loaded for as long as the instance does, whether or not anyone is in them.
    /// The instance is deleted a while after its last player has left it (instance_empty_timeout_minutes).
    /// </summary>
    /// <param name="owner">Whatever the instance belongs to, if anything, for finding it again with Find()</param>
    public static WorldInstance Create(InstanceTemplate template, object owner = null)
    {
        var instance = Register(template, owner);

        Load(instance);

        return instance;
    }

    /// <summary>
    /// The instance of a template that belongs to an owner, made if there is none yet. Finding it and making it is one step,
    /// so two players who arrive at the same moment (two members of a fellowship going through the same portal) get the same instance.
    /// </summary>
    /// <param name="created">True if this call made the instance, which is when whatever it needs to be set up is to be done</param>
    public static WorldInstance FindOrCreate(InstanceTemplate template, object owner, out bool created)
    {
        var instance = FindOrRegister(template, owner, out created);

        if (created)
        {
            // not with the lock held: this is LandblockManager's
            Load(instance);
        }

        return instance;
    }

    internal static WorldInstance FindOrRegister(InstanceTemplate template, object owner, out bool created)
    {
        lock (sync)
        {
            var instance = Find(template, owner);
            created = instance == null;

            return instance ?? Register(template, owner);
        }
    }

    private static void Load(WorldInstance instance)
    {
        // The instance has to be known before its landblocks are loaded: LandblockManager only loads
        // a landblock in an instance if the instance's template says it is part of it.
        foreach (var landblockId in instance.Template.Footprint)
        {
            LandblockManager.GetLandblock(landblockId, instance.Id, false, true);
        }

        _log.Information("[INSTANCE] Created {Instance}", instance);
    }

    /// <summary>
    /// Makes a new instance without loading anything. Create() is what makes one that can be entered.
    /// </summary>
    internal static WorldInstance Register(InstanceTemplate template, object owner = null)
    {
        lock (sync)
        {
            var instance = new WorldInstance(++lastInstanceId, template, owner, UtcNow());
            instances.Add(instance.Id, instance);
            return instance;
        }
    }

    public static WorldInstance Get(uint instanceId)
    {
        lock (sync)
        {
            instances.TryGetValue(instanceId, out var instance);
            return instance;
        }
    }

    /// <summary>
    /// The guid an object has in an instance, for a guid that was written down in the world database, such as the activation target of a weenie.
    /// In the persistent world the guid is what the world database says it is. In an instance the statics have guids of their own.
    /// </summary>
    public static uint TranslateWorldGuid(uint instanceId, uint guid)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return guid;
        }

        return Get(instanceId)?.TranslateWorldGuid(guid) ?? guid;
    }

    /// <summary>
    /// The world database guid a guid in this instance stands in for, for a guid that is a static object of the instance
    /// (or the guid itself, for a guid that was not remapped, such as a dynamic object, or anything in the persistent world).
    /// </summary>
    public static uint TranslateInstanceGuid(uint instanceId, uint guid)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return guid;
        }

        return Get(instanceId)?.TranslateInstanceGuid(guid) ?? guid;
    }

    public static List<WorldInstance> GetInstances()
    {
        lock (sync)
        {
            return instances.Values.OrderBy(i => i.Id).ToList();
        }
    }

    /// <summary>
    /// Finds the instance of a template that belongs to an owner, if there is one that isn't shutting down
    /// </summary>
    public static WorldInstance Find(InstanceTemplate template, object owner)
    {
        lock (sync)
        {
            return instances.Values.FirstOrDefault(i =>
                !i.IsClosing && ReferenceEquals(i.Template, template) && ReferenceEquals(i.Owner, owner)
            );
        }
    }

    /// <summary>
    /// Whether a landblock is part of an instance. Every landblock is part of the persistent world.
    /// This is false for an instance that doesn't exist (any more), which is what stops anything loading landblocks for it.
    /// </summary>
    public static bool IsInFootprint(uint instanceId, LandblockId landblockId)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return true;
        }

        lock (sync)
        {
            return instances.TryGetValue(instanceId, out var instance) && instance.Template.Contains(landblockId);
        }
    }

    /// <summary>
    /// Whether anyone can be sent to this landblock in this instance. That is any landblock of the persistent world that is not one
    /// of the ones that only exist as instances, and for an instance only a landblock of its footprint, as long as the instance is not shutting down.
    /// </summary>
    public static bool CanEnter(uint instanceId, LandblockId landblockId)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return !IsInstanceOnly(landblockId);
        }

        lock (sync)
        {
            return instances.TryGetValue(instanceId, out var instance)
                && !instance.IsClosing
                && instance.Template.Contains(landblockId);
        }
    }

    /// <summary>
    /// Which instance a player ends up in when they are teleported somewhere, if nothing says which.<para />
    /// Someone in an instance who teleports somewhere inside that instance's footprint (a portal inside a dungeon) stays in the instance.
    /// A teleport to anywhere else (a recall, a lifestone) goes to the persistent world. Nobody gets into an instance
    /// from the persistent world without being sent there on purpose.
    /// </summary>
    public static uint ResolveDestinationInstance(uint currentInstance, Position destination)
    {
        if (currentInstance == Landblock.PersistentInstance)
        {
            return Landblock.PersistentInstance;
        }

        return IsInFootprint(currentInstance, destination.LandblockId) ? currentInstance : Landblock.PersistentInstance;
    }

    #endregion

    #region Players

    /// <summary>
    /// Sends a player into an instance, to its entry position unless told otherwise
    /// </summary>
    /// <param name="fromPortal">True if a portal is sending them, so they're held to the time between portal teleports</param>
    public static void Enter(Player player, WorldInstance instance, Position destination = null, bool fromPortal = false)
    {
        WorldManager.ThreadSafeTeleport(
            player,
            destination ?? instance.Template.EntryPosition,
            fromPortal: fromPortal,
            instanceId: instance.Id
        );
    }

    /// <summary>
    /// Sends a player out of the instance they are in, to where the instance's template sends players
    /// </summary>
    public static bool Leave(Player player)
    {
        if (player.InstanceId == Landblock.PersistentInstance)
        {
            return false;
        }

        var destination = GetReturnPosition(player);
        if (destination == null)
        {
            return false;
        }

        WorldManager.ThreadSafeTeleport(player, destination, instanceId: Landblock.PersistentInstance);
        return true;
    }

    /// <summary>
    /// Where a player who leaves their instance ends up: the template's own return position, or their sanctuary,
    /// or where they started, whichever of those is the first one that is still a place the persistent world
    /// will let them into. If none of them are, WorldManager's ultimate fallback always is.
    /// </summary>
    private static Position GetReturnPosition(Player player)
    {
        return FirstEnterablePosition(Get(player.InstanceId)?.Template.ReturnPosition, player.Sanctuary, player.Instantiation);
    }

    /// <summary>
    /// The first of these candidates whose landblock the persistent world will still let someone into (the same check
    /// Teleport() itself makes for a destination there), or WorldManager's ultimate fallback if none of them are.<para />
    /// For whenever a player has to end up in the persistent world and what is on hand for that (a saved sanctuary, a
    /// template's return position, where they started) might not be reachable any more, for instance because its
    /// landblock has since become instance only. A caller with a live instance to consider (a respawn, which can stay
    /// in the instance the player died in) wants CanReach instead: this always means the persistent world.
    /// </summary>
    public static Position FirstEnterablePosition(params Position[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate != null && CanEnter(Landblock.PersistentInstance, candidate.LandblockId))
            {
                return candidate;
            }
        }

        return new Position(WorldManager.DefaultFallbackPosition);
    }

    /// <summary>
    /// Whether Teleport() would actually accept sending someone at this instance to this position: the same
    /// resolution and check it makes itself (ResolveDestinationInstance, then CanEnter). Lets a caller validate a
    /// candidate destination (a sanctuary, a saved position) before relying on it, instead of finding out only
    /// when the teleport silently refuses to happen.
    /// </summary>
    public static bool CanReach(uint fromInstance, Position destination)
    {
        return CanEnter(ResolveDestinationInstance(fromInstance, destination), destination.LandblockId);
    }

    public static void OnPlayerChangedInstance(Player player, uint fromInstance, uint toInstance)
    {
        OnMemberLeft(fromInstance, player.Guid.Full);
        OnMemberEntered(toInstance, player.Guid.Full);
    }

    /// <summary>
    /// Called when a player who is in an instance has moved.<para />
    /// In an instance that has a boundary, this remembers where the player last was in the part they are allowed in,
    /// and when they get into the boundary (or somewhere that isn't in the instance at all) it turns them back to there.
    /// A player who has just arrived, and has not been anywhere yet, is turned back to where the instance lets players in.
    /// </summary>
    public static void OnPlayerMoved(Player player)
    {
        var template = Get(player.InstanceId)?.Template;

        if (template == null || !template.HasBoundary)
        {
            return;
        }

        var landblockId = player.Location.LandblockId;

        if (template.Contains(landblockId) && !template.IsBoundary(landblockId))
        {
            player.InstanceSafePosition = new Position(player.Location);
            return;
        }

        var now = UtcNow();

        if (now < player.InstanceTurnBackAllowedAfter)
        {
            return;
        }

        player.InstanceTurnBackAllowedAfter = now + SweepBackCooldown;

        var destination = new Position(player.InstanceSafePosition ?? template.EntryPosition);

        player.SendMessage("You can go no further that way.", ChatMessageType.Broadcast);

        // no instance id: the destination is in the instance's footprint, so the player stays in it
        WorldManager.ThreadSafeTeleport(player, destination);
    }

    /// <summary>
    /// A player was saved inside a landblock that only exists in instances, which means the server went down while they were in it.
    /// The instance is gone, so they are sent to where it sent players, or their sanctuary, or where they started, whichever of
    /// those the persistent world still lets someone into (their sanctuary can be the very same instance-only landblock, or
    /// anywhere else that has since stopped being reachable, so this is never trusted unchecked).
    /// </summary>
    public static void OnPlayerLogin(Player player)
    {
        if (player.Location == null)
        {
            return;
        }

        var template = GetInstanceOnlyTemplate(player.Location.LandblockId);

        // where every player has an instance of their own they are put in it as they enter the world (AssignPersonalInstance), not moved out
        if (template == null || template.Personal)
        {
            return;
        }

        var destination = FirstEnterablePosition(template.ReturnPosition, player.Sanctuary, player.Instantiation);

        _log.Information(
            "[INSTANCE] {Player} was saved inside {Template}, which only exists as an instance. Moving them to {Destination}",
            player.Name,
            template.Name,
            destination.ToLOCString()
        );

        player.Location = new Position(destination);
    }

    /// <summary>
    /// A player who logs out inside an instance is saved at the place the instance sends players, so they log back in there,
    /// in the persistent world, instead of at coordinates that mean nothing outside the instance.<para />
    /// The exception is an instance where every player has one of their own (a training academy): they are saved where they are,
    /// because they get a new instance of it as they log in (AssignPersonalInstance), and carry on from where they left off.
    /// </summary>
    public static void OnPlayerLoggingOut(Player player)
    {
        if (player.InstanceId == Landblock.PersistentInstance)
        {
            return;
        }

        if (Get(player.InstanceId)?.Template.Personal != true)
        {
            var destination = GetReturnPosition(player);
            if (destination != null)
            {
                player.Location = new Position(destination);
            }
        }

        OnMemberLeft(player.InstanceId, player.Guid.Full);

        player.InstanceId = Landblock.PersistentInstance;
    }

    #region Personal instances

    /// <summary>
    /// Makes the training academies known, so that every player who logs in inside one gets an instance of it of their own
    /// (AssignPersonalInstance). Done at startup, with the islands, so that /instance list shows them and admins can open them.
    /// </summary>
    public static void RegisterStarterAcademies()
    {
        foreach (var academy in StarterAcademies.All)
        {
            RegisterTemplate(academy.Template);
        }
    }

    /// <summary>
    /// The registered template whose landblocks a player who logs in at this landblock gets an instance of their own of, if there is one
    /// </summary>
    public static InstanceTemplate GetPersonalTemplate(LandblockId landblockId)
    {
        lock (sync)
        {
            return templates.Values.FirstOrDefault(t => t.Personal && t.Contains(landblockId));
        }
    }

    /// <summary>
    /// Called as a player logs in, just before they enter the world. If they are standing in a place where every player has an instance
    /// of their own (a training academy, which is where every new character starts), this makes theirs and puts them in it, so that they
    /// enter the world there and not in the persistent world. It is also what puts them back in one after they log out inside it.<para />
    /// Whatever goes wrong leaves them in the persistent world, as before: logging in must never fail because of an instance.
    /// </summary>
    public static void AssignPersonalInstance(Player player)
    {
        if (player.Location == null || player.InstanceId != Landblock.PersistentInstance)
        {
            return;
        }

        if (!PropertyManager.GetBool("starter_academy_instances").Item)
        {
            return;
        }

        var template = GetPersonalTemplate(player.Location.LandblockId);
        if (template == null)
        {
            return;
        }

        var instance = Register(template);

        try
        {
            Load(instance);

            player.InstanceId = instance.Id;
            OnMemberEntered(instance.Id, player.Guid.Full);
        }
        catch (Exception ex)
        {
            _log.Error(
                ex,
                "[INSTANCE] Could not make an instance of {Template} for {Player}, who enters the persistent world instead",
                template.Name,
                player.Name
            );

            AbandonLoginInstance(player);
            Close(instance.Id);
        }
    }

    /// <summary>
    /// Takes a player out of the instance that AssignPersonalInstance put them in, because they could not be put in the world there.
    /// The instance is deleted, since nobody else will ever be in it.
    /// </summary>
    public static void AbandonLoginInstance(Player player)
    {
        var instanceId = player.InstanceId;

        if (instanceId == Landblock.PersistentInstance)
        {
            return;
        }

        OnMemberLeft(instanceId, player.Guid.Full);

        player.InstanceId = Landblock.PersistentInstance;

        Close(instanceId);
    }

    #endregion

    internal static void OnMemberEntered(uint instanceId, uint memberGuid)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return;
        }

        lock (sync)
        {
            if (instances.TryGetValue(instanceId, out var instance))
            {
                instance.MemberGuids.Add(memberGuid);
                instance.EmptySince = null;
            }
        }
    }

    internal static void OnMemberLeft(uint instanceId, uint memberGuid)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return;
        }

        lock (sync)
        {
            if (
                instances.TryGetValue(instanceId, out var instance)
                && instance.MemberGuids.Remove(memberGuid)
                && instance.MemberGuids.Count == 0
            )
            {
                instance.EmptySince = UtcNow();
            }
        }
    }

    #endregion

    #region Looking at and moving things

    /// <summary>
    /// An instance as a person reads it. 0 is the persistent world.
    /// </summary>
    public static string Describe(uint instanceId)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return "0 (the persistent world)";
        }

        var instance = Get(instanceId);

        if (instance == null)
        {
            return $"{instanceId} (does not exist any more)";
        }

        var players = instance.MemberCount;

        return $"{instanceId} ({instance.Template.Name}, {players} player{(players == 1 ? "" : "s")}{(instance.IsClosing ? ", shutting down" : "")})";
    }

    /// <summary>
    /// Where a landblock is, in relation to an instance
    /// </summary>
    public static string DescribePlace(uint instanceId, LandblockId landblockId)
    {
        if (instanceId == Landblock.PersistentInstance)
        {
            return IsInstanceOnly(landblockId)
                ? "a landblock that only exists in instances, so nothing should be here"
                : "the persistent world";
        }

        var template = Get(instanceId)?.Template;

        if (template == null)
        {
            return "an instance that does not exist any more";
        }

        if (!template.Contains(landblockId))
        {
            return "OUTSIDE the landblocks of the instance";
        }

        return template.IsBoundary(landblockId)
            ? "the ring of the instance, where players are turned back"
            : "inside the instance";
    }

    /// <summary>
    /// Why an object can't be moved from one instance to another, or null if it can.<para />
    /// Objects that are not players, are lying in a landblock and have a guid of their own (they were made while the server was running)
    /// can be. Players are moved by teleporting them.
    /// </summary>
    internal static string WhyNotMovable(
        ObjectGuid guid,
        bool isPlayer,
        bool isInLandblock,
        bool isGenerator,
        bool isGenerated
    )
    {
        if (isPlayer)
        {
            return "Players are moved with /instance enter and /instance leave.";
        }

        if (!isInLandblock)
        {
            return "It is not on the ground: it is in a container, or worn by someone.";
        }

        if (!guid.IsDynamic())
        {
            return "It is one of the objects that a landblock is made of (a static object). Taking it out of the landblock would take it away from everyone who is in it.";
        }

        if (isGenerator)
        {
            return "It is a generator, and what it made would stay behind.";
        }

        if (isGenerated)
        {
            return "A generator made it and keeps count of it. Make another one in the instance instead.";
        }

        return null;
    }

    /// <summary>
    /// Why an object can't be moved to an instance it is already in (the move leaves it in the world but shown to nobody).
    /// </summary>
    internal static string WhyNotMovableTo(uint currentInstance, uint targetInstance)
    {
        if (currentInstance != targetInstance)
        {
            return null;
        }

        return $"It is already in {(targetInstance == Landblock.PersistentInstance ? "the persistent world" : $"instance {targetInstance}")}.";
    }

    /// <summary>
    /// Takes an object out of the instance it is in and puts it in another one (0 is the persistent world), at a place in it.
    /// It is done the way picking something up and putting it down is: nothing of it is left behind, and everyone who could see
    /// it stops seeing it, and everyone in the other instance sees it appear. See WhyNotMovable for what can be moved.
    /// </summary>
    public static bool TryMoveObject(WorldObject wo, uint instanceId, Position destination, out string problem)
    {
        problem =
            WhyNotMovable(wo.Guid, wo is Player, wo.CurrentLandblock != null, wo.IsGenerator, wo.Generator != null)
            ?? WhyNotMovableTo(wo.InstanceId, instanceId);

        if (problem != null)
        {
            return false;
        }

        if (!CanEnter(instanceId, destination.LandblockId))
        {
            problem =
                $"Landblock {destination.LandblockId.Landblock:X4} is not somewhere anything can be in instance {instanceId}.";
            return false;
        }

        var previousInstance = wo.InstanceId;
        var previousLocation = wo.Location;

        wo.CurrentLandblock.RemoveWorldObject(wo.Guid, false);

        wo.InstanceId = instanceId;
        wo.Location = new Position(destination);

        if (wo.EnterWorld())
        {
            return true;
        }

        // it can't go there: put it back where it was
        wo.InstanceId = previousInstance;
        wo.Location = previousLocation;
        wo.EnterWorld();

        problem = "It could not be placed there: something is in the way.";
        return false;
    }

    #endregion

    #region Ending instances

    /// <summary>
    /// Shuts an instance down: everyone in it is sent out, and it is deleted as soon as they have gone
    /// </summary>
    public static bool Close(uint instanceId)
    {
        List<uint> members;

        lock (sync)
        {
            if (!instances.TryGetValue(instanceId, out var instance))
            {
                return false;
            }

            instance.IsClosing = true;
            members = instance.MemberGuids.ToList();
        }

        foreach (var guid in members)
        {
            var player = PlayerManager.GetOnlinePlayer(guid);

            if (player == null || !Leave(player))
            {
                // they are not online, or there is nowhere to send them, so they can't be waited for
                OnMemberLeft(instanceId, guid);
            }
        }

        return true;
    }

    /// <summary>
    /// Deletes the instances that have been empty for longer than the timeout, and the ones that are shutting down and have emptied.
    /// Called every world tick.
    /// </summary>
    public static void Tick()
    {
        // no lock for this: nearly always there are no instances, and being a tick late for the first one doesn't matter
        if (lastInstanceId == 0)
        {
            return;
        }

        Tick(UtcNow(), EmptyTimeout());
    }

    internal static void Tick(DateTime now, TimeSpan timeout)
    {
        List<WorldInstance> finished;

        lock (sync)
        {
            finished = instances
                .Values.Where(i =>
                    i.MemberCount == 0
                    && (i.IsClosing || (i.EmptySince.HasValue && now - i.EmptySince.Value >= timeout))
                )
                .ToList();

            foreach (var instance in finished)
            {
                instances.Remove(instance.Id);
            }
        }

        foreach (var instance in finished)
        {
            Unload(instance);
        }
    }

    /// <summary>
    /// The instance is no longer known, so nothing can load a landblock for it. Its landblocks are unloaded by the landblock manager,
    /// which destroys everything in them. Nothing in an instance is saved, so that is the end of it.
    /// </summary>
    private static void Unload(WorldInstance instance)
    {
        foreach (var landblockId in instance.Template.Footprint)
        {
            var landblock = LandblockManager.TryGetLandblock(landblockId, instance.Id);
            if (landblock == null)
            {
                continue;
            }

            landblock.Permaload = false;
            LandblockManager.AddToDestructionQueue(landblock);
        }

        _log.Information("[INSTANCE] Deleted {Instance}", instance);
    }

    #endregion
}
