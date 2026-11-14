# Coding Foxes — NASA Space Apps Challenge 2026

Team instruction set for 14–15 November 2026. Challenge and stack are **kickoff decisions**. This is a new repository, separate from the old Amusnet/Unity project.

## Start here
- AI agents and teammates: [AGENTS.md](AGENTS.md) is authoritative.
- Tool entry points: [CLAUDE.md](CLAUDE.md), [GEMINI.md](GEMINI.md), [CODEX.md](CODEX.md) defer to AGENTS.md.
- Roles: [TEAM.md](TEAM.md). Git and communication: [WORKFLOW.md](WORKFLOW.md).
- Data integrity: [NASA_DATA.md](NASA_DATA.md). Progress: [DEVLOG.md](DEVLOG.md). Deadlines: [SUBMISSION.md](SUBMISSION.md).

## Confirmed setup
- Team: Coding Foxes. Audience: humans and AI agents.
- AI tools: Claude Code, Gemini, Codex, Antigravity and other AI tools.
- Repository: new. Challenge and technical stack: decide at kickoff.
- Do not assume Unity/C# or carry over old project decisions.

## Official references
- [2026 event hub](https://www.spaceappschallenge.org/2026/)
- [Challenges and challenge resources](https://www.spaceappschallenge.org/2026/challenges/)
- [Sofia event and schedule](https://www.spaceappschallenge.org/2026/local-events/sofia/?tab=schedule)
- [2026 participant terms](https://www.spaceappschallenge.org/legal/)
- [Global Organizing Team guidance on preparation, data and attribution](https://github.com/nasa/spaceapps/discussions/1827)
- [Sofia community Discord](https://discord.gg/ESms8db7dy)

## Confirmed rules vs local details
The official 2026 site lists the event for Nov 14–15, project submission opening Nov 14 at 09:00 and closing Nov 15 at 23:59 local time. Global Awards require at least one NASA data source listed in the selected challenge Resources. Cite/list all data, resources and tools used.

The Sofia event listing gives Incubator, Sofia Tech Park; registration 09:00, opening 10:00, hacking 10:30 on Nov 14; demo deadline 14:00, finalist pitches 16:00, closing 17:30 and global deadline 23:59 on Nov 15. These are **local organizer details**, reconfirm onsite. Venue access and overnight arrangements are not established here.

## Kickoff decisions
Challenge/link: [Build a Junior Astronaut Mission Trainer](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/) (brief: [docs/challenge/brief.md](docs/challenge/brief.md)) | Project/user/success criterion: outpost-management game for kids 7–12; proposed criterion: after one session a player can explain one real trade-off | Stack/hosting/accounts: TBD, see [docs/decisions/0001-engine.md](docs/decisions/0001-engine.md) | Members/roles: see [TEAM.md](TEAM.md) | Repo URL/default branch: https://github.com/sixfiguredev0/nasa-hackathon-nov-2026, default `main` (pending creation) | Demo format/language/presenters: TBD

## Repository layout

| Path | Purpose |
|---|---|
| Root `*.md` | Rules and status: AGENTS, tool pointers, TEAM, WORKFLOW, NASA_DATA, SUBMISSION, DEVLOG. |
| [`docs/challenge/`](docs/challenge/) | Official challenge brief and requirements checklist. |
| [`docs/design/`](docs/design/) | Game design drafts: core loop, resources, audience layers. |
| [`docs/decisions/`](docs/decisions/) | Decision records, one file per decision. |
| [`docs/data/`](docs/data/) | Candidate datasets under research. The official register is in NASA_DATA.md. |
| [`docs/planning/BOARD.md`](docs/planning/BOARD.md) | Task board. Claim a task before starting it. |
| [`docs/source/`](docs/source/) | Other planning notes, kept as received. |
| [`game/`](game/) | The playable game. Empty until the stack is decided. |
| [`data/`](data/) | NASA data the game ships with: `raw/` as downloaded, `processed/` as used. |
| [`tools/`](tools/) | Scripts: data fetching, conversion, build helpers. |
| [`assets/`](assets/) | Engine-independent source art and audio, with credits. |
| [`submission/`](submission/) | Demo video, slides, screenshots. Checklist is in SUBMISSION.md. |
