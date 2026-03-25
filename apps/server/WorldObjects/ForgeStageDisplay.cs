using System;
using ACE.Entity.Enum.Properties;

namespace ACE.Server.WorldObjects;

public enum ForgeStage
{
    None,
    Unstable,
    ResonanceStabilized,
    Stable,
    Destabilized,
}

public static class ForgeStageDisplay
{
    private const PropertyInt ForgePassCountProperty = (PropertyInt)10011;
    private const PropertyBool TerminalDestabilizedLockProperty = (PropertyBool)10012;

    private const uint UnstableOverlayIcon = 0x06005EC2;
    private const uint ResonanceStabilizedOverlayIcon = 0x06005EC0;
    private const uint StableOverlayIcon = 0x06005EBE;
    private const uint DestabilizedOverlayIcon = 0x06005EC1;

    public static ForgeStage GetStage(WorldObject item)
    {
        if (item == null)
        {
            return ForgeStage.None;
        }

        if (item.GetProperty(TerminalDestabilizedLockProperty) == true)
        {
            return ForgeStage.Destabilized;
        }

        if ((item.GetProperty(ForgePassCountProperty) ?? 0) >= 1)
        {
            return ForgeStage.Stable;
        }

        if (item.GetProperty(PropertyBool.IsUnstable) != true)
        {
            return ForgeStage.None;
        }

        return item.GetProperty(PropertyInt.Lifespan).HasValue
            ? ForgeStage.Unstable
            : ForgeStage.ResonanceStabilized;
    }

    public static string GetStageLabel(WorldObject item)
    {
        return GetStage(item) switch
        {
            ForgeStage.Unstable => "Unstable",
            ForgeStage.ResonanceStabilized => "Resonance Stabilized",
            ForgeStage.Stable => "Stable",
            ForgeStage.Destabilized => "Destabilized",
            _ => null,
        };
    }

    public static bool IsAllowedStage(WorldObject item, params ForgeStage[] allowedStages)
    {
        var stage = GetStage(item);

        return stage == ForgeStage.None || Array.IndexOf(allowedStages, stage) >= 0;
    }

    public static void ApplyStageOverlay(WorldObject item)
    {
        if (item == null)
        {
            return;
        }

        var overlayIcon = GetStageOverlayIcon(GetStage(item));
        if (overlayIcon.HasValue)
        {
            item.SetProperty(PropertyDataId.IconOverlay, overlayIcon.Value);
        }
        else
        {
            item.RemoveProperty(PropertyDataId.IconOverlay);
        }
    }

    public static uint? GetStageOverlayIcon(ForgeStage stage)
    {
        return stage switch
        {
            ForgeStage.Unstable => UnstableOverlayIcon,
            ForgeStage.ResonanceStabilized => ResonanceStabilizedOverlayIcon,
            ForgeStage.Stable => StableOverlayIcon,
            ForgeStage.Destabilized => DestabilizedOverlayIcon,
            _ => null,
        };
    }
}