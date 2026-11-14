# 0004: Contracts between simulation, UI and content

- Status: Proposed (cross-team; not in force until Developers 2 and 3 agree)
- Date: 2026-11-14
- Deciders: team (Developers 1, 2, 3)

## Context

Three workstreams build against each other from the first hour. Without an agreed boundary the likely failures are: a sol advancing twice, an event effect applied by both the UI and the simulation, resource arithmetic re-implemented in UI code, and event files that the simulation cannot load.

## Options

1. **Agree a small versioned contract now, freeze it after the first playable slice (recommended).**
2. Let each side build and reconcile at integration. Cheapest today, most expensive on Sunday morning.
3. Developer 1 builds UI glue and event files too. Removes the boundary and the parallelism.

## Decision (proposed)

Option 1. The contract text is in [ARCHITECTURE.md](../planning/dev1-simulation/ARCHITECTURE.md), sections 6 and 7. The binding rules:

**Ownership**
- Only `GameSession` changes simulation state. Nothing else writes resources, health, facility state, the sol counter or the mission outcome.
- UI code reads `Snapshot`, `Forecast`, `LastReport`, `PendingEvent` and `History`, and calls the four commands. It contains no resource arithmetic. Anything it wants to display that needs a rule (a forecast, whether a button is allowed, why not) is requested from the simulation.
- Content files declare effects. They never execute them.
- Display text for events lives in content files (Developer 3). Text for UI chrome and for command rejection codes lives in UI code (Developer 2). The simulation emits stable codes and IDs, never sentences.

**Timing**
- A sol advances only through `AdvanceSol(expectedSol)`. A call with a stale sol number is rejected.
- A pending event blocks `AdvanceSol` until `ChooseEventOption` is accepted.
- Immediate effects apply when the choice is accepted, once. Delayed effects apply at the start of their due sol, once, and are reported with their source choice.
- The UI re-renders from the snapshot on `Changed`. It may animate using `LastReport` but must not derive state from its own animation.

**Stability**
- IDs for resources, facilities, events and choices are lowercase `snake_case`, never reused, never renamed after first use in content.
- The content schema carries `schemaVersion`. The simulation rejects unknown versions and unknown effect types with a clear error rather than guessing.
- New effect types, commands or state fields are added by Developer 1 on request. Requests go on [BOARD.md](../planning/BOARD.md).

## What each developer can do before agreement

| Developer | Safe to do now | Wait for agreement |
|---|---|---|
| 1 | Build the Core slice and tests against this proposal. | Anything inside the shared scene or project settings. |
| 2 | Layout, art, placeholder HUD driven by hard-coded sample values shaped like the snapshot. | Wiring to real commands; relying on exact field names. |
| 3 | Research, sources, event prose, choice trade-offs, science notes. Draft events in the proposed JSON. | Relying on effect types beyond `resource`, `health`, `modifier`; final numeric balance. |

## Consequences

- Easier: integration is a wiring task; duplicate updates are rejected by construction.
- Harder: Developer 2 cannot shortcut by editing values directly; Developer 3 is limited to three effect types until more are requested.
- Affects: `RedFrontier.Core` public surface (Developer 1), all UI scripts (Developer 2), all event and mission content (Developer 3).
- Revisit when: Developer 2's chosen UI technology needs a different notification style; Developer 3's events need an effect the closed set cannot express; or the first integration shows a field missing.
