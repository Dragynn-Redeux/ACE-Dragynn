using System.Collections.Generic;
using System.Linq;
using ACE.Database.Models.Shard;
using Microsoft.EntityFrameworkCore;
using System;

namespace ACE.Database;

public class ShardConfigDatabase
{
    public bool BoolExists(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesBoolean.Any(r => r.Key == key);
        }
    }

    public bool DoubleExists(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesDouble.Any(r => r.Key == key);
        }
    }

    public bool LongExists(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesLong.Any(r => r.Key == key);
        }
    }

    public bool StringExists(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesString.Any(r => r.Key == key);
        }
    }

    public void AddBool(string key, bool value, string description = null)
    {
        var stat = new ConfigPropertiesBoolean
        {
            Key = key,
            Value = value,
            Description = description
        };

        using (var context = new ShardDbContext())
        {
            context.ConfigPropertiesBoolean.Add(stat);

            context.SaveChanges();
        }
    }

    public void AddLong(string key, long value, string description = null)
    {
        var stat = new ConfigPropertiesLong
        {
            Key = key,
            Value = value,
            Description = description
        };

        using (var context = new ShardDbContext())
        {
            context.ConfigPropertiesLong.Add(stat);

            context.SaveChanges();
        }
    }

    public void AddDouble(string key, double value, string description = null)
    {
        var stat = new ConfigPropertiesDouble
        {
            Key = key,
            Value = value,
            Description = description
        };

        using (var context = new ShardDbContext())
        {
            context.ConfigPropertiesDouble.Add(stat);

            context.SaveChanges();
        }
    }

    public void AddString(string key, string value, string description = null)
    {
        var stat = new ConfigPropertiesString
        {
            Key = key,
            Value = value,
            Description = description
        };

        using (var context = new ShardDbContext())
        {
            context.ConfigPropertiesString.Add(stat);

            context.SaveChanges();
        }
    }

    public ConfigPropertiesBoolean GetBool(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesBoolean.AsNoTracking().FirstOrDefault(r => r.Key == key);
        }
    }

    public ConfigPropertiesLong GetLong(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesLong.AsNoTracking().FirstOrDefault(r => r.Key == key);
        }
    }

    public ConfigPropertiesDouble GetDouble(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesDouble.AsNoTracking().FirstOrDefault(r => r.Key == key);
        }
    }

    public ConfigPropertiesString GetString(string key)
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesString.AsNoTracking().FirstOrDefault(r => r.Key == key);
        }
    }

    public List<ConfigPropertiesBoolean> GetAllBools()
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesBoolean.AsNoTracking().ToList();
        }
    }

    public List<ConfigPropertiesLong> GetAllLongs()
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesLong.AsNoTracking().ToList();
        }
    }

    public List<ConfigPropertiesDouble> GetAllDoubles()
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesDouble.AsNoTracking().ToList();
        }
    }

    public List<ConfigPropertiesString> GetAllStrings()
    {
        using (var context = new ShardDbContext())
        {
            return context.ConfigPropertiesString.AsNoTracking().ToList();
        }
    }
    public bool DeleteResonanceZoneEntry(int id)
    {
        using var context = new ShardDbContext();

        var entry = context.ResonanceZoneEntries.FirstOrDefault(z => z.Id == id);
        if (entry == null)
        {
            return false;
        }

        context.ResonanceZoneEntries.Remove(entry);
        context.SaveChanges();
        return true;
    }

    public ResonanceZoneRow FindResonanceZoneNear(
        uint cellId,
        float x,
        float y,
        float z,
        float tolerance)
    {
        using var context = new ShardDbContext();

        // Pull same-cell zones only (cheap filter)
        var candidates = context.ResonanceZoneEntries
            .Where(z => z.CellId == cellId)
            .AsNoTracking()
            .ToList();

        ResonanceZoneRow best = null;
        var bestDistSq = tolerance * tolerance;

        foreach (var zne in candidates)
        {
            var dx = zne.X - x;
            var dy = zne.Y - y;
            var dz = zne.Z - z;
            var distSq = dx * dx + dy * dy + dz * dz;

            if (distSq <= bestDistSq)
            {
                best = zne;
                bestDistSq = distSq;
            }
        }

        return best;
    }
    public IReadOnlyList<ResonanceZoneRow> GetResonanceZoneEntriesAll()
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries
            .ToList();
    }

    public List<ResonanceZoneRow> GetResonanceZoneEntriesEnabled()
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries
            .AsNoTracking()
            .Where(z => z.IsEnabled)
            .ToList();
    }
    public List<ResonanceZoneRow> GetResonanceZoneEntriesDisabled()
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries
            .AsNoTracking()
            .Where(z => !z.IsEnabled)
            .ToList();
    }
    public ACE.Database.Models.Shard.ResonanceZoneRow GetResonanceZoneEntryById(int id)
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries.FirstOrDefault(r => r.Id == id);
    }

    public DateTime? GetResonanceZoneEntriesLastModifiedUtc()
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries
            .AsNoTracking()
            .Max(z => (DateTime?)z.ModifiedAt);
    }

    public int InsertResonanceZoneEntry(ResonanceZoneRow entry)
    {
        using var context = new ShardDbContext();
        context.ResonanceZoneEntries.Add(entry);
        entry.ModifiedAt = DateTime.UtcNow;
        context.SaveChanges();
        return entry.Id;
    }
   public bool UpdateResonanceZoneEntry(
        int id,
        string name,
        float? radius,
        float? maxDistance,
        string shroudEventKey,
        string stormEventKey,
        bool? isEnabled = null)
    {
        using var context = new ShardDbContext();

        var entry = context.ResonanceZoneEntries.FirstOrDefault(z => z.Id == id);
        if (entry == null)
        {
            return false;
        }

        if (name != null)
        {
            entry.Name = name;
        }

        if (radius.HasValue)
        {
            entry.Radius = radius.Value;
        }

        if (maxDistance.HasValue)
        {
            entry.MaxDistance = maxDistance.Value;
        }

        if (shroudEventKey != null)
        {
            entry.ShroudEventKey = shroudEventKey;
        }

        if (stormEventKey != null)
        {
            entry.StormEventKey = stormEventKey;
        }

        if (isEnabled.HasValue)
        {
            entry.IsEnabled = isEnabled.Value;
        }
        entry.ModifiedAt = DateTime.UtcNow;
        context.SaveChanges();
        return true;
    }



    public void SaveBool(ConfigPropertiesBoolean stat)
    {
        using (var context = new ShardDbContext())
        {
            context.Entry(stat).State = EntityState.Modified;

            context.SaveChanges();
        }
    }

    public void SaveLong(ConfigPropertiesLong stat)
    {
        using (var context = new ShardDbContext())
        {
            context.Entry(stat).State = EntityState.Modified;

            context.SaveChanges();
        }
    }

    public void SaveDouble(ConfigPropertiesDouble stat)
    {
        using (var context = new ShardDbContext())
        {
            context.Entry(stat).State = EntityState.Modified;

            context.SaveChanges();
        }
    }

    public void SaveString(ConfigPropertiesString stat)
    {
        using (var context = new ShardDbContext())
        {
            context.Entry(stat).State = EntityState.Modified;

            context.SaveChanges();
        }
    }
}
