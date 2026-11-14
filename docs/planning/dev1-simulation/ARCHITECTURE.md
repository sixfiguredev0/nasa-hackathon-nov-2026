# RED FRONTIER simulation architecture (Developer 1)

Status: **proposed**. Decisions are recorded in [0002](../../decisions/0002-first-release-scope.md), [0003](../../decisions/0003-simulation-architecture.md), [0004](../../decisions/0004-simulation-contracts.md) and [0005](../../decisions/0005-hybrid-event-structure.md) (hybrid events).

**Implemented (2026-11-14):** Stage 1 and the hybrid-event extension, in `game/sim/Core`. Where the code differs from this text, the code and the implementation notes in 0003 win; the sections below are updated for the public contract (sections 6 and 7). This document is the detail behind them. Code and JSON below are illustrative shapes, not final signatures. All numbers are placeholder game values, not scientific data.

Written without sight of the combined Unity project. Section 9 lists what to check when it arrives.

## 1. Shape

```
Content JSON (text assets)            Developer 3 (events), Developers 1+3 (mission balance)
        |  parsed by
        v
RedFrontier.Runtime   SimulationHost (MonoBehaviour), ContentLoader, DebugHud     Developer 1
        |  constructs and exposes
        v
RedFrontier.Core      GameSession: state, rules, commands, reports. No UnityEngine. Developer 1
        ^  reads snapshots, calls commands, listens to Changed
        |
RedFrontier.UI        HUD, panels, map, debrief                                     Developer 2
```

Dependencies point one way: UI -> Runtime -> Core. Core knows nothing about Unity, scenes, text assets or UI. Tests reference Core only.

There is one "manager": `SimulationHost`. It loads content, creates the `GameSession` and hands it to whoever asks. It has no rules.

## 2. Modules in Core

| Module | Responsibility | Must not |
|---|---|---|
| `MissionConfig`, `ResourceDef`, `FacilityDef`, `EventDef`, `EffectDef` | Plain serialisable definitions loaded from JSON. Immutable after load. | Hold runtime state. |
| `SimState` | The complete runtime state (section 3). | Contain logic. |
| `GameSession` | Sole owner of the mutable state. Validates and executes commands, publishes snapshot, forecast, report, history. | Reference Unity; expose its private state. |
| `SolResolver` | The order of operations for one sol (section 4). Pure function of state and config. Also used for the forecast. | Raise UI concerns or read the clock. |
| `EffectApplier` | Applies an effect to state, or queues it if delayed. The only code that interprets effect types. | Be called from outside `GameSession`. |
| `EventDirector` | Chooses the event for a sol: scripted first, then seeded pool. | Apply effects. |
| `ContentValidator` | Checks config and events: unique IDs, known targets, known effect types, bounds, 1 to 3 choices, and a lint that every choice in a multi-choice event carries a cost. | Fix content silently. |
| `Rng` | Small seeded generator; its state is a field of `SimState`. | Use `System.Random` or time. |

## 3. State and configuration

```csharp
[Serializable] public sealed class SimState {
    public int version;            // state format, for a later save feature
    public int seed; public uint rngState;
    public int sol;                // 1-based: the sol being planned, not yet resolved
    public MissionPhase phase;     // Planning | AwaitingDecision | Ended
    public MissionOutcome outcome; // None | Success | Failure
    public string endReason;       // code, e.g. "health_zero", "mission_complete"
    public int crewCount; public int crewHealth;      // health 0..100
    public List<ResourceState> resources;   // id, amount, capacity
    public List<FacilityState> facilities;  // id, level (index into the def's levels)
    public List<ActiveModifier> modifiers;  // facilityId, percent, lastSol, sourceEventId, sourceChoiceId
    public List<ScheduledEffect> scheduled; // dueSol, effect, sourceEventId, sourceChoiceId, chosenOnSol
    public PendingEvent pending;            // eventId, instanceId; empty when none
    public List<string> usedEventIds;       // for once-only events
    public int nextEventInstanceId;
}
```

Configuration (`mission.json`, illustrative values):

