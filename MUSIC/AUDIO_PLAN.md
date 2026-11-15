# RED FRONTIER audio plan, as implemented

Game: the real-time outpost builder. `MainMenu.unity` → fly-to-Mars → `MarsOutpost.unity`.
Baseline: `feat/red-frontier-outpost` @ `a5b9d7f`. Implemented on `feat/music`.
This replaces the earlier proposal. Every row below is wired in code, or deliberately silent.

## Architecture (smallest coherent version)

| Piece | File | Role |
|---|---|---|
| `GameAudio` | `Assets/Scripts/Audio/GameAudio.cs` | One persistent manager (`DontDestroyOnLoad`, duplicate guard). 16 pooled 2D sources with per-cue cooldown, voice limit and priority stealing. Crossfaded loops owned by a game object. Ambience beds. Music section controller. Pause. WebGL unlock. Scene cleanup. Missing clips warn once and play nothing. Variation and music choice use their own `System.Random`, never gameplay RNG. |
| `AudioCatalog` | `Assets/Scripts/Audio/AudioCatalog.cs` | Stable sound IDs (`Sfx.*`, `MusicIds.*`). Per ID: bus, variation count, gain, cooldown, max voices, priority, pitch jitter. |
| `MusicSelector` | `Assets/Scripts/Audio/MusicSelector.cs` | Next gameplay movement (never one of the last two) and rest length. |
| `AudioPrefs` | `Assets/Scripts/Audio/AudioPrefs.cs` | `MusicVolume` 0.7, `EffectsVolume` 0.9, `AmbienceVolume` 0.7 in PlayerPrefs. **`FrontierUi.MasterVolume` (`AudioListener.volume`, key `MasterVolume`) is unchanged.** Category levels multiply on top of it. |
| `AudioMixRow` | `Assets/AudioMixRow.cs` | Three compact sliders (Music / Effects / Ambience) under Master volume in title Options and in pause Settings. |
| `OutpostAudio` | `Assets/Scripts/Outpost/OutpostAudio.cs` | Reads colony state, never writes it. Edge-triggered alerts, colony hum level, shortage signal for music. Added by `Colony.Start`. |
| Import rules | `Assets/Scripts/Audio/Editor/AudioImportRules.cs` | Music: Compressed In Memory, Vorbis q0.7, not preloaded, load in background. SFX: Decompress On Load. Ambience: Compressed In Memory. No Streaming, no AudioMixer. WebGL transcodes to AAC itself. |

WebGL notes: browsers keep audio suspended until a real click or key press. `GameAudio` starts
nothing until it sees one, so title music begins on the first interaction. Category volume is
set per source; no mixer effects are used. Music clips load right before a movement starts and
unload after it fades, so at most two movements are decoded in browser memory at once. Loops
use two-source crossfades instead of `AudioSource.loop`, so AAC frame padding cannot open a gap.

## Music: suite "Red Frontier", about 11 minutes, instrumental

| ID | Length | Character | When |
|---|---|---|---|
| `MUS_TITLE` | 97.8 s | Welcoming organ swell and felt-piano theme, strings rise and settle (D) | Title screen. Plays through, rests 6–12 s, repeats. Starts on the first interaction in WebGL. |
| `MUS_ARRIVAL` | 23.7 s | Short wondrous swell resolving on D | From Start (launch) across the scene load and rocket landing. 1.2 s crossfade out of the title. |
| `MUS_SURFACE_A` "First Light" | 93.2 s | Sparse felt piano over organ drone, arpeggio emerges (G/D) | Gameplay rotation |
| `MUS_SURFACE_B` "Red Horizon" | 100 s | Warmer low-string melody over shimmering pads (D/A) | Gameplay rotation |
| `MUS_SURFACE_C` "Quiet Machines" | 88.2 s | Minimal analog textures, long breaths (Bm/D) | Gameplay rotation |
| `MUS_SURFACE_D` "Long Day" | 99.7 s | Organ chorale answered by piano, gentle lift | Gameplay rotation |
| `MUS_SURFACE_E` "Solitude" | 86.3 s | Lonely solo piano, warm turn at the end (Em) | Gameplay rotation |
| `MUS_TENSION` "Thin Air" | 79.6 s | Low organ drone, wavering strings, never climaxes | Sustained life-support shortage |

