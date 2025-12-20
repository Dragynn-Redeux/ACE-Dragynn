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
    public List<ResonanceZoneEntry> GetResonanceZoneEntriesEnabled()
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries
            .AsNoTracking()
            .Where(z => z.IsEnabled)
            .ToList();
    }
    public DateTime? GetResonanceZoneEntriesLastModifiedUtc()
    {
        using var context = new ShardDbContext();
        return context.ResonanceZoneEntries
            .AsNoTracking()
            .Max(z => (DateTime?)z.ModifiedAt);
    }

    public int InsertResonanceZoneEntry(ResonanceZoneEntry entry)
    {
        using var context = new ShardDbContext();
        context.ResonanceZoneEntries.Add(entry);
        context.SaveChanges();
        return entry.Id;
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
