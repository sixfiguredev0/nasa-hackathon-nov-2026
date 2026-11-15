# Production prompts v2: the overnight run (2026-11-15)

Supersedes `audition_pack_v1.md`, which was proposed and never generated. Every prompt below was
actually generated. Flows in the ElevenLabs workspace:

- SFX and ambience: flow `3RwACgrCocQq0VVYBAEM`
- Music: flow `izO4t6ilZxMzlux1XlEy`

Models: `eleven_music_v2_5` (instrumental, `lyrics_type: instrumental`) and `eleven_text_to_sound_v2`
(`prompt_influence: 0.5` unless noted). No artist names, song titles or reference recordings
were used.

## Music: one suite with a shared identity

Each movement repeats the same identity clause: D-centred harmony, around 70 BPM, warm organ-like
pads, felt piano, restrained strings, airy analog textures and slowly evolving arpeggios, with no
drums, vocals, harsh highs or deep sub-bass.

| ID (node) | Duration | Takes | Prompt |
|---|---|---|---|
| MUS_TITLE (`hW5tVwYW7pjY8u9GBqTa`) | 100 s | 2 | Instrumental cinematic ambient chamber piece in D major, slow 70 BPM, calm cosmic wonder, intimate and hopeful. Warm sustained organ-like pads, soft felt piano, restrained string ensemble, airy analog synth textures and a delicate slowly evolving arpeggio. Opens with a gentle organ swell and a simple felt-piano melody, strings enter warmly in the middle with a modest rise, then it settles back to quiet piano and pads for a calm, open ending. Spacious and soft, no drums, no vocals, no harsh highs, light low end. |
| MUS_SURFACE_A_REFTEST (`02tiR67RfvtuoHuG9LZw`) | 95 s (rendered 100) | 1 | Same as MUS_SURFACE_A below, with the selected title take wired into the audio input. **Rejected**: it kept the reference's length, followed its loudness envelope (r = 0.94) and added 32% sub-60 Hz energy. |
| MUS_SURFACE_A "First Light" (`77Gl5AXy1EWa4MlwWy4j`) | 95 s | 2 | Instrumental cinematic ambient chamber music in D major, around 70 BPM, calm cosmic wonder. Warm sustained organ-like pads, soft felt piano, restrained strings, airy analog synth textures, delicate evolving arpeggios. Sparse and gentle: felt piano phrases with long pauses over a soft organ drone, a quiet arpeggio slowly emerges midway, the harmony drifts to G major and B minor before returning home, ending softly on a sustained D chord. Spacious, no drums, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |
| MUS_SURFACE_B "Red Horizon" (`KvcrXC1rB1baV4Vq4TP4`) | 100 s | 2 | Instrumental cinematic ambient chamber music in D major, around 70 BPM, calm cosmic wonder. Warm sustained organ-like pads, soft felt piano, restrained strings, airy analog synth textures, delicate evolving arpeggios. Warmer and fuller: low strings carry a slow singing melody over shimmering analog pads and a delicate felt-piano arpeggio, a modest swell in the middle, then a calm descent to a quiet ending. Spacious, no drums, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |
| MUS_SURFACE_C "Quiet Machines" (`6a044R3LLHqMiQAFeq8w`) | 90 s | 2 | Instrumental ambient chamber music in D major, around 70 BPM, calm and weightless. Airy analog synth textures and soft organ tones lead; a slow pulsing synth arpeggio drifts in and out, felt piano appears only as occasional single notes, long breathing spaces with no melody for stretches, gently fading to near silence at the end. Minimal and spacious, no drums, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |
| MUS_SURFACE_D "Long Day" (`XnrYkDxSruWvokeO4k5P`) | 100 s | 2 | Instrumental cinematic ambient chamber music in D major, around 70 BPM, hopeful and tender. A warm organ chorale leads, felt piano answers each phrase, restrained strings rise gently into a brief brighter passage in A major, then settle back to D for an unhurried soft ending, with a delicate analog arpeggio shimmering underneath. No drums, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |
| MUS_SURFACE_E "Solitude" (`9JLsxM4cyofOGe3zh0aO`) | 90 s | 2 | Instrumental cinematic ambient chamber music in B minor, around 66 BPM, lonely and reflective. Solo felt piano with sparse, intimate phrases and long decays, distant soft string harmonics and a faint warm organ pad, an airy analog shimmer; near the end the harmony turns warmly to D major before a very quiet close. No drums, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |
| MUS_TENSION "Thin Air" (`udSqFv046ANkWzFMGTtc`) | 80 s | 2 | Instrumental restrained tension ambient cue in D minor, around 70 BPM, uneasy but calm. Low sustained organ drone, slow wavering string swells, sparse muted felt-piano notes and a soft slowly pulsing analog synth, a gradual quiet build that never climaxes, ending soft and unresolved. No drums, no ticking, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |
| MUS_ARRIVAL (`QGULnObm8AorjMD5x3jk`) | 24 s | 2 | Short instrumental arrival cue in D major, around 70 BPM, wondrous and gentle. A soft rising swell of warm organ and restrained strings with shimmering analog textures and a few felt-piano notes, cresting softly and resolving on a sustained D major chord that fades out. No drums, no vocals, no harsh highs; bass kept light and soft with no deep sub-bass. |

## SFX and ambience

