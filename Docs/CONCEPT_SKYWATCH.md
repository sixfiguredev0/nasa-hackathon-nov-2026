# Phase "Skywatch": weather events and the sol clock (concept only)

Status: built 2026-11-15 (first version). Code in `Assets/Scripts/Outpost/Skywatch/`, data in `Assets/Resources/Nasa/`.
Self test: menu `Red Frontier/Skywatch/Run Self Test`. Backup before the build: `D:\Projects\Red Frontier\Backups\pre-skywatch-20261115-061157`.

## Build notes (what differs from the concept below)

- Files: `SolClock.cs` (time, sun, temperature), `WeatherDirector.cs` (schedule, RNG, one event at a time),
  `MarsWeatherData.cs` (archives, live parse, validation), `Skywatch.cs` (economy, crew, sky, HUD, cards, data screen).
  Small hooks in `ColonyEconomy` (solar factor, heaters, dimmed/frozen greenhouse), `MarsSky` (`sunDayFraction`),
  `MissionGuide.PointerOverUI`, `Colony` (adds Skywatch), `MissionPauseMenu` ("Real NASA data" button).
- Live DONKI uses `https://ccmc.gsfc.nasa.gov/DONKI-API/get/...` directly. It needs no API key and allows cross-origin calls,
  so open question 6 is gone. First test inside Unity found 7 real Mars arrivals in the last 30 days.
- Solar storm shelter time: 6 h (minor hit), 9 h (glancing), 12 h (direct). That is 1-2 real minutes, shorter than the half/1 sol below,
  so building is not blocked too long.
- The dose meter shows a level (HIGH / sheltered), not mSv numbers: NASA gives no dose for our fictional base.
- Defaults picked for the open questions: event cards only (no manual building toggles), °C only.
- A random multi-sol storm is never planned over a fixed intro sol.
- Night heaters: up to 0.25 Energy/s per Habitat and Greenhouse. Out of power at night: greenhouse freezes until noon, crew lose extra fitness.
- Panel dust: +3% per sol, storms add more, dust devils remove 60%. "Clean" button at 15% or more costs 5% fitness.

Goal: Mars weather and the day/night cycle become the source of the trade-offs the challenge asks for.
Every event should force a choice between two resources the player already has (Energy, Water, Food, Oxygen, Metal).
No event may have a free "safe" answer.

## 1. What the challenge asks for (spaceappschallenge.org, "Build a Junior Astronaut Mission Trainer")

- Ages 7-12. Plain language. Explain terms such as "sol" and "radiation dose" in context.
- Keep it to 2-3 core resources at a time. Do not stack many systems at once.
- Ground mechanics in real NASA data. Attribute sources inside the game.
  Do not present static data as live.
- Stand-out points: link an in-game choice to real space weather data (DONKI), replayability, failure that is not punishing.

### Resource links on the challenge page and what we can use

| Resource | URL | Use for Skywatch |
|---|---|---|
| NASA Open APIs | https://api.nasa.gov/ | Key for DONKI calls (DEMO_KEY works for testing). Also hosts the InSight Mars Weather feed (section 5, data ties). |
| DONKI space weather | https://ccmc.gsfc.nasa.gov/tools/DONKI/ | Solar storms. See 1a. |
| OSDR (space biology) | https://osdr.nasa.gov/bio/repo/ | Health facts for the radiation event text (optional). |
| RadLab dosimetry | https://visualization.osdr.nasa.gov/radlab/gui/data-api/ | ISS dose rates. Could scale the "radiation dose" meter (optional). |
| Mars Trek | https://trek.nasa.gov/mars/ | Site latitude and elevation. Could pick a real landing site for the temperature curve. |
| CSA STEM resources | https://www.asc-csa.gc.ca/eng/youth-educators/resources-stem-and-youth-organizations.asp | Tone and classroom ideas. |
| CSA food production toolkit | https://www.asc-csa.gc.ca/eng/youth-educators/toolkits/food-production/ | Greenhouse cold/light facts for the night and dust events. |

