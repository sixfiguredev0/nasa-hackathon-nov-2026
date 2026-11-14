# 0006: Event fallback choice, facility cost aggregation and Unity source layout

- Status: Proposed (Developer 1 provisional integration policy; Developers 2 and 3 to review)
- Date: 2026-11-14
- Deciders: Developer 1; review by Developers 2 and 3 and the integrator

## Context

The 2026-11-14 review (`_review/REVIEW-2026-11-14.md`, local only) confirmed two simulation defects:

1. A facility level with two costs on the same resource (Power 3 + Power 3, stock 5) passed validation and ran, leaving Power at -1. Affordability was checked per entry; choice costs were already summed per resource.
2. An event whose choices were all unavailable (for example all cost Power at 0 Power) blocked `AdvanceSol` (`decision_pending`) and every facility change. Restart was the only exit.

The combined Unity project also arrived (Unity 2022.3.18f1, Unity Version Control), so Core had to move into it with one authoritative source.

## Decision

**Facility costs.** A level's `uses` are summed per resource before deciding whether it runs, the same rule as choice costs. Duplicate entries stay valid. The final balance is never clamped: an unaffordable level falls back to a lower level, as before.

**Fallback choice (content rule, validator error).** Every event's `choices`, and every variant's `choices`, must contain at least one *fallback*: a choice with no `requires` and no negative `resource` effect. A fallback may carry a negative consequence, such as a health loss. Under the availability rules (`requires` are checks, negative resource effects are costs paid in full), such a choice is available in every state, so a validated event can always be answered. The rule is implemented once, in `EffectApplier.IsFallback`, and used by `ContentValidator`.

**Runtime guard.** When an event is raised (at mission start, after `Restart`, and in step 7 of `AdvanceSol`), `GameSession` checks that at least one choice is available. If none is, the mission ends at once with outcome `Failure`, end reason `content_error_no_available_choice`, and a `mission_ended` history entry carrying the event and variant IDs. `SimulationHost` logs it as a Console error; the debug HUD says it is a content error, not a player failure. The guard never skips the event and never waives a cost. It uses no RNG, so runs stay reproducible. Checking only on raise is enough because stock cannot change while a decision is pending.

**Unity layout.** The Git checkout's work tree is the workspace root, so the imported Unity project is tracked in place at `Red Frontier/`. Core, its shared NUnit tests and the placeholder content were moved with `git mv` into `Red Frontier/Assets/RedFrontier/`. The plain .NET projects in `game/sim/` include those files by path; .NET-only tests (System.Text.Json) stay in `game/sim/Tests`.

## Options considered

- Facility costs: reject duplicate resource entries in validation. Rejected: summing is already the rule for choices and keeps content forgiving.
- No-choice events: convert unaffordable costs into a free choice, or skip or auto-resolve the event. Rejected: both hide content bugs and break the "every choice costs something" rule. A visible content-error end is honest and debuggable.
- Fallback as a validator warning only. Rejected: it would let a trap reach players.
- Unity layout: move the project into `game/` (decision 0001's interim rule), or keep Core in `game/sim` and link it into Assets with a junction or local package. Rejected for now: moving the open, Plastic-tracked project was out of scope, and junctions or `manifest.json` edits are fragile and change shared files.

## Consequences

- **Contract change for Developer 3:** each event and variant needs a fallback choice; content without one fails to load.
- **Contract change for Developer 2:** new end reason `content_error_no_available_choice` (outcome `Failure`). The mission can end at sol 1 (or on `Restart`) if content is malformed. The `mission_ended` log entry has `eventId` and `variantId` for this reason.
- `GameSession` has an `internal` constructor that skips validation, so tests can reach the guard. It is not public API.
- The Unity project sits at `Red Frontier/` (with a space), not under `game/`. This deviates from 0001's interim rule. The integrator should confirm or move it before merging; moving later is a `git mv` plus reopening the project from its new path.
- The repository and Unity Version Control both track the Unity project for now. The team must choose one source of truth for `Assets/RedFrontier/`.
- Checkpoint 1 commits only Developer 1's folder `Red Frontier/Assets/RedFrontier/**` (with `.meta` files), plus `game/sim` and the docs. `ProjectSettings/`, `Packages/` (which contains the local Unity MCP package entry) and `Assets/Scenes/SampleScene.unity` stay untracked in Git. They belong to the Unity Version Control project and its owner, and committing them would push a local tool dependency and shared settings onto teammates. Until the integrator decides, the Git branch alone is not an openable Unity project; it needs the Plastic workspace around it.
- `SimulationHost` initialises in `OnEnable`, not `Awake`. After a script reload in Play Mode, Unity calls `OnEnable` again but not `Awake`, and a `GameSession` cannot survive the reload. The host now rebuilds the session from the current seed and logs a warning that progress was lost, instead of leaving `Session` null.
