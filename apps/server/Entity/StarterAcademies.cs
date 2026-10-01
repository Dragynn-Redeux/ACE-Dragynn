using System.Collections.Generic;
using ACE.Entity;

namespace ACE.Server.Entity;

/// <summary>
/// The three training academies that new characters start in, one for each starter town. They are three copies of the same dungeon.
/// Every player who logs in inside one gets an instance of it of their own (InstanceManager.AssignPersonalInstance), so that new
/// characters never share one.
/// </summary>
public static class StarterAcademies
{
    public sealed class Academy
    {
        public string StarterTown { get; }

        public LandblockId Landblock { get; }

        /// <summary>
        /// Where a new character starts
        /// </summary>
        public Position Start { get; }

        /// <summary>
        /// Where an academy's exit portal leads (the first of its two, weenies 1050940 to 1050945 in the world database). It is where
        /// players are sent when they are made to leave an academy instance, for instance by /instance leave.
        /// </summary>
        public Position Exit { get; }

        /// <summary>
        /// Personal, and not instance only: the academy in the persistent world stays, for the people who build in it
        /// </summary>
        public InstanceTemplate Template { get; }

        internal Academy(string starterTown, uint landblock, Position start, Position exit)
        {
            StarterTown = starterTown;
            Landblock = new LandblockId(landblock << 16 | 0xFFFF);
            Start = start;
            Exit = exit;
            Template = new InstanceTemplate(
                $"academy:{starterTown.ToLowerInvariant()}",
                new[] { Landblock },
                start,
                exit,
                instanceOnly: false,
                personal: true
            );
        }
    }

    public static readonly Academy Shoushi = new Academy(
        "Shoushi",
        0x20FC,
        new Position(0x20FC016E, 29.9f, -130f, 0.005f, 0f, 0f, 0f, 1f),
        new Position(0xDE510100, 81.60713f, 131.96498f, 16.005f, 0f, 0f, -0.704816f, -0.70939f)
    );

    public static readonly Academy Yaraq = new Academy(
        "Yaraq",
        0x20FD,
        new Position(0x20FD016E, 29.9f, -130f, 0.005f, 0f, 0f, 0f, 1f),
        new Position(0x7D680105, 64.1957f, 12.004427f, 14.004999f, 0f, 0f, -0.704943f, 0.709264f)
    );

    public static readonly Academy Holtburg = new Academy(
        "Holtburg",
        0x20FE,
        new Position(0x20FE016E, 29.9f, -130f, 0.005f, 0f, 0f, 0f, 1f),
        new Position(0xA5B40100, 136.57654f, 62.048416f, 52.005f, 0f, 0f, -0.703554f, 0.710642f)
    );

    public static readonly IReadOnlyList<Academy> All = new[] { Shoushi, Yaraq, Holtburg };

    /// <summary>
    /// The academy a new character of this starter town starts in. A starter town that is not Shoushi or Yaraq is Holtburg.
    /// </summary>
    public static Academy ForStarterTown(string starterTown)
    {
        return starterTown switch
        {
            "Shoushi" => Shoushi,
            "Yaraq" => Yaraq,
            _ => Holtburg,
        };
    }
}
