using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Server.Managers;
using ACE.Server.WorldObjects;
using Serilog;

namespace ACE.Server.Entity;

public class AllegianceNode
{
    private static readonly ILogger _log = Log.ForContext(typeof(AllegianceNode));

    public readonly ObjectGuid PlayerGuid;
    public IPlayer Player => PlayerManager.FindByGuid(PlayerGuid);

    public readonly Allegiance Allegiance;

    public readonly AllegianceNode Monarch;
    public readonly AllegianceNode Patron;
    public Dictionary<uint, AllegianceNode> Vassals;

    public uint Rank;

    public bool IsMonarch => Patron == null;

    public bool HasVassals => TotalVassals > 0;

    public int TotalVassals => Vassals != null ? Vassals.Count : 0;

    public int TotalFollowers
    {
        get
        {
            var totalFollowers = 0;

            foreach (var vassal in Vassals.Values)
            {
                totalFollowers += vassal.TotalFollowers + 1;
            }

            return totalFollowers;
        }
    }

    public AllegianceNode(
        ObjectGuid playerGuid,
        Allegiance allegiance,
        AllegianceNode monarch = null,
        AllegianceNode patron = null
    )
    {
        PlayerGuid = playerGuid;
        Allegiance = allegiance;
        Monarch = monarch ?? this;
        Patron = patron;
    }

    public void BuildChain(Allegiance allegiance, List<IPlayer> players, Dictionary<uint, List<IPlayer>> patronVassals)
    {
        patronVassals.TryGetValue(PlayerGuid.Full, out var vassals);

        Vassals = new Dictionary<uint, AllegianceNode>();

        if (vassals != null)
        {
            foreach (var vassal in vassals)
            {
                var node = new AllegianceNode(vassal.Guid, allegiance, Monarch, this);
                node.BuildChain(allegiance, players, patronVassals);

                Vassals.Add(vassal.Guid.Full, node);
            }
        }
        CalculateRank();
    }

    private void CalculateRank()
    {
        // NEW RANK FORMULA
        // A player's allegiance rank depends on the number of unique accounts are under them in
        // their allegiance tree. Accounts who are also above them in the chain do not count towards
        // their rank. Additionally, up to 4 ranks may be obtained from the Leadership skill.
        //
        // Final Rank = FollowerRank + Leadership bonus.
        //
        // Follower Rank:
        // - 1 unique follower = 2
        // - 5 unique followers = 3
        // - 10 unique followers = 4
        // - 25 unique followers = 5
        // - 50 unique followers = 6
        //
        // Leadership bonus = 1 per 50, up to 4

        if (Player == null)
        {
            Rank = 1;
            return;
        }

        var uniqueFollowers = GetUniqueFollowers(this);
        uint baseRank = uniqueFollowers switch
        {
            >= 50 => 6,
            >= 25 => 5,
            >= 10 => 4,
            >= 5 => 3,
            >= 1 => 2,
            _ => 1
        };

        var currentLeadership = Player.GetCurrentLeadership();
        uint leadershipBonus = currentLeadership switch
        {
            >= 200 => 4,
            >= 150 => 3,
            >= 100 => 2,
            >= 50 => 1,
            _ => 0
        };

        Rank = Math.Min(baseRank + leadershipBonus, 10u);
    }

    private double GetUniqueFollowers(AllegianceNode playerNode)
    {
        if (playerNode.Vassals == null || playerNode.Vassals.Count == 0)
        {
            return 0;
        }

        double uniqueFollowers = 0;

        foreach (var vassal in playerNode.Vassals.Values)
        {
            // check to see if character level is at least 10
            if (vassal.Player.GetProperty(PropertyInt.Level) < 10)
            {
                continue;
            }

            // check to see if player has logged in within the past 2 weeks
            var loginTimestamp = vassal.Player.GetProperty(PropertyFloat.LoginTimestamp);
            if (loginTimestamp == null || loginTimestamp + 1209600 < Time.GetUnixTime())
            {
                continue;
            }

            // check to see if this character is an alt on the same account
            if (vassal.Player.Account.AccountId == playerNode.Player.Account.AccountId)
            {
                continue;
            }

            var rankContrib = vassal.Player.GetProperty(PropertyFloat.RankContribution);
            uniqueFollowers += rankContrib != null ? (double)rankContrib : 1.0;

            // recursively count nested vassals
            uniqueFollowers += GetUniqueFollowers(vassal);
        }

        return uniqueFollowers;
    }

    public void Walk(Action<AllegianceNode> action, bool self = true)
    {
        if (self)
        {
            action(this);
        }

        foreach (var vassal in Vassals.Values)
        {
            vassal.Walk(action, true);
        }
    }

    public void ShowInfo(int depth = 0)
    {
        var prefix = "".PadLeft(depth * 2, ' ');
        Console.WriteLine($"{prefix}- {Player.Name}");
        foreach (var vassal in Vassals.Values)
        {
            vassal.ShowInfo(depth + 1);
        }
    }

    public void OnLevelUp()
    {
        var playerLevel = Player.Level ?? 1;

        // When a player reaches level 10, they may now count toward their patron's rank
        // Recalculate rank up the chain
        if (playerLevel == 10)
        {
            RecalculateRankChain();
        }

        // patron = self node
        var patronLevel = playerLevel;

        // find vassals who are not passing xp
        foreach (var vassal in Vassals.Values.Where(i => !i.Player.ExistedBeforeAllegianceXpChanges))
        {
            var vassalLevel = vassal.Player.Level ?? 1;

            // check if vassal now meets criteria for passing xp
            if (patronLevel >= vassalLevel)
            {
                vassal.Player.ExistedBeforeAllegianceXpChanges = true;
            }
        }
    }

    private void RecalculateRankChain()
    {
        // Recalculate rank for this node
        CalculateRank();

        // Recalculate rank for patron up to monarch
        if (Patron != null)
        {
            Patron.RecalculateRankChain();
        }
    }
}
