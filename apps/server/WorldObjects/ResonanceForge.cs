using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;

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

    public override void ActOnUse(WorldObject activator)
    {
        if (activator is not Player player)
        {
            return;
        }

        ForgeStagingService.HandleClickUse(player, this);
    }

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

        ForgeStagingService.TryHandleDirectItemFastPath(player, source, target, false);
        player.SendUseDoneEvent();
    }
}
