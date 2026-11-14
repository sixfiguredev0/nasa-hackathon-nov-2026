# Agent and teammate rules

Three people are prompting several agents in parallel. These rules keep agents from overwriting each other.

## Before you start

1. Read `README.md`, this file, and `docs/design/game-design.md`.
2. Open `docs/planning/BOARD.md`. Pick an unclaimed task, write your name or agent tag next to it, commit that change first.
3. Work only on files your task needs. If you must touch an area another task owns, note it on the board.

## Branches

- One branch per task: `<member>/<short-task>` (for example `yasen/sol-loop`).
- Never commit directly to `main`. Merge through a pull request.
- Keep pull requests small. One task, one PR.

## Decisions

- Do not make a project-wide choice (engine, framework, art style, data source) inside a task.
- Propose it as a new file in `docs/decisions/` using `TEMPLATE.md`, status `Proposed`. A human sets it to `Accepted`.
- Until the engine decision is accepted, do not add engine-specific files outside `game/` or `tools/`.

## Content rules from the challenge

- Audience is ages 7 to 12. Plain language. Explain any science term in context.
- No free choices: every player decision must cost something.
- Two or three core resources, not more.
- Real NASA data only, attributed in-game. Never present archived data as live.
- Browser-playable, no account, no download.

## Writing docs

- Keep docs short. Update the existing file instead of creating a near-duplicate.
- Files in `docs/source/` are original team notes. Do not edit them. Summarise into `docs/design/` instead.

## Commits

- Imperative subject line under 72 characters.
- Do not commit secrets, API keys, or large binaries (over about 10 MB) without agreement on the board.