Controller rules (`GameAudio.UpdateMusic`):
- Gameplay starts only after the arrival cue ends, then waits 3–6 s.
- Each movement plays to its natural ending, followed by silence: 8–20 s, or 25–50 s about 30%
  of the time, with the wind bed carrying the space. No movement repeats within two picks. The
  rotation is endless, so long sessions are fine.
- Pitch is never altered. Separate generations are never layered as stems: only whole-section
  crossfades are used.
- **Tension:** `lifeSupportShort` must hold for 6 s. Then a 4 s crossfade to `MUS_TENSION`.
  While the shortage lasts, tension alternates with the lonely `MUS_SURFACE_E` (Solitude) with
  12–25 s silences between them, so a long crisis never loops one cue. Recovery must hold for 12 s, then a 6 s fade-out
  and a 10–14 s rest before calm music returns. Tension is then locked out for 45 s. Brief
  fluctuations never switch the music; the alert cues still report them.
- **Pause:** music ducks to 45% (0.5 s unscaled fade) and keeps playing. Ambience ducks to 60%.
- **Return to title:** `MainMenu.Awake` crossfades back to `MUS_TITLE` (2 s).

## Interactions

Silence is a deliberate decision where marked "—". Hover and focus moves are silent everywhere.

### Title and options (`MainMenu.cs`, `PixelUi.cs`)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| Any PixelButton press without its own cue (Options toggles, Fullscreen, Resolution, crew cards…) | `SFX_UI_CONFIRM` (3 variations) | `PixelButton.OnPointerClick` / `OnSubmit` → `GameAudio.UiPress` | Skipped if the button's action played a specific cue in the same frame, so a press never makes two sounds |
| Hover / keyboard focus move | — | | Silent on purpose |
| Start → crew pick opens; Options opens | `SFX_UI_PANEL_OPEN` | `OpenCrewPick` / `OpenOptions` | One-shot |
| Back / Esc closes crew pick or Options | `SFX_UI_BACK` | `CloseCrewPick` / `CloseOptions` | One-shot |
| Slider step (master or category) | `SFX_UI_SELECT` | `PixelSlider.Set` (user changes only) | 70 ms cooldown. The tick previews the new level. |
| Crew card picked → launch | `SFX_LAUNCH_WHOOSH` + `MUS_ARRIVAL` | `MainMenu.Launch` → `GameAudio.Launch(reduced)` | The whoosh matches the 1.45 s flight. **With Reduce Motion the whoosh is skipped** (no flight), but arrival music still plays. |
| Launch error (scene missing) | `SFX_UI_BACK` | `Pick` → `CloseCrewPick` | The existing on-screen message stays |

### Arrival (`Building.LandFromAbove`, `Crew.cs`)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| Rocket descends (1.8 s quadratic ease-out) | `SFX_ROCKET_DESCENT` | Start of the motion path | 1.9 s clip that fades as the descent ends |
| Touchdown | `SFX_ROCKET_TOUCHDOWN` | End of `LandFromAbove`, on both paths | **Reduce Motion skips the descent, so touchdown is the only landing cue** |
| Surface bed | `AMB_MARS_WIND` | `OutpostAudio.Start` → `EnterOutpost` | 3 s fade-in, crossfaded loop, ducked while paused, fades on scene exit |
| Crew leaves / re-enters the rocket | `SFX_HATCH` (2 variations) | `Crew.Exit` / `Crew.Enter` at the door fade | 1 s cooldown, so one hatch per trip for a two-person crew |
| Marlonis message waiting | `SFX_GUIDE_NOTIFY` | `MissionGuide.Start`, 1.5 s delay (after touchdown) | One-shot. The unread badge stays the primary signal. |

