# World Stability

One quick reference for live shard admins working with the tide-mage world stability tracker.

## Admin Controls

The relevant shard property is `fragment_stability_phase_one`.

### 1. Inspect Current Value

Property:

- `fragment_stability_phase_one`
- default value: `0`
- current documented max: `15000`

Command:

- `fetchlong fragment_stability_phase_one`

Example:

```text
fetchlong fragment_stability_phase_one
```

### 2. Adjust Current Value

Command:

- `modifylong fragment_stability_phase_one <value>`

Examples:

```text
modifylong fragment_stability_phase_one 0
modifylong fragment_stability_phase_one 5000
modifylong fragment_stability_phase_one 15000
```

Notes:

- use this when you need to advance, reduce, or reset tide-mage arc progress on the shard
- `15000` is the current max described by the property registration

## Minimal Notes

- this property is a shard-level long property, not a per-item or per-weenie property
- the current code also displays it as a percentage in some game text