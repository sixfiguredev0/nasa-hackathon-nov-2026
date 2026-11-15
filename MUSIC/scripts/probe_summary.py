"""Map webgl_probe.mjs audio events to asset IDs by clip duration (technical observation only).

usage: node webgl_probe.mjs <port> log <since> | python -I probe_summary.py
Buffer starts (decompressed SFX) carry about one AAC frame (~0.023 s) of padding; media elements (music,
ambience) report the clip length. Unity WebGL starts a duplicate zero-length node per one-shot; duplicates
within 20 ms are folded. Several cues share a length, so a match lists every candidate.
"""
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
log = json.loads((ROOT / "manifests" / "processing_log.json").read_text())
lengths = [(r["duration_s"], pathlib.Path(r["file"]).stem) for rows in log.values() for r in rows]


def name(d, media):
    hits = [s for L, s in lengths if (abs(L - d) <= 0.05 if media else -0.005 <= d - L <= 0.03)]
    return "/".join(sorted(set(hits))) or f"?{d}"


events = json.loads(sys.stdin.read() or "[]")
last = {}
for e in events:
    k = e["k"]
    if k == "start":
        key = (e["dur"], round(e["t"], 1))
        if key in last:
            continue
        last[key] = 1
        print(f"{e['t']:8.2f}  sfx    {name(e['dur'], False)}")
    elif k in ("media-play", "media-pause", "media-end"):
        if k == "media-pause" and last.get(("p", e["dur"], round(e["t"], 1))):
            continue
        last[("p", e["dur"], round(e["t"], 1))] = 1
        extra = f" at {e.get('at')}s" if k != "media-end" else ""
        print(f"{e['t']:8.2f}  {k:6} {name(e['dur'], True)}{extra}")
    elif k in ("error", "warn", "pageerror", "media-play-rejected"):
        print(f"{e['t']:8.2f}  {k:6} {e.get('msg', '')[:140]}")
    elif k == "ctx":
        print(f"{e['t']:8.2f}  ctx    {e['state']}")
