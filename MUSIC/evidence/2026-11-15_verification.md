# Verification: 2026-11-15 (feat/music)

**Nothing was auditioned by ear.** The agent had no listening capability. Every claim below is
a technical observation: analysis scripts, Unity tests, and Web Audio and media-element events
hooked in a headless Chrome running the real WebGL build. The user has not yet listened to or
approved anything.

## Automated checks (final code)

| Check | Result | Evidence |
|---|---|---|
| Audio self-test: every catalogue variation imported, loadable from Resources, correct import settings | **PASS**: 49 clips, 0 failures | `verification/audio_selftest.txt` |
| PlayMode tests (`RedFrontier.Audio.Tests`): duplicate manager, cooldown, voice limit, gate, settings persistence, loop cleanup on owner destroy, scene-change cleanup and unpause, pause affects gameplay loops only and resume does not stack, music selector (no recent repeats, full coverage, tiny pools), missing clips and unknown IDs safe, generic UI press suppressed by a specific cue, title → arrival → gameplay sequencing | **13/13 PASS** | `verification/playmode_results.txt` |
| Existing project self-test (`OutpostEditor.RunSelfTest`), regression for mechanics | **ALL PASS** | `verification/outpost_selftest.txt` |
| WebGL build (`AudioVerification.BuildWebGL`) | **Succeeded, 0 errors** | `verification/webgl_build.txt` |

Build size: Unity's build report lists **Sounds 14.7 MB** of a 40.9 MB uncompressed WebGL build.
That is the audio's contribution: 7 movements at 1.5–1.9 MB, arrival 0.47 MB, wind 0.59 MB,
hum 0.40 MB, SFX the rest. Scripts add about 0.02 MB. The verification build used uncompressed
output; the project's own WebGL compression setting is untouched and applies on top.

## Browser sessions (local server, headless Chrome 2026, `index.html` of the build)

Chrome ran with `--autoplay-policy=document-user-activation-required`, the desktop default,
except session 1, which used the stricter `user-gesture-required`. Timelines are in
`verification/webgl_session4_timeline.txt` and `verification/webgl_session5_timeline.txt`.

| Area | Observed |
|---|---|
| Fresh launch / unlock | AudioContext starts **suspended**. Unity's resume attempts are refused until the first real click. Title music starts on that click and never before it. |
| Title, Options | Panel open/close, back on Esc and on the Back button, slider steps tick once per step. The mix row renders under Master volume. |
| Persistence | Effects changed to 70% in one browser session reads 70% after a full reload (default is 90%). Music muted at 0% earlier stayed muted. |
| Launch (motion) | Whoosh 1.6 s with arrival music. Title fades in 1.2 s. Descent roar starts with the descent, and touchdown follows it by **1.81–1.82 s** (animation 1.8 s). Wind bed starts. Guide chirp 1.5 s after touchdown. |
| Launch (Reduce Motion on) | No whoosh, no descent. **Touchdown only**, right after the instant landing. Arrival music still plays. |
| Build | Panel open. Slot select tick. Refused placements (overlap, outside area, "Need 4 Food more") give the denied cue with the red text still shown. Placement → place cue → hatch → footsteps → construction loop (two-source crossfade at clip end − 0.35 s) → completion cue → hatch on return. |
| Colony bed | Hum starts when the first building is commissioned. Wind and hum crossfade every 27.5 s and 17.5 s for 16 minutes, with **no rejected plays and no gaps**. |
| Guide | Notification chirp, panel open, Next blip per line. |
| Shortage | Alert when oxygen (or food) runs out. Tension starts **6.0 s** later, crossfading from a calm movement in 4 s. With the final code: onset, +30 s and +90 s reminders, then silence. Fitness warning once when entering the lowest segment. |
| Stall | One stall cue 1 s after an unpowered Greenhouse was commissioned. No repeats. |
| Long crisis (final code) | Tension plays to its end, an 18 s rest, then **Solitude (E)**, so tension does not loop back-to-back. |
| Pause | Panel open. Construction loop pauses and resumes without restarting or stacking. Wind and hum continue, ducked. A **28 s pause during a shortage** produced no false all-clear, no music change and no extra alarm on resume. |
| Return to title / relaunch | Title music restarts. Beds fade within about 1 s. Tension fades over 2 s. A second mission in the same page gives one wind bed (no stacking) and the full arrival chain again. |
| Rotation | Arrival → rest (4.8–5.3 s) → a gameplay movement. Movements seen in rotation across sessions: A, B, C, D and E. No movement repeated back-to-back. Each ends naturally before a rest. |
| Long run | Session 4: **about 16 minutes** of continuous outpost playback, 1,900 audio events. Session 5: 5 minutes, final code. |
| Console | Only Emscripten's benign `warning: 2 FS.syncfs operations in flight` (PlayerPrefs IDBFS sync), at scene load. No errors or exceptions. |

## Defects found in the browser and fixed (then re-verified)

1. **Pause faked a recovery.** The economy ticks with `dt = 0` while paused, so
   `lifeSupportShort` read false. The all-clear played on pause and the music left tension after
   12 s. Fix: `OutpostAudio` skips frames where game time does not advance. Re-verified with a
   28 s pause during a shortage.
2. **Alarm spam during a long crisis.** The shortage alert repeated every 30 s indefinitely.
   Now: onset plus reminders at +30 s and +90 s. Re-verified.
3. **Tension looped back-to-back during a long crisis.** It now alternates with Solitude after
   12–25 s rests. Re-verified.
4. Safety change: non-preloaded music now starts after a 3 s grace period, without waiting for a
   load state. An early "music missing" observation turned out to be a gap in the probe (Unity
   plays compressed clips through media elements, which the first probe did not hook). The change
   is kept as a harmless safeguard.

## Not verified / limitations

- **True in-game recovery** (shortage ending through gameplay) was not reached. The test
  colonies collapsed: food runs out and unfit crews halve production, so buildings stay
  underpowered. The all-clear chime and the tension → calm path did run in the browser, but only
  when the pause defect above triggered them. The recovery logic is a simple edge check in
  `OutpostAudio`.
- **Upgrade purchase in a browser**: not exercised. No upgrade was affordable in the test
  colonies. The hook is one line, guarded by `TryUpgrade`'s return value.
- **Gym training loop**: not exercised (no Gym built).
- **Safari or strict autoplay policies**: with `user-gesture-required`, Chrome refused several
  later media-element plays (wind crossfade, colony hum) until the next click. Unity's player
  queues and resumes them on the next interaction. The Chrome/Edge/Firefox default policy showed
  no refusals.
- **Browser audio memory** was not measured. Music uses Unity's compressed media-element path, so
  movements are not fully decoded into memory.
- Category captions in the **pause** Settings page render small, because the panel is narrow.
  The title Options page shows them at normal size.
- All mix values are technical starting points. They need a listening pass by the team.
