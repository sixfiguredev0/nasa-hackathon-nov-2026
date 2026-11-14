# Candidate NASA datasets

Research notes on sources named in the challenge. The official record is the **Source register in [NASA_DATA.md](../../NASA_DATA.md)**: once a dataset is used, record it there with full details and mark it **Used** here.

| Dataset | What it gives us | Possible game use | Access | Status |
|---|---|---|---|---|
| DONKI (Space Weather Database Of Notifications, Knowledge, Information) | Solar flares, CMEs, solar energetic particle events | Radiation storm events and their likelihood | Public API via api.nasa.gov (needs a free API key; never commit it) | Candidate |
| OSDR (Open Science Data Repository) | Astronaut radiation dosimetry, biological research | Crew health effects, "Why?" explanations | Public portal and API | Candidate |
| Artemis / lunar surface data | Lunar south pole environment, sunlight, temperature | Moon site conditions, solar power availability | Various NASA portals | Candidate |
| Mars surface missions (rovers, InSight) | Weather, dust, radiation at the surface | Mars site conditions, dust storms reducing solar power | Various NASA portals | Candidate |

## Rules

- Raw downloads go in `data/raw/`, with a note of the source URL and download date.
- Data the game reads goes in `data/processed/`.
- The game shows an attribution screen listing every dataset used.
- If data is archived, the game says so. Never label it live.
