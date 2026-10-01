using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Network.GameMessages.Messages;

namespace ACE.Server.WorldObjects;

/// <summary>
/// Retired: Bezel Fragments are now applied directly (see <see cref="BezelFragment"/>).
/// Kept so existing tools in player inventories still load, and point players to the fragments.
/// </summary>
public class BezelTool : WorldObject
{
    /// <summary>
    /// A new biota be created taking all of its values from weenie.
    /// </summary>
    public BezelTool(Weenie weenie, ObjectGuid guid)
        : base(weenie, guid)
    {
        SetEphemeralValues();
    }

    /// <summary>
    /// Restore a WorldObject from the database.
    /// </summary>
    public BezelTool(Biota biota)
        : base(biota)
    {
        SetEphemeralValues();
    }

    private void SetEphemeralValues() { }

    public override void HandleActionUseOnTarget(Player player, WorldObject target)
    {
        SendObsoleteMessage(player);
    }

    public static void SendObsoleteMessage(Player player)
    {
        player.Session.Network.EnqueueSend(
            new GameMessageSystemChat(
                "Bezel Tools are no longer needed. Use Bezel Fragments directly on an item to add a socket.",
                ChatMessageType.Craft
            )
        );
        player.SendUseDoneEvent();
    }

    private const uint BezelToolBlacksmithing = 1053976;
    private const uint BezelToolTailoring = 1053977;
    private const uint BezelToolWoodworking = 1053978;
    private const uint BezelToolSpellcrfting = 1053979;
    private const uint BezelToolJewelcrafting = 1053980;

    public static bool IsBezelTool(WorldObject worldObject)
    {
        return worldObject.WeenieClassId is BezelToolBlacksmithing
            or BezelToolTailoring
            or BezelToolWoodworking
            or BezelToolSpellcrfting
            or BezelToolJewelcrafting;
    }
}
