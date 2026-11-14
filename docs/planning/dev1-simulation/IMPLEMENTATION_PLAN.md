# Developer 1 implementation plan

Status: **proposed**, for the coding agent. Read [ARCHITECTURE.md](ARCHITECTURE.md) first. Current state and open questions are in [HANDOFF.md](HANDOFF.md).

Clock (Sofia local, from [SUBMISSION.md](../../../SUBMISSION.md), to reconfirm onsite): local demo video due Sun 15 Nov 14:00; global submission closes 23:59. Target times below are suggestions to make cuts visible, not commitments.

Verification legend: **T** = automated tests on Core, runnable without Unity. **E** = Unity Editor play mode, needs a person or agent with the Editor. **W** = Web build opened in a browser. Never report E or W as done from an environment that cannot run them.

## Gates before any code

| Gate | Why | Who clears it |
|---|---|---|
| G1. Team confirms Unity 6 Web as the engine, and 0001 is updated. | [0001](../../decisions/0001-engine.md) is recorded open; BOARD says the web export test "blocks all game code"; [AGENTS.md](../../../AGENTS.md) forbids assuming Unity. C# written before this is a bet. | Developer 1 with the team; integrator edits 0001. |
| G2. Scope [0002](../../decisions/0002-first-release-scope.md) accepted, or an alternative third meter named. | Decides the content of `mission.json`, not the code. Stages 1 and 2 can proceed on the proposal. | Team. |
| G3. Where the agent runs and how it verifies. | Without a .NET SDK or the Editor the agent cannot compile anything. | Developer 1. |
| G4. Base branch for code. | No `main` exists. The remote default is a session branch. | Integrator. |

## Stage 0: Pre-flight (30 min)

- **Result:** known facts replace assumptions. A test command exists and runs an empty test.
- **Do:** check whether the combined Unity project has landed (`git fetch`, list branches, look under `game/`). If yes, work through ARCHITECTURE section 9 and record findings in HANDOFF. If no, use `game/sim/`. Set up the test runner agreed in G3.
- **Files:** HANDOFF.md; test project scaffold; possibly a `.gitignore` negation requested from the integrator.
- **Acceptance:** HANDOFF states the Unity version or "project not present"; one trivial test passes with the exact command recorded.
- **Defer:** nothing. Do not skip.

## Stage 1: Core vertical slice (target Sat 17:30) [T]

The smallest end-to-end game in plain C#: initial state, one player action, one sol, one event, visible consequences, a mission outcome.

- **Result:** `GameSession` built from an in-code or JSON config with two resources (power, air), two facilities (solar array, life support), crew needs for air, Crew Health, a mission length, and one scripted two-choice event with immediate effects.
- **Do:** `SimState`, definitions, `Rng`, `GameSession` with the four commands and `Changed`, `SolResolver` steps 0 to 8, `EffectApplier` for `resource` and `health`, scripted trigger only, `SolReport`, snapshot copy.
- **Files:** `Core/**`, `Tests/**`.
- **Depends on:** G1, G3. Not on the Unity project.
- **Acceptance (each is a test):**
  1. A scripted sensible playthrough ends in Success on the expected sol.
  2. A playthrough with life support off ends in Failure, with air shortage listed as the cause in the reports.
  3. `SetFacilityLevel` changes the next sol's flows and nothing before `AdvanceSol`.
  4. `AdvanceSol` with a stale `expectedSol` is rejected and state is unchanged.
  5. `AdvanceSol` is rejected while an event is pending.
  6. The event's immediate effects apply once; a repeated `ChooseEventOption` is rejected.
  7. Writing to `Snapshot` does not alter later snapshots.
  8. `Changed` fires exactly once per accepted command and not on rejection.
  9. Same seed and commands give identical final state twice.
  10. Every command except `Restart` is rejected after the mission ends; `Restart` returns to sol 1.
- **Handoff:** tell Developers 2 and 3 the slice exists; point them to ARCHITECTURE sections 6 and 7.
- **Defer if short:** nothing here is optional.

## Stage 2: Unity bridge and debug HUD (target Sat 19:30) [E]

