"""Download generated takes into MUSIC/auditions without storing their signed URLs.

usage: python -I fetch.py ID TAKE GENERATION_ID URL [ID TAKE GENERATION_ID URL ...]
Writes auditions/<ID>__take<TAKE>__<GENERATION_ID>.mp3, refuses to overwrite, then probes it.
"""
import pathlib
import subprocess
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
FFPROBE = ROOT / "tools" / "ffmpeg" / "bin" / "ffprobe.exe"


def main(args):
    if len(args) % 4:
        sys.exit(__doc__)
    failed = 0
    for i in range(0, len(args), 4):
        sound_id, take, gen, url = args[i:i + 4]
        out = ROOT / "auditions" / f"{sound_id}__take{take}__{gen}.mp3"
        if out.exists():
            print(f"skip (exists) {out.name}")
            continue
        with urllib.request.urlopen(url, timeout=120) as response:
            data = response.read()
        if not data:
            print(f"EMPTY {out.name}")
            failed += 1
            continue
        out.write_bytes(data)
        probe = subprocess.run([str(FFPROBE), "-v", "error", "-show_entries", "format=duration:stream=codec_name,sample_rate,channels",
                                "-of", "compact=p=0:nk=1", str(out)], capture_output=True, text=True)
        ok = probe.returncode == 0 and probe.stdout.strip()
        failed += not ok
        print(f"{'ok ' if ok else 'BAD'} {out.name} {len(data)}B {probe.stdout.strip().replace(chr(10), ' ')}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main(sys.argv[1:])
