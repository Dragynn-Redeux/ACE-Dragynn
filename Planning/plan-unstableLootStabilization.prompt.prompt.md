# Plan: Unstable Loot Stabilization System (Simplified)

**TL;DR** — Two-stage stabilization using existing UpgradeKit infrastructure. The charged stabilizer (non-stackable, consumed) scales item ONCE via `UpgradeKit.UpgradeItem()`, removes Lifespan, and makes account bound. The Resonance Forge removes instability flags for final commitment, and later will also handle destabilization. Minimal code duplication by directly leveraging existing public APIs. No additional visibility changes needed.

## Implementation Steps

### 1. Create StabilizationDevice.cs (~80 lines)
- Inherit from `WorldObject` (non-stackable charged stabilizer, consumed on use)
- `HandleActionUseOnTarget()` → **yes/no confirmation dialog** (simple X on Y)
- Call `UpgradeKit.UpgradeItem(player, target)` directly — **ONE TIME ONLY** for all stat scaling
- Remove `Lifespan` property (makes scales permanent)
- Set account bound via `SetProperty(PropertyBool.Bonded, true)` or equivalent
- Consume the stabilizer (delete item) after success
- Broadcast updated state without removing `IsUnstable` flag
- Send completion message: "(PH) You stabilize the item"

### 2. Create ResonanceForge.cs (~50 lines)
- Inherit from `WorldObject` (stationary device, not consumable)
- **Phase 1 (now):** if `IsUnstable == true` and `Lifespan == null`, remove `IsUnstable` flag only
- Remove `IconOverlay` property
- Broadcast updated state
- Send completion message: "(PH) The device hums and aligns it"
- **NOTE: Flag removal only, NO UpgradeKit.UpgradeItem() call**
- **Phase 2 (future):** if item is stable and eligible, allow destabilization with confirmation dialog

### 3. Verify UpgradeKit method architecture
- `UpgradeKit.UseObjectOnTarget()` (lines 39–142) = full workflow with kit validation + confirmation dialogs
- `UpgradeKit.UpgradeItem()` (lines 144–333) = pure scaling logic, no constraints ✅
- Device calls `UpgradeItem()` ONCE for all stat scaling
- Forge does NOT call `UpgradeItem()` — only removes flags

### 4. No additional changes to UpgradeKit.cs
- All scaling logic already present and comprehensive
- Handles: Weapons (damage, mods, offense, defense, skill mods) + Armor (level, ward, skill mods) + Jewelry (8 GearRating properties, WardLevel, skill mods) + Universal (spells, item mana)

### 5. Integration points (updated)
- Charged stabilizer creation: replace uncharged scanner with charged stabilizer item when scan completes
- Charged stabilizer uses new WeenieType (e.g., 150) and handler class
- Resonance Forge placement: world location registration (stationary device)
- Resonance Forge uses new WeenieType (e.g., 151) and handler class
- Player tier → wield difficulty mapping already available via `UpgradeKit` internal methods

## Verification Checklist

- [ ] Stabilizer: Use charged stabilizer on unstable item, confirm dialog, verify stats scaled correctly, item account bound, `IsUnstable` flag remains, message displays
- [ ] Stabilizer scales ONCE: Use stabilizer, verify upgrade applied, repeat use blocked or no double-scaling
- [ ] Forge stabilization: Use forge after stabilizer, verify only flags removed, stats NOT changed
- [ ] Flow complete: Stabilizer scales + binds, forge finalizes (no re-scaling)
- [ ] Tier scaling: Verify low-level and high-level players get appropriately scaled items

## Design Decisions

- ✅ `UpgradeKit.UpgradeItem()` called ONCE by charged stabilizer only
- ✅ Resonance Forge does NOT call `UpgradeItem()` — only removes flags
- ✅ Charged stabilizer removes Lifespan (scales become permanent)
- ✅ Charged stabilizer makes item account bound
- ✅ Resonance Forge removes `IsUnstable` + `IconOverlay` (final commitment)
- ✅ Yes/no confirmation dialog for initial stabilization (simple X on Y)
- ✅ No kit requirement checks applied

## Future: Destabilization (Forge)

- If item is stable and eligible, forge offers a confirmation dialog before destabilization
- Destabilization sets a permanent flag to block other crafting systems
- Proposed crafting block comment:
	- NOTE (future): if target has PropertyBool.IsDestabilized == true, block all crafting/recipe modifications for this item.

## Key Discovery

`UpgradeKit.UpgradeItem()` is **already public static** and comprehensively handles:

**Weapons**: Damage, DamageMod, ElementalDamageMod, WeaponRestorationSpellsMod, WeaponOffense, WeaponPhysicalDefense, WeaponMagicalDefense, WeaponSkillMods (LifeMagic/WarMagic)

**Armor/Clothing**: ArmorLevel, WardLevel, 18 ArmorSkillMods (Attack, Deception, DualWield, Health, HealthRegen, LifeMagic, MagicDef, Mana, ManaRegen, Perception, PhysicalDef, Run, Shield, Stamina, StaminaRegen, Thievery, TwohandedCombat, WarMagic)

**Jewelry**: WardLevel, 8 GearRating properties (GearMaxHealth, GearMaxStamina, GearMaxMana, GearCritDamage, GearCritResist, GearDamage, GearDamageResist), Deception/Perception mods, SpecialRatings (43 gear ratings)

**Universal**: ScaleUpSpells, ScaleUpItemMana, ScaleUpSpecialRatings

**No tier calculation needed** — `UpgradeItem()` handles it internally via `GetHighestWieldDifficultyForPlayer()` and `GetRequiredLevelFromPlayerTier()`
