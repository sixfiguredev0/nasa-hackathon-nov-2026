# Red Frontier

A pixel-art Mars outpost game for kids aged 7–12, built by **Coding Foxes** for the
NASA Space Apps Challenge 2026 (Sofia, 14–15 November 2026).

Challenge: [Build a Junior Astronaut Mission Trainer](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/).

You crash on Mars with no memory. Marlonis, the ship computer, guides you as you build a base,
keep your crew alive and repair the ship. Real NASA space-weather and Mars-weather data drive the
events, so every storm or solar burst forces a real trade-off between energy, water, food,
oxygen and metal.

## Team

| Member | GitHub |
|---|---|
| Angel Angelov | — |
| Yasen Katevski | [@sixfiguredev0](https://github.com/sixfiguredev0) |
| pulserr3 | — |

## Run it

1. Open the repository folder in **Unity 2022.3.18f1** (Unity Hub → Add project from disk).
2. Open `Assets/Scenes/MainMenu.unity` and press Play. In the editor, Play always starts at the
   main menu (`Assets/Editor/PlayFromMainMenu.cs`).
3. Build: File → Build Settings, scenes `MainMenu`, `SampleScene`, `MarsOutpost`. WebGL is supported.

Controls: WASD / arrow keys / right-mouse drag to pan, **B** or the bottom-right button for
Builder Mode, **Esc** to pause, any key or click to skip the opening.

## What's in the game

- Procedural Mars terrain with craters, mesas and dunes; terrain shapes what each building yields.
- Outpost economy: rocket, habitat, solar, greenhouse, gym and more, with crew upkeep and fitness.
- A hard mission: crew can die, seeded events (solar particle burst, dust storm, equipment
  failure, resupply delay), and a debrief that explains what happened.
- Skywatch: a sol clock with day and night, Mars weather events and a "Real NASA data" screen.
- Story arc "Signal Home" with two endings.
- Soundtrack, sound effects and ambience made for the game.

## NASA data

Data ships with the game in `Assets/Resources/Nasa/` (with its retrieval manifest) and is
credited inside the game.

- [NASA CCMC DONKI](https://ccmc.gsfc.nasa.gov/tools/DONKI/): solar particle events and Mars
  CME arrivals, archived and live via `https://ccmc.gsfc.nasa.gov/DONKI-API/get/`.
- [NASA InSight Mars Weather](https://api.nasa.gov/) (archived feed): temperature, wind, pressure.
- Mars surface facts from Curiosity REMS, Perseverance MEDA and InSight mission results.

See [Docs/CRITERIA_FIXES.md](Docs/CRITERIA_FIXES.md) and
[Docs/CONCEPT_SKYWATCH.md](Docs/CONCEPT_SKYWATCH.md) for record IDs and links.

## Repository layout

| Path | Contents |
|---|---|
| `Assets/Scenes/` | `MainMenu`, `MarsOutpost` and other scenes. |
| `Assets/Scripts/Menu/` | Title screen, main menu, mission screens, shared pixel UI. |
| `Assets/Scripts/Mars/` | Procedural terrain and sky. |
| `Assets/Scripts/Outpost/` | Buildings, economy, crew, mission, story, Skywatch. |
| `Assets/Scripts/Audio/` | Audio manager, cue catalog and tests. |
| `Assets/Art/`, `Assets/Resources/` | Sprites, sprite sheets, NASA data, audio clips. |
| `Assets/RedFrontierOutpost/` | Resource HUD prefab and its preview scene. |
| `ArtSource/` | Aseprite sources for the Mars ground and space art. |
| `MUSIC/` | Audio production workspace: plan, prompts, scripts, usage rights. |
| `Tools/` | Art converters (Aseprite, Mars art) and the economy simulator. |
| `Docs/` | Design notes: environment, mission, story, Skywatch, NASA exercise. |

## Credits

Game audio was generated with ElevenLabs on a paid plan; usage rights are recorded in
[MUSIC/evidence/2026-11-15_usage_rights.md](MUSIC/evidence/2026-11-15_usage_rights.md).
NASA data is used under NASA's open data policy and is not an endorsement by NASA.