### Building and upgrades (`BuilderMode.cs`, `UpgradeBubbles.cs`, `Crew.cs`)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| BUILD button / B opens or closes the menu | `SFX_UI_PANEL_OPEN` / `SFX_UI_PANEL_CLOSE` | `BuilderMode.Toggle` | One-shot |
| Pick or unpick a building | `SFX_UI_SELECT` | Slot click | One-shot |
| Ghost moving / turning red or green | — | | Silent on purpose; colour is the signal |
| Blueprint placed (resources paid) | `SFX_BUILD_PLACE` (3 variations, ±4% pitch) | `Place` when `TryPlace` succeeds | 80 ms cooldown keeps Shift-placing tidy |
| Placement refused | `SFX_UI_DENIED` | `Place` when `TryPlace` fails | 300 ms cooldown. The red reason text stays visible. |
| Cancel placement (right-click, Esc) | `SFX_UI_BACK` | Right-click with a selection, or `CancelForEscape` | One-shot |
| Crew building | `SFX_CONSTRUCT_LOOP` | `Crew.Work` build phase | Crossfaded loop owned by Crew. Stops when the work ends. Pauses with the pause menu. Stops if Crew is destroyed. |
| Construction or upgrade finished | `SFX_BUILD_COMPLETE` (2 variations) | After `job.FinishBuild()` in `Crew.Work` | **Only when the crew finishes real work.** Never during scene setup, rocket spawn or sprite refreshes. |
| Right-click building → bubbles; ↑ / i toggles | `SFX_UI_SELECT` | `UpgradeBubbles` | Closing by clicking empty space is silent |
| Upgrade bought | `SFX_UPGRADE_PURCHASE` (2 variations) | `TryUpgrade` returns true | Purchase and completion are distinct cues. Completion follows when the crew finishes. |
| Unaffordable, locked or owned bubble clicked | `SFX_UI_DENIED` | `TryUpgrade` returns false | The tooltip already explains why |
| Esc closes bubbles | `SFX_UI_BACK` | `CancelForEscape` | One-shot |

### Crew detail (`Crew.cs`)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| Walking | `SFX_FOOTSTEP` (4 variations, ±6% pitch) | Every second bob step in `Crew.Walk` | Very quiet (−27 dB), at most 2 voices, 120 ms cooldown |
| Training in a Gym | `SFX_GYM_LOOP` | `Crew.Work` gym phase | Quiet crossfaded loop. Pauses with the pause menu. |

### Colony state (`OutpostAudio.cs`, read-only)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| Oxygen, Food or Water runs out | `SFX_ALERT_SHORTAGE` | `lifeSupportShort` false → true | Reminders at +30 s and +90 s, then silent until recovery (HUD and tension music carry it). Highest priority. |
| Life support restored | `SFX_ALERT_RECOVERED` | true → false after at least 1.5 s short | 6 s cooldown |
| Building stalls (red tint) | `SFX_ALERT_STALL` (2 variations) | A building stalled continuously for 1 s | One cue for any number of new stalls. A building re-arms after running 3 s. 8 s cooldown. Held back within 4 s of a shortage alert. |
| Crew fitness enters the lowest gauge segment | `SFX_ALERT_FITNESS` | fitness < 0.2 | Re-arms above 0.3. 30 s cooldown. |
| Colony machinery | `AMB_COLONY_HUM` | Every frame: level = (commissioned buildings − 1) / 6 | Glides over 2 s. Silent with only the Rocket. |
| Resource bar changes | — | | Silent on purpose; the bar is the information |

### Marlonis dialogue (`MissionGuide.cs`)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| Open dialogue | `SFX_UI_PANEL_OPEN` | `ToggleDialogue` (open) | One-shot |
| Next line | `SFX_GUIDE_NEXT` (2 variations) | `Advance` | One-shot |
| Done / close | `SFX_UI_PANEL_CLOSE` | `CloseDialogue` | One-shot |

### Pause (`MissionPauseMenu.cs`)
| Interaction | Sound ID | Trigger | Behaviour |
|---|---|---|---|
| Esc pauses | `SFX_UI_PANEL_OPEN` | `Pause` → `GameAudio.SetPaused(true)` | Construction and gym loops pause. Music and ambience duck. UI sounds keep working (they ignore `timeScale`). All fades use unscaled time. |
| Resume (button or Esc) | `SFX_UI_PANEL_CLOSE` | `Resume` → `SetPaused(false)` | Loops resume where they stopped, without restarting or stacking. Silent while a scene load is under way. |
| Esc from Settings or the confirmation page | `SFX_UI_BACK` | `HandleEscape` | One-shot |
| Return to title | generic confirm | Button | The scene change stops gameplay loops and beds; music crossfades to the title |

## Not wired (deliberately)
- `MissionScreen.cs` (placeholder scene) and `StormMission.cs` (logic with no scene hook): no
  gameplay to score yet.
- No new gameplay systems, timers or state were added. Every hook is a call at an existing
  action point.
