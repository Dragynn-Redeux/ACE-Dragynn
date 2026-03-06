# Destabilized Loot: Direct Item Modifiers

Date: 2026-03-06
Source: Google design doc excerpts (one-time cached review)
Purpose: Candidate effect pool for Resonance Forge destabilize pass.

## Scope Rule
Only include item-facing direct modifiers (what item appraise/properties show as changed).

## Exclusions
Do not use skill/focus/class mechanics as destabilize effects.
Examples excluded: `Overload`, `Battery`, `Enchant`, and similar ability systems.

## Direct Property Modifiers (Static)
- Attack modifier (+/-)
- Damage modifier / percent base damage (+/-)
- Physical Defense modifier (+/-)
- Magic Defense modifier (+/-)
- Armor Level (+/-)
- Aegis (+/-)
- Shield modifier (+/-)
- Two-Handed Combat modifier (+/-)
- Dual Wield modifier (+/-)
- War Magic modifier (+/-)
- Life Magic modifier (+/-)
- Perception modifier (+/-)
- Deception modifier (+/-)
- Thievery modifier (+/-)
- Mana Regeneration modifier (+/-)
- Stamina Regeneration modifier (+/-)
- Health Regeneration modifier (+/-)
- Mana Conversion modifier (+/-)
- Maximum Mana (+/-)
- Elemental Damage bonus (+/-) (caster-oriented lines)

## Direct Tradeoff Modifiers
- Damage (+/-) with Weapon Time (+/-)
- Armor Level (+/-) with Stamina/Mana use penalty (+/-)
- Armor Level/resource penalty balancing variants (one improves while another worsens)
- HP-threshold split: damage (+/-) below threshold and counter-effect above threshold
- Damage (+/-) with mirrored self-damage (`Hematite` pattern)

## Imbue/Tag-Style Item Effects
- Critical Strike
- Crippling Blow
- Armor Rend
- Aegis Rend
- Elemental Rends:
  - Cold Rending
  - Pierce Rending
  - Acid Rending
  - Slash Rending
  - Lightning Rending
  - Bludgeon Rending

## Conditional / Ramping Item Effects
- Reprisal
- Familiarity
- Bravado
- Last Stand
- Ramping War Magic damage
- Ramping Aegis penetration
- Ramping Piercing Resistance penetration
- Ramping Critical Damage
- Chance to gain life on hit
- Chance to gain mana on hit
- Elemental bonus plus area proc variants (frost mist, acid mist, lightning ground, fire ground)
- Chance to cleave nearby enemy
- Passive block percent
- Deflect damage on block

## Resonance Forge Fit Guidance
Favor candidates with instability-themed risk/reward:
- Volatile upside + explicit downside
- Conditional surges (threshold or ramping)
- Backlash/self-cost patterns

Avoid broad system-level or economy-level design items in this pool.
