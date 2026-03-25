# Resonance And Portal Storms

One quick reference for live shard admins working with resonance-zone effects.

## Resonance Storms

In code, the resonance-side zone effect is implemented as a shroud zone.

### 1. Add Or Update A Resonance Zone At Your Location

Command:

- `shroudadd key=<zoneKey> name=<name> radius=<float> max=<float> event=<shroudEventKey>`

Example:

```text
shroudadd key=holtburg_rz_01 name=HoltburgRidge radius=25 max=45 event=rz_holtburg_shroud
```

Notes:

- if a zone already exists very close to your current location, the command updates it instead of inserting a second row
- `radius` is the inner zone size
- `max` is the outer allowed distance and must be greater than or equal to `radius`

### 2. Inspect Existing Resonance Zones

Command:

- `rzlist [all|enabled|disabled] [range] [filter]`

Examples:

```text
rzlist
rzlist all
rzlist all 500
rzlist all 500 holtburg
```

Notes:

- default scope is `enabled`
- default range is `200`
- `all` and `disabled` show everything if no range is supplied

### 3. Modify An Existing Resonance Zone By Id

Command:

- `rzmodify <id> [name=<name>] [radius=<float>] [max=<float>] [shroud=<eventKey|clear>] [storm=<eventKey|clear>] [enabled=<true|false>]`

Examples:

```text
rzmodify 12 radius=30 max=50
rzmodify 12 shroud=rz_holtburg_shroud
rzmodify 12 enabled=false
```

Operational notes:

- a zone is active only when its event key is active
- if both shroud and storm keys are blank, the zone is selection-eligible but inert in practice
- the effect is driven by the resonance zone table, not by weenie properties

## Portal Storms

Portal storms use the same resonance-zone table, but with a storm event key instead of a shroud event key.

### 1. Add Or Update A Portal Storm Zone At Your Location

Command:

- `stormadd key=<zoneKey> name=<name> radius=<float> max=<float> event=<stormEventKey>`

Example:

```text
stormadd key=linvak_ps_01 name=LinvakStorm radius=35 max=60 event=ps_linvak_active
```

### 2. Tune The Portal Storm Population Threshold

Property:

- `ps_cap`
- default value: `8`
- meaning: number of eligible players in a landblock required to trigger a portal storm

Commands:

- `fetchdouble ps_cap`
- `modifydouble ps_cap <value>`

Examples:

```text
fetchdouble ps_cap
modifydouble ps_cap 8
modifydouble ps_cap 12
```

Creator notes:

- portal storms do not have a separate warning radius
- portal storm warnings use the same active region as the storm itself: `max` if set, otherwise `radius`
- warnings begin when eligible players inside that region reach `ps_cap - 1`
- the actual portal storm fire condition is `ps_cap`

### 3. Inspect And Adjust Portal Storm Zones

Commands:

- `rzlist [all|enabled|disabled] [range] [filter]`
- `rzmodify <id> [name=<name>] [radius=<float>] [max=<float>] [shroud=<eventKey|clear>] [storm=<eventKey|clear>] [enabled=<true|false>]`

Examples:

```text
rzlist all 500 storm
rzmodify 18 storm=ps_linvak_active
rzmodify 18 enabled=false
```

### 4. Live Storm Notes

- resonance and portal zones both live in the `resonance_zone_entries` shard table
- portal storm activation is gated by the zone's `stormEventKey`
- portal storm warning checks count only eligible players already inside the storm region
- with the current code, there is no extra outer warning band for portal storms beyond the zone's own `max` or `radius`
- only `ps_cap` is clearly registered as a normal shard property in the current property list
- the service also reads fallback values for `ps_global`, `ps_interval`, and `ps_cooldown`, but those are not documented here as normal admin-facing shard properties
- if you have developer-level access, the event system is what actually starts or stops the event key behind a zone