# Forge Crossroads Summary

## Purpose

This note is a design checkpoint, not an implementation plan.

The current resonance forge flow is mechanically functional on the server, but cold-state drag-to-forge appears blocked by client behavior before the server receives an action. We need to decide whether to:

1. keep the current custom forge object and support click/use as the primary interaction, or
2. redesign the transformation endpoint as an NPC-style object that rides the client-tested give/use path.


## Current State

### What already works

1. Scanner / stabilization flow works.
2. Forge click/use flow works.
3. Guided forge selection works.
4. Destabilize mutation logic works server-side.
5. Appraise-visible destabilized state works.
6. Ingredient consumption / restore and terminal lock logic exist.

### What does not work reliably

1. Cold-state item drag to forge.
2. Client appears to block some drag/drop interactions before the server sees them.

### Key evidence

1. Dragging to the garbage barrel produces server logs via the give-object path.
2. Dragging to the forge or to an unopened chest can produce the same client error with no server log.
3. This strongly suggests the client is deciding whether a target is a valid drag recipient before sending a request.


## Current Architecture

### Stabilization step

Primary file:

1. `apps/server/WorldObjects/StabilizationDevice.cs`

Role:

1. Uses an item on a target item.
2. Validates unstable state.
3. Runs server-side item upgrade / scaling.
4. Clears lifespan on success.
5. Leaves `IsUnstable` in place so forge can process the item later.

### Forge orchestration step

Primary files:

1. `apps/server/WorldObjects/ForgeStagingService.cs`
2. `apps/server/WorldObjects/DestabilizedLootForge.cs`
3. `apps/server/WorldObjects/DestabilizedLootEffects.cs`

Roles:

1. Determine first-pass and second-pass eligibility.
2. Route click flow and direct item flow.
3. Confirm and execute final destabilize.
4. Apply actual stat mutation.
5. Mark the item terminally destabilized and blocked from later alteration.

### Object model

Current forge object:

1. `apps/server/WorldObjects/ResonanceForge.cs`
2. Inherits `WorldObject`.
3. Uses custom `HandleActionUseOnTarget` behavior.

Important comparison:

1. `Creature` inherits `Container` in `apps/server/WorldObjects/Creature.cs`.
2. The garbage barrel uses creature-style data and the give path.


## What Transfers Cleanly To An NPC-Style Forge

These parts are mostly interaction-agnostic and can be reused with little or no core logic change.

### 1. Stabilization logic

Files:

1. `apps/server/WorldObjects/StabilizationDevice.cs`
2. `apps/server/WorldObjects/StabilizationSpellProfile.cs`
3. `apps/server/WorldObjects/UpgradeKit.cs`

Why transferable:

1. This step is already server-authoritative.
2. It operates on the target item, not on forge object identity.
3. NPC-style forge redesign does not require replacing the scanner step.

### 2. Destabilize mutation engine

Files:

1. `apps/server/WorldObjects/DestabilizedLootEffects.cs`

Why transferable:

1. This is the actual mutation engine.
2. It already works on `WorldObject item`.
3. It does not care whether the triggering endpoint is a forge object, NPC dialogue, or scripted give interaction.

### 3. Finalization / commit logic

Files:

1. `apps/server/WorldObjects/DestabilizedLootForge.cs`

Why transferable:

1. Ingredient validation, consumption, rollback, and commit are all server-side.
2. Item lookup, confirmation, and final mutation are already separated.
3. This class could be reused as the "finalize transformation" service even if the player talks to an NPC rather than using a forge object.

### 4. Eligibility rules

Files:

1. `apps/server/WorldObjects/ForgeStagingService.cs`

Why transferable:

1. First-pass gate is already explicit: unstable, lifespan removed, not terminally destabilized.
2. Second-pass gate is already explicit: forge pass count and unlock state.
3. NPC flow can call the same predicates.

### 5. Appraise and item-state markers

Files:

1. `apps/server/Network/Structure/AppraiseInfo.cs`

Why transferable:

1. These are item-state outputs, not interaction outputs.
2. Any future NPC endpoint should keep the same item markers.

### 6. Post-transform alteration blocking

Files:

1. `apps/server/WorldObjects/DestabilizedLootForge.cs`
2. `apps/server/WorldObjects/ArmorPatch.cs`
3. `apps/server/WorldObjects/Salvage.cs`

Why transferable:

1. These are policy checks on terminally destabilized items.
2. They do not depend on how the item got transformed.


## What Needs Adaptation But Not A Full Rewrite

These pieces are reusable in concept, but their entrypoints and messaging would need to change.

### 1. Forge staging service

File:

