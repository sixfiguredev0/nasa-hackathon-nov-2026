# Audition pack v1: PROPOSED, awaiting approval

Status: `planned`. Nothing has been generated.
Scope: the P1 rows of `AUDIO_PLAN.md`. Instrumental only. No references to existing games
or artists.

Pricing measured with free `estimate_only` calls on 2026-11-15 (see
`evidence/2026-11-15_account_and_estimates.md`):
SFX `eleven_text_to_sound_v2` = **11 credits/s** (55 when duration is left on auto) ·
Music `eleven_music_v2_5` = **27.5 credits/s**. Each take is charged.
Re-run `estimate_only` per asset right before generating.

## Music: `eleven_music_v2_5`, `lyrics_type: instrumental`, `instrumental: true`

| ID | Duration | Takes | Est. credits | Prompt |
|---|---|---|---|---|
| `MUS_TITLE` | 60 s | 2 | 3,300 | Spacious ambient synth score, slow tempo around 70 BPM, hopeful and slightly lonely. Warm analog pads, a soft pulsing low arpeggio, distant bell-like lead melody, gentle tape saturation. Builds very slightly in the middle and settles back to a quiet pad so the ending can loop. Instrumental. |
| `MUS_OUTPOST` | 90 s | 2 | 4,950 | Calm, focused ambient electronic loop for long play sessions, steady 90 BPM. Muted plucked synth ostinato, round sub bass, soft brushed electronic percussion, airy pads. Even energy throughout, no big climax or drop, sparse enough to sit under sound effects; ends on the opening chord for looping. Instrumental. |

## Ambience: `eleven_text_to_sound_v2`, `loop: true`, `prompt_influence: 0.5`

| ID | Duration | Takes | Est. credits | Prompt |
|---|---|---|---|---|
| `AMB_MARS_WIND` | 20 s | 2 | 440 | Thin, dry desert wind over open rocky ground, low steady rumble with soft high whistling gusts, fine dust hiss, no birds, no insects, no voices |

## SFX: `eleven_text_to_sound_v2`, `prompt_influence: 0.5`, default 4 takes

| ID | Duration | Loop | Takes | Est. credits | Prompt |
|---|---|---|---|---|---|
| `SFX_UI_CONFIRM` | 0.5 s | no | 4 | 22 | Short clean sci-fi interface confirm blip, soft rounded digital tone, close-mic, dry |
| `SFX_ROCKET_LANDING` | 5 s | no | 4 | 220 | Rocket descending on retro thrusters, rumbling roar easing down, heavy metal landing-leg thud, then venting steam hiss fading out |
| `SFX_BUILD_PLACE` | 1 s | no | 4 | 44 | Metal construction frame set down on gravel, solid clunk with a short servo whir, dry close-mic |
| `SFX_BUILD_DENIED` | 0.8 s | no | 4 | 36 | Short low two-tone error buzz from a computer console, soft, not harsh |
| `SFX_CONSTRUCTION_LOOP` | 4 s | yes | 4 | 176 | Light construction work, rhythmic small hammering on metal panels and a cordless power driver, steady, no voices |
| `SFX_BUILD_COMPLETE` | 1.5 s | no | 4 | 66 | Bright rising synth chime with a soft pneumatic seal hiss, satisfying task-complete sound |
| `SFX_UPGRADE_PURCHASE` | 1.5 s | no | 4 | 66 | Upward sci-fi power-up sweep with a crisp digital click at the end, positive and short |
| `SFX_ALERT_LIFE_SUPPORT` | 2 s | no | 4 | 88 | Spacecraft warning alarm, two urgent descending beeps repeated twice, clear and mid-pitched, not piercing |

## Totals

| Group | Est. credits |
|---|---|
| Music (2 tracks × 2 takes) | 8,250 |
| Ambience (1 × 2 takes) | 440 |
| SFX (8 × 4 takes) | 718 |
| **Pack total** | **≈ 9,408** |
| Proposed hard cap, including a little slack for minimum-charge rounding on short SFX | **10,500** (≈ 8% of the 131,000 monthly allowance) |

Cheaper options if wanted: music at 1 take each (−4,125), or `MUS_OUTPOST` at 60 s (−825/take).
