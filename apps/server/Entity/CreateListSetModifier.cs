using System;

namespace ACE.Server.Entity;

public class CreateListSetModifier
{
    public CreateListSet Set;
    public float Modifier;

    /// <summary>
    /// Number of guaranteed draws from the set's trophy pool when
    /// Set.TrophyProbability * Modifier exceeds 1.0 (i.e. floor of the effective probability).
    /// </summary>
    public int GuaranteedDrops;

    /// <summary>
    /// The fractional trophy modifier used for the final probabilistic draw.
    /// When effective probability <= 1.0 this equals Modifier (normal behaviour).
    /// When effective probability > 1.0 this represents only the leftover fraction.
    /// </summary>
    public float TrophyMod;

    public float NoneMod => NoneProbability / Set.NoneProbability;

    public float TrophyProbability => Set.TrophyProbability * TrophyMod;

    public float NoneProbability => 1.0f - TrophyProbability;

    public CreateListSetModifier()
    {
        Modifier = 1.0f;
    }

    public CreateListSetModifier(CreateListSet set, float modifier)
    {
        Set = set;
        Modifier = modifier;

        var trophyProbability = Set.TrophyProbability;
        var effectiveProb = trophyProbability * modifier;

        if (effectiveProb > 1.0f)
        {
            GuaranteedDrops = (int)Math.Floor(effectiveProb);
            var fractionalProb = effectiveProb - GuaranteedDrops;
            TrophyMod = fractionalProb > 0f ? fractionalProb / trophyProbability : 0.0f;
        }
        else
        {
            TrophyMod = modifier;
        }
    }
}
