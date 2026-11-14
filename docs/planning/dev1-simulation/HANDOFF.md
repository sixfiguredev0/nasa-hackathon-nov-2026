# Developer 1 handoff

Keep this short and current. Replace sections rather than appending history; history belongs in [DEVLOG.md](../../../DEVLOG.md).

Last updated: 2026-11-14, after Stage 0, Stage 1 and the hybrid-event extension.

## Branch and commits

- Branch: `feat/dev1-core`, based on Fable's planning commit `6685ec9` (`claude/clever-einstein-rwr1ng`).
- Latest implementation commit: `f0eacb6` (core, tests, example content). Docs commit follows it on the same branch.
- **Not pushed** (instruction: no push, merge or PR in this task). The work ran in a cloud container. Unpushed commits are lost when the container is reclaimed, so pushing this branch needs Developer 1's approval soon.
- `main` still does not exist. Combined Unity project: not present on any remote branch (checked 2026-11-14).

## Authorisation recorded

Developer 1 authorised starting the independent C# core before the Unity install finishes, the `main` branch exists or the planning gates are formally cleared. That authorisation is not a claim that a Unity build passed or that team decisions are final. Food, the 10-sol length and the feature cuts in 0002 stay **proposed**. The slice uses Power, Air and Crew Health only; mission length is configurable.

## Done

- **Stage 0.** No Unity project available, so the code is in `game/sim/` as planned. .NET 8 SDK 8.0.428 installed user-local in the container (with approval). Test command below.
- **Stage 1.** `GameSession` as the only state owner; definitions, state and reports in plain `[Serializable]` C#; sol steps 1 to 4 and 6 to 8; four commands plus `Can*` queries; `Changed` once per accepted command; detached snapshot and config copies; `SolReport`; event-free `Forecast`; one scripted two-choice event; success, failure, restart. All ten acceptance tests pass (`Stage1Tests` A1 to A10).
- **Hybrid events** ([0005](../../decisions/0005-hybrid-event-structure.md)): milestones on fixed sols, authored variants resolved once, seeded random pool with windows, once-only, weights and minimum gap; ID-sorted ordering; reproduction log.
- Pulled forward because correctness needed them (recorded in [0003](../../decisions/0003-simulation-architecture.md) implementation notes): facility level fallback, capacity/waste, health recovery, `cannot_afford`, immediate failure when a choice zeroes health.
- Content validator with cost-free-choice lint. Illustrative example JSON in `game/sim/Content/example/`.

## Not done

- Stage 2 (Unity bridge, `ContentLoader`, `SimulationHost`, debug HUD, asmdefs). Needs the combined project.
- Stage 3 (Web build). Stage 4 (contract freeze with Developers 2 and 3).
- Stage 5 items still missing: `modifier` effects, `delaySols` and scheduled effects (step 5). The validator rejects both for now.
- Bot balance runs (Stage 7). Final content and balance are Developer 3's and the team's.

## Build and test

From `game/sim/`, with the .NET 8 SDK (in this container it is at `~/.dotnet/dotnet`):

```bash
dotnet build Core/RedFrontier.Core.csproj
dotnet test Tests/RedFrontier.Core.Tests.csproj
```

Result on `f0eacb6`: Core builds with 0 warnings, 0 errors (`netstandard2.1`, `LangVersion 9.0`, warnings as errors). Tests: `Passed! - Failed: 0, Passed: 37, Skipped: 0, Total: 37`.

Mutation check: removing the affordability check, running the forecast on live state, or letting random events displace milestones each made tests fail (2, 3 and 16 respectively). Reverted.

## Not verified

- **Unity:** nothing. Developer 1 reports a local install of Unity **6000.6.9f1** in progress; Editor launch and Web Build Support are not yet verified. C# 9 / netstandard2.1 compatibility is assumed from Fable's plan, not tested in an Editor. NUnit tests use only classic `Assert.*` calls, expected to run in the Unity Test Runner, unverified.
- **JsonUtility:** the example JSON was parsed with System.Text.Json (public fields, case-sensitive) as a stand-in. A real `JsonUtility` load must be checked in Stage 2, and in a Web build in Stage 3. `uint rngState` in `SimState` is expected to serialise; unverified.
- **Browser:** nothing.

## Public API (proposed until Developers 2 and 3 agree, see [0004](../../decisions/0004-simulation-contracts.md))

