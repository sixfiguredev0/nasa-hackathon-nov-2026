# Junior Astronaut Mission Trainer

Coding Foxes' NASA Space Apps Challenge 2026 entry: a browser-playable game for kids ages 7 to 12 who run a lunar or Martian outpost and learn real mission trade-offs (power, life support, radiation shielding) grounded in NASA open data.

## Kickoff decisions

| Item | Decision | Record |
|---|---|---|
| Challenge | [Build a Junior Astronaut Mission Trainer](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/) | [`docs/challenge/brief.md`](docs/challenge/brief.md) |
| Stack / hosting | **Undecided.** Unity, a lightweight web stack and Godot are under comparison. | [`docs/decisions/0001-engine.md`](docs/decisions/0001-engine.md) |
| NASA data | Candidates: DONKI, OSDR, Artemis and Mars surface data. None confirmed. | [`docs/data/nasa-datasets.md`](docs/data/nasa-datasets.md) |

Decisions and risks are logged in [`DEVLOG.md`](DEVLOG.md).

## Instructions for agents and teammates

| File | Purpose |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Authoritative shared instructions. Read first. |
| [`CLAUDE.md`](CLAUDE.md), [`CODEX.md`](CODEX.md), [`GEMINI.md`](GEMINI.md) | Tool-specific pointers to AGENTS.md. |
| `WORKFLOW.md` | Collaboration and Git rules. *Not yet added.* |
| `NASA_DATA.md` | Event boundary and data rules. *Not yet added.* |
| `SUBMISSION.md` | Submission requirements. *Not yet added.* |
| [`DEVLOG.md`](DEVLOG.md) | Dated decisions, data changes, milestones, risks. |

## Where things live

| Path | Purpose |
|---|---|
| [`docs/challenge/`](docs/challenge/) | Official challenge brief and requirements checklist. |
| [`docs/design/`](docs/design/) | Game design drafts: core loop, resources, audience layers. |
| [`docs/decisions/`](docs/decisions/) | Decision records, one file per decision. |
| [`docs/data/`](docs/data/) | Dataset table and attribution rules. |
| [`docs/planning/BOARD.md`](docs/planning/BOARD.md) | Task board. Claim a task before starting it. |
| [`docs/source/`](docs/source/) | Other planning notes, kept as received. |
| [`game/`](game/) | The playable game. Empty until the stack is decided. |
| [`data/`](data/) | Curated NASA data the game ships with (`raw/` as downloaded, `processed/` as used). |
| [`tools/`](tools/) | Scripts: data fetching, conversion, build helpers. |
| [`assets/`](assets/) | Engine-independent source art and audio, with credits. |
| [`submission/`](submission/) | Hackathon deliverables: demo, slides, dataset explanation. |
