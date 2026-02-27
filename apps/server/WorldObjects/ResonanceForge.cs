using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

public class ResonanceForge : WorldObject
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public ResonanceForge(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public ResonanceForge(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private static void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        UseObjectOnTarget(player, this, target);
    }

    public static void UseObjectOnTarget(Player player, WorldObject source, WorldObject target)
    {
        if (player.IsBusy)
        {
            player.SendUseDoneEvent(WeenieError.YoureTooBusy);
            return;
        }

        // PHASE 1: Stabilization (finalize by removing IsUnstable flag)
        if (target.GetProperty(PropertyBool.IsUnstable) == true && target.Lifespan == null)
        {
            // Remove IsUnstable flag
            target.RemoveProperty(PropertyBool.IsUnstable);

            // Remove IconOverlay property
            target.RemoveProperty(PropertyDataId.IconOverlay);

            // Broadcast updated state
            player.EnqueueBroadcast(new GameMessageUpdateObject(target));
            player.Session.Network.EnqueueSend(
                new GameMessageSystemChat(
                    "(PH) The device hums and aligns it",
                    ChatMessageType.Broadcast
                )
            );
            player.SendUseDoneEvent();
            return;
        }

        // PHASE 2 (future): Destabilization
        // NOTE (future): if target has PropertyBool.IsDestabilized == true, block all crafting/recipe modifications for this item.

        // Invalid target
        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                "This item cannot be processed by the forge.",
                ChatMessageType.Broadcast
            )
        );
        player.SendUseDoneEvent();
    }
}