```csharp
var session = new GameSession(MissionConfig config, IEnumerable<EventDef> events, int seed); // throws ContentValidationException
session.Changed += change => Redraw();   // once per accepted command; change.kind: CommandApplied|SolResolved|EventRaised|MissionEnded|Restarted

// Read (fresh copies every time)
SimState snapshot      = session.Snapshot;      // sol, phase, outcome, endReason, crewHealth, resources[], facilities[], pending, log[]
SolReport forecast     = session.Forecast;      // next sol at current settings, no events; null once Ended
SolReport last         = session.LastReport;    // null before the first sol
PendingEventView ev    = session.PendingEvent;  // null if none; choices[] with effects, requires, available, unavailableReason
IReadOnlyList<LogEntry> history = session.History;

// Commands -> CommandResult { ok, reason }
session.SetFacilityLevel("life_support", 1);
session.ChooseEventOption(ev.instanceId, "clean_panels");
session.AdvanceSol(snapshot.sol);
session.Restart();       // same seed
session.Restart(1234);   // explicit new seed
// Queries, no side effects: CanSetFacilityLevel, CanAdvanceSol, CanChooseEventOption
```

Reason codes: `mission_ended`, `decision_pending`, `unknown_facility`, `invalid_level`, `no_pending_event`, `stale_event`, `unknown_choice`, `requirement_not_met`, `cannot_afford`, `stale_sol`. End reasons: `health_zero`, `mission_complete`. Facility run reason: `insufficient_resource`.

`SolReport`: `sol`; `resources[]` {id, start, made, usedByFacilities, usedByCrew, wasted, end, shortage}; `facilities[]` {id, requestedLevel, levelRun, reason}; `healthBefore`, `healthAfter`, `healthCauses[]` {code `shortage`|`recovery`, resource, amount}; `eventRaisedId`, `eventRaisedVariantId`; `outcome`, `endReason`.

`LogEntry` (`history`): `sol`, `kind` (`event_raised`, `choice_made`, `shortage`, `mission_ended`), `eventId`, `eventClass` (`major`|`random`), `variantId`, `instanceId`, `choiceId`, `resource`, `amount`, `code`. `ToString()` gives one line for bug reports; include the seed.

## Event schema (for Developer 3)

ARCHITECTURE section 7 plus [0005](../../decisions/0005-hybrid-event-structure.md). In short: one JSON file per event; `trigger.sol` > 0 is a milestone on that sol (optionally with `variants`), `trigger.sol` 0 is the random pool (`minSol`, `maxSol`, `weight`, `once`). Effects: `resource` (negative = cost paid in full) and `health`. 1 to 3 choices. Keep one choice per event always affordable. Examples: `game/sim/Content/example/`.

## Minimal usage for UI (Developer 2)

```csharp
void Redraw() {
    var s = session.Snapshot;
    // draw meters from s.resources and s.crewHealth; disable Advance unless session.CanAdvanceSol().ok
    var ev = session.PendingEvent;
    if (ev != null) foreach (var c in ev.choices) { /* button c.label, c.preview, enabled = c.available */ }
    if (s.phase == MissionPhase.Ended) { /* debrief from s.outcome, s.endReason, session.History */ }
}
```

No resource arithmetic in UI code. Display text for codes belongs to Developer 2.

## Unresolved issue: every choice unaffordable

If all choices of a pending event are unavailable (`requirement_not_met` or `cannot_afford`), the player cannot answer it, `AdvanceSol` is blocked by `decision_pending`, and the mission is stuck. This is an **open gameplay and integration issue**, not only a content-authoring rule: the core does not prevent it, the validator cannot detect it (it depends on runtime stock), and the UI has no defined behaviour for it. Options to decide with Developers 2 and 3: a guaranteed fallback choice per event, a core rule that unaffordable costs convert to a defined alternative, or skipping/auto-resolving with a logged reason. Not decided; no code change yet.

## Exact next task

1. Developer 1: approve pushing `feat/dev1-core` (risk of loss otherwise) and tell Developers 2 and 3 the slice exists (point them at this file and 0005).
2. Review the code, and inspect the colleague's combined Unity project (not yet seen). Stage 2 does not start until both are done and Developer 1 says so. Then Stage 2: Check ARCHITECTURE section 9 first (Unity version, packages, existing code), `git mv` `game/sim/Core` into `Assets/RedFrontier/Core/` with `RedFrontier.Core.asmdef` (`noEngineReferences: true`), then `ContentLoader`, `SimulationHost`, debug HUD. Run the tests in the Unity Test Runner.

Blocker for Stage 2: the combined Unity project and the Editor. Nothing else in Developer 1's scope is blocked.

## Shared updates needed from their owners

- Integrator: create `main`; record 0001; decide whether to keep the nested `game/sim/.gitignore` csproj negation or move it to the root file; README layout entry for `game/sim/`.
- Developers 2 and 3: accept or amend 0002, 0004 and the 0005 schema.
