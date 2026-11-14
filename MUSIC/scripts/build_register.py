"""Build manifests/asset_register.json from selection.json, processing_log.json, the C# catalogue and auditions/.

usage: python -I build_register.py
Statuses are factual: every shipped file is generated + technically checked + agent-selected.
Nothing is marked user-approved; that needs the user's own audition.
"""
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent
CATALOG = REPO / "Assets" / "Scripts" / "Audio" / "AudioCatalog.cs"

FLOW_SFX, FLOW_MUSIC = "3RwACgrCocQq0VVYBAEM", "izO4t6ilZxMzlux1XlEy"

# What each cue is and where it plays (mirrors AUDIO_PLAN.md).
META = {
    "MUS_TITLE": ("Welcoming suite movement: organ swell, felt-piano theme, strings rise and settle (D major)", "Title screen", "MainMenu.Awake -> GameAudio.EnterTitle", "Plays to its natural end, 6-12 s of silence, repeats; 2 s crossfade from any other music"),
    "MUS_ARRIVAL": ("Short rising organ/string swell resolving on D", "Launch -> landing", "MainMenu.Launch -> GameAudio.Launch", "Starts with the fly-to-Mars transition and carries across the scene load; gameplay music waits for it to finish"),
    "MUS_SURFACE_A": ("'First Light': sparse felt piano over organ drone, arpeggio emerges (G/D)", "Outpost gameplay rotation", "MusicSelector after a rest", "Natural start and end; never within 2 picks of itself; 8-20 s rests (30%: 25-50 s)"),
    "MUS_SURFACE_B": ("'Red Horizon': warmer low-string melody over shimmering pads (D/A)", "Outpost gameplay rotation", "MusicSelector after a rest", "As MUS_SURFACE_A"),
    "MUS_SURFACE_C": ("'Quiet Machines': minimal analog textures, drifting arpeggio, long breaths (B minor/D)", "Outpost gameplay rotation", "MusicSelector after a rest", "As MUS_SURFACE_A"),
    "MUS_SURFACE_D": ("'Long Day': organ chorale answered by felt piano, gentle string lift", "Outpost gameplay rotation", "MusicSelector after a rest", "As MUS_SURFACE_A"),
    "MUS_SURFACE_E": ("'Solitude': lonely solo felt piano, distant string harmonics, warm turn at the end (E minor)", "Outpost gameplay rotation", "MusicSelector after a rest", "As MUS_SURFACE_A"),
    "MUS_TENSION": ("'Thin Air': low organ drone, wavering strings, muted piano, never climaxes (D)", "Sustained life-support shortage", "Colony economy lifeSupportShort for >= 6 s", "4 s crossfade in; repeats after 3-6 s while short; leaves after 12 s recovered with a 6 s fade; 45 s lockout"),
    "SFX_UI_CONFIRM": ("Soft glassy felt-piano chime", "Any menu button without its own cue", "PixelButton click/submit (GameAudio.UiPress)", "One-shot, suppressed when the action plays a specific cue in the same frame"),
    "SFX_UI_BACK": ("Soft descending two-note blip", "Close/back/cancel", "MainMenu Close*, pause Esc-back, BuilderMode cancel, UpgradeBubbles Esc", "One-shot"),
    "SFX_UI_SELECT": ("Tiny muted tap", "Selections and slider steps", "Builder slot pick, bubble open/toggle, PixelSlider value step", "One-shot, 70 ms cooldown stops drag spam"),
    "SFX_UI_PANEL_OPEN": ("Airy upward swish", "Panels opening", "Options, crew pick, Builder menu, pause, Marlonis dialogue", "One-shot"),
    "SFX_UI_PANEL_CLOSE": ("Airy downward swish", "Panels closing", "Builder menu close, resume, Marlonis dialogue close", "One-shot"),
    "SFX_UI_DENIED": ("Soft low two-tone blip", "Unavailable action", "Placement refused, unaffordable/locked upgrade bubble", "One-shot, 300 ms cooldown; the red on-screen reason stays visible"),
    "SFX_LAUNCH_WHOOSH": ("Rising airy whoosh with shimmer", "Start -> 1.45 s fly-to-Mars", "GameAudio.Launch when Reduce Motion is off", "One-shot; skipped with Reduce Motion"),
    "SFX_ROCKET_DESCENT": ("Muffled retro-thruster roar easing down, cut to 1.9 s", "Rocket descent over the Landing Pad", "Building.LandFromAbove start (motion path only)", "One-shot aligned to the 1.8 s quadratic ease-out"),
    "SFX_ROCKET_TOUCHDOWN": ("Deep metal thud then venting hiss", "Rocket touchdown", "Building.LandFromAbove end (both motion and Reduce Motion paths)", "One-shot"),
    "SFX_HATCH": ("Pneumatic hiss and latch", "Crew leaving/entering the rocket", "Crew.Exit / Crew.Enter at the door fade", "One-shot, 1 s cooldown = one hatch per trip"),
    "SFX_BUILD_PLACE": ("Soft metal frame clunk with servo whir", "Blueprint placed (paid)", "BuilderMode.Place when Colony.TryPlace succeeds", "One-shot, 3 variations, slight pitch variation, 80 ms cooldown for Shift-placing"),
    "SFX_CONSTRUCT_LOOP": ("Muffled rhythmic tapping and driver", "Crew building", "Crew.Work build phase", "Crossfaded loop owned by Crew; stops at completion, pauses with the pause menu"),
    "SFX_BUILD_COMPLETE": ("Two warm rising chimes with seal hiss", "Construction or upgrade finished", "Crew.Work after job.FinishBuild", "One-shot; never on scene setup or refresh"),
    "SFX_UPGRADE_PURCHASE": ("Soft upward power-up with glassy click", "Upgrade bought", "UpgradeBubbles when TryUpgrade succeeds", "One-shot (completion cue follows when the crew finishes)"),
    "SFX_GUIDE_NOTIFY": ("Two soft rising radio beeps", "Marlonis message waiting", "MissionGuide.Start (1.5 s after the badge appears)", "One-shot"),
    "SFX_GUIDE_NEXT": ("Single soft radio blip", "Marlonis dialogue line advance", "MissionGuide.Advance", "One-shot"),
    "SFX_ALERT_SHORTAGE": ("Calm two-tone caution alarm", "Oxygen/Food/Water ran out", "OutpostAudio: lifeSupportShort false->true", "One-shot; repeats every 30 s while short; 12 s cooldown"),
    "SFX_ALERT_RECOVERED": ("Two soft ascending all-clear chimes", "Life support restored", "OutpostAudio: short->ok after >= 1.5 s short", "One-shot, 6 s cooldown"),
    "SFX_ALERT_STALL": ("Soft electrical power-down", "Building stalls (red tint)", "OutpostAudio: a building stalled for 1 s (grouped, re-arms after 3 s running)", "One-shot, 8 s cooldown, held back within 4 s of a shortage alert"),
    "SFX_ALERT_FITNESS": ("Gentle low two-note monitor chime", "Crew fitness enters the lowest gauge segment", "OutpostAudio: fitness < 0.2 (re-arms above 0.3)", "One-shot, 30 s cooldown"),
    "SFX_FOOTSTEP": ("Soft boot on dusty gravel", "Crew walking", "Crew.Walk, every second bob step", "One-shot, very quiet, 4 variations, max 2 voices"),
    "SFX_GYM_LOOP": ("Muffled suit rustle and light thumps", "Crew training in a Gym", "Crew.Work gym phase", "Crossfaded loop owned by Crew; pauses with the pause menu"),
    "AMB_MARS_WIND": ("Gusty thin wind and dust hiss (high-passed)", "Outpost surface bed", "OutpostAudio.Start -> GameAudio.EnterOutpost", "Crossfaded loop (2.5 s overlap), 3 s fade-in, ducked while paused, fades on scene exit"),
    "AMB_COLONY_HUM": ("Quiet machinery drone, fans, pump", "Outpost machinery bed", "OutpostAudio: level = (commissioned buildings - 1) / 6", "Crossfaded loop; level glides over 2 s"),
}


