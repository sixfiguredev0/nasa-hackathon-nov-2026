# 0005: Hybrid event structure

- Status: **Direction confirmed by Developer 1 for the team (2026-11-14). Schema and tuning proposed** until Developer 3 agrees.
- Date: 2026-11-14
- Deciders: team (direction); Developers 1 and 3 (schema, tuning)

## Problem

0003/0004 planned scripted events plus a later seeded pool, with no rule for how they interact. The team wants every playthrough to share the same mission arc while differing in detail (brief: replayability is valued by judges).

## Decision

1. **Major events are milestones** on configured sols (`trigger.sol`). They always occur on that sol and cannot be displaced.
2. **Major events may vary** through small authored `variants`. One is picked with the seeded RNG when the event is raised, stored in the pending event, and never rerolled.
3. **Random events** (`trigger.sol` 0) may occur only on sols with no major event, at most one event per sol, using `eventChancePercent`, `minSol`/`maxSol` windows, `once`, `weight`, and a mission-wide `randomEventMinGapSols`.
4. Every cost of the resolved variant is shown before the choice. No hidden random penalty after confirmation.

## Schema extension (proposed, schemaVersion stays 1)

| Where | Field | Meaning |
|---|---|---|
| `mission.json` | `randomEventMinGapSols` | Minimum difference in sol numbers between two random events. 0 or 1 allows consecutive sols. |
| event | `variants[]` | Major events only. Each: `id`, `weight` (0 = 1), optional `title`/`body` overrides, full `choices`. Use either `choices` or `variants`, not both. |
| variant | bound | Every variant must have the same choice IDs, and each choice must touch the same effect types and targets. Only amounts, requirements and text may differ. The validator enforces this. |

Example: [milestone_dust_storm.json](../../game/sim/Content/example/events/milestone_dust_storm.json).

## Rules of resolution (implemented)

- Step 7 of the sol: major on this sol wins; otherwise, if chance > 0, the gap allows it and the eligible pool is non-empty, roll `0..99 < eventChancePercent`, then a weighted pick from the pool ordered by event ID. Empty pool or zero chance: no RNG draw, the sol continues.
- Events are sorted by ID at load, so file-loading order cannot change a run.
- All draws use the core's seeded RNG stored in the state. Forecasts, snapshots, `PendingEvent` reads and rejected commands do not touch it.
- The log records, per event: sol, event ID (the milestone ID for majors), class `major`/`random`, variant, instance, and the choice made.

## Rationale

Fixed milestones keep the demo path and tutorial predictable and testable. Authored variants give replay value without a generator that can produce unfair or nonsensical combinations. The "same shape" rule is the cheapest enforceable bound on variation.

## Evidence

`game/sim/Tests/HybridEventTests.cs`, 13 tests, 200 seeds: milestones on sols 3 and 7 for every seed; all three variants appear; more than 10 distinct event sequences; no random event on a milestone sol; windows, once-only and gap respected; reads and rejections leave state and RNG unchanged; restart with the same seed replays; load order irrelevant. Fixture content is illustrative.

## Consequences

- Developer 3 authors major events with variants and a random pool; numbers are placeholders until balanced together.
- Variants cannot change which choices exist or what they affect. Wider variation needs a new decision.
- A choice whose cost is unaffordable is shown as unavailable (`cannot_afford`). Content should keep at least one choice per event always available (for example a Crew Health cost), or the player can be stuck. The validator cannot check this; it is a content rule.
- Revisit if playtests show the random rate or gap is wrong (tuning only), or if milestones need conditions beyond a fixed sol.
