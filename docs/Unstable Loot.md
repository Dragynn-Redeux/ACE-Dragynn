# Unstable Loot

One quick reference for the two people who need this system most on a real shard:

- content creators authoring weenies
- admins operating and troubleshooting the system live

## Content Creator SQL Reference

Use these examples when authoring content for the unstable loot system.

### 1. Monster That Drops Unstable Loot

Required property:

- `PropertyBool.UnstableLoot = 177`

Optional quality weighting:

- `PropertyFloat.LootQualityMod = 172`

```sql
-- Monster that can drop unstable loot
INSERT INTO weenie_properties_bool (object_Id, type, value)
VALUES (<monster_wcid>, 177, True);

-- Optional quality weighting for the generated loot
INSERT INTO weenie_properties_float (object_Id, type, value)
VALUES (<monster_wcid>, 172, <float_value>);
```

Example:

```sql
INSERT INTO weenie_properties_bool (object_Id, type, value)
VALUES (900001, 177, True);

INSERT INTO weenie_properties_float (object_Id, type, value)
VALUES (900001, 172, 0.50);
```

### 2. Resonance Forge Object

Required weenie type:

- `WeenieType.ResonanceForge = 88`

```sql
INSERT INTO weenie (class_Id, class_Name, type)
VALUES (<forge_wcid>, '<forge_name>', 88);
```

Example:

```sql
INSERT INTO weenie (class_Id, class_Name, type)
VALUES (900100, 'ace900100_resonanceforge', 88);
```

### 3. Stabilization Device Object

Required weenie type:

- `WeenieType.StabilizationDevice = 87`

```sql
INSERT INTO weenie (class_Id, class_Name, type)
VALUES (<device_wcid>, '<device_name>', 87);
```

Example:

```sql
INSERT INTO weenie (class_Id, class_Name, type)
VALUES (900101, 'ace900101_stabilizationdevice', 87);
```

### 4. Optional Test-Item Notes

These are mostly useful for hand-authored test items, not normal content authoring.

Fresh unstable item:

- `PropertyBool.IsUnstable = 178`
- `PropertyInt.Lifespan = 267`
- `PropertyBool.IsSellable = 69` set to `False`

Scanner-ready unstable item:

- `PropertyBool.IsUnstable = 178`
- `PropertyInt.Lifespan = 267` present

Resonance-stabilized item:

- `PropertyBool.IsUnstable = 178`
- `PropertyInt.Lifespan = 267` absent

## Live Admin Reference

Use these commands and shard properties to create test items, tune destabilize behavior, and troubleshoot the system on a live server.

### 1. Create Unstable Loot Directly

Commands:

- `ciu <type> <tier> [count] [qualityMod]`
- `createunstable <type> <tier> [count] [qualityMod]`

Examples:

```text
ciu melee 8 1 0.5
createunstable jewelry 7 10 1.0
```

Notes:

- tier range is `1` to `8`
- `qualityMod` is clamped to `0.0` through `2.0`

### 2. Tune Destabilize Variance

Property:

- `destabilize_variance`
- default value: `20`
- meaning: each eligible destabilize property rolls within `+/-` that percent

Commands:

- `fetchdouble destabilize_variance`
- `modifydouble destabilize_variance <value>`

Examples:

```text
fetchdouble destabilize_variance
modifydouble destabilize_variance 20
modifydouble destabilize_variance 35
```

### 3. Enable Stabilization / Destabilize Debug Logging

Property:

- `debug_stabilization`
- default value: `false`

Commands:

- `fetchbool debug_stabilization`
- `modifybool debug_stabilization <true|false>`

Examples:

```text
fetchbool debug_stabilization
modifybool debug_stabilization true
modifybool debug_stabilization false
```

### 4. Live Forge Notes

- Destabilize requires `1x` Pulsing Resonance Fragment
- Pulsing Resonance Fragment WCID: `2023154`
- First forge pass works on resonance-stabilized items
- Second forge pass is the destabilize path and remains locked until `fragment_stability_phase_one >= 9900`
- The unlock threshold is hardcoded at `9900`, which is displayed in game as `66.0%`

### 5. Minimal Live Admin Checklist

When an admin needs to test the whole loop quickly:

1. Create unstable loot with `ciu` or `createunstable`
2. Use the stabilization device on the unstable item
3. If the item reaches the forge, confirm it is resonance-stabilized and not still on the scanner-only step
4. Confirm you have `1x` Pulsing Resonance Fragment for the destabilize pass
5. Tune destabilize behavior with `modifydouble destabilize_variance <value>`
6. Turn debug logging on with `modifybool debug_stabilization true`