def catalog():
    src = CATALOG.read_text(encoding="utf-8")
    consts = dict(re.findall(r'public const string (\w+) = "(\w+)"', src))
    rows = {}
    for m in re.finditer(r"new CueDef\((Sfx\.\w+|MusicIds\.\w+|id), AudioBus\.(\w+), (\d+), (-?[\d.]+)f(?:, ([\d.]+)f)?(?:, (\d+))?(?:, (\d+))?(?:, ([\d.]+)f)?\)", src):
        ref = m.group(1)
        row = {"bus": m.group(2), "variations": int(m.group(3)), "volume_db": float(m.group(4)),
               "cooldown_s": float(m.group(5) or 0.05), "max_voices": int(m.group(6) or 2), "priority": int(m.group(7) or 50),
               "pitch_jitter": float(m.group(8) or 0)}
        if ref == "id":
            for g in ["MUS_SURFACE_A", "MUS_SURFACE_B", "MUS_SURFACE_C", "MUS_SURFACE_D", "MUS_SURFACE_E"]:
                rows[g] = dict(row)
        else:
            rows[consts[ref.split(".")[1]]] = row
    return rows


def prompts():
    table = {}
    for line in (ROOT / "prompts" / "production_v2.md").read_text(encoding="utf-8").splitlines():
        m = re.match(r"\| (\w+)(?: \"[^\"]+\")? \(`(\w+)`\) \| ([^|]+) \|(?: [^|]+ \|)? (\d+) \| (.+) \|$", line)
        if m:
            table[m.group(1)] = {"node": m.group(2), "prompt": m.group(5).strip()}
    return table


