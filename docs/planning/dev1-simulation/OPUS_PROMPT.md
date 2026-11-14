# Prompt for the coding agent (Stages 0 and 1)

Developer 1 fills the bracketed lines before sending. Anything left as "UNRESOLVED" must be treated as unresolved, not assumed.

---

You are the coding agent for Developer 1 (simulation and integration) on RED FRONTIER, team Coding Foxes, NASA Space Apps Challenge 2026. The game is a turn-based Mars outpost manager for children aged 7 to 12, intended as a Unity 6 Web build. You are implementing the simulation core. You are not building UI, art, or event content.

## Read first, in this order

1. `AGENTS.md` and `WORKFLOW.md`. They are binding.
2. `docs/planning/dev1-simulation/HANDOFF.md`: current state, what is accepted, what is only proposed.
3. `docs/planning/dev1-simulation/ARCHITECTURE.md`: the design you implement.
4. `docs/planning/dev1-simulation/IMPLEMENTATION_PLAN.md`: stages and acceptance criteria.
5. `docs/decisions/0001` to `0004`, `docs/challenge/brief.md`.

## Decisions from Developer 1

- Engine: [ACCEPTED Unity 6 Web by the team on <date/time> | UNRESOLVED: stop after Stage 0 and report]
- Scope 0002 (Power, Air, Food, Crew Health, radiation via events, 10 sols): [ACCEPTED | PROPOSED: build Stage 1 anyway, it uses only power and air | CHANGED: <details>]
- Where you run: [machine with Unity Editor <version> | no Editor; .NET SDK <installed | may be installed | not allowed>]
- Combined Unity project: [present at <branch/path> | NOT PRESENT: write Core in `game/sim/` as plain C#]
- Base branch and working branch: [<base> / <branch>]
- Pushing: [allowed to <branch> | not allowed]

## Task

Do Stage 0, then Stage 1, of the implementation plan. Stop after Stage 1 and report. Do not start Stage 2.

Stage 0: confirm the facts above against the repository. If a combined Unity project is present, inspect it using ARCHITECTURE section 9 before writing anything and adopt its conventions where adequate; record what you found in HANDOFF.md. Set up a test runner and prove it with one passing test. Record the exact command.

Stage 1: implement the Core vertical slice so that all ten acceptance tests in the plan pass.

## Constraints

- Core has no `UnityEngine` reference. Target what Unity 6 compiles: C# 9 features at most, .NET Standard 2.1 APIs, no external packages in Core.
- State and definition classes are plain `[Serializable]` classes with public fields, so `JsonUtility` can read them later. No dictionaries, nullables or polymorphism in anything that will be loaded from JSON.
- Integers only in the rules. No `System.Random`, no clock, no static mutable state.
- `GameSession` is the only writer of state. Follow the sol order in ARCHITECTURE section 4 exactly. If you believe the order is wrong, stop and say why; do not quietly change it.
- Implement what Stage 1 lists and nothing more. No save system, no extra effect types, no generic event bus, no dependency injection, no interfaces with a single implementation unless a test needs one.
- All numeric values are placeholder game values. Do not present any of them as scientific data, and do not add science claims to code comments or content.
- Do not create a Unity project, edit project settings, add packages, or touch scenes. Do not edit README.md or WORKFLOW.md. Do not change `.gitignore`; if you need a tracked `.csproj`, say so in your report.
- Do not open a pull request. Do not merge. Do not push unless the line above allows it.
- Keep commits small and focused, on the working branch only.

## Verification and honesty

- Run the tests and paste the summary line. If you cannot run them, say so plainly and do not describe the code as working.
- You cannot verify anything in the Unity Editor or a browser unless the line above says you have the Editor. Do not claim it.
- If a criterion cannot be met as written, report the gap rather than weakening the test.

## Records

- Update `docs/planning/dev1-simulation/HANDOFF.md`: branch, commit, what changed, what was verified and how, the test command, the exact next task.
- Append one row to `DEVLOG.md` for a meaningful decision or milestone. Append-only.
- If you had to depart from ARCHITECTURE.md, add a short dated note to the relevant decision record's Consequences section with the reason, and flag it in your report. Cross-team contract changes (0004) are proposals until Developers 2 and 3 agree.

## Report back

1. What you built, with file paths.
2. Test command and result.
3. Any departure from the architecture, and why.
4. Anything you found in the Unity project that conflicts with the plan.
5. Questions for Developer 1, grouped, only where the answer changes what you do next.
