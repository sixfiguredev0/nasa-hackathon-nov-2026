"""Compare two tracks' 1-second chroma and loudness envelopes (detects reworks/copies). usage: similarity.py A B"""
import sys, numpy as np, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from analyze import decode, SR

def feats(p):
    x = decode(p); n = SR; frames = x[:len(x) // n * n].reshape(-1, n)
    spec = np.abs(np.fft.rfft(frames * np.hanning(n), axis=1)) ** 2
    f = np.fft.rfftfreq(n, 1 / SR); ok = (f > 60) & (f < 2000)
    pc = np.round(12 * np.log2(f[ok] / 440) + 9).astype(int) % 12
    chroma = np.zeros((len(frames), 12))
    for i in range(12): chroma[:, i] = spec[:, ok][:, pc == i].sum(1)
    chroma /= chroma.sum(1, keepdims=True) + 1e-12
    env = 10 * np.log10(spec.sum(1) + 1e-12)
    return chroma, env

a, b = (feats(p) for p in sys.argv[1:3]); m = min(len(a[1]), len(b[1]))
frame_sim = np.mean([np.dot(a[0][i], b[0][i]) / (np.linalg.norm(a[0][i]) * np.linalg.norm(b[0][i]) + 1e-12) for i in range(m)])
print(f"chroma_framewise_cos={frame_sim:.3f} envelope_corr={np.corrcoef(a[1][:m], b[1][:m])[0,1]:.3f}")
