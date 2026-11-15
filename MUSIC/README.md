# MUSIC: RED FRONTIER audio production

Production workspace for the game's soundtrack, sound effects and ambience. Unity gets only the
shipping copies, under `Assets/Audio/Resources/Audio/`. Originals, edits, prompts and records
stay here. `MUSIC/` is outside `Assets/`, so Unity never imports it.

Target game: the real-time outpost builder (`MainMenu.unity` → `MarsOutpost.unity`). This is not
the Developer 1 turn-based prototype.

## Start here

| File | What it is |
|---|---|
| `AUDIO_PLAN.md` | Every implemented interaction → sound ID, trigger, behaviour; music controller rules; architecture |
| `LOG.md` | Decision and progress log, including credits and the handoff state. Append-only. |
| `manifests/asset_register.json` | **The asset record**: one entry per shipped cue with description, purpose, trigger, playback, loop, variations, model, prompt, flow/node/generation IDs, edits, source file, shipping path, volume, cooldown, voices, status, listening note, licence. Also lists every take that was not selected. Rebuilt by `scripts/build_register.py`. |
| `manifests/selection.json` | Which audition take became which shipping file, and the edits applied |
| `manifests/processing_log.json` | The exact edits per file, with measured duration, loudness and true peak |
| `prompts/production_v2.md` | Every prompt actually generated, with node IDs. `audition_pack_v1.md` was the earlier proposal and was never generated. |
| `evidence/` | Account and estimate snapshot, usage-rights check, verification evidence |

## Folders

| Path | Contents | Git |
|---|---|---|
| `auditions/` | Untouched downloads: `<ID>__take<N>__<generation_id>.mp3`. Never edited. | ignored (local) |
| `working/` | Temporary renders, analysis output, Unity logs | ignored |
| `final/music`, `final/sfx`, `final/ambience` | Rendered masters (`<ID>_NN.ogg/.wav`) | ignored. The tracked binaries are the identical shipping copies in `Assets/Audio`. |
| `tools/` | ffmpeg static build (BtbN GitHub release, GPL), production only, never shipped | ignored |
| `scripts/` | `fetch.py` download and probe · `analyze.py` loudness, peaks, spectrum, onsets, key/tempo, seams · `sections.py` dropouts and per-segment key · `similarity.py` rework detection · `process.py` render masters (`--export` copies to Unity) · `build_register.py` asset register | tracked |

Originals in `auditions/` and the masters in `final/` exist only on the production machine.
Back them up outside git if they need to survive the machine.

## Status vocabulary

Each shipped cue records the statuses it has actually reached:

- `generated`: downloaded from ElevenLabs into `auditions/`.
- `technically checked`: decoded and measured (loudness, true peak, silence, spectrum,
  dropouts, loop seams). This is not a listening test.
- `agent-selected`: chosen and edited by the agent from technical evidence.
- `user-approved`: **only** after the user has listened and approved. Nothing has this status yet.

The agent had no way to listen to audio. No file has been "heard" by the agent, and records
never claim otherwise.

## Workflow (how this delivery was produced, and how to change a cue)

1. Plan the cue in `AUDIO_PLAN.md`. Add or adjust its row in `AudioCatalog.cs`.
2. Write the prompt into `prompts/`. Run `estimate_only` (free), then generate on the existing
   flow. Never resubmit a call to retry: poll the job.
3. `EL_BASE=… EL_EXPIRES=… python -I scripts/fetch.py ID:take:session:generation:signature …`.
   The signed URL parts are passed only on the command line and never written to files.
4. `python -I scripts/analyze.py auditions/<ID>*`. Compare takes and choose.
5. Record the choice and edits in `manifests/selection.json`. Then run
   `python -I scripts/process.py --export <ID>` and `python -I scripts/build_register.py`.
6. In Unity: menu **Red Frontier → Audio → Run Self Test**, or run the PlayMode tests.
7. Append the decision and the credits used to `LOG.md`.

## Ignore rules (`MUSIC/.gitignore`)

These are applied and narrow: `auditions/*`, `working/*`, `tools/`, masters under `final/`,
`__pycache__/`, credential and audio-tool sidecar patterns. The root `.gitignore` and Plastic
`ignore.conf` are unchanged. No credentials are used or stored. ElevenLabs is reached through
MCP OAuth.
