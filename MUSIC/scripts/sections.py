"""Internal silences and per-segment key estimates. usage: sections.py FILE [FILE ...]"""
import sys, pathlib, numpy as np
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from analyze import decode, SR, NOTES, MAJOR, MINOR

def key_of(seg):
    n = 4096
    fr = np.lib.stride_tricks.sliding_window_view(seg, n)[::n // 2]
    spec = np.abs(np.fft.rfft(fr * np.hanning(n), axis=1)) ** 2
    f = np.fft.rfftfreq(n, 1 / SR); ok = (f > 60) & (f < 2000)
    pc = np.round(12 * np.log2(f[ok] / 440) + 9).astype(int) % 12
    c = np.zeros(12); np.add.at(c, pc, spec[:, ok].sum(0))
    s = [(np.corrcoef(np.roll(MAJOR, k), c)[0, 1], NOTES[k]) for k in range(12)] + [(np.corrcoef(np.roll(MINOR, k), c)[0, 1], NOTES[k] + "m") for k in range(12)]
    return max(s)[1]

for p in sys.argv[1:]:
    x = decode(p); w = int(0.2 * SR)
    rms = np.array([20 * np.log10(np.sqrt(np.mean(x[i:i + w] ** 2)) + 1e-9) for i in range(0, len(x) - w, w)])
    quiet = rms < -55; spans = []; i = 0
    while i < len(quiet):
        if quiet[i]:
            j = i
            while j < len(quiet) and quiet[j]: j += 1
            if i > 0 and j < len(quiet) and (j - i) * 0.2 >= 0.8: spans.append(f"{i*0.2:.1f}-{j*0.2:.1f}s")
            i = j
        else: i += 1
    seg = 20 * SR
    keys = [key_of(x[k:k + seg]) for k in range(0, len(x) - SR * 5, seg)]
    print(f"{pathlib.Path(p).name[:24]:24} gaps={spans or '-'} keys={' '.join(keys)}")
