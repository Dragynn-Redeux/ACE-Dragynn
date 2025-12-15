using System;
using System.Collections.Generic;
using System.Linq;
using ACE.Common;
using ACE.Entity.Enum;
using ACE.Server.Entity;

namespace ACE.Server.WorldObjects;

partial class Player
{
    private double _lastHotspotHintTick;
    private const double HotspotHintTickTime = 3;

    public double NextTriggerTimeBlue;
    public double NextTriggerTimeYellow;
    public double NextTriggerTimeRed;

    /// <summary>
    /// Check which Menhir Field we are in and recharge valid sigil trinkets
    /// Recharges sigil trinkets that are in the player's pack or equipped, and only of lower tiers
    /// </summary>
    public void RechargeSigilTrinkets(Hotspot manaField)
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();
        var heldSigilTrinketsBlue = GetHeldSigilTrinketsBlue();
        var heldSigilTrinketsYellow = GetHeldSigilTrinketsYellow();
        var heldSigilTrinketsRed = GetHeldSigilTrinketsRed();

        // if player is within the mana field, is not meditating, and has any sigil trinkets on them: trigger a blue effect flash on the player every 3 seconds.
        var forwardCommand = CurrentMotionState.MotionState.ForwardCommand;
        if (forwardCommand != MotionCommand.MeditateState)
        {
            if (_lastHotspotHintTick + HotspotHintTickTime < Time.GetUnixTime())
            {
                var showHintEffect = false;

                var allHeldScarabs = new List<SigilTrinket>()
                    .Concat(equippedSigilTrinkets)
                    .Concat(heldSigilTrinketsBlue)
                    .Concat(heldSigilTrinketsYellow)
                    .Concat(heldSigilTrinketsRed)
                    .ToList();
                if (allHeldScarabs.Count > 0)
                {
                    showHintEffect = true;
                }

                if (showHintEffect)
                {
                    PlayParticleEffect(PlayScript.RestrictionEffectBlue, Guid);
                    _lastHotspotHintTick = Time.GetUnixTime();
                }
            }
            return;
        }

        if (manaField.Tier == 0) // Low
        {
            foreach (var sigilTrinket in equippedSigilTrinkets)
            {
                if (sigilTrinket.SigilTrinketColor == 0)
                {
                    sigilTrinket.RechargeSigilTrinket(manaField, this);
                }

                SetSigilTrinketsBonus(manaField);
            }
        }