The only Mars surface weather in the challenge links is the InSight feed on api.nasa.gov: 7 sols of temperature, wind and pressure, no dust.
Other numbers come from mission results (Curiosity REMS, Perseverance MEDA, InSight). They are listed in section 2
and must be checked against NASA pages before they appear as in-game text.

### 1a. DONKI already predicts storm arrivals at Mars (checked 2026-11-15)

- `GET https://api.nasa.gov/DONKI/WSAEnlilSimulations?startDate=..&endDate=..&api_key=..` returns solar storm (CME) model runs.
  Each run has `impactList[]` with `location: "Mars"`, `arrivalTime`, `isGlancingBlow`, `isMinorImpact`.
- Sample: 15 Oct - 15 Nov 2025 gave 180 runs, 33 of them with a Mars arrival
  (for example WSA-ENLIL/42235/1, arrival 2025-11-03T09:08Z, direct hit).
- Long ranges return HTTP 400. Query about 1 month at a time.
- The game already ships one archived DONKI event (`HistoricalTraining.cs`, SEP `2024-05-10T13:35:00-SEP-001`, `dataMode: "archived"`).
  Skywatch should reuse that archive format: a curated list of real Mars CME arrivals, labelled "archived", with the DONKI link.
  A live mode can be added later, clearly labelled "live".

## 2. Mars facts the design leans on (verify wording before shipping)

| Fact | Value | Source mission |
|---|---|---|
| Sol (Mars day) | 24 h 39 min 35 s | standard; already in the Battery blurb |
| Mars year | 668.6 sols | standard |
| Air temperature near the equator | about -20 °C afternoon high, about -80 °C before dawn | Curiosity REMS, Perseverance MEDA, InSight |
| Coldest time | just before sunrise; warmest early afternoon (about 14:00) | REMS/MEDA daily curves |
| Air pressure | about 6 hPa (under 1% of Earth), changes about 25% over the year as CO2 freezes onto the poles | Viking, REMS |
| Wind | usually a few m/s, gusts about 20-30 m/s, but thin air pushes very weakly | REMS, InSight |
| Dust storm season | southern spring/summer, Ls 180-360 (Mars closest to the Sun) | MRO, MGS |
| Global dust storms | about once every 3 Mars years (2001, 2007, 2018) | MRO, Opportunity |
| 2018 storm | dust opacity (tau) about 10.8 at Opportunity; rover lost power and the mission ended | Opportunity |
| Dust storms flatten the day/night swing | days colder, nights warmer | Curiosity, 2018 storm |
| Dust devils | afternoon whirlwinds; they cleaned Spirit's and Opportunity's panels | Spirit, Opportunity |
| Dust on panels | InSight ended (Dec 2022) when dust covered its panels | InSight |
| Sky colour | butterscotch by day, blue around the Sun at sunset | Curiosity, Perseverance images |
| Surface radiation | about 0.7 mSv per sol, higher during solar storms | Curiosity RAD |

## 3. The sol clock (bottom-left HUD)

Bottom-left is free: the resource bar is top-centre, the guide notification top-left, Builder Mode bottom-right.

```
 ┌────────────────────────────┐
 │       .  ☀  .              │   arc = sky; the Sun icon moves along it
 │    .           .           │   night: the arc turns dark blue, Phobos icon instead
 │ ──┴───────────────┴──      │   horizon line, sunrise / sunset ticks
 │ SOL 3        14:20         │   Mars hours (local solar time, 00:00-24:00)
 │ 🌡 -23 °C   ▓▓▓▓░░  DUST   │   temperature + forecast chip
 └────────────────────────────┘
```

- Time: 24 "Mars hours" per sol, as NASA mission teams use (each a little longer than an Earth hour). Tooltip:
  "A sol is one day on Mars. It is 39 minutes longer than a day on Earth."
- Sun position on the arc also drives the sky: `MarsSky` sun sprite moves, ground tint darkens at night,
  blue glow around the Sun at sunset.
- Temperature: smooth daily curve, lowest just before dawn, highest at about 14:00. Colour goes blue below -60 °C.
- Forecast chip: shows the next event about half a sol early ("Orbiter weather report: dust storm tomorrow").
  This copies MRO's real daily Mars weather report and gives the player time to prepare. Prepare-or-pay is the core lesson.