| ID (node) | Duration | Loop | Takes | Prompt |
|---|---|---|---|---|
| SFX_UI_CONFIRM (`ccqJy4XFsK8ka9fMRj5J`) | 0.6 | no | 4 | Soft warm interface confirm tone, one gentle glassy chime with a felt-piano attack, short clean decay, close and dry |
| SFX_UI_BACK (`zHdaHpDbLkBTxItrBZ4J`) | 0.6 | no | 4 | Soft warm interface back tone, gentle descending two-note glassy blip with a felt attack, short, close and dry |
| SFX_UI_SELECT (`xt3T1fqPZG0hwcrZN7Ny`) | 0.5 | no | 4 | Tiny soft interface tick, muted wooden glass tap, very short, close and dry |
| SFX_UI_PANEL_OPEN (`VjfoEpJpu5DfpfeR0ejZ`) | 0.9 | no | 4 | Soft airy interface panel opening, gentle upward breathy swish with a faint warm chime, short and dry |
| SFX_UI_PANEL_CLOSE (`avydSuWgIY2EVeYxXwUE`) | 0.8 | no | 4 | Soft airy interface panel closing, gentle downward breathy swish with a muted low tone, short and dry |
| SFX_UI_DENIED (`z8gVJhrMQIoFimlpn1Yl`) | 0.6 | no | 4 | Soft low muted two-tone error blip from a computer console, gentle and dull, not harsh, short and dry |
| SFX_LAUNCH_WHOOSH (`HfCH1U4FPCiMItIl0Z3h`) | 1.6 | no | 4 | Smooth rising airy whoosh with a soft shimmering synth swell, gentle and spacious, fading out at the end |
| SFX_ROCKET_DESCENT (`yjcjXFw7vfZL5EJ44iR0`) | 2.2 | no | 4 | Rocket retro thrusters firing during a slow descent, muffled deep rumbling roar that steadily eases down, heard through thin distant air |
| SFX_ROCKET_TOUCHDOWN (`9deHJU9qSZnvIOcUs6bh`) | 3.0 | no | 4 | Heavy spacecraft landing legs touching down on gravel, deep muffled metal thud, then pressurised steam venting hiss fading out |
| SFX_HATCH (`8hXg7aXI8aZEcT7eiddq`) | 1.2 | no | 4 | Spacecraft airlock hatch opening, soft pneumatic hiss with a light metallic latch clunk, muffled |
| SFX_BUILD_PLACE (`rpLLV2iTNbYW3WOWM7UH`) | 0.8 | no | 4 | Light metal frame set down on gravel, soft solid clunk with a short servo whir, muffled and dry |
| SFX_CONSTRUCT_LOOP (`5hR8UOuMul4Qc8TreLDJ`) | 4.0 | yes | 4 | Gentle construction work, soft rhythmic tapping on metal panels and a quiet electric screwdriver, muffled, steady, no voices |
| SFX_BUILD_COMPLETE (`JXAO1K5RLDlHIfaA1sIY`) | 1.6 | no | 4 | Warm satisfying completion chime, two soft rising bell-like felt piano notes with a gentle pneumatic seal hiss |
| SFX_UPGRADE_PURCHASE (`KzDzl4GL3Vwg30MkmECm`) | 1.0 | no | 4 | Soft upward synth power-up sweep ending in a gentle glassy click, short, warm and positive |
| SFX_GUIDE_NOTIFY (`OXbrEkQRKsO3j9ryCnEV`) | 1.0 | no | 4 | Gentle radio comms chirp, two soft rising beeps with a faint warm static crackle, short |
| SFX_GUIDE_NEXT (`Dfs1ejdxaszEf0Yi7DQE`) | 0.5 | no | 4 | Very short soft radio blip, a single quiet beep with faint static, close |
| SFX_ALERT_SHORTAGE (`KdKBAUa1jX8Uk7REYyKs`) | 2.0 | no | 4 | Calm spacecraft caution alarm, two soft descending mid-pitched tones repeated twice, clear but not piercing |
| SFX_ALERT_RECOVERED (`O0VmBSVoeUQI1H3UqS1N`) | 1.4 | no | 4 | Calm all-clear tone, two soft ascending warm chime notes, reassuring, short |
| SFX_ALERT_STALL (`4lMJpWsdun0z7CdawNvl`) | 1.2 | no | 4 | Machine powering down, soft descending electrical hum with a muted click, short |
| SFX_ALERT_FITNESS (`qHsVYvEy9lJ0GEAeFlwH`) | 1.4 | no | 4 | Gentle low two-note medical monitor chime, soft and warm, slightly concerned, short |
| SFX_FOOTSTEP (`ocReQAmpsTXSsOKfKArc`) | 0.5 | no | 4 | Single soft footstep of a heavy boot on fine dusty gravel, muffled, close |
| SFX_GYM_LOOP (`3dGAgbX0lM626rRr6TSa`) | 4.0 | yes | 3 | Soft rhythmic exercise in a spacesuit, gentle fabric rustle and light boot thumps on dirt in a steady slow rhythm, muffled, no voices |
| AMB_MARS_WIND (`mzMg0T2EjjhyWjV42agf`) | 30 | yes | 3 | Thin dry desert wind over open rocky ground, soft low steady rumble with gentle airy gusts and fine dust hiss, distant and calm, no birds, no insects, no voices |
| Wind attempt 2 (same node, `prompt_influence 0.6`) | 30 | yes | 3 | Soft airy wind breathing across dusty rocky plains, gentle whooshing gusts and fine blowing sand hiss in the mid and high range, calm and distant, no low rumble, no birds, no insects, no voices. **Not used**: still about 97% of energy below 60 Hz. The shipped bed is attempt 1, take 2, high-passed. |
| AMB_COLONY_HUM (`tAB5rAWzC0G86Fnl5Rz3`) | 20 | yes | 3 | Quiet steady colony machinery hum, soft electrical drone with distant ventilation fans and faint pump cycling, muffled and calm |
