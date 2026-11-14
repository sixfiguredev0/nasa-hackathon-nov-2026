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

## Progress
### 2026-11-14
- Record challenge, roles, stack, NASA dataset and initial scope.
### 2026-11-15
- Record demo readiness, risks, submission status and handoff.