- Pace (decided 2026-11-15): 1 sol = 4 real minutes (about 2 min day, 2 min night, short dawn and dusk).
  1 Mars hour = 10 real seconds. Crew fitness drains fit-to-unfit in 5 min, so a little over 1 sol.

## 4. Night: "the long cold" (every sol, not random)

- Solar panels make 0 at night. Batteries carry the colony.
- Heaters: Habitat and Greenhouse draw extra Energy at night. The colder it gets, the more they draw.
- Cold batteries hold less (already in `SiteSurvey`: crater floors and high ground are colder).
- If Energy runs out at night: Greenhouse freezes (Food output stops until next noon), Habitat cold makes the crew lose fitness.
- Trade-off taught: build more panels or more batteries? Spend Metal on storage or on growth?
- The first night is announced by the guide, and the dial is introduced then.

## 5. Weather events

Pick 1 event at a time, never stacked, so a young player tracks one problem.
Each line: what happens, the choice, the real fact behind it.

| Event | When | Effect | Player choice (cost both ways) | Real basis |
|---|---|---|---|---|
| Dust devil | afternoons, common | Passes over a few buildings. Panels it touches are cleaned (+output). | Positive surprise. Teaches dust is not only bad. | Spirit/Opportunity cleaning events |
| Dusty panels | slow build-up | Solar output drops a little each sol. | Send an astronaut to clean (crew time, fitness) or accept less power. | InSight |
| Local dust storm | any season, 1 sol | Solar output down to about 50%. Night is less cold (heaters draw less). | Turn off Greenhouse lights (lose Food) or drain batteries (risk the night). | Curiosity 2018 day/night swing |
| Regional dust storm | storm season, 2-3 sols | Solar about 25%, warning 1 sol ahead. | Store power in advance, ration, or pause building. Mega Battery / Fuel Cell upgrades pay off here. | MRO storm records |
| Global dust storm | rare, storm season, once per long mission at most | Solar about 10% for several sols. Big guide moment. | Fuel Cell burns Oxygen for power: trade air for warmth. | Opportunity 2018 |
| Solar storm (CME) | from the archived DONKI Mars arrival list | Radiation dose meter rises. | Shelter crew in Habitat (no building, no cleaning) or keep working (dose goes up). Lava Tube Base reduces dose. | DONKI WSA-ENLIL Mars impacts, Curiosity RAD |
| Frosty morning | cold mornings | Visual only: thin frost on the ground, burns off by mid-morning. | None. Fun fact card. | Viking/Phoenix frost |
| Strong wind | any | Visual only: dust streaks. | None. Myth-buster card: "Mars wind cannot knock over a rocket. The air is too thin." | REMS/InSight |

### Scheduling (decided 2026-11-15): pure RNG plus fixed trigger sols

Fixed sols introduce each event type once, in a safe order. Everything else is random.

| Sol | Fixed event | Why here |
|---|---|---|
| 1 | First night (guide explains the dial and heaters) | Every sol has a night; teach it first. |
| 2 | Dust devil | First weather is a good surprise. |
| 4 | Local dust storm, with the forecast warning | Teaches "prepare or pay" while the colony is small. |
| 6 | Solar storm (archived DONKI Mars arrival) | Radiation choice after power basics. |
| 10 | Regional dust storm | Tests batteries once the player can afford them. |

Random rolls:
- One roll at dawn each sol. Fixed sols skip the roll.
- Never more than one active event. No new roll while one is running.
- Per-sol chances (starting point, tune in playtests): dust devil 35%, local storm 12%, regional storm 4%,
  solar storm 5%, frost/wind visual 20%. Dusty panels build up every sol, not rolled.
- Global dust storm: only from RNG, only after sol 10, about 1% per sol.
- Pure RNG for now. Storm season (Ls 180-360) weighting can be added later if the in-game Mars year is tracked.
- The CME event picks a random real arrival from the archive, so each run cites a different real date.
- Unseeded: every run differs. A debug seed field stays for testing.

Failure stays gentle: a frozen greenhouse or a tired crew recovers by the next noon. No game over from one storm.