```json
{
  "schemaVersion": 1,
  "missionLengthSols": 10,
  "crewCount": 4,
  "startHealth": 100, "healthRecoveryPerSol": 5,
  "resources": [
    { "id": "power", "start": 8,  "capacity": 20 },
    { "id": "air",   "start": 12, "capacity": 20 },
    { "id": "food",  "start": 12, "capacity": 20 }
  ],
  "crewNeeds": [
    { "resource": "air",  "perCrewPerSol": 1, "healthPenaltyPerSol": 25 },
    { "resource": "food", "perCrewPerSol": 1, "healthPenaltyPerSol": 10 }
  ],
  "facilities": [
    { "id": "solar_array", "startLevel": 0,
      "levels": [ { "id": "on", "makes": [ { "resource": "power", "amount": 10 } ] } ] },
    { "id": "habitat_life_support", "startLevel": 2,
      "levels": [ { "id": "off" },
                  { "id": "eco",  "uses": [ { "resource": "power", "amount": 2 } ], "makes": [ { "resource": "air", "amount": 3 } ] },
                  { "id": "full", "uses": [ { "resource": "power", "amount": 4 } ], "makes": [ { "resource": "air", "amount": 5 } ] } ] },
    { "id": "greenhouse", "startLevel": 2,
      "levels": [ { "id": "off" },
                  { "id": "eco",  "uses": [ { "resource": "power", "amount": 2 } ], "makes": [ { "resource": "food", "amount": 2 } ] },
                  { "id": "full", "uses": [ { "resource": "power", "amount": 5 } ], "makes": [ { "resource": "food", "amount": 5 } ] } ] }
  ],
  "eventChancePercent": 0
}
```

Units and rules of thumb:
- **Air and Food** are counted in astronaut-days: one unit keeps one crew member supplied for one sol. This is an honest game unit that needs no invented kilograms. Real figures belong in Developer 3's science notes with sources.
- **Power** is an abstract "power unit". If the team wants a real unit on screen, Developer 3 supplies a sourced conversion; until then the UI should not print "kWh".
- Every level states its inputs and outputs explicitly. There is no percentage scaling of levels, so a child (and a balancer) reads exactly what Eco does.
- Capacity clamps stock. Overflow is lost and reported as `wasted`.
- Facility order in the file is the power priority order. Producers of power come first.

## 4. Sol resolution

