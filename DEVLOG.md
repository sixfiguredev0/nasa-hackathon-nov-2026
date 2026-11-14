# Development log

Record concise dated decisions, data changes, milestones and risks. Never log secrets or personal data.

## Kickoff snapshot
- Challenge and link: Build a Junior Astronaut Mission Trainer, https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/
- Project / user / problem / success criterion: Outpost-management game for kids 7 to 12. Problem: space content hides real mission trade-offs. Success criterion (proposed): after one session a player can explain one real trade-off, such as power vs. oxygen.
- Stack / hosting: TBD, see docs/decisions/0001-engine.md
- Team lead / submission owner:
- NASA data source from challenge Resources: TBD. Candidates DONKI, OSDR (Resources tab not yet reviewed)
- Demo fallback:

| Local date/time | Decision/change | Reason/evidence | Owner |
|---|---|---|---|
| Kickoff | Challenge and stack TBD | Decide as team at event start | |
| 2026-11-14 | Challenge chosen: Junior Astronaut Mission Trainer | Team discussion; brief in docs/challenge/brief.md | |
| 2026-11-14 | Stack still open; Unity vs. web stack vs. Godot | Risk: Unity web builds load slowly and are weak on mobile browsers; test before deciding | |
| 2026-11-14 | Initial repo structure added | Stack-agnostic layout so work can start in parallel | |
| 2026-11-14 | Branch model: one integrator merges to `main` via PRs; everyone uses short-lived task branches | Avoid long-lived branches colliding before the demo deadline; see WORKFLOW.md | |
| 2026-11-14 | Pending: create `main`, set as default, protect it | Repo default is currently the setup branch | |
| 2026-11-14 14:30 | Proposed, not yet agreed: first-release scope (0002), simulation architecture (0003), simulation/UI/content contracts (0004). No game code written. | Brief R10 and about 24 h to local demo argue for three meters plus Crew Health; no Unity project in the repo yet; details in docs/planning/dev1-simulation/ | Dev 1 (simulation) |
| 2026-11-14 | Simulation core Stage 0+1 built in `game/sim` (plain C#, no Unity), plus hybrid events (0005): fixed milestones with authored variants, seeded random events between. 37 tests pass with `dotnet test`. Unity, JsonUtility and browser unverified. Fixture values are placeholders. Not pushed. | Dev 1 authorised starting before Unity install finished; team confirmed hybrid event direction; schema proposed | Dev 1 (simulation) |
| 2026-11-14 | Dev 1 Stage 2: first playable Unity slice (2022.3.18f1). Core moved into `Red Frontier/Assets/RedFrontier/` (one authoritative source; `game/sim` keeps .NET projects by path). Fixed duplicate facility costs (summed per resource) and no-available-choice deadlock (fallback choice required; runtime content-error end). Debug scene `Dev1_Debug`. Unity EditMode 52/52 pass; Play Mode success/failure/restart checked. .NET suite and browser not run (no SDK; no WebGL module for 2022.3.18f1). Local diff, not pushed. | Review 2026-11-14 findings; policy proposed in decision 0006 for Devs 2 and 3; Unity project tracked in place at `Red Frontier/`, deviating from 0001 interim `game/` rule | Dev 1 (simulation) |
| 2026-11-14 | Dev 1 Opus checkpoint 1 (verify and preserve): fixed null GameSession after a Play Mode script reload (host init moved to OnEnable; HUD binds idempotently); PlayMode lifecycle tests added. Unity EditMode 52/52, PlayMode 4/4, .NET 47/47 (workspace-local SDK 8.0.428). WebGL module installed for 2022.3.18f1; uncompressed Development build of Dev1_Debug (23.3 MB) played in Edge 157 over localhost through success, failure, events, facilities, forecast and both restarts using CDP-injected clicks, 0 console errors. Editor HUD driven with real mouse input up to success and new-seed restart. Local commit only. | Codex-reported uninitialised session reproduced and fixed; browser acceptance previously blocked by missing module; commit limited to Dev 1 folder because ProjectSettings and Packages belong to the Plastic project | Dev 1 (simulation) |

## Progress
### 2026-11-14
- Record challenge, roles, stack, NASA dataset and initial scope.
### 2026-11-15
- Record demo readiness, risks, submission status and handoff.
