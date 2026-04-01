using System.Collections.Generic;
using System.Linq;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

partial class Creature
{
    /// <inheritdoc/>
    public override bool NeedsPhysicsUpdate =>
        PhysicsObj != null
        && PhysicsObj.is_active()
        && (
            IsDead
            || PhysicsObj.InitialUpdates <= 1
            || (PhysicsObj.IsAnimating && (!IsMonster || !IsAwake))
        );

    /// <summary>
    /// Called every ~5 seconds for Creatures
    /// </summary>
    public override void Heartbeat(double currentUnixTime)
    {
        var expireItems = new List<WorldObject>();

        // added where clause
        foreach (
            var wo in EquippedObjects.Values.Where(i => i.EnchantmentManager.HasEnchantments || i.Lifespan.HasValue)
        )
        {
            // FIXME: wo.NextHeartbeatTime is double.MaxValue here
            //if (wo.NextHeartbeatTime <= currentUnixTime)
            //wo.Heartbeat(currentUnixTime);

            // just go by parent heartbeats, only for enchantments?
            // TODO: handle players dropping / picking up items
            wo.EnchantmentManager.HeartBeat(CachedHeartbeatInterval);

            if (wo.IsLifespanSpent)
            {
                expireItems.Add(wo);
            }
        }

        VitalHeartBeat();

        EmoteManager.HeartBeat();

        DamageHistory.TryPrune();

        QuestManager.DecrementRampQuest();

        if (numRecentAttacksReceived > 0)
        {
            attacksReceivedPerSecond = numRecentAttacksReceived / (float)CachedHeartbeatInterval;
            numRecentAttacksReceived = 0;
        }
        else if (attacksReceivedPerSecond > 0.0f)
        {
            attacksReceivedPerSecond = 0.0f;
        }

        CheckCannotReachTarget();

        if (Location is not null)
        {
            LastHeartbeatPosition = new Position(Location);
        }

        // delete items when RemainingLifespan <= 0
        foreach (var expireItem in expireItems)
        {
            expireItem.DeleteObject(this);

            if (this is Player player)
            {
                player.Session.Network.EnqueueSend(
                    new GameMessageSystemChat(
                        $"Its lifespan finished, your {expireItem.Name} crumbles to dust.",
                        ChatMessageType.Broadcast
                    )
                );
            }
        }

        base.Heartbeat(currentUnixTime);
    }
}