The player plans sol N (sets levels, answers that sol's event). `AdvanceSol(expectedSol: N)` then runs the whole list below synchronously, once:

| Step | What happens | Notes |
|---|---|---|
| 0 | Validate: phase is Planning, `expectedSol == sol`. | Otherwise reject; nothing changes. |
| 1 | Facilities run in config order at their set level. A facility that cannot afford its level drops to the highest lower level it can afford. Outputs are scaled by active modifiers (integer percent, floor, minimum 0). | The report records requested level, level actually run, and why. |
| 2 | Crew needs are consumed. Unmet need is a shortage. | |
| 3 | Health: subtract the penalty for each shortage. If there were none, add recovery. Clamp 0 to 100. | Report lists each cause with its amount. |
| 4 | `sol` becomes N+1. Modifiers whose `lastSol` is below N+1 expire. | |
| 5 | Scheduled effects with `dueSol == N+1` apply in the order they were queued, then leave the queue. | Reported with source event, choice and the sol it was chosen. |
| 6 | End check: health at 0 gives Failure. Otherwise N+1 beyond `missionLengthSols` gives Success. If ended, stop here. | Failure is checked first. |
| 7 | `EventDirector` picks at most one event for sol N+1. If one is raised, phase becomes AwaitingDecision. | Major milestone (`trigger.sol`) always wins; its variant is picked and stored now. Otherwise the seeded random pool (see [0005](../../decisions/0005-hybrid-event-structure.md)). |
| 8 | Publish snapshot, forecast, report and history. Fire `Changed` once. | |

Timing of effects:

| Kind | Applied | Lasts |
|---|---|---|
| `SetFacilityLevel` | Recorded at once. Changes flows only when the sol resolves. Free to change again before advancing. | Until changed. |
| Immediate effect of a choice (`delaySols` 0) | When `ChooseEventOption` is accepted, during planning of the current sol, before that sol's flows. | Permanent change to stock or health. |
| Timed modifier (`durationSols` d) chosen on sol S | Affects the flows of sols S to S+d-1. | Expires in step 4. |
| Delayed effect (`delaySols` k >= 1) chosen on sol S | Step 5 of the advance that enters sol S+k. A delayed modifier starts on that sol. | As per its type. |

Exactly-once guarantees:
- A second `AdvanceSol(N)` after the first succeeded is rejected because `sol` is already N+1.
- A second `ChooseEventOption` for the same `instanceId` is rejected because the pending event is cleared on the first.
- Scheduled effects are removed from the queue in the same step that applies them, and the queue is part of the state.

At mission start, step 7 runs for sol 1, so the mission can open with a scripted briefing event.

## 5. Mission outcome, restart, determinism, save

- **Success:** the sol counter passes `missionLengthSols` with health above 0. **Failure:** health reaches 0. Both set phase Ended; every command except `Restart` is then rejected.
- **Restart:** `Restart(seed)` rebuilds the state from config inside the same session and fires `Changed` with kind `Restarted`. No scene reload. Same seed replays the same mission; a new seed varies the event pool.
- **Determinism:** same config, events, seed and command sequence give the same state. Integers only; events are sorted by ID after loading because load order is not guaranteed.
- **Save/load:** not in the first release. Because the state is one serialisable object and effects in flight live inside it, a later save is "serialise the snapshot, store it, construct a session from it". Not built until asked.

## 6. Contract for the UI developer (proposed)

```csharp
public sealed class GameSession {
    // Read. All are replaced, never mutated in place, after each accepted command.
    public MissionConfig Config { get; }
    public SimState Snapshot { get; }          // a copy; writing to it changes nothing
    public SolForecast Forecast { get; }       // next sol at current settings, excluding unknown events
    public SolReport LastReport { get; }       // null before the first sol resolves
    public PendingEventView PendingEvent { get; } // null when none; includes per-choice availability
    public IReadOnlyList<HistoryEntry> History { get; } // choices, delayed consequences, shortages, outcome

    public event Action<SessionChange> Changed; // exactly once per accepted command

    // Invoke. Each returns CommandResult { bool ok; string reason; }
    public CommandResult SetFacilityLevel(string facilityId, int level);
    public CommandResult ChooseEventOption(int eventInstanceId, string choiceId);
    public CommandResult AdvanceSol(int expectedSol);
    public CommandResult Restart(int seed);

    // Ask before showing a control. Same checks as the commands, no side effects.
    public CommandResult CanSetFacilityLevel(string facilityId, int level);
    public CommandResult CanAdvanceSol();
    public CommandResult CanChooseEventOption(int eventInstanceId, string choiceId);

    // Added in implementation:
    public CommandResult Restart();            // same seed: replays the run
    public int Seed { get; }
    public IReadOnlyList<string> Warnings { get; } // content lint found at construction
}
// As built: Forecast and LastReport are both SolReport (Forecast is null once Ended);
// History is IReadOnlyList<LogEntry>; the constructor is
// new GameSession(MissionConfig config, IEnumerable<EventDef> events, int seed)
// and throws ContentValidationException on invalid content.
```

| Command | Accepted when | Rejection codes |
|---|---|---|
| `SetFacilityLevel` | Phase Planning, facility exists, level exists. | `mission_ended`, `decision_pending`, `unknown_facility`, `invalid_level` |
| `ChooseEventOption` | Phase AwaitingDecision, instance matches, choice exists, its `requires` are met, its costs are affordable in full. | `mission_ended`, `no_pending_event`, `stale_event`, `unknown_choice`, `requirement_not_met`, `cannot_afford` |
| `AdvanceSol` | Phase Planning, `expectedSol` equals current sol. | `mission_ended`, `decision_pending`, `stale_sol` |
| `Restart` | Always. | none |

`SessionChange.kind` is one of `CommandApplied`, `SolResolved`, `EventRaised`, `MissionEnded`, `Restarted`. The safe UI pattern is: on any `Changed`, re-render everything from the read properties; optionally animate from `LastReport` when the kind is `SolResolved`.

`SolReport` gives, per resource: start, made, used by facilities, used by crew, wasted, change from effects, end, shortage. Per facility: requested level, level run, reason code if they differ. Plus health before and after with a list of causes, delayed effects that landed with their source, the event raised, and the outcome. This is what the "what changed and why" summary and the debrief are built from.

Getting the session in Unity: `SimulationHost.Session`, available after the host's `Awake`. The host uses `[DefaultExecutionOrder(-100)]` so UI scripts may subscribe in their own `OnEnable` or `Start`.

Rules for UI code: no arithmetic on resources; no caching of state across `Changed`; never construct a second `GameSession`; disable the Advance button while animating if desired, but correctness does not depend on it.

## 7. Contract for the content developer (proposed)

One JSON file per event, named `<id>.json`. One file per event keeps several authors and agents out of each other's way.

```json
{
  "schemaVersion": 1,
  "id": "dust_storm_first",
  "category": "environment",
  "severity": "warning",
  "trigger": { "sol": 3 },
  "title": "A dust storm is coming",
  "body": "Dust in the air will block some sunlight. Your solar panels will make less power.",
  "scienceNote": "Short, true, sourced explanation written by Developer 3.",
  "dataBasis": "illustrative",
  "sourceRef": "",
  "choices": [
    {
      "id": "clean_panels",
      "label": "Send the rover to brush the panels",
      "preview": "Uses 3 power now. Panels lose less sunlight.",
      "requires": [ { "resource": "power", "min": 3 } ],
      "effects": [
        { "type": "resource", "target": "power", "amount": -3 },
        { "type": "modifier", "target": "solar_array", "percent": -20, "durationSols": 2 }
      ]
    },
    {
      "id": "wait",
      "label": "Save power and wait",
      "preview": "Costs nothing now. Panels make much less power for 2 sols.",
      "effects": [
        { "type": "modifier", "target": "solar_array", "percent": -50, "durationSols": 2 }
      ]
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `trigger.sol` | Scripted: raise at the start of exactly this sol. Use for the tutorial and the demo path. |
| `trigger.minSol`, `maxSol`, `weight`, `once` | Random pool: eligible window, relative weight (0 = 1), whether it may occur only once. Leave `sol` at 0. Random events only occur on sols with no major event. |
| `variants` | Major events only, optional: authored variants, one picked when the event is raised. Same choice IDs and effect types/targets in every variant; amounts, requirements and text may differ. Replaces `choices`. See [0005](../../decisions/0005-hybrid-event-structure.md). |
| `severity` | `info`, `warning`, `critical`. Presentation hint only. Every event must be answered before the next sol; an `info` event has one choice. |
| `dataBasis` | `illustrative` or `archived_nasa`. With `archived_nasa`, `sourceRef` must name the entry in the [NASA_DATA.md](../../../NASA_DATA.md) register and the text must give the real date. Nothing is ever labelled live. |
| `requires` | Resource minimums for a choice to be selectable. A check only: nothing is spent. The UI shows unavailable choices as disabled with the reason. |
| `preview` | The one-sentence cost statement shown before confirming. Must match the effects; the validator cannot check prose. |

Effect types, closed set for schema version 1:

| `type` | Fields | Does |
|---|---|---|
| `resource` | `target` (resource id), `amount` (signed) | A gain is clamped to capacity. A loss is a cost: the choice is unavailable (`cannot_afford`) unless the full amount can be paid. Never clamped to 0. |
| `health` | `amount` (signed) | Changes Crew Health, clamped 0 to 100. |
| `modifier` | `target` (facility id), `percent` (signed), `durationSols` (>= 1) | Scales that facility's output for the duration. **Stage 5; rejected by the validator in the current build.** |
| any | optional `delaySols` (>= 1) | Queues the effect instead of applying it now. **Stage 5; rejected in the current build.** |

Because the parser has no nulls, 0 means "not set" for `sol`, `delaySols`, `minSol`, `maxSol`.

Limits Developer 3 should know: at most 3 choices; no branching on earlier choices; no new effect types without a request to Developer 1. The validator fails a file with unknown IDs or types, and warns when a choice in a multi-choice event has no negative effect and no requirement, since the brief requires every choice to cost something.

Radiation events need no special system: one choice costs power or halves output for a sol (sheltering), the other costs health (exposure).

## 8. Unity layout (provisional)

```
game/<UnityProject>/Assets/RedFrontier/
  Core/        RedFrontier.Core.asmdef (noEngineReferences: true)      Developer 1
  Runtime/     RedFrontier.Runtime.asmdef -> Core                      Developer 1
               SimulationHost.cs, ContentLoader.cs, DebugHud.cs, SimulationHost.prefab
  UI/          RedFrontier.UI.asmdef -> Core, Runtime                  Developer 2
  Content/Resources/mission.json                                       Developers 1+3
  Content/Resources/Events/<id>.json                                   Developer 3
  Tests/       EditMode tests -> Core                                  Developer 1
```

- Content is loaded with `Resources.Load` and `Resources.LoadAll`, which are synchronous and work in Web builds. Adding an event is adding a file; no prefab or scene edit.
- Assembly definitions reference each other by name, not GUID, so they can be written without the Editor.
- The same NUnit test sources should run in the Unity Test Runner and under a plain `dotnet test` project that includes the Core sources by path. The root `.gitignore` currently ignores every `*.csproj`; that test project needs a negation rule, which is a shared-file change to request from the integrator.
- If the combined project has not arrived when coding starts, Core and its tests are written in `game/sim/` as plain C# and moved into `Assets/RedFrontier/` with `git mv` later. That folder is not a Unity project and does not compete with the colleague's.

Likely conflict areas, and the proposed rule for each:

| Area | Risk | Rule |
|---|---|---|
| Main scene (`.unity`) | Two people edit, YAML conflict. | Developer 2 owns the scene. Developer 1 adds one `SimulationHost` prefab instance once, by arrangement. |
| `ProjectSettings/`, `Packages/manifest.json` | Silent overwrite. | One owner (integrator). Changes announced before commit. |
| `.meta` files | Two people create the same folder and get different GUIDs. | Create the `Assets/RedFrontier/` skeleton once, commit it, then branch. Files added outside the Editor get their `.meta` from the first person to open the project, who commits them at once. |
| `.gitignore` | Unanchored `[Bb]uild/`, `[Ll]ogs/`, `[Tt]emp/`, `dist/` match any folder of that name under `Assets/`. | Do not name source folders `Build`, `Logs`, `Temp`, `Dist`. |
| `DEVLOG.md`, `BOARD.md` | Parallel appended rows conflict textually. | Trivial to resolve: keep both rows. |
| Decision numbering | Another branch may also create `0002`. | Renumber at merge; cross-links use relative paths and are easy to fix. |
| Content balance numbers | Developers 1 and 3 both tune `mission.json`. | Developer 3 proposes, Developer 1 commits, until balance settles. |

## 9. To check when the combined Unity project arrives

1. `ProjectSettings/ProjectVersion.txt`: exact Unity version. Core must compile at that C# level; avoid anything newer than C# 9.
2. `Packages/manifest.json`: render pipeline, Input System, UI Toolkit or uGUI, Test Framework, any JSON package.
3. Existing folders, namespaces, assembly definitions and any manager or event framework. Adopt adequate conventions instead of the layout above.
4. Scenes present and who owns them. Where the host prefab should go.
5. Build target and Player settings: Web platform installed, compression format, decompression fallback, code stripping level.
6. Whether the project sits directly in `game/` or in a subfolder, and whether `.gitignore` and `.gitattributes` cover it.
7. Whether anything in it already implements resources, turns or events. If so, reconcile before writing more.

## 10. Web build notes (unverified, from prior knowledge)

To confirm on the first real build, not to be reported as facts:
- Static hosts differ in whether they send the headers that compressed Unity Web builds need. The usual workarounds are the "Decompression Fallback" player setting or building uncompressed. Decide the host (for example itch.io or GitHub Pages) before the first build check.
- `JsonUtility` with `[Serializable]` public-field classes is expected to survive code stripping. The first browser build must load one event file to prove it.
- Player storage in Web builds is browser storage and can be unavailable in private windows or embedded frames. One more reason save is deferred.
- IL2CPP Web builds are slow to produce. Budget for it and build early.
