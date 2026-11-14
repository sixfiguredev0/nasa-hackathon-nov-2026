# MUSIC decision and progress log

Append-only. Newest entry at the bottom. Format: `## YYYY-MM-DD: title`, then bullets.
Tags: **DECISION**, **DONE**, **OPEN**, **BLOCKER**.

## 2026-11-15: Setup

- **DONE** Verified: repo `main1`, base branch `feat/red-frontier-outpost` @ `a5b9d7f`
  (clean). Created task branch `audio/music-production-setup`. Nothing committed or pushed.
- **DONE** Repo has no CLAUDE.md or AGENTS.md. Relevant docs are
  `Assets/RedFrontierOutpost/Setup.md` and `Docs/MARS_ENVIRONMENT.md`. Ignore rules:
  root `.gitignore` and Plastic `ignore.conf`. Both left unchanged.
- **DONE** ElevenLabs MCP connected. Music (`eleven_music_v1/v2/v2_5`) and SFX
  (`eleven_text_to_sound_v2`) available. Account: creator tier, 0 / 131,000 credits used,
  resets 2026-12-14. See `evidence/2026-11-15_account_and_estimates.md`.
- **DONE** Inspected the real-time outpost builder (MainMenu → MarsOutpost). No existing
  audio code. Action map in `AUDIO_PLAN.md`.
- **DECISION** `MUSIC/` sits at the repo root, outside `Assets/`, so Unity does not import
  working files. Approved playback copies go to `Assets/Audio/` later.
- **DECISION** Keep originals exactly as downloaded in `auditions/`. Edits happen only on
  copies in `working/`.
- **DECISION** Proposed `MUSIC/.gitignore` (in README) is not applied until the user
  approves it.
- **OPEN** Audition pack v1 (`prompts/audition_pack_v1.md`, about 9,400 credits, cap
  10,500) is waiting for user approval. **Do not generate before approval.**
- **OPEN** ffmpeg/sox are not installed. Trims, fades and loop points need a tool
  (Audacity, or ffmpeg installed on approval) or have to be done in Unity.
- **OPEN** ElevenLabs commercial-use terms for Music and SFX on the Creator tier are not
  yet verified.

## 2026-11-15: Overnight production run (feat/music)

- **DECISION** User authorised autonomous production on a brand-new branch `feat/music`,
  task budget 120,000 credits total, keeping a reserve of at least 11,000. Agent selects
  assets; statuses are generated / technically checked / agent-selected / user-approved.
- **DONE** `git fetch`: `origin/feat/red-frontier-outpost` is still `a5b9d7f`, so there
  are no teammate updates. Created `feat/music` from it. No remote `feat/music` existed.
  Unity was not running. Unity 2022.3.18f1 has WebGL support installed.
- **DONE** Production tools: ffmpeg static build (BtbN GitHub release, GPL) in
  `MUSIC/tools/` (ignored, production-only). `scripts/fetch.py` and `scripts/analyze.py`
  added. `MUSIC/.gitignore` added (auditions, working, tools and final masters stay local).
- **DONE** First paid check: `SFX_UI_CONFIRM` × 4 on flow `3RwACgrCocQq0VVYBAEM`, node
  `ccqJy4XFsK8ka9fMRj5J`. Actual cost 2 credits per take (8 total). The estimate said 26.4,
  so the actual SFX rate is about 3.3 credits/s against an estimated 11. Downloaded,
  non-empty, decoded by ffmpeg. **Delivered format: MP3, 128 kbps, 44.1 kHz stereo.**
  The connection offers no lossless option. Usage after: 8 / 131,000.
- **DECISION** MP3 originals are preserved. Edited SFX ship as 16-bit WAV (no second lossy
  pass in the repo). Edited music ships as Ogg Vorbis q8 (near-transparent over 128k MP3,
  keeps the repo small). Unity transcodes per platform: Vorbis on desktop, AAC on WebGL.
- **DECISION** No listening capability is exposed: the flow LLM node takes text input
  only. Selection relies on technical checks and is disclosed as such.