def main():
    sel = json.loads((ROOT / "manifests" / "selection.json").read_text())
    log = json.loads((ROOT / "manifests" / "processing_log.json").read_text())
    cat, pr = catalog(), prompts()
    used = set()
    assets = []
    for a in sel["assets"]:
        desc, where, trigger, playback = META[a["id"]]
        c = cat.get(a["id"], {})
        music = a["kind"] == "music"
        files = []
        for take, r in zip(a["takes"], log[a["id"]]):
            used.add(take)
            files.append({
                "final_master": "MUSIC/" + r["file"], "shipping_copy": r.get("export"), "source": "MUSIC/" + r["source"],
                "generation_id": take.split("__")[-1], "duration_s": r["duration_s"], "lufs": r["lufs"],
                "true_peak_db": r["true_peak_db"], "editing": r["edits"],
            })
        assets.append({
            "id": a["id"], "category": "music" if music else ("ambience" if a["id"].startswith("AMB_") else "sfx"),
            "description": desc, "game_action": where, "trigger": trigger, "playback": playback,
            "loop": a["kind"] == "loop", "variations": len(files),
            "model": "eleven_music_v2_5" if music else "eleven_text_to_sound_v2",
            "flow_id": FLOW_MUSIC if music else FLOW_SFX, "node_id": pr.get(a["id"], {}).get("node"),
            "prompt": pr.get(a["id"], {}).get("prompt"), "selection_note": a.get("note"),
            "volume_db": c.get("volume_db"), "cooldown_s": c.get("cooldown_s"), "max_voices": c.get("max_voices"),
            "priority": c.get("priority"), "pitch_jitter": c.get("pitch_jitter"), "catalog_variations": c.get("variations"),
            "status": ["generated", "technically checked", "agent-selected"],
            "user_approved": False,
            "listening": "not auditioned by ear: no listening capability was available to the agent",
            "licence": "ElevenLabs Creator (paid) output; see MUSIC/evidence/2026-11-15_usage_rights.md",
            "files": files,
        })
        if c and c.get("variations") != len(files):
            raise SystemExit(f"catalogue variations mismatch for {a['id']}: {c.get('variations')} vs {len(files)}")
    rejected = sorted(p.stem for p in (ROOT / "auditions").glob("*.mp3") if p.stem not in used)
    out = {
        "schema_version": 2,
        "generated_by": "MUSIC/scripts/build_register.py",
        "status_vocabulary": ["generated", "technically checked", "agent-selected", "user-approved"],
        "assets": assets,
        "not_selected_takes": {"status": ["generated", "technically checked", "not selected"], "files": ["MUSIC/auditions/" + s + ".mp3" for s in rejected]},
    }
    (ROOT / "manifests" / "asset_register.json").write_text(json.dumps(out, indent=1))
    print(f"{len(assets)} assets, {sum(len(a['files']) for a in assets)} files, {len(rejected)} not selected")


if __name__ == "__main__":
    main()
