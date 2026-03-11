using ACE.Common;

namespace ACE.Server.WorldObjects;

internal static class StabilizationSpellProfile
{
    private enum SpellPromotionBucket
    {
        Minor,
        Major,
        Epic,
        Legendary,
    }

    public static int GetWeightedSpellLevel(int newTier, bool isCantrip)
    {
        var bucket = RollSpellPromotionBucket(newTier);

        if (isCantrip)
        {
            return bucket switch
            {
                SpellPromotionBucket.Minor => 1,
                SpellPromotionBucket.Major => 2,
                SpellPromotionBucket.Epic => 3,
                SpellPromotionBucket.Legendary => 4,
                _ => 1,
            };
        }

        return newTier switch
        {
            3 => bucket == SpellPromotionBucket.Major ? 4 : 3,
            4 => bucket == SpellPromotionBucket.Major ? 4 : 3,
            5 => bucket switch
            {
                SpellPromotionBucket.Major => 4,
                SpellPromotionBucket.Epic => 5,
                _ => 3,
            },
            6 => bucket switch
            {
                SpellPromotionBucket.Epic => 6,
                SpellPromotionBucket.Legendary => 7,
                _ => 5,
            },
            7 => bucket switch
            {
                SpellPromotionBucket.Epic => 6,
                SpellPromotionBucket.Legendary => 7,
                _ => 5,
            },
            _ => 3,
        };
    }

    private static SpellPromotionBucket RollSpellPromotionBucket(int newTier)
    {
        var roll = (float)ThreadSafeRandom.Next(0.0f, 1.0f);

        return newTier switch
        {
            3 => roll < 0.75f ? SpellPromotionBucket.Minor : SpellPromotionBucket.Major,
            4 => roll < 0.50f ? SpellPromotionBucket.Minor : SpellPromotionBucket.Major,
            5 => roll switch
            {
                < 0.25f => SpellPromotionBucket.Minor,
                < 0.99f => SpellPromotionBucket.Major,
                _ => SpellPromotionBucket.Epic,
            },
            6 => roll switch
            {
                < 0.80f => SpellPromotionBucket.Major,
                < 0.99f => SpellPromotionBucket.Epic,
                _ => SpellPromotionBucket.Legendary,
            },
            7 => roll switch
            {
                < 0.70f => SpellPromotionBucket.Major,
                < 0.90f => SpellPromotionBucket.Epic,
                _ => SpellPromotionBucket.Legendary,
            },
            _ => SpellPromotionBucket.Major,
        };
    }
}