1. `apps/server/WorldObjects/ForgeStagingService.cs`

What changes:

1. Today it assumes click-use or direct item-to-forge object flow.
2. In NPC mode, it would become more of a staging / validation coordinator.
3. The service should stop owning client-specific hints like fast-path drag tips.

What likely remains:

1. `IsEligibleForFirstPass(...)`
2. `GetForgePassCount(...)`
3. `TryProcessItem(...)` split into more explicit named operations

### 2. Confirmation and dialogue text

Files:

1. `apps/server/WorldObjects/StabilizationDevice.cs`
2. `apps/server/WorldObjects/DestabilizedLootForge.cs`
3. `apps/server/WorldObjects/ForgeStagingService.cs`

What changes:

1. Current strings are partly placeholder / system style.
2. NPC mode would want tell, direct broadcast, or emote-backed interaction text.
3. This is mostly presentation work, not engine work.

### 3. Interaction wrappers

Files:

1. `apps/server/WorldObjects/ResonanceForge.cs`
2. `apps/server/WorldObjects/Player_Inventory.cs`

What changes:

1. The current wrappers are built around object use and inventory routing.
2. In NPC mode, these wrappers would shrink or be replaced by give/use conversation handlers.


## What Likely Needs A Real Redesign

These are the areas where the current approach is bound to the forge-as-custom-object model and should not be carried forward blindly.

### 1. Drag support through container-like paths

Files heavily involved:

1. `apps/server/WorldObjects/Player_Inventory.cs`

Why redesign / drop:

1. We have strong evidence the client sometimes blocks the request before the server sees it.
2. Server-side interception cannot fix a request the client never sends.
3. Continuing to chase drag support through container and give routing is likely diminishing-return work.

### 2. Forge as a pure `WorldObject` interaction endpoint

File:

1. `apps/server/WorldObjects/ResonanceForge.cs`

Why redesign:

1. This is likely not a client-recognized drag recipient from a cold state.
2. If long-term design requires reliable drag-give behavior, the data shape the client sees probably has to change.

### 3. Temporary debug instrumentation

Files:

1. `apps/server/WorldObjects/Player_Inventory.cs`
2. `apps/server/WorldObjects/ForgeStagingService.cs`

Why redesign / remove later:

1. These logs are diagnostic only.
2. They should not become part of the final system design.


## NPC-Style Forge: Likely Best Long-Term Shape

If we choose the NPC path, the cleanest version is probably:

1. Keep a dedicated custom class for the forge endpoint.
2. Make it creature-like in data so the client accepts give interactions.
3. Make it object-like in presentation so it still feels like a forge or crafting station.

### Candidate shape

1. Custom forge class deriving from `Creature` or creature-backed endpoint with a dedicated forge behavior class.
2. Weenie/data shaped like an object-looking NPC:
3. `NpcLooksLikeObject = true`
4. `DontTurnOrMoveWhenGiving = true`
5. Controlled acceptance behavior, likely narrower than unconditional gift intake.

### What that buys us

1. Likely client acceptance of drag/give from a cold state.
2. Ability to support dialogue, tell text, use conversation, and item give in one model.
3. Cleaner long-term UX than fighting the current drag/client mismatch.

### What it costs

1. Data redesign for the forge endpoint.
2. Revalidation of targeting, appraise/profile behavior, AI-adjacent creature assumptions, and emotes.
3. A planned migration rather than a tiny patch.


## Recommended Decision Options

### Option A: Stay With Current Custom Object Forge

Best if:

1. click/use is acceptable as the official interaction
2. minimal further change is preferred
3. drag support is considered optional

Keep:

1. current scanner flow
2. current forge click/use flow
3. current destabilize engine and commit logic

Drop:

1. further attempts to force cold-state drag through server-only routing

### Option B: Long-Term NPC-Style Forge Redesign

Best if:

1. drag-to-forge is important to the experience
2. conversation / tell / emote-driven interaction is desirable
3. a more deliberate redesign is acceptable

Keep:

1. scanner logic
2. destabilize engine
3. commit / ingredient / lock logic
4. appraise markers
5. alteration blocking

Rewrite or refactor:

1. forge endpoint object/data
2. interaction entrypoints
3. player-facing messaging flow
4. staging service responsibilities


## Practical Recommendation

If the immediate goal is to finish the destabilize feature with low risk, choose Option A and stop chasing drag.

If the immediate goal is to build the correct long-term interaction model and drag matters, choose Option B and treat it as a planned redesign, not a bugfix.

The important conclusion from the current investigation is that the destabilize engine itself is not the problem. The crossroads is almost entirely about the interaction endpoint model the client is willing to cooperate with.