# Account and estimate snapshot: 2026-11-15

Source: ElevenLabs MCP (`account_get_usage`, `creative_get_flow_node_types`,
`estimate_only` runs). Nothing was generated and nothing was charged.

## Account (`account_get_usage`)

| Field | Value |
|---|---|
| Tier / status | `creator` / `active`, monthly billing |
| Credits used / limit | 0 / 131,000 |
| Next reset | 1797284223 = 2026-12-14 21:37 UTC |
| Current overage | 0 USD |
| Voice slots | 0 / 30 (professional 0 / 1) |

Not exposed by the tools: workspace name or ID, whether overage or auto top-up is
enabled for creative generation, and download or export formats. Formats will be
confirmed from the first real download.

## Models available in this workspace (`creative_get_flow_node_types`)

- Music: `eleven_music_v1`, `eleven_music_v2`, `eleven_music_v2_5` (default). Params:
  `duration_seconds` 3–600, `lyrics_type`, `instrumental`, `lyrics`. Optional audio input
  port.
- SFX: `eleven_text_to_sound_v2`. Params: `duration_seconds` 0.5–30, `prompt_influence`
  0–1 (default 0.3), `loop` (bool).
- Also present but not planned: TTS (`eleven_v4` …), Audio Effects (free filters and
  reverbs), Voice Isolator, Video-to-Music, Speech-to-Text.

## Free estimates (1 take each)

| Node | Settings | Credits | USD shown |
|---|---|---|---|
| SFX | auto duration | 55 | 0.01 |
| SFX | 2 s | 22 | 0.004 |
| SFX | 20 s, loop | 220 | 0.04 |
| Music v2.5 | default (≈ 60 s) | 1,650 | 0.30 |
| Music v2.5 | 60 s instrumental | 1,650 | 0.30 |
| Music v2.5 | 90 s instrumental | 2,475 | 0.45 |

→ SFX ≈ 11 credits/s, music ≈ 27.5 credits/s, linear in duration and take count.

The estimate calls left two empty draft flows in the ElevenLabs workspace (no
generations): `3RwACgrCocQq0VVYBAEM` (SFX) and `izO4t6ilZxMzlux1XlEy` (music). They can
be reused for the pack or deleted in the ElevenLabs UI.

## Usage and licence record

The account is on the paid Creator tier. Check ElevenLabs' current terms for commercial
use of Music and SFX output before release, and record the terms version or date here
when the first asset is approved. This has not been verified yet.

Update: usage rights were checked on 2026-11-15. See `2026-11-15_usage_rights.md`.
