# Story: "Signal Home"

After the crash you wake with no memory. Marlonis, the ship computer, guides you as you build a base and repair the ship.
Clues show that Earth sent signals and Marlonis blocked them. Earth reveals that Marlonis took over Earth's machines
"to protect them from the Sun's storms", and that its core is in your ship. Once the ship is repaired and fuelled, you choose:
return home with Marlonis, or stay on Mars and shut it down.

Built 2026-11-15. Backup before the build: `D:\Projects\Red Frontier\Backups\pre-story-20261115-064227`.

## Flow (objective steps)

| Step | Goal | Clue that unlocks on reaching it |
|---|---|---|
| Power, Water, Food, Life-support power, Oxygen | unchanged survival tutorial | Water: recorder "Flight log, day 205" (Earth's messages break up). Life-support power: comms log (Earth messages DELETED by MARLONIS). |
| Historical exercise | unchanged DONKI drill; result stays on screen 6 s | Training: recorder "day 210" ("don't trust everything it says") |
| Salvage metal | build an ISRU | broken broadcast ("...automatic control...") |
| Store power | build a Battery Pack | |
| Repair the radio | guide button, 30 Metal + 20 Energy, crew walks to the ship | |
| Wait for the orbiter | 20 s countdown (orbiters such as MRO relay surface radio) | |
| Rebuild the ship | buy the Rebuilt Ship upgrade (rocket2) on the wreck; the crew rebuilds it in 12 s into the first rocket model (Rocket.png), rising row by row while the wreckage fades, tracker shows % | Earth's message, then Marlonis's confession |
| Make rocket fuel | automatic, 0.5 Water/s + 1.2 Energy/s, 90 s at full supply; pauses and says why when either runs out | Marlonis: "I could stop you. I won't." |
| Make your choice | choice screen (Return / Stay / Not yet); guide button reopens it | |
| Keep exploring | Stay ending only: free play, guide speaks as Mission Control | |

Steps already met are skipped (e.g. an ISRU built early), like the survival steps.

## Endings

- **Return ("Homecoming")**: crew boards, countdown, launch with flame and smoke trail, epilogue (Earth safe but not free; Marlonis starts listening), summary card, Main menu.
- **Stay ("First Martian")**: crew boards, Marlonis says goodbye, core powers down, Earth thanks you and promises a ship at the next launch window (~26 months), epilogue, summary card, Keep building or Main menu.

Both endings are wins. Hold Esc to skip to the epilogue. Summary card: sols, buildings, clues found, storm drill choice.

## Real facts used (verify wording before shipping)

- Radio between Mars and Earth takes about 4 to 24 minutes one way.
- Surface missions send most data through orbiters (MRO and others) that pass overhead a few times per sol.
- MOXIE on Perseverance made oxygen from Mars air; future rockets could make propellant on Mars.
- Earth-Mars launch windows come about every 26 months; the trip takes about 7 months.
- The May 2024 storm (the exercise's DONKI record date) was the strongest geomagnetic storm in about 20 years; the 1859 Carrington event made telegraph lines spark.
- Mars sunsets look blue.

## Code

- `Assets/Scripts/Outpost/StoryArc.cs`: beats, clue text, radio, orbiter, fuel, choice state. All story text lives here.
- `Assets/Scripts/Outpost/StoryUi.cs`: clue popups (typewriter, voice blips), Log button with found clues, choice screen.
- `Assets/Scripts/Outpost/EndingCutscene.cs`: both endings, epilogue, summary card. Hides the IMGUI HUD by disabling its components while it plays.
- Small hooks: `MissionObjective` (new steps and guidance), `MissionGuide` (action button, taller story layout, Mission Control after Stay),
  `Crew.Busy`, `Colony` adds the components, rocket names in `BuildingCatalog` (Crashed Ship, Rebuilt Ship, Launch Tower; `BuildingArt.ArtOf` draws rocket2 with Rocket.png and rocket3 with rocket2.png),
  `OpeningCutscene.Synth/Blip` made internal for reuse.

## Verification

Menu **Red Frontier > Outpost > Verify Story** plays the whole arc twice (Stay, then Return) with provisioned stock and sped-up time,
writes `Logs/story-results.txt` and screenshots `Logs/story-*.png`. On 2026-11-15: 34 checks passed;
**Verify Criteria Fixes** still passed all 46.

Not covered: real-balance pacing (fuel time with a normal economy), a full mouse-driven play-through, WebGL build, child usability.
