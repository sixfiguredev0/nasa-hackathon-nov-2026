# NASA datasets

Candidate sources named in the challenge. Mark each as **Used**, **Candidate** or **Rejected** and note how the game uses it.

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