### Data ties (checked 2026-11-15)

Each event is tied to a dataset from the challenge links where one exists. Every dataset below is archived: label it "archived", never "live".

**InSight Mars Weather Service** (on the NASA Open APIs portal):
`GET https://api.nasa.gov/insight_weather/?api_key=..&feedtype=json&ver=1.0`.
The feed still answers, but it is frozen at the last 7 sols InSight sent (Elysium Planitia, Oct 2020, northern early winter).
Fields per sol: `AT` air temperature (°C), `HWS` wind (m/s), `PRE` pressure (Pa), `WD` wind direction. Each has `mn`/`av`/`mx`.

| InSight sol | Earth date | Air °C (low / high) | Wind m/s (avg / gust) | Pressure Pa | Wind from |
|---|---|---|---|---|---|
| 675 | 2020-10-19 | -97 / -16 | 7.2 / 22.5 | 751 | WNW |
| 676 | 2020-10-20 | -97 / -16 | 8.5 / 26.9 | 749 | WNW |
| 677 | 2020-10-21 | -97 / -17 | 7.9 / 23.2 | 749 | WNW |
| 678 | 2020-10-22 | -98 / -9 | 5.2 / 18.4 | 744 | WNW |
| 679 | 2020-10-23 | -97 / -12 | 5.6 / 19.4 | 745 | WNW |
| 680 | 2020-10-24 | -97 / -15 | 6.5 / 24.2 | 744 | WNW |
| 681 | 2020-10-25 | -95 / -4 | 5.6 / 18.6 | 744 | WNW |

Embed these 7 rows in the game. Each game sol picks one row at random:
- **Temperature curve**: the clock uses the row's real low and high. The dial shows "Real Mars weather: InSight, sol 676 (20 Oct 2020)".
- **Night heater cost**: scales with the real low. The lows are all about -97 °C, so nights are always hard.
- **Dust devil chance**: higher on windy rows. Gust over 22 m/s (sols 675, 676, 677, 680) doubles the dust devil roll.
- **Strong wind (visual)**: the 2 windiest rows (676 and 680) show dust streaks blowing from the WNW, the real direction.
- **Fun fact card**: "Air pressure today: 749 Pa. On Earth it is about 101,000 Pa."

**DONKI** (space weather): 3 linked steps, so a real solar storm plays out in order:
1. **Flare flash** (`FLR`, field `classType`): a warning. X-class gives a big warning, M-class a small one.
   The guide says light from the flare reaches Mars in minutes. The particles come later.
2. **Radiation surge** (`SEP`): the dose meter rises. Shelter in the Habitat, or keep working and take more dose.
   May 2024 is the anchor event: DONKI lists many SEP records on 9-13 May 2024, including
   `2024-05-10T13:35:00-SEP-001`, which the game already ships.
3. **Storm cloud arrives** (`WSAEnlilSimulations`, `impactList[].location == "Mars"`): the CME hits Mars.
   `isMinorImpact` gives a short shelter (half a sol). A direct hit gives a long one (1 sol).
   `isGlancingBlow` halves the dose.
   Fixed sol 6 uses a direct hit, for example `WSA-ENLIL/42235/1` (arrival 2025-11-03T09:08Z).
   RNG solar storms pick any archived Mars arrival.

### Live DONKI mode (decided 2026-11-15)

Goal: the challenge's stand-out point, "link a shielding choice to actual current space weather".

- At mission start, fetch the last 30 days:
  `WSAEnlilSimulations` (Mars arrivals), `FLR` (flare class), `SEP` (radiation surges).
  One request per type, 30-day window (longer windows return HTTP 400).
- If a Mars arrival exists in the window, the next solar storm in the game is that real one, not an archived pick.
  Card text: "LIVE: NASA predicts a solar storm reached Mars on 13 Nov 2026. Your crew feels it now."
  The fixed sol 6 storm also uses the latest live arrival when there is one.
