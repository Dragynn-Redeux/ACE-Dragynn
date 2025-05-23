using System.ComponentModel;

namespace ACE.Entity.Enum.Properties;

public enum PropertyBool : ushort
{
    // properties marked as ServerOnly are properties we never saw in PCAPs, from here:
    // http://ac.yotesfan.com/ace_object/not_used_enums.php
    // source: @OptimShi
    // description attributes are used by the weenie editor for a cleaner display name

    Undef = 0,

    [Ephemeral]
    [ServerOnly]
    Stuck = 1,

    [Ephemeral]
    Open = 2,
    Locked = 3,
    RotProof = 4,
    AllegianceUpdateRequest = 5,
    AiUsesMana = 6,
    AiUseHumanMagicAnimations = 7,
    AllowGive = 8,
    CurrentlyAttacking = 9,
    AttackerAi = 10,

    [ServerOnly]
    IgnoreCollisions = 11,

    [ServerOnly]
    ReportCollisions = 12,

    [ServerOnly]
    Ethereal = 13,

    [ServerOnly]
    GravityStatus = 14,

    [ServerOnly]
    LightsStatus = 15,

    [ServerOnly]
    ScriptedCollision = 16,

    [ServerOnly]
    Inelastic = 17,

    [ServerOnly]
    [Ephemeral]
    Visibility = 18,

    [ServerOnly]
    Attackable = 19,
    SafeSpellComponents = 20,
    AdvocateState = 21,
    Inscribable = 22,
    DestroyOnSell = 23,
    UiHidden = 24,
    IgnoreHouseBarriers = 25,
    HiddenAdmin = 26,
    PkWounder = 27,
    PkKiller = 28,
    NoCorpse = 29,
    UnderLifestoneProtection = 30,
    ItemManaUpdatePending = 31,

    [Ephemeral]
    GeneratorStatus = 32,

    [Ephemeral]
    ResetMessagePending = 33,
    DefaultOpen = 34,
    DefaultLocked = 35,
    DefaultOn = 36,
    OpenForBusiness = 37,
    IsFrozen = 38,
    DealMagicalItems = 39,
    LogoffImDead = 40,
    ReportCollisionsAsEnvironment = 41,
    AllowEdgeSlide = 42,
    AdvocateQuest = 43,

    [Ephemeral]
    [SendOnLogin]
    IsAdmin = 44,

    [Ephemeral]
    [SendOnLogin]
    IsArch = 45,

    [Ephemeral]
    [SendOnLogin]
    IsSentinel = 46,

    [SendOnLogin]
    IsAdvocate = 47,
    CurrentlyPoweringUp = 48,

    [Ephemeral]
    GeneratorEnteredWorld = 49,
    NeverFailCasting = 50,
    VendorService = 51,
    AiImmobile = 52,
    DamagedByCollisions = 53,
    IsDynamic = 54,
    IsHot = 55,
    IsAffecting = 56,
    AffectsAis = 57,
    SpellQueueActive = 58,

    [Ephemeral]
    GeneratorDisabled = 59,
    IsAcceptingTells = 60,
    LoggingChannel = 61,
    OpensAnyLock = 62,
    UnlimitedUse = 63,
    GeneratedTreasureItem = 64,
    IgnoreMagicResist = 65,
    IgnoreMagicArmor = 66,
    AiAllowTrade = 67,

    [SendOnLogin]
    SpellComponentsRequired = 68,
    IsSellable = 69,
    IgnoreShieldsBySkill = 70,
    NoDraw = 71,
    ActivationUntargeted = 72,
    HouseHasGottenPriorityBootPos = 73,

    [Ephemeral]
    GeneratorAutomaticDestruction = 74,
    HouseHooksVisible = 75,
    HouseRequiresMonarch = 76,
    HouseHooksEnabled = 77,
    HouseNotifiedHudOfHookCount = 78,
    AiAcceptEverything = 79,
    IgnorePortalRestrictions = 80,
    RequiresBackpackSlot = 81,
    DontTurnOrMoveWhenGiving = 82,

    [ServerOnly]
    NpcLooksLikeObject = 83,
    IgnoreCloIcons = 84,
    AppraisalHasAllowedWielder = 85,
    ChestRegenOnClose = 86,
    LogoffInMinigame = 87,
    PortalShowDestination = 88,
    PortalIgnoresPkAttackTimer = 89,
    NpcInteractsSilently = 90,
    Retained = 91,
    IgnoreAuthor = 92,
    Limbo = 93,
    AppraisalHasAllowedActivator = 94,
    ExistedBeforeAllegianceXpChanges = 95,
    IsDeaf = 96,

