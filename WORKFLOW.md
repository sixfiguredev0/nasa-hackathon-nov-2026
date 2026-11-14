# Team workflow

## Communication
- Primary channel: decide at kickoff (in-person, Sofia Discord, or other).
- Keep updates short: Done / Next / Blocked, with owner and checkpoint.
- Record decisions and scope changes in DEVLOG.md. Coordinate before editing shared files or broad refactors.

## Git
At kickoff confirm repository URL, access, default branch and protection. Use short-lived `feat/<topic>`, `fix/<topic>` or `docs/<topic>` branches. Start from current default branch, commit focused changes, push early, review meaningful diffs, and merge only when the default branch remains demoable. Avoid force-pushing shared branches. Resolve conflicts with file owners; never discard others' uncommitted work. Simplify only by team agreement.

### Unity routing
Start future work on a new branch from current GitHub `main` (after promotion; until then `integration/map-v1-dev1` is only a candidate). Ownership by folder: docs/integration/MAP-V1-DEV1.md. GitHub `main` and Plastic `/main` are not synchronised.

### Branch roles
- One integrator owns `main` and is the only person who merges into it, through pull requests. `main` must always be demoable.
- Everyone, the integrator included, works on short-lived task branches (`feat/`, `fix/`, `docs/`), merged within a few hours and then deleted. No long-lived personal branches.
- One agent per branch. Two agents never share a branch; use a separate clone or `git worktree` per agent.
- Split work by folder to avoid conflicts (for example `game/`; `data/` and `tools/`; `docs/design/`, `assets/` and `submission/`), matching TEAM.md roles.
- Only the integrator edits README.md and WORKFLOW.md. DEVLOG.md and docs/planning/BOARD.md take appended rows only.
- Merge the latest `main` into your branch before opening a pull request.

## Build cadence
Confirm challenge, user, core outcome and NASA data path before implementation. Build a thin end-to-end demo early; integrate often; retain a known-good demo and fallback recording/screenshots. Freeze risky changes before milestones. Reserve time for citations, accessibility, cleanup and rehearsal.
