# MUSIC: RED FRONTIER audio production

All audio-production working material for RED FRONTIER lives here. Unity only gets
**approved playback files**, copied into `Assets/Audio/` later. The originals and their
records stay in this folder.

Target game: the real-time outpost builder (`Assets/Scenes/MainMenu.unity` →
`Assets/Scenes/MarsOutpost.unity`, scripts in `Assets/Scripts/Outpost/`). This is not the
Developer 1 turn-based prototype.

## Folder structure

| Path | Contents | Git |
|---|---|---|
| `README.md` | This file: structure and workflow | tracked |
| `AUDIO_PLAN.md` | Game actions → required audio, hooks, priorities | tracked |
| `LOG.md` | Decision and progress log. Append-only, newest entry at the bottom | tracked |
| `prompts/` | Exact generation prompts and parameters, one file per batch | tracked |
| `auditions/` | **Untouched original downloads**, never edited or renamed after download | proposed ignore |
| `working/` | Edited intermediates (trims, fades, loop-point work) | proposed ignore |
| `final/music`, `final/sfx`, `final/ambience` | **Approved** masters only | tracked (small files) |
| `manifests/asset_register.json` | One record per audition and final asset (schema below) | tracked |
| `evidence/` | Account and usage snapshots, estimates, licence/terms notes | tracked |

`MUSIC/` is outside `Assets/`, so Unity does not import it and it gets no `.meta` files.

## Status vocabulary

Each take in `asset_register.json` has exactly one status:

- `planned`: prompt written, not generated.
- `generated`: downloaded into `auditions/`. **Nobody has listened to it yet.**
- `auditioned`: a person listened to it. The listener and their notes are recorded.
- `rejected`: auditioned and turned down. The file stays in `auditions/`. Record why.
- `approved`: the user approved it. Only then may it be copied or edited into `final/`.
- `exported`: approved and copied to `Assets/Audio/`. Record the export path.

An agent must never mark a file `auditioned` or `approved` on its own judgment. Only the
user's explicit listening feedback can set those statuses.

## Workflow

1. **Plan**: add or adjust the asset row in `AUDIO_PLAN.md`.
2. **Prompt**: write the exact prompt, model and parameters in `prompts/<batch>.md`.
3. **Estimate**: run ElevenLabs `estimate_only` (free). Record the figure in `evidence/`.
   Get the user's approval of the batch and its credit budget.
4. **Generate**: one call per asset. Never repeat a call to retry, because every call is
   charged. Snapshot `account_get_usage` before and after, saved to `evidence/`.
5. **Download**: save to `auditions/<ID>__take<N>__<generation_id>.<ext>` with the format
   kept as delivered. Add a register entry with status `generated`.
6. **Audition**: the user listens. Record the verdict per take (`auditioned` →
   `approved` / `rejected`).
7. **Edit**: work on copies in `working/` (trim, fade, loop points, normalise). Record
   every step in the register's `editing` field.
8. **Finalise**: the master goes to `final/<category>/<ID>.<ext>`. Status `approved`.
9. **Export**: when integration starts, copy to `Assets/Audio/<category>/` on an audio
   branch. Status `exported`.
10. **Log**: append a line to `LOG.md` for every decision, batch, approval or rejection.

## Naming

- Stable ID: `<CAT>_<NAME>`, upper snake case, e.g. `SFX_BUILD_PLACE`, `MUS_OUTPOST`,
  `AMB_MARS_WIND`. IDs never change after the first generation.
- Variations of one approved asset: `<ID>_01`, `<ID>_02`, …
- Audition file: `<ID>__take<N>__<generation_id>.<ext>`.
- Final file: `<ID>[_NN].<ext>`.

## Asset record schema (`manifests/asset_register.json`)

Every final asset records: ID and filename, sound description, game action or location,
trigger and playback behaviour, duration, loop status, variations, prompt, model,
generation ID, editing, source file, volume, cooldown, max simultaneous voices, review
status, and the usage and licence record. The `_template` entry in the file shows each
field.

## Proposed git exclusions (not applied)

The root `.gitignore` and Plastic `ignore.conf` stay unchanged. Proposed addition, as a
new `MUSIC/.gitignore`, applied only after approval:

```gitignore
# bulky originals and intermediates stay local (preserve them on disk or in a backup)
auditions/*
working/*
!auditions/.gitkeep
!working/.gitkeep
# audio-tool sidecars and credentials
*.reapeaks
*.pkf
*.asd
.env
*.key
```

Originals in `auditions/` are preserved on disk and are **not** deleted when ignored.
Keep a backup outside git, because ignored files have no version history. Never write
API keys or credentials anywhere in `MUSIC/`. The ElevenLabs connection is through MCP
OAuth, so no key is needed in the repo.
