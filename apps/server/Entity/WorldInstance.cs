using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ACE.Server.Entity;

/// <summary>
/// One live copy of an InstanceTemplate. Everything in it exists for as long as it does, and nothing in it is ever saved.<para />
/// The state in here is protected by the InstanceManager, which is the only thing that changes it,
/// except for the guids of its static objects, which are made while its landblocks load, on other threads.
/// </summary>
public class WorldInstance
{
    /// <summary>
    /// The id of the instance. 0 is the persistent world, so this is never 0, and it is never used again while the server runs.
    /// </summary>
    public uint Id { get; }

    public InstanceTemplate Template { get; }

    public DateTime CreatedAt { get; }

    /// <summary>
    /// Whatever the instance belongs to, if anything (the fellowship a dungeon was opened for, for example). Not used by the instance itself.
    /// </summary>
    public object Owner { get; }

    /// <summary>
    /// When the last player left, or when the instance was made if nobody has been in it yet. Null while there are players in it.
    /// </summary>
    internal DateTime? EmptySince { get; set; }

    /// <summary>
    /// True once the instance has been told to shut down. Nobody can be sent into it any more, and it is deleted as soon as it is empty.
    /// </summary>
    public bool IsClosing { get; internal set; }

    internal HashSet<uint> MemberGuids { get; } = new HashSet<uint>();

    public int MemberCount => MemberGuids.Count;

    /// <summary>
    /// The guid the world database gave to a static object, and the guid the copy of it in this instance has instead.
    /// Statics in an instance can't keep their guids, because the same landblock exists more than once. Those guids are unique in
    /// the whole world, so one map is enough for all the landblocks of the instance. It is filled while the landblocks load.
    /// </summary>
    private readonly ConcurrentDictionary<uint, uint> worldGuids = new ConcurrentDictionary<uint, uint>();

    /// <summary>
    /// The reverse of worldGuids: the world database guid a static object's guid in this instance stands in for.
    /// </summary>
    private readonly ConcurrentDictionary<uint, uint> instanceGuids = new ConcurrentDictionary<uint, uint>();

    internal void MapWorldGuid(uint worldGuid, uint guidInInstance)
    {
        worldGuids[worldGuid] = guidInInstance;
        instanceGuids[guidInInstance] = worldGuid;
    }

    /// <summary>
    /// The guid an object has in this instance, for a guid that was written down in the world database (or the guid itself,
    /// if it is not one of those: the guid of something that was made in the instance is the same wherever it is looked up)
    /// </summary>
    public uint TranslateWorldGuid(uint guid)
    {
        return worldGuids.TryGetValue(guid, out var guidInInstance) ? guidInInstance : guid;
    }

    /// <summary>
    /// The world database guid a guid in this instance stands in for (or the guid itself, if it was not remapped:
    /// the guid of something that was made in the instance, rather than copied from the world database)
    /// </summary>
    public uint TranslateInstanceGuid(uint guid)
    {
        return instanceGuids.TryGetValue(guid, out var worldGuid) ? worldGuid : guid;
    }

    internal WorldInstance(uint id, InstanceTemplate template, object owner, DateTime createdAt)
    {
        Id = id;
        Template = template;
        Owner = owner;
        CreatedAt = createdAt;
        EmptySince = createdAt;
    }

    public override string ToString()
    {
        return $"instance {Id}: {Template}";
    }
}
