using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Database.Models.World;
using ACE.Entity;
using ACE.Server.Factories;
using ACE.Server.Managers;

namespace ACE.Server.WorldObjects;

partial class WorldObject
{
    public List<LandblockInstance> LinkedInstances = new List<LandblockInstance>();

    public WorldObject ParentLink;
    public List<WorldObject> ChildLinks = new List<WorldObject>();

    public void ActivateLinks(List<LandblockInstance> sourceObjects, List<Biota> biotas, WorldObject parent = null)
    {
        if (LinkedInstances.Count == 0)
        {
            return;
        }

        if (IsGenerator)
        {
            AddGeneratorLinks();
            return;
        }

        if (parent == null)
        {
            parent = this;
        }

        foreach (var link in LinkedInstances)
        {
            WorldObject wo = null;

            // In an instance the guid from the world db can't be used, because the same landblock exists more than once,
            // and nothing is restored from the shard
            var biota = InstanceId == 0 ? biotas.FirstOrDefault(b => b.Id == link.Guid) : null;
            if (biota == null)
            {
                wo = WorldObjectFactory.CreateWorldObject(
                    DatabaseManager.World.GetCachedWeenie(link.WeenieClassId),
                    InstanceId == 0 ? new ObjectGuid(link.Guid) : GuidManager.NewEphemeralStaticGuid()
                );
            }
            else
            {
                wo = WorldObjectFactory.CreateWorldObject(biota);
                //Console.WriteLine("Loaded child biota " + wo.Name);
            }

            if (wo == null)
            {
                continue;
            }

            wo.InstanceId = InstanceId;

            if (InstanceId != 0)
            {
                InstanceManager.Get(InstanceId)?.MapWorldGuid(link.Guid, wo.Guid.Full);
            }

            wo.Location = new Position(
                link.ObjCellId,
                link.OriginX,
                link.OriginY,
                link.OriginZ,
                link.AnglesX,
                link.AnglesY,
                link.AnglesZ,
                link.AnglesW
            );
            parent.SetLinkProperties(wo);
            var added = CurrentLandblock?.AddWorldObject(wo) ?? false;

            // like Landblock.CreateWorldObjects(), this bypasses EnterWorld(), so archetype stats have to be set here
            if (added && wo is Creature creature and not Player)
            {
                creature.ApplyArchetypeSystem();
            }

            if (wo.PhysicsObj != null)
            {
                wo.PhysicsObj.Order = 0;
            }

            wo.ParentLink = parent;
            parent.ChildLinks.Add(wo);

            // process nested links recursively
            foreach (var subLink in link.LandblockInstanceLink)
            {
                var linkInstance = sourceObjects.FirstOrDefault(x => x.Guid == subLink.ChildGuid);

                if (linkInstance != null)
                {
                    wo.LinkedInstances.Add(linkInstance);
                }
            }

            if (wo.LinkedInstances.Count > 0)
            {
                wo.ActivateLinks(sourceObjects, biotas);
            }
        }
    }

    public virtual void SetLinkProperties(WorldObject wo)
    {
        // empty base
        Console.WriteLine($"{Name}.SetLinkProperties({wo.Name}) called for unknown parent type: {WeenieType}");
    }

    public virtual void UpdateLinkProperties(WorldObject wo)
    {
        // empty base
        Console.WriteLine($"{Name}.UpdateLinkProperties({wo.Name}) called for unknown parent type: {WeenieType}");
    }

    public void UpdateLinks()
    {
        foreach (var link in ChildLinks)
        {
            UpdateLinkProperties(link);

            foreach (var subLink in link.ChildLinks)
            {
                UpdateLinkProperties(subLink);
            }
        }
    }
}