        if (manaField.Tier == 1) // Moderate
        {
            foreach (var sigilTrinket in equippedSigilTrinkets)
            {
                if (sigilTrinket.SigilTrinketColor == 0 || sigilTrinket.SigilTrinketColor == 1)
                {
                    sigilTrinket.RechargeSigilTrinket(manaField, this);
                }

                SetSigilTrinketsBonus(manaField);
            }

            foreach (var sigilTrinket in heldSigilTrinketsBlue)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);
            }
        }

        if (manaField.Tier == 2) // High
        {
            foreach (var sigilTrinket in equippedSigilTrinkets)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);

                SetSigilTrinketsBonus(manaField);
            }

            foreach (var sigilTrinket in heldSigilTrinketsBlue)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);
            }

            foreach (var sigilTrinket in heldSigilTrinketsYellow)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);
            }
        }

        if (manaField.Tier == 3) // Lyceum
        {
            foreach (var sigilTrinket in heldSigilTrinketsBlue)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);
            }

            foreach (var sigilTrinket in heldSigilTrinketsYellow)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);
            }

            foreach (var sigilTrinket in heldSigilTrinketsRed)
            {
                sigilTrinket.RechargeSigilTrinket(manaField, this);
            }
        }
    }

    /// <summary>
    /// Check which Menhir Field we are in and recharge EQUIPPED valid scarabs
    /// Additionally, reset the scarab's current bonus and assign it a new one based on which mana field they are in
    /// </summary>
    private void SetSigilTrinketsBonus(Hotspot manaField)
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();

        var bonusStat = manaField.SigilTrinketBonusStat ?? 0;
        var bonusAmount = manaField.SigilTrinketBonusStatAmount ?? 0;

        foreach (var sigilTrinket in equippedSigilTrinkets)
        {
            if (sigilTrinket.SigilTrinketColor == 0 && manaField.Tier == 0) // Blue
            {
                sigilTrinket.SetSigilTrinketBonusStat(bonusStat, bonusAmount);
            }
            else if (sigilTrinket.SigilTrinketColor == 1 && manaField.Tier == 1) // Yellow
            {
                sigilTrinket.SetSigilTrinketBonusStat(bonusStat, bonusAmount);
            }
            else if (sigilTrinket.SigilTrinketColor == 2 && manaField.Tier == 2) // Red
            {
                sigilTrinket.SetSigilTrinketBonusStat(bonusStat, bonusAmount);
            }
        }
    }

    private void UpdateSigilTrinketManaReservation()
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();

        if (equippedSigilTrinkets == null)
        {
            return;
        }

        foreach (var sigilTrinket in equippedSigilTrinkets)
        {
            sigilTrinket.OnEquip(this);
        }
    }

    public bool HasRechargeableSigilTrinkets(int hotspotTier)
    {
        var heldSigilTrinketsBlue = GetHeldSigilTrinketsBlue();
        var heldSigilTrinketsYellow = GetHeldSigilTrinketsYellow();
        var heldSigilTrinketsRed = GetHeldSigilTrinketsRed();

        var equippedSigilTrinketBlue = GetEquippedSigilTrinketOfType(SigilTrinketColor.Blue);
        var equippedSigilTrinketYellow = GetEquippedSigilTrinketOfType(SigilTrinketColor.Yellow);
        var equippedSigilTrinketRed = GetEquippedSigilTrinketOfType(SigilTrinketColor.Red);

        return hotspotTier switch
        {
            0 => // Low
                equippedSigilTrinketBlue?.Structure < equippedSigilTrinketBlue?.MaxStructure,
            1 => // Moderate
                equippedSigilTrinketYellow?.Structure < equippedSigilTrinketYellow?.MaxStructure
                || SigilTrinketInListCanBeRecharged(heldSigilTrinketsBlue),
            2 => // High
                equippedSigilTrinketRed?.Structure < equippedSigilTrinketRed?.MaxStructure
                || SigilTrinketInListCanBeRecharged(heldSigilTrinketsYellow)
                || SigilTrinketInListCanBeRecharged(heldSigilTrinketsBlue),
            3 => // Lyceum
                SigilTrinketInListCanBeRecharged(heldSigilTrinketsRed)
                || SigilTrinketInListCanBeRecharged(heldSigilTrinketsYellow)
                || SigilTrinketInListCanBeRecharged(heldSigilTrinketsBlue),
            _ => false
        };
    }

    private bool SigilTrinketInListCanBeRecharged(List<SigilTrinket> listOfSigilTrinkets)
    {
        return listOfSigilTrinkets.Any(sigilTrinket => sigilTrinket.Structure < sigilTrinket.MaxStructure);
    }

    public bool HasMatchingMenhirBonusStat(int sigilTrinketBonusStat, int sigilTrinketBonusStatAmount)
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();

        return equippedSigilTrinkets.Any(sigilTrinket => (sigilTrinket.SigilTrinketBonusStat ?? 0) == sigilTrinketBonusStat && (sigilTrinket.SigilTrinketBonusStatAmount ?? 0) == sigilTrinketBonusStatAmount);
    }

    public float GetSigilTrinketManaReductionMod(Spell spell, Skill skill, Enum effectId)
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();
        if (equippedSigilTrinkets.Count < 1)
        {
            return 1.0f;
        }

        var sigilTrinketEvent = new SigilTrinketEvent()
        {
            Player = this,
            Skill = skill,
            EffectId = effectId,
            TriggerSpell = spell
        };

        foreach (var sigilTrinket in equippedSigilTrinkets)
        {
            if (sigilTrinketEvent.HasReadySigilTrinketEffect(sigilTrinket))
            {
                if (sigilTrinket.SigilTrinketReductionAmount != null)
                {
                    return 1.0f - (float)sigilTrinket.SigilTrinketReductionAmount;
                }
            }
        }

        return 1.0f;
    }

    /// <summary>
    /// Check for sigil trinket effects when performing a physical attack
    /// </summary>
    /// <param name="target">Target of attack, if any</param>
    /// <param name="damageEvent">DamageEvent info</param>
    /// <param name="onCrit">If the attack crit</param>
    /// <param name="skill"></param>
    /// <param name="effectId"></param>
    public void CheckForSigilTrinketOnAttackEffects(Creature target, DamageEvent damageEvent, Skill skill, Enum effectId, bool onCrit = false)
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();
        if (equippedSigilTrinkets.Count == 0)
        {
            return;
        }

        var sigilTrinketEvent = new SigilTrinketEvent
        {
            Player = this,
            Target = target,
            DamageEvent = damageEvent,
            OnCrit = onCrit,
            Skill = skill,
            EffectId = effectId
        };

        foreach (var sigilTrinket in equippedSigilTrinkets)
        {
            if (sigilTrinketEvent.HasReadySigilTrinketEffect(sigilTrinket))
            {
                sigilTrinketEvent.StartSigilTrinketEffect(sigilTrinket);
            }
        }
    }

    /// <summary>
    /// Check for sigil trinket effects when casting a spell or hitting a target with a spell
    /// </summary>
    /// <param name="target">Target of spell, if any</param>
    /// <param name="spell">The spell cast by player</param>
    /// <param name="isWeaponSpell">If spell was cast from an equipped weapon (proc)</param>
    /// <param name="creatureToCastSpellFrom">The creature that the spell targeted and hit</param>
    /// <param name="onCrit">If the spell crit</param>
    /// <param name="sigilTrinketSpell">If the spell was cast from an equipped sigil trinket</param>
    /// <param name="skill"></param>
    /// <param name="effectId"></param>
    public void CheckForSigilTrinketOnCastEffects(WorldObject target, Spell spell, bool isWeaponSpell, Skill skill, Enum effectId, Creature creatureToCastSpellFrom = null, bool onCrit = false, bool sigilTrinketSpell = false)
    {
        // Don't allow sigil trinket effects to occur if spell was generated from a sigil trinket
        if (sigilTrinketSpell)
        {
            return;
        }

        var equippedSigilTrinkets = GetEquippedSigilTrinkets();
        if (equippedSigilTrinkets.Count == 0)
        {
            return;
        }

        var sigilTrinketEvent = new SigilTrinketEvent
        {
            Player = this,
            Target = target,
            TriggerSpell = spell,
            IsWeaponSpell = isWeaponSpell,
            CreatureToCastSpellFrom = creatureToCastSpellFrom,
            OnCrit = onCrit,
            Skill = skill,
            EffectId = effectId
        };

        foreach (var sigilTrinket in equippedSigilTrinkets)
        {
            if (sigilTrinketEvent.HasReadySigilTrinketEffect(sigilTrinket))
            {
                sigilTrinketEvent.StartSigilTrinketEffect(sigilTrinket);
            }
        }
    }

    /// <summary>
    /// Check for sigil trinket effects when casting a spell or hitting a target with a spell
    /// </summary>
    /// <param name="spellSource">Spell source, if any</param>
    /// <param name="spell">The spell cast by source</param>
    /// <param name="onCrit">If the spell crit</param>
    /// <param name="damage"></param>
    /// <param name="skill"></param>
    /// <param name="effectId"></param>
    public void CheckForSigilTrinketOnSpellHitReceivedEffects(WorldObject spellSource, Spell spell, int damage, Skill skill, Enum effectId, bool onCrit = false)
    {
        var equippedSigilTrinkets = GetEquippedSigilTrinkets();
        if (equippedSigilTrinkets.Count == 0)
        {
            return;
        }

        var sigilTrinketEvent = new SigilTrinketEvent
        {
            Player = this,
            Target = spellSource,
            TriggerSpell = spell,
            SpellDamageReceived = Math.Abs(damage),
            OnCrit = onCrit,
            Skill = skill,
            EffectId = effectId
        };

        foreach (var sigilTrinket in equippedSigilTrinkets)
        {
            if (sigilTrinketEvent.HasReadySigilTrinketEffect(sigilTrinket))
            {
                sigilTrinketEvent.StartSigilTrinketEffect(sigilTrinket);
            }
        }
    }

    public void CreateSigilSpellProjectilesFromTarget(Spell castSpell, Creature creatureToCastSpellFrom)
    {
        CreateSpellProjectiles(castSpell, creatureToCastSpellFrom, this, false, false, 0, true);
    }

    public void CreateSigilPlayerSpell(WorldObject target, Spell castSpell, bool isWeaponSpell)
    {
        CreatePlayerSpell(target, castSpell, isWeaponSpell, true);
    }
}
