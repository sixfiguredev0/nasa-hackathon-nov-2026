# RED FRONTIER audio plan

Source of truth: the code on `feat/red-frontier-outpost` @ `a5b9d7f`, inspected 2026-11-15.
The game has **no audio code yet**: no `AudioSource`, `AudioClip` or `PlayOneShot`
anywhere in `Assets/`. Every hook below is a *proposed* call site. Integration is a
separate, later task and adds no game mechanics.

Flow: `MainMenu` (title → Options / crew pick → Start) → fly-to-Mars transition →
`MarsOutpost` (rocket lands → crew, Builder Mode, upgrades, economy, mission guide,
pause menu).

## Integration notes (for the later Unity task)

- The pause menu sets `Time.timeScale = 0` (`MissionPauseMenu.cs:63`). Gameplay SFX should
  pause with it (`AudioListener.pause` or scaled-time cooldowns). UI clicks and pause-menu
  sounds must use `ignoreListenerPause` / unscaled time.
- The title and outpost UI share `FrontierUi.Button` → `PixelButton` (`PixelUi.cs:304`).
  One hook there covers every uGUI button. The outpost's IMGUI controls (`BuilderMode`,
  `UpgradeBubbles`) need their own calls.
- Hover moves keyboard focus (`PixelUi.cs:341`), so a hover tick fires on keyboard
  navigation too. Keep it quiet and rate-limited.
- The crew is 1 or 2 astronauts (`Crew.Picked`), which caps simultaneous footsteps and
  hops at 2.
- The options menu has a "Reduce motion" setting but no volume control. A volume slider is
  out of scope here; it is noted for the integration task.

## Action → audio map

Priority: **P1** = in the first audition pack · P2 = next pack · P3 = nice to have.

