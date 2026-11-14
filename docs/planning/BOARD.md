# Task board

Claim a task by writing your name or agent tag in **Owner** and committing before you start. Move it to Done with a link to the PR. Branch per task as in [WORKFLOW.md](../../WORKFLOW.md) (`feat/`, `fix/`, `docs/`).

## To do

| Task | Owner | Notes |
|---|---|---|
| Test engine web export on phone and Chromebook (decision 0001) | | Blocks all game code |
| Check DONKI API access and pick 3 to 5 real storm events | | Needs a free api.nasa.gov key |
| Paper-test one sol with three resources | | Try with a child if possible |
| Decide prototype site: Moon, Mars, or both | | |
| Pick art style and camera | | Keep it cheap |
| Draft attribution screen text | | |
| Create `main`, set as default, protect it | | Integrator, on GitHub |
| Assign roles in TEAM.md | | |
| Review challenge Resources tab and record datasets in NASA_DATA.md register | | Global Awards need one from Resources |
| Fill team lead, submission owner, demo fallback in DEVLOG.md | | |
| Team: accept or amend decisions 0002 (scope) and 0004 (contracts) | | Devs 2 and 3 to answer; silence is not agreement |
| Dev 3: review hybrid event schema (0005) and example events in game/sim/Content/example | | Proposed schema; numbers are placeholders |
| Devs 2 and 3: review decision 0006 (fallback choice, content-error end, Unity path) | | Contract change for content and UI |
| Decide Git vs Unity Version Control as source of truth for `Assets/RedFrontier/` | Integrator | Both currently see the files |
| Review ProjectSettings/EditorSettings.asset (differs from Plastic base after builds) and decide whether ProjectSettings/, Packages/ (incl. local MCP package) belong in Git | Integrator | Not committed by Dev 1 checkpoint 1 |

## In progress

| Task | Owner | Branch |
|---|---|---|
| Simulation architecture and staged plan (docs/planning/dev1-simulation/) | Dev 1 (simulation) | `claude/clever-einstein-rwr1ng` |
| Simulation core: Stage 1 done, hybrid events done; next Stage 2 Unity bridge (needs combined project) | Dev 1 (simulation) | `feat/dev1-core` (local, unpushed) |
| Stage 2 Unity slice: Core in Assets, loader, host, debug HUD, scene `Dev1_Debug`; review fixes A and B | Dev 1 (simulation) | `feat/dev1-core` (local diff, workspace-root checkout) |
| Checkpoint 1: lifecycle fix, PlayMode tests, .NET and WebGL browser verification (awaiting Codex review) | Dev 1 (simulation) | `feat/dev1-core` (local commit, not pushed) |
| Integration candidate: map v1 + Dev 1 in `Integration_MapV1`, Functions panel, full project config; promotion to `main` pending checks | Integration role (executed by Opus) | `integration/map-v1-dev1` |
| Dev 1 Stage 5: four resources, radiation, modifiers, delayed effects (decision 0007); not yet the shared baseline | Dev 1 (simulation) | `feat/dev1-complete` |

## Done

| Task | Owner | PR |
|---|---|---|
| Initial repo structure | | |
