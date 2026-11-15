"""Render final masters from untouched auditions, per manifests/selection.json.

usage: python -I process.py [--export] [ID ...]
  Writes MUSIC/final/<music|sfx|ambience>/<ID>_NN.(ogg|wav) and manifests/processing_log.json.
  --export also copies each master to Assets/Audio/Resources/Audio/<Music|Sfx|Ambience>/.
Originals in auditions/ are only read. Intermediate float files go to working/.
"""
import json
import pathlib
import re
import shutil
import subprocess
import sys

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO = ROOT.parent
FF = str(ROOT / "tools" / "ffmpeg" / "bin" / "ffmpeg.exe")
SEL = ROOT / "manifests" / "selection.json"
LOG = ROOT / "manifests" / "processing_log.json"


def run(args):
    p = subprocess.run([FF, "-hide_banner", "-nostats", "-y", *args], capture_output=True, text=True)
    if p.returncode:
        raise RuntimeError(p.stderr[-800:])
    return p.stderr


def load(path, sr):
    raw = subprocess.run([FF, "-v", "error", "-i", str(path), "-ar", str(sr), "-ac", "2", "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def save_wav(x, sr, out):
    pcm = np.clip(x, -1, 1)
    tmp = ROOT / "working" / (out.stem + ".f32")
    tmp.write_bytes(pcm.astype(np.float32).tobytes())
    run(["-f", "f32le", "-ar", str(sr), "-ac", "2", "-i", str(tmp), "-c:a", "pcm_s16le", str(out)])
    tmp.unlink()


def loudness(path):
    err = run(["-i", str(path), "-af", "ebur128=peak=true", "-f", "null", "-"])
    s = err[err.rfind("Summary:"):]
    I = float(re.search(r"I:\s+(-?[\d.]+|-inf)", s).group(1).replace("-inf", "-70"))
    tp = float(re.search(r"Peak:\s+(-?[\d.]+|-inf)", s).group(1).replace("-inf", "-70"))
    S = [float(v) for v in re.findall(r" S:\s*(-?[\d.]+)", err)]
    return I, tp, max(S) if S else I


def db(v):
    return 20 * np.log10(max(float(v), 1e-9))


def trim(x, sr, lead_db, keep_lead, tail_db, keep_tail):
    env = np.abs(x).max(1)
    loud = np.nonzero(env > 10 ** (lead_db / 20))[0]
    if not len(loud):
        return x, 0.0, 0.0
    start = max(0, loud[0] - int(keep_lead * sr))
    tail = np.nonzero(env > 10 ** (tail_db / 20))[0]
    end = min(len(x), tail[-1] + int(keep_tail * sr))
    return x[start:end], start / sr, (len(x) - end) / sr


def fades(x, sr, fin, fout):
    n = len(x)
    a, b = min(n, int(fin * sr)), min(n, int(fout * sr))
    if a:
        x[:a] *= np.linspace(0, 1, a)[:, None]
    if b:
        x[-b:] *= (np.cos(np.linspace(0, np.pi / 2, b)) ** 2)[:, None]
    return x


def filters(asset):
    f = []
    if asset.get("highpass"):
        f.append(f"highpass=f={asset['highpass']}:poles=2")
    if asset.get("lowpass"):
        f.append(f"lowpass=f={asset['lowpass']}:poles=2")
    return f


def prefilter(src, asset, sr, kind):
    """Decode + filter into float32 at the target rate (one decode of the MP3, no re-encode)."""
    f = filters(asset)
    if kind == "music":
        f = [f"highpass=f={asset.get('highpass', 35)}:poles=2"] + [v for v in f if not v.startswith("highpass")]
    tmp = ROOT / "working" / (src.stem + ".pre.f32")
    args = ["-i", str(src)] + (["-af", ",".join(f)] if f else []) + ["-ar", str(sr), "-ac", "2", "-f", "f32le", str(tmp)]
    run(args)
    x = np.frombuffer(tmp.read_bytes(), dtype=np.float32).reshape(-1, 2).copy()
    tmp.unlink()
    return x, f


def render(asset, index, take):
    kind = asset["kind"]
    src = ROOT / "auditions" / (take + ".mp3")
    if not src.exists():
        raise FileNotFoundError(src.name)
    sid = f"{asset['id']}_{index + 1:02}"
    steps = []
    if kind == "music":
        sr = 48000
        x, f = prefilter(src, asset, sr, kind)
        steps += f
        x, cut_lead, cut_tail = trim(x, sr, -50, 0.05, -55, 0.6)
        steps.append(f"trim lead {cut_lead:.2f}s tail {cut_tail:.2f}s")
        x = fades(x, sr, 0.03, 1.5)
        steps.append("fade in 30 ms, fade out 1.5 s")
        tmp = ROOT / "working" / f"{sid}.wav"
        save_wav(x, sr, tmp)
        I, tp, smax = loudness(tmp)
        gain = min(-18 - I, -14 - smax)
        gain = min(gain, -1.5 - tp)
        out = ROOT / "final" / "music" / f"{sid}.ogg"
        run(["-i", str(tmp), "-af", f"volume={gain:.2f}dB", "-c:a", "libvorbis", "-q:a", "8", str(out)])
        steps.append(f"gain {gain:+.2f} dB (from I {I} LUFS, S max {smax}, TP {tp})")
        steps.append("encode Ogg Vorbis q8")
        tmp.unlink()
    else:
        sr = 44100
        x, f = prefilter(src, asset, sr, kind)
        steps += f
        if kind == "sfx":
            x, cut_lead, cut_tail = trim(x, sr, -50, 0.005, -60, 0.03)
            steps.append(f"trim lead {cut_lead:.3f}s tail {cut_tail:.3f}s")
            if asset.get("max_len") and len(x) > asset["max_len"] * sr:
                x = x[: int(asset["max_len"] * sr)]
                steps.append(f"cut to {asset['max_len']} s")
            fo = asset.get("fade_out", 0.04)
            x = fades(x, sr, 0.003, fo)
            steps.append(f"fade in 3 ms, fade out {fo} s")
            w = int(0.3 * sr)
            mono = x.mean(1)
            if len(mono) > w:
                csum = np.concatenate([[0], np.cumsum(mono ** 2)])
                rms = np.sqrt((csum[w:] - csum[:-w]) / w).max()
            else:
                rms = np.sqrt(np.mean(mono ** 2))
            gain = min(-16 - db(rms), -1 - db(np.abs(x).max()))
            steps.append(f"gain {gain:+.2f} dB (loudest 300 ms RMS -> -16 dBFS, peak cap -1 dBFS)")
        else:  # loop
            rms = np.sqrt(np.mean(x.mean(1) ** 2))
            target = asset.get("rms", -24)
            gain = min(target - db(rms), -1 - db(np.abs(x).max()))
            steps.append(f"gain {gain:+.2f} dB (RMS -> {target} dBFS, peak cap -1 dBFS); no trim/fade, engine crossfades")
        x = x * 10 ** (gain / 20)
        folder = "ambience" if asset["id"].startswith("AMB_") else "sfx"
        out = ROOT / "final" / folder / f"{sid}.wav"
        save_wav(x, sr, out)
        steps.append("16-bit PCM WAV 44.1 kHz")
    I, tp, smax = loudness(out)
    dur = float(subprocess.run([str(pathlib.Path(FF).with_name("ffprobe.exe")), "-v", "error", "-show_entries", "format=duration",
                                "-of", "csv=p=0", str(out)], capture_output=True, text=True).stdout)
    return {"file": out.relative_to(ROOT).as_posix(), "source": f"auditions/{take}.mp3", "edits": steps,
            "duration_s": round(dur, 3), "lufs": I, "true_peak_db": tp, "smax": smax}


def main(argv):
    export = "--export" in argv
    only = {a for a in argv if not a.startswith("--")}
    sel = json.loads(SEL.read_text())
    log = json.loads(LOG.read_text()) if LOG.exists() else {}
    for asset in sel["assets"]:
        if only and asset["id"] not in only:
            continue
        if "PENDING" in asset["takes"]:
            print(f"skip {asset['id']} (pending)")
            continue
        results = []
        for i, take in enumerate(asset["takes"]):
            r = render(asset, i, take)
            results.append(r)
            print(f"{r['file']:40} {r['duration_s']:7}s I {r['lufs']:6} TP {r['true_peak_db']:5}  <- {take[-20:]}")
            if export:
                folder = {"music": "Music", "ambience": "Ambience"}.get(pathlib.Path(r["file"]).parent.name, "Sfx")
                dest = REPO / "Assets" / "Audio" / "Resources" / "Audio" / folder
                dest.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / r["file"], dest / pathlib.Path(r["file"]).name)
                r["export"] = (dest / pathlib.Path(r["file"]).name).relative_to(REPO).as_posix()
        log[asset["id"]] = results
    LOG.write_text(json.dumps(log, indent=1))


if __name__ == "__main__":
    main(sys.argv[1:])