| # | Game action (actual) | Code location | Audio ID | Type | Behaviour | Pri |
|---|---|---|---|---|---|---|
| 1 | Title screen idle (Mars, orbiting relay) | `MainMenu.cs` Awake / Update | `MUS_TITLE` | music | Loop, fade in on load, fade out over the 1.45 s launch | P1 |
| 2 | Any uGUI button press (Start, Options, Back, crew card, pause, guide) | `PixelButton` `PixelUi.cs:304` | `SFX_UI_CONFIRM` | sfx | One-shot, unscaled, 2 variations | P1 |
| 3 | Button hover / focus move | `PixelUi.cs:341`, `:367` | `SFX_UI_HOVER` | sfx | One-shot, very quiet, 60 ms cooldown | P2 |
| 4 | Back / Esc closes a panel | `MainMenu` CloseOptions / CloseCrewPick, `MissionPauseMenu.Resume` `:68` | `SFX_UI_BACK` | sfx | One-shot | P2 |
| 5 | Start → arrow flies into Mars (1.45 s) | `MainMenu.FlyToMars` (`MainMenu.cs:474`) | `SFX_LAUNCH_WHOOSH` | sfx | One-shot timed to the zoom; skipped when Reduce motion is on | P2 |
| 6 | Rocket descends and touches down on the Landing Pad | `Building.LandFromAbove` `Building.cs:49` (called `Colony.cs:48`) | `SFX_ROCKET_LANDING` | sfx | One-shot, thruster roar that settles into a thud and hiss, ~5 s | P1 |
| 7 | Outpost gameplay | `Colony` once `Ready` (`Colony.cs:57`) | `MUS_OUTPOST` | music | Long calm loop, low under SFX | P1 |
| 8 | Mars surface bed | MarsOutpost scene | `AMB_MARS_WIND` | ambience | Seamless loop, always on in the outpost | P1 |
| 9 | Builder Mode opened / closed (B or BUILD button) | `BuilderMode.Toggle` `BuilderMode.cs:43` | `SFX_BUILD_MENU` | sfx | One-shot (open; pitch-down for close) | P2 |
| 10 | Building chosen in the menu (ghost appears) | `BuilderMode.cs:149` | reuse `SFX_UI_CONFIRM` | sfx | One-shot | — |
| 11 | Blueprint placed (paid) | `BuilderMode.Place` success `BuilderMode.cs:157`, i.e. `Colony.TryPlace` returned a building | `SFX_BUILD_PLACE` | sfx | One-shot, 3 variations, 80 ms cooldown (Shift multi-place) | P1 |
| 12 | Placement refused (blocked / too expensive; red flash text) | `BuilderMode.cs:159` | `SFX_BUILD_DENIED` | sfx | One-shot, 300 ms cooldown | P1 |
| 13 | Placement cancelled (right-click / Esc) | `BuilderMode.cs:74`, `CancelForEscape` `:53` | reuse `SFX_UI_BACK` | sfx | One-shot | — |
| 14 | Crew leaves the rocket (fade at the door) | `Crew.Exit` `Crew.cs:138` | `SFX_HATCH` | sfx | One-shot at the door, once per trip (not per member) | P2 |
| 15 | Crew re-enters the rocket | `Crew.Enter` `Crew.cs:151` | reuse `SFX_HATCH` | sfx | One-shot | — |
| 16 | Crew walking (1 px bob = step) | `Crew.Walk` `Crew.cs:185` | `SFX_FOOTSTEP_REGOLITH` | sfx | Per bob, 4 variations, max 2 voices, very quiet | P3 |
| 17 | Crew building (hop loop, 3 s ÷ efficiency) | `Crew.Work` `Crew.cs:91–95` | `SFX_CONSTRUCTION_LOOP` | sfx | Looping while the job runs, stops at FinishBuild | P1 |
| 18 | Building/upgrade finished, sprite swaps | `Building.FinishBuild` `Building.cs:90` (from `Crew.cs:97`) | `SFX_BUILD_COMPLETE` | sfx | One-shot | P1 |
| 19 | Right-click building → bubbles open; tree / info toggle | `UpgradeBubbles.Update` `:33`, `:87–88` | reuse `SFX_UI_CONFIRM` | sfx | One-shot | — |
| 20 | Upgrade bought (yellow bubble) | `UpgradeBubbles.cs:90` when `TryUpgrade` returns true | `SFX_UPGRADE_PURCHASE` | sfx | One-shot (crew then builds it → 17, 18) | P1 |
| 21 | Upgrade click on an unaffordable or locked bubble | `UpgradeBubbles.cs:90` when `TryUpgrade` returns false | reuse `SFX_BUILD_DENIED` | sfx | One-shot | — |
| 22 | Crew training in a Gym (jumping jacks) | `Crew.Work` `Crew.cs:105–111` | `SFX_GYM_TRAIN` | sfx | Soft loop while training | P3 |
| 23 | A building stalls (missing input, red tint) | `ColonyEconomy.Run` `ColonyEconomy.cs:123`, edge `stalled` false→true | `SFX_POWER_DOWN` | sfx | One-shot on the edge only, 2 s cooldown per building | P2 |
| 24 | Life support runs out (O₂ / Food / Water) | `ColonyEconomy.lifeSupportShort` `:57`, `:106`, edge false→true | `SFX_ALERT_LIFE_SUPPORT` | sfx | One-shot alarm on the edge, then repeat every 10 s while short | P1 |
| 25 | Crew fitness drops into its lowest segment | `Colony.DrawCrewFitness` reads `economy.fitness` | `SFX_ALERT_FITNESS` | sfx | One-shot on crossing a threshold (e.g. < 0.2) | P3 |
| 26 | Marlonis guide: open / Next / Done | `MissionGuide.ToggleDialogue` `:138`, `Advance` `:158` | `SFX_GUIDE_BLIP` | sfx | One-shot per line, radio-chirp character | P2 |
| 27 | Pause menu open | `MissionPauseMenu.Pause` `:57` | `SFX_UI_PAUSE` (or reuse confirm) | sfx | Unscaled; music ducks −6 dB while paused | P2 |
| 28 | Return to title | `MissionPauseMenu.ReturnToTitle` `:209` | — | — | Music crossfades back to `MUS_TITLE` | — |

`MissionScreen.cs` (placeholder scene) and `StormMission.cs` (logic with no scene hook
yet) are not wired into gameplay. No audio is planned for them until they are.

## Mix targets (starting points, tuned in-engine)

| Bus | Relative level | Notes |
|---|---|---|
| Music | −14 dB | Ducks −6 dB while paused |
| Ambience | −18 dB | Constant bed |
| UI | −10 dB | Unscaled time |
| Gameplay SFX | −8 dB | Alerts at −4 dB |

## Delivery format

ElevenLabs delivers the format the platform chooses (confirmed at first download, see
`LOG.md`). Originals are kept as delivered. Unity playback copies: WAV for short SFX
(Decompress On Load), Vorbis for music and ambience (Streaming).
