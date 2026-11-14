# 0003: Simulation architecture

- Status: Proposed (Developer 1 owns the simulation; the parts that touch the shared Unity project need team agreement)
- Date: 2026-11-14
- Deciders: Developer 1; team for shared-project items

## Context

- Decision [0001](0001-engine.md) (engine) is still recorded as open. Developer 1 reports the team direction is Unity 6 with a Web build. This record assumes that and is void if 0001 lands elsewhere.
- No Unity project is in the repository. A colleague is preparing a combined project. Its Unity version, packages, UI technology and folder conventions are unknown.
- Three developers and several AI agents work in parallel. Unity scenes and project settings merge badly.
- Coding agents may run where no Unity Editor is available, so the rules must be verifiable without it.
- One sol must resolve exactly once, effects must apply exactly once, and UI code must not hold game rules.

## Options

1. **MonoBehaviour managers per system** (GameManager, ResourceManager, EnergySystem, FoodSystem and so on, as sketched in the draft plan section 9). Familiar. Spreads state across scene objects, depends on script execution order, cannot be tested without the Editor, and invites UI code to poke values.
2. **Plain C# simulation core with one Unity bridge (recommended).** All rules and state in an engine-free assembly. One MonoBehaviour hosts it. UI reads snapshots and sends commands.
3. **ScriptableObject-driven architecture** (SO events, SO variables). Editor-friendly, but content must be authored in the Editor, `.asset` files are awkward for AI agents and for review, and it is more framework than a one-mission game needs.

## Decision (proposed)

Option 2. Full detail in [ARCHITECTURE.md](../planning/dev1-simulation/ARCHITECTURE.md). Summary of the component decisions:

| ID | Decision | Rationale | Alternative and trade-off |
|---|---|---|---|
| A1 | Rules and state live in `RedFrontier.Core`, an assembly with no `UnityEngine` reference. | Testable with a plain .NET runner and in the Unity Test Runner; cannot depend on scenes. | Managers in scenes: quicker to wire by hand, untestable outside the Editor. |
| A2 | One object, `GameSession`, owns the only mutable state. It publishes a deep-copied snapshot after each accepted command. | The UI cannot corrupt state even by accident; the same copy routine serves forecast and later save. | Shared mutable state with read-only interfaces: less copying, weaker guarantee. State is a few dozen integers, so copying is free. |
| A3 | `AdvanceSol` is one synchronous, atomic call with a fixed order of operations, guarded by `expectedSol`. | No coroutines or frame timing in the rules; a double click or duplicate call is rejected rather than advancing twice. | Time-sliced resolution with animation callbacks: nicer pacing, but the UI can animate from the returned report instead. |
| A4 | Resources and facilities are data rows processed by one generic flow rule, not one class per system. | Less code than seven system classes; the resource set can change without code changes. | Bespoke systems: richer per-system behaviour that the first release does not need. |
| A5 | All quantities are integers. Percentages are integer percent. | Identical results in tests, Editor and WebAssembly; whole numbers suit the audience. | Floats: finer balance, risk of drift and ugly numbers. |
| A6 | Randomness comes from a small seeded generator whose state is part of the simulation state. | Same seed and commands give the same mission in every runtime; replays and bug reports are reproducible. | `System.Random`: zero code, but its sequence is not guaranteed identical across runtimes. |
| A7 | Mission config and events are JSON text assets, parsed in the Unity bridge with `JsonUtility`, validated in Core. | Text merges cleanly, AI agents and Developer 3 can author without the Editor, no extra package, safe under code stripping. | ScriptableObjects: inspector editing, Editor-only authoring. Newtonsoft: richer JSON, adds a package and stripping risk. |
| A8 | Effects are a closed set of types applied only by Core. Delayed effects sit in a queue inside the state and are removed when applied. | Exactly-once application; content cannot introduce rules. | Scripted effects or callbacks: flexible, unreviewable in the time available. |
| A9 | No save/load in the first release. State is kept as a plain serialisable object. | A mission lasts about ten minutes; restart covers the need. Web storage adds failure modes to test. | Save now: matches the draft plan, costs a stage and browser testing. |
| A10 | A throwaway IMGUI debug HUD, owned by Developer 1, makes the slice playable before the real UI exists. | Unblocks integration and the early browser build; touches no shared scene content beyond one host object. | Wait for Developer 2's HUD: couples the two workstreams from the first hour. |

## Consequences

- Easier: parallel work, testing, determinism, late changes to the resource list, a later save feature.
- Harder: `JsonUtility` has no dictionaries, nullables or polymorphism, so the schemas are flat arrays with string discriminators and "0 means not set". The UI must re-render from snapshots rather than bind to live objects.
- Shared-project items needing agreement: assembly definitions, folder layout under `Assets/`, one host prefab in the main scene, location of content JSON. See [0004](0004-simulation-contracts.md).
- Affects: all simulation code (Developer 1); how UI code reads and invokes (Developer 2); content format (Developer 3).
- Revisit when: the combined Unity project arrives with conventions that conflict (for example an existing manager framework or a different data approach); the engine decision changes; or `JsonUtility` cannot parse the agreed schema in a Web build.