    [Ephemeral]
    [SendOnLogin]
    IsPsr = 97,
    Invincible = 98,
    Ivoryable = 99,
    Dyable = 100,
    CanGenerateRare = 101,
    CorpseGeneratedRare = 102,
    NonProjectileMagicImmune = 103,

    [SendOnLogin]
    ActdReceivedItems = 104,
    Unknown105 = 105,

    [Ephemeral]
    FirstEnterWorldDone = 106,
    RecallsDisabled = 107,
    RareUsesTimer = 108,
    ActdPreorderReceivedItems = 109,

    [Ephemeral]
    Afk = 110,
    IsGagged = 111,
    ProcSpellSelfTargeted = 112,
    IsAllegianceGagged = 113,
    EquipmentSetTriggerPiece = 114,
    Uninscribe = 115,
    WieldOnUse = 116,
    ChestClearedWhenClosed = 117,
    NeverAttack = 118,
    SuppressGenerateEffect = 119,
    TreasureCorpse = 120,
    EquipmentSetAddLevel = 121,
    BarberActive = 122,
    TopLayerPriority = 123,

    [SendOnLogin]
    NoHeldItemShown = 124,

    [SendOnLogin]
    LoginAtLifestone = 125,
    OlthoiPk = 126,

    [SendOnLogin]
    Account15Days = 127,
    HadNoVitae = 128,

    [SendOnLogin]
    NoOlthoiTalk = 129,
    AutowieldLeft = 130,

    // Timeline
    UseArchetypeSystem = 131,
    OverrideArchetypeXp = 132,
    OverrideArchetypeHealth = 133,
    OverrideArchetypeStamina = 134,
    OverrideArchetypeMana = 135,
    OverrideArchetypeSkills = 136,
    BossKillXpReward = 137,
    ArmorPatchApplied = 138,
    UseLegacyThreatSystem = 139,
    OverrideVisualRange = 140,
    AffectsOnlyAis = 141,
    ExamineItemsSilently = 142, // allows for no/custom message upon NPC Emote Refuse examination of items
    TakeItemsSilently = 143, // allows for no/custom messages for NPC TakeItems emote
    DungeonLockout = 144, // if object is on landblock, no new players will be added to permitted list
    CannotBreakStealth = 145,
    CampfireHotspot = 146,
    MutableQuestItem = 147,
    StruckByUnshrouded = 148,
    MenhirManaHotspot = 149,
    UseNearbyPlayerScaling = 150,
    IsBankContainer = 151,
    ShroudKillXpReward = 152,
    IsPlayerTierChest = 153,
    UpgradeableQuestItem = 154,
    FellowshipRequired = 155,
    SpecialPropertiesRequireMana = 156,
    RepeatConfirmation = 157,
    SilentCombat = 158,
    ReturnHomeWhenStuck = 159,
    ResetFromHotspot = 160,
    JewelAlternateEffect = 161,
    JewelSocket1AlternateEffect = 162,
    JewelSocket2AlternateEffect = 163,
    JewelSocket3AlternateEffect = 164,
    JewelSocket4AlternateEffect = 165,
    JewelSocket5AlternateEffect = 166,
    JewelSocket6AlternateEffect = 167,
    JewelSocket7AlternateEffect = 168,
    JewelSocket8AlternateEffect = 169,
    JewelSocket9AlternateEffect = 170,
    JewelSocket10AlternateEffect = 171,

    /* custom */
    [ServerOnly]
    LinkedPortalOneSummon = 9001,

    [ServerOnly]
    LinkedPortalTwoSummon = 9002,

    [ServerOnly]
    HouseEvicted = 9003,

    [ServerOnly]
    UntrainedSkills = 9004,

    [Ephemeral]
    [ServerOnly]
    IsEnvoy = 9005,

    [ServerOnly]
    UnspecializedSkills = 9006,

    [ServerOnly]
    FreeSkillResetRenewed = 9007,

    [ServerOnly]
    FreeAttributeResetRenewed = 9008,

    [ServerOnly]
    SkillTemplesTimerReset = 9009,

    [ServerOnly]
    FreeMasteryResetRenewed = 9010,

    [Ephemeral]
    [ServerOnly]
    IsPseudoRandomGenerator = 9011,

    [ServerOnly]
    IsModified = 9012,

    [ServerOnly]
    VendorSellsSalvage = 9013,

    [ServerOnly]
    VendorSellsSpecialItems = 9014,
}

public static class PropertyBoolExtensions
{
    public static string GetDescription(this PropertyBool prop)
    {
        var description = prop.GetAttributeOfType<DescriptionAttribute>();
        return description?.Description ?? prop.ToString();
    }
}