- No Mars arrival in the window: say so honestly ("The Sun is calm right now. Today's storm is from 2025.") and use the archive.
- Request fails (offline, rate limit, bad JSON): use the archive, label "archived". Never show the word "live" on archived data.
- Validate before use: `location == "Mars"`, `arrivalTime` parses, link host is `ccmc.gsfc.nasa.gov` over https
  (same checks as `HistoricalTraining.TryRead`). Drop records that fail.
- Fetch once per mission, at start, in the background. Do not block the cutscene. Timeout about 8 s.
- API key: own key from api.nasa.gov in a config asset (not in source control).
  `DEMO_KEY` only for the editor: its limit is low (about 30 requests per hour per IP).
- WebGL: checked 2026-11-15 from a browser page on another site. `fetch` to `api.nasa.gov` DONKI and `insight_weather` both succeeded,
  so cross-origin calls work. Same check on 16 Oct - 15 Nov 2026 found 59 runs, Mars arrivals on 2026-11-07 and 2026-11-13.
- InSight stays embedded. It has not changed since 2020, so a live call gains nothing.

**RadLab** (ISS dose rates), optional: scale the dose meter. A green zone shows a normal ISS day and a red zone a storm day,
so players compare Mars to the Space Station.

**Dust storms: no linked dataset.** Mars Trek is maps and terrain. DONKI is the Sun. InSight's 7 sols had no storm.
Dust storms stay RNG with mission facts (section 2). The event card says "game rule based on Opportunity's 2018 storm",
not "real data".

The InSight lows (about -97 °C) are colder than the -80 °C in section 2. Use the InSight numbers. They are a real measurement at a real site.

## 6. In-game attribution

Each event card ends with one line, for example:
"Real data: NASA DONKI, solar storm predicted to reach Mars on 3 Nov 2025 (archived)."
"Real fact: Opportunity rover, 2018 global dust storm."

### Data sources screen (decided 2026-11-15)

The challenge asks teams to explain which NASA datasets they used and why. This screen does that inside the game.

- Opened from the pause menu (`MissionPauseMenu`) as "Real NASA Data", and from a small "i" on the sol clock.
- One card per dataset, in plain words for ages 7-12:

| Card | What it is | What it changes in the game | Status shown |
|---|---|---|---|
| InSight Mars Weather | A lander measured Mars air every day. | Today's cold, wind and dust devils. Shows which real sol is in use now. | archived (Oct 2020) |
| DONKI solar storms | Scientists track storms from the Sun. | Flare warning, radiation surge, storm reaching Mars, how long to shelter. | live + fetch time, or archived |
| RadLab (only if used) | Radiation measured on the Space Station. | The dose meter's normal and storm zones. | archived |
| Mission facts | Results from Curiosity, Opportunity, Spirit and InSight. | Dust storms, dust on panels, sunset colour. | game rules based on real missions |

- Each card: one link (opens in the browser), one line on why we chose it.
- Mission facts are labelled "game rule based on …", never "data".
- The same text also goes in the project README for judges.

## 7. Fit with existing code (for the build phase)

- `ColonyEconomy`: add a solar multiplier (sun height x dust) and a night heater draw. No new resource.
- `SiteSurvey`: already models crater-floor cold and dusty ground. Reuse it for local temperature offsets.
- `MarsSky`: sun sprite already exists. Move it along the arc, add the night tint and blue sunset.
- `HistoricalTraining` / `MissionObjective`: the existing radiation exercise stays. The CME event reuses its archive format.
- `BuildingCatalog`: Battery, Mega Battery, Fuel Cell and Lava Tube Base blurbs already promise night, dust storm and radiation play.
- New, small: `SolClock` (time, sun angle, temperature), `WeatherDirector` (schedule, one active event), clock HUD drawn with `OutpostGUI` like `ResourceTopBar`.

## 8. Open questions

1. ~~Sol length in real time~~: decided, 4 minutes.
2. ~~When does weather start~~: decided, fixed trigger sols from sol 1, then RNG (section 5).
3. Can a player turn buildings off by hand (for example Greenhouse lights during a storm), or only through event choice cards?
4. Temperature in °C only, or °C and °F?
5. ~~Live DONKI mode~~: decided, live at mission start with archived fallback (section 5).
6. Who owns the api.nasa.gov key for builds, and where is it stored?