- **Result:** the slice is playable in the Editor with no art and no real UI.
- **Do:** move Core into the project if it was built in `game/sim/`. Add assembly definitions, `ContentLoader` (JsonUtility, `Resources`), `SimulationHost`, an IMGUI `DebugHud` showing meters, facility level buttons, the pending event with its choices, Advance Sol, Restart, last report and seed. Put the slice's mission and event into JSON. Run `ContentValidator` at load and log failures.
- **Files:** `Runtime/**`, `Content/Resources/**`, one prefab. One prefab instance in a scene, placed by arrangement with the scene owner.
- **Depends on:** the combined Unity project; scene owner's consent.
- **Acceptance:** in play mode a person completes one mission to Success and one to Failure using only the debug HUD; the console shows no errors; EditMode tests pass in the Test Runner; the JSON content passes the validator.
- **Handoff:** Developer 2 can now reference `SimulationHost.Session`. Developer 3 can drop an event file in and see it.
- **Defer if short:** report detail in the HUD.

## Stage 3: Browser build check (target Sat 20:30) [W]

Two checks. 3a does not depend on any simulation work and should be done by whoever has the Editor as soon as the combined project exists.

- **3a Result:** an empty scene from the combined project builds for Web and loads in a desktop browser from a local static server.
- **3b Result:** the Stage 2 slice does the same, loads its JSON, and can be played to an outcome.
- **Do:** agree the host and compression setting first (ARCHITECTURE section 10). Record build time, download size, time to first frame, and the browsers and devices tried.
- **Files:** none of ours, unless Player settings change; those belong to the integrator.
- **Acceptance:** 3b played to Success in a desktop browser; one touch device tried and the result written down, pass or fail; numbers recorded in DEVLOG and against decision 0001.
- **Defer if short:** the touch device check, not the desktop one.

## Stage 4: Contract freeze v1 (15 min, after Stage 2)

- **Result:** [0004](../../decisions/0004-simulation-contracts.md) marked accepted or amended; one example event file and the reason-code list published.
- **Acceptance:** Developers 2 and 3 have each said yes or named a change. Silence is not agreement.
- **Defer:** cannot be deferred past the start of UI wiring.

## Stage 5: Agreed systems (target Sat 22:30) [T]

- **Result:** the 0002 scope in full: third resource and greenhouse, level fallback when power is short, capacity and waste, health recovery, `modifier` effects with duration, `delaySols`, `Forecast`, complete `SolReport` causes, `History`.
- **Files:** `Core/**`, `Tests/**`, `mission.json`.
- **Acceptance (tests):** a facility short of power runs at the highest affordable lower level and reports it; a 2-sol modifier affects exactly two sols; a delayed effect lands on the right sol, once, with its source in the report; forecast equals the actual result when no event intervenes; stock never leaves 0 to capacity.
- **Handoff:** Developer 2 gets forecast and report fields; Developer 3 gets `modifier` and `delaySols`.
- **Defer if short, in order:** forecast; history beyond choices; the third resource.

## Stage 6: Events at scale (target Sun 09:30) [T, E]

- **Result:** Developer 3's events load from one file each. Scripted events drive the demo path; a seeded pool varies other sols; `requires` and `once` are honoured; the validator lint for cost-free choices runs over all content in tests.
- **Files:** `Core/EventDirector`, `Core/ContentValidator`, tests; content files are Developer 3's.
- **Acceptance:** all content passes validation in a test; two seeds give different pool events and the same scripted ones; an unavailable choice is rejected with `requirement_not_met`.
- **Defer if short:** the random pool. Scripted events alone still satisfy the demo.

## Stage 7: Balance and outcome (target Sun 10:30) [T]

- **Result:** the mission is winnable with care and losable through neglect.
- **Do:** two scripted bot strategies as tests over a range of seeds: "never touch anything" must not reliably win; "sensible" must usually win. Optional grade from final health. Tune `mission.json` with Developer 3.
- **Acceptance:** bot win rates recorded in DEVLOG as simulation results, not as playtest evidence.
- **Defer if short:** the grade.

## Stage 8: Integration and freeze (Sun 10:30 to 12:00) [E, W]

- **Result:** Developer 2's UI drives the real session; debug HUD hidden in the release build; final Web build made and played through.
- **Acceptance:** full mission in the browser to both outcomes; Restart works without reload; no console errors; a known-good build kept aside as the fallback.
- **Rule:** no new features after the freeze. Fixes only.

## Not planned for the first release

Save and resume, facility condition and repair, upgrades, morale, water as its own resource, communication delay, conditional effects, forecast warnings of future events, localisation. Each can be added on the same architecture. Any of them needs a new decision record before work starts.
