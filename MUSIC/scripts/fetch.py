"""Download generated takes into MUSIC/auditions without storing their signed URLs.

usage: EL_BASE=<content base url> EL_EXPIRES=<expires> python -I fetch.py ID:TAKE:SESSION:GENERATION:SIGNATURE [...]
  EL_BASE / EL_EXPIRES come from the generation result and are passed only on the command line,
  so no account path or signature is written to tracked files.
Writes auditions/<ID>__take<TAKE>__<GENERATION>.mp3, never overwrites, then probes it with ffprobe.
"""
import os
import pathlib
import subprocess
import sys
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
FFPROBE = ROOT / "tools" / "ffmpeg" / "bin" / "ffprobe.exe"


def main(tokens):
    base, expires = os.environ.get("EL_BASE"), os.environ.get("EL_EXPIRES")
    if not tokens or not base or not expires:
        sys.exit(__doc__)
    failed = 0
    for token in tokens:
        sound_id, take, session, gen, sig = token.split(":", 4)
        out = ROOT / "auditions" / f"{sound_id}__take{take}__{gen}.mp3"
        if out.exists():
            print(f"skip (exists) {out.name}")
            continue
        url = f"{base}/{session}/{gen}/content.mp3?Expires={expires}&KeyName=assets-cdn-key-1&Signature={sig}"
        try:
            with urllib.request.urlopen(url, timeout=120) as response:
                data = response.read()
        except Exception as e:  # report and continue with the rest of the batch
            print(f"FAIL {out.name}: {type(e).__name__}")
            failed += 1
            continue
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
