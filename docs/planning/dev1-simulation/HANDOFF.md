# Developer 1 handoff (current state)

Current state only. Checkpoint history is in [DEVLOG.md](../../../DEVLOG.md); decisions are in [docs/decisions](../../decisions/).

## Where it is

- GitHub branch **`feat/dev1-complete`** (delivery reference; GitHub `main` does not exist yet and was not created).
- Unity **2022.3.18f1** exactly. Project folder: **`Red Frontier/`** in the repository.
- Scene to open: **`Assets/RedFrontier/Scenes/Integration_MapV1.unity`** (first in Build Settings), or menu **Red Frontier > Play Integration Scene**.
- Unity Version Control (Plastic) is a separate system: see "Unity Version Control" below.

## Play

Press Play. The Functions panel (right, ~420 px; **Functions** button or **Tab**; Tab is ignored while typing the seed) shows Power (Energy), Air (Oxygen), Water, Food, Crew Health, radiation, active modifiers, queued delayed effects, **Advance**, facilities, the forecast, events with effect previews, the outcome, same-seed and new-seed restart, and history. It reopens for a new event or outcome. Map panning (WASD/arrows/right-drag) works only while the panel is closed. Restarting a mission never regenerates the map (seed 1337, random seed off).

Documented routes (both checked in the browser build, see Checks):
- **Win, seed 1:** sol 1 turn the Radiation shelter on, then always take the first choice (take cover, brush panels, fix, rest, unload now) and Advance. The resupply delivers 6 food on sol 9.
- **Lose, seed 3:** never touch facilities, always take the last (fallback) choice. The dust storm adds solar -50% on sols 3-4; the flare and unshielded radiation end the mission on sol 10.

## Developer 1 scope: done

| Area | State |
|---|---|
| Resources | Energy (`power`), Oxygen (`air`), Water, Food: start, capacity, production, facility costs, crew need and shortage penalty, all data in `mission.json`. Facilities that cannot pay drop a level; stock never leaves 0..capacity. |
| Facilities | solar array, life support (off/eco/full), water recycler, greenhouse (power + water -> food), radiation shelter (power, shielding). |
| Radiation | Baseline + event exposure per sol; shielding from facilities that ran; damage = unshielded units x damage per unit, applied once; shown in forecast, report and panel. |
| Health | Shortage and radiation damage; recovery only on a sol without either; 0 health ends the mission and cannot be revived (decision 0007 death latch). |
| Timed modifiers | Output only, additive stacking, expire after their last sol; restart clears. |
| Delayed effects | Land once when entering their due sol; included in the forecast and report; resource costs cannot be delayed. |
| Events | Hybrid milestones + seeded random pool (0005), fallback rule and runtime guard (0006), determinism, no RNG use by forecasts/UI/rejected commands. |
| Balance | Stage 7 benchmark in `game/sim/Tests/BalanceTests.cs` (below). |
| Unity bridge | `SimulationHost` (one session), `DebugHud` (debug HUD / Functions panel), `MapPanelBridge` (map panning), `IntegrationTools` (scene setup, play, WebGL build). |

Not in Developer 1 scope: production menu/GUI (Developer 2), authored and NASA-sourced content and final balance values (Developer 3), save/load (not planned for the first release), hosting.

## Semantics (decision 0007)

Order of one Advance: 1 facilities (modifiers scale output) -> 2 crew needs -> 3a radiation -> 3b health -> 5 delayed effects due on the next sol -> 4 sol+1, expire modifiers -> 6 failure, then success -> 7 raise the next event. Choices apply immediately when accepted; `radiation` effects add to the current sol. All numbers are **illustrative game values**, not NASA data, physical doses or medical thresholds.

## Developer 2: UI contract

Get the session from `SimulationHost.Session`; subscribe to `GameSession.Changed` (exactly once per accepted command) and re-read everything. Read: `Config`, `Snapshot` (incl. `modifiers`, `scheduled`, `radiationPending`, `radiationTotal`), `Forecast` and `LastReport` (`SolReport`: per-resource start/made/used/wasted/`fromEffects`/end/shortage, facilities with `modifierPercent`/`shielding`, `radiationExposure/Blocked/Net`, `delayedApplied`, health causes `shortage`/`radiation`/`recovery`/`delayed_effect`), `PendingEvent`, `History`. Commands: `SetFacilityLevel`, `ChooseEventOption`, `AdvanceSol(expectedSol)`, `Restart()`/`Restart(seed)`, with `Can...` checks and reason codes ([ARCHITECTURE section 6](ARCHITECTURE.md)). No resource arithmetic in UI. `DebugHud` stays as a debug tool; hide it in the release scene when the production UI exists.

## Developer 3: content contract

`Content/Resources/mission.json` (resources, crew needs, facilities with `uses`/`makes`/`shielding`, `radiationPerSol`, `radiationDamagePerUnit`, event chance and gap) and one JSON per event in `Content/Resources/Events/`. Effect types `resource`, `health`, `modifier` (`target` facility, `percent` >= -100, `durationSols` >= 1), `radiation` (`amount` >= 1); any non-cost effect may set `delaySols`. Every event/variant needs a fallback choice (no requirement, no immediate resource cost). `ContentValidator` rejects invalid content at load; tests validate the bundled files. Schema: [ARCHITECTURE section 7](ARCHITECTURE.md), decisions 0005-0007. Current content is placeholder, labelled illustrative.

## Ownership and workflow

Developer 1: `Assets/RedFrontier/Core`, `Runtime`, `Tests`, `game/sim`. Developer 3: `Assets/RedFrontier/Content`. Map owner: `Assets/Scripts/Mars`, `Assets/Art/Mars`, `Assets/Scenes/MarsOutpost.unity`, `ArtSource`, `Tools/MarsArt`, `Docs`. Developer 2: production GUI. One integration role: `Integration_MapV1`, Build Settings, `Packages`, `ProjectSettings`. One agent per branch/workspace; branch from the shared baseline; commit assets with `.meta`; never overwrite others' uncommitted work. Details: [docs/integration/MAP-V1-DEV1.md](../../integration/MAP-V1-DEV1.md).

## Test and build

- .NET (from `game/sim`): `dotnet test Tests/RedFrontier.Core.Tests.csproj` (shared Core tests + content + balance).
- Unity: Test Runner, EditMode and PlayMode (or `-batchmode -runTests -testPlatform EditMode|PlayMode`).
- WebGL: `-batchmode -quit -buildTarget WebGL -executeMethod RedFrontier.Integration.IntegrationTools.BuildWebGL -buildPath <out>`; serve `<out>` over http://127.0.0.1. The build sets WebGL compression to Disabled in ProjectSettings; do not commit that change.

## Checks (final, see DEVLOG for numbers and evidence paths)

Recorded in DEVLOG under "Developer 1 final verification". Known limitations:
- The map hash for seed 1337 differs between Editor (`134c314f`) and WebGL (`b6ed98ff`) with identical crater/rock counts; each platform is repeatable. Map owner code; likely floating-point differences.
- Balance is a simulation benchmark, not human playtesting.
- A teammate branch `feat/red-frontier-outpost` (orphan snapshot of the whole project at repository root) adds an Outpost system with its own `ColonyEconomy` (Energy/Water/Food/Oxygen/Metal) and `OutpostGUI`. It is not merged here: it duplicates simulation state ownership and needs a team decision with its owner.

## Unity Version Control

See DEVLOG "Unity Version Control transfer" for repository, branch, changeset and status.
