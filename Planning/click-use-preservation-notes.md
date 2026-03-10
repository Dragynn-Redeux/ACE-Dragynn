# Click/Use Local Delta Facts

Date: 2026-03-09
Purpose: Record the observed working-tree changes relative to `HEAD` without making branch recommendations.

## Scope

This note describes the local delta that existed when reviewed on 2026-03-09.

The reviewed files were:

1. `apps/server/WorldObjects/ForgeStagingService.cs`
2. `apps/server/WorldObjects/Player_Inventory.cs`
3. `apps/server/WorldObjects/StabilizationDevice.cs`
4. `Planning/forge-npc-crossroads-summary.md`

## Observed Facts

### 1. `ForgeStagingService.cs` contains two kinds of local changes

File:

1. `apps/server/WorldObjects/ForgeStagingService.cs`

Observed code changes:

1. A `Serilog` import and logger field were added.
2. Debug helper methods were added for forge tracing.
3. Debug log calls were added around click-use and direct-item handling.
4. `GetProcessableInventoryItems(...)` was changed from:
	- unstable and not terminally destabilized
	to:
	- `IsEligibleForFirstPass(i, out _)`

Observed implication:

1. The file currently mixes a behavioral refinement with temporary diagnostics.
2. The behavioral refinement narrows bulk candidate selection to the same explicit first-pass rule used elsewhere.

### 2. `Player_Inventory.cs` contains the drag-path experiment work

File:

1. `apps/server/WorldObjects/Player_Inventory.cs`

Observed code changes:

1. A forge-specific debug logging helper was added.
2. `TryHandleForgePutItemFastPath(...)` was added.
3. `HandleActionPutItemInContainer(...)` now checks the forge fast path before normal container processing.
4. Additional forge-related checks were added in put-item continuation paths.
5. `HandleActionGiveObjectRequest(...)` was changed to work from a raw target object, not only a `Container` cast.
6. Forge-related debug logging was added around raw target resolution and closed-container branches.
7. NPC give handling was widened so the raw target can be passed through when it is recognized as a forge target.

Observed implication:

1. The local delta in this file is centered on intercepting and diagnosing drag/give behavior for forge targets.
2. The changes affect generic inventory and give-object routing, not only forge-local code.

### 3. `StabilizationDevice.cs` contains a message-only local change

File:

1. `apps/server/WorldObjects/StabilizationDevice.cs`

Observed code change:

1. The success chat text was changed from a direct system-style stabilization message to:
	- `The unstable resonance within {target.Name} stills; its decay is arrested and its power is fixed to your current tier.`

Observed implication:

1. The current local delta in this file is presentation text only.
2. No logic change was observed in the reviewed diff.

### 4. `forge-npc-crossroads-summary.md` exists as an untracked design note

File:

1. `Planning/forge-npc-crossroads-summary.md`

Observed file state:

1. The file is currently untracked relative to `HEAD`.
2. The file documents the endpoint-model investigation around custom forge object vs creature-backed/NPC-style forge behavior.
3. The file includes the specific candidate NPC-style properties:
	- `NpcLooksLikeObject = true`
	- `DontTurnOrMoveWhenGiving = true`

Observed implication:

1. This file captures design reasoning and investigation history.
2. It does not participate in runtime behavior.

## High-Level Summary Of The Local Delta

1. The current local diff contains one mixed logic-and-debug file: `ForgeStagingService.cs`.
2. The current local diff contains one drag-experiment hotspot: `Player_Inventory.cs`.
3. The current local diff contains one presentation-only code change: `StabilizationDevice.cs`.
4. The current local diff contains one design-only untracked document: `forge-npc-crossroads-summary.md`.

## Relationship To The Click/Use Decision Record

The observed facts above are the source-level details behind the broader branch decision already recorded elsewhere:

1. The gameplay engine itself was not the main unresolved problem.
2. The experimental local work concentrated primarily in endpoint routing and drag/give handling.
3. The current working-tree delta was not a uniform feature set; it was a mix of diagnostics, drag-path experiments, one eligibility refinement, one message change, and one design memo.