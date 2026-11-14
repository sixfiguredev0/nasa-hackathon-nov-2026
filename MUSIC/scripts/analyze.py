"""Technical audio report (no subjective listening). usage: python -I analyze.py FILE [FILE ...] [--json OUT]

Per file: duration, integrated loudness (LUFS), loudness range, true peak, sample peak,
leading/trailing silence (< -50 dBFS), spectral balance (<60 Hz sub, >8 kHz air), centroid,
onset rate (percussiveness proxy), largest 400 ms loudness jump (sudden peaks),
estimated key (Krumhansl chroma) and tempo (onset autocorrelation), and loop-seam mismatch.
"""
import json
import pathlib
import re
import subprocess
import sys

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent.parent
FFMPEG = str(ROOT / "tools" / "ffmpeg" / "bin" / "ffmpeg.exe")
SR = 22050
NOTES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
MAJOR = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MINOR = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def decode(path, sr=SR):
    raw = subprocess.run([FFMPEG, "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(sr), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def ebur128(path):
    err = subprocess.run([FFMPEG, "-hide_banner", "-nostats", "-i", str(path), "-af", "ebur128=peak=true+sample",
                          "-f", "null", "-"], capture_output=True, text=True).stderr
    summary = err[err.rfind("Summary:"):]
    get = lambda key: float(re.search(key + r":\s+(-?[\d.]+|-inf)", summary).group(1).replace("-inf", "-120"))
    return {"lufs": get("I"), "lra": get("LRA"), "true_peak_db": get("Peak"),
            "sample_peak_db": float(re.findall(r"Peak:\s+(-?[\d.]+|-inf)", summary)[-1].replace("-inf", "-120"))}


def db(x):
    return 20 * np.log10(max(float(x), 1e-9))


def report(path):
    x = decode(path)
    r = {"file": pathlib.Path(path).name, "duration_s": round(len(x) / SR, 3)}
    r.update(ebur128(path))
    thresh = 10 ** (-50 / 20)
    loud = np.nonzero(np.abs(x) > thresh)[0]
    r["lead_silence_s"] = round(loud[0] / SR, 3) if len(loud) else r["duration_s"]
    r["tail_silence_s"] = round((len(x) - 1 - loud[-1]) / SR, 3) if len(loud) else r["duration_s"]

    n = 2048
    frames = np.lib.stride_tricks.sliding_window_view(x, n)[::n // 2] if len(x) > n else x[None, :n]
    spec = np.abs(np.fft.rfft(frames * np.hanning(frames.shape[1]), axis=1)) ** 2
    freqs = np.fft.rfftfreq(frames.shape[1], 1 / SR)
    total = spec.sum() + 1e-12
    r["sub60_pct"] = round(100 * spec[:, freqs < 60].sum() / total, 2)
    r["air8k_pct"] = round(100 * spec[:, freqs > 8000].sum() / total, 2)
    r["centroid_hz"] = round(float((spec.sum(0) * freqs).sum() / total), 0)

    flux = np.maximum(np.diff(np.log1p(spec), axis=0), 0).sum(1)
    if len(flux) > 8:
        thr = np.median(flux) + 3 * np.std(flux)
        peaks = np.nonzero((flux[1:-1] > thr) & (flux[1:-1] > flux[:-2]) & (flux[1:-1] >= flux[2:]))[0]
        r["onsets_per_s"] = round(len(peaks) / max(r["duration_s"], 1e-3), 2)
        hop = (n // 2) / SR
        ac = np.correlate(flux - flux.mean(), flux - flux.mean(), "full")[len(flux) - 1:]
        lags = np.arange(len(ac)) * hop
        band = (lags > 60 / 140) & (lags < 60 / 50)
        r["tempo_bpm_est"] = round(60 / lags[band][np.argmax(ac[band])], 1) if band.any() else None

    win = int(0.4 * SR)
    if len(x) > 2 * win:
        rms = np.array([db(np.sqrt(np.mean(x[i:i + win] ** 2))) for i in range(0, len(x) - win, win)])
        r["max_jump_db_400ms"] = round(float(np.max(np.diff(rms))), 1)
        r["rms_profile_db"] = [round(float(c.mean()), 1) for c in np.array_split(rms, 12)]

    if r["duration_s"] > 8:
        chroma = np.zeros(12)
        ok = (freqs > 60) & (freqs < 2000)
        pitch = np.round(12 * np.log2(freqs[ok] / 440.0) + 9).astype(int) % 12
        energy = spec[:, ok].sum(0)
        np.add.at(chroma, pitch, energy)
        scores = [(np.corrcoef(np.roll(MAJOR, k), chroma)[0, 1], NOTES[k] + " major") for k in range(12)]
        scores += [(np.corrcoef(np.roll(MINOR, k), chroma)[0, 1], NOTES[k] + " minor") for k in range(12)]
        scores.sort(reverse=True)
        r["key_est"] = scores[0][1]
        r["key_conf"] = round(float(scores[0][0]), 2)
        r["key_runner_up"] = scores[1][1]

    seam = int(0.05 * SR)
    if len(x) > 4 * seam:
        a, b = x[-seam:], x[:seam]
        r["loop_seam_rms_diff_db"] = round(abs(db(np.sqrt(np.mean(a ** 2))) - db(np.sqrt(np.mean(b ** 2)))), 1)
        r["loop_seam_step"] = round(float(abs(x[-1] - x[0])), 4)
    return r


def main(argv):
    out = None
    if "--json" in argv:
        i = argv.index("--json")
        out, argv = argv[i + 1], argv[:i] + argv[i + 2:]
    results = []
    for f in argv:
        try:
            results.append(report(f))
        except Exception as e:  # keep batch going; report the failure
            results.append({"file": pathlib.Path(f).name, "error": str(e)})
    for r in results:
        print(json.dumps({k: v for k, v in r.items() if k != "rms_profile_db"}))
    if out:
        pathlib.Path(out).write_text(json.dumps(results, indent=1))


if __name__ == "__main__":
    main(sys.argv[1:])
