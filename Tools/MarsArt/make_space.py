# Converts ArtSource/Mars/Space/*.aseprite (indexed, old 0x0004 or new 0x2019 palette) into trimmed PNGs in Assets/Art/Mars/Space.
import struct, zlib, os, glob, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_mars_art import write_png
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

def decode(path):
    d = open(path, 'rb').read()
    _, _, frames, w, h, depth = struct.unpack('<IHHHHH', d[:14])
    assert depth == 8, 'only indexed sprites supported'
    trans = d[28]
    pos, pal, img = 128, {}, [[None] * w for _ in range(h)]
    for _ in range(frames):
        fsize, _, oc, _, _, nc = struct.unpack('<IHHHHI', d[pos:pos + 16])
        p = pos + 16
        for _ in range(nc if nc else oc):
            csize, ctype = struct.unpack('<IH', d[p:p + 6])
            body = d[p + 6:p + csize]
            if ctype == 0x0004 and not pal:
                n = struct.unpack('<H', body[:2])[0]; q = 2; idx = 0
                for _ in range(n):
                    idx += body[q]; cnt = body[q + 1] or 256; q += 2
                    for _ in range(cnt):
                        pal[idx] = tuple(body[q:q + 3]) + (255,); q += 3; idx += 1
            elif ctype == 0x2019:
                _, first, last = struct.unpack('<III', body[:12]); q = 20
                for i in range(first, last + 1):
                    flags = struct.unpack('<H', body[q:q + 2])[0]
                    pal[i] = tuple(body[q + 2:q + 6]); q += 6
                    if flags & 1: q += 2 + struct.unpack('<H', body[q:q + 2])[0]
            elif ctype == 0x2005:
                _, x, y, _, ct = struct.unpack('<HhhBH', body[:9])
                if ct == 2:
                    cw, ch = struct.unpack('<HH', body[16:20])
                    raw = zlib.decompress(body[20:])
                    for yy in range(ch):
                        for xx in range(cw):
                            v = raw[yy * cw + xx]
                            if v != trans and 0 <= y + yy < h and 0 <= x + xx < w:
                                img[y + yy][x + xx] = pal.get(v, (255, 0, 255, 255))
            p += csize
        pos += fsize
    return w, h, img

for f in sorted(glob.glob(os.path.join(ROOT, 'ArtSource', 'Mars', 'Space', '*.aseprite'))):
    w, h, img = decode(f)
    pts = [(x, y) for y in range(h) for x in range(w) if img[y][x]]
    x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
    y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
    out = [[img[y][x] for x in range(x0, x1 + 1)] for y in range(y0, y1 + 1)]
    name = 'Space_' + os.path.basename(f).replace('Sprite-', '').replace('.aseprite', '')
    write_png(os.path.join(ROOT, 'Assets', 'Art', 'Mars', 'Space', name + '.png'), x1 - x0 + 1, y1 - y0 + 1, out)
    cols = {c for r in out for c in r if c}
    print(name, x1 - x0 + 1, 'x', y1 - y0 + 1, 'colors', len(cols))


# ---- derived sprites: sun (background removed, 3 shimmer frames), small Earth, Phobos, Deimos, shooting star ----
import math, random
OUT = os.path.join(ROOT, 'Assets', 'Art', 'Mars', 'Space')
H2 = lambda s: tuple(int(s[i:i + 2], 16) for i in (0, 2, 4)) + (255,)

def save(name, px):
    write_png(os.path.join(OUT, name + '.png'), len(px[0]), len(px), px)

w, h, sun = decode(os.path.join(ROOT, 'ArtSource', 'Mars', 'Space', 'Sprite-sun.aseprite'))
CX, CY, R = 40, 66, 36
drop = {H2('330000'), H2('333300'), H2('000000'), H2('663333'), H2('666633'), H2('993333'), H2('cc3333')}
corona = {H2('663300'), H2('993300'), H2('996633'), H2('996600')}
base = [[None] * (2 * R + 1) for _ in range(2 * R + 1)]
for y in range(2 * R + 1):
    for x in range(2 * R + 1):
        sx, sy = CX - R + x, CY - R + y
        if 0 <= sx < w and 0 <= sy < h:
            c = sun[sy][sx]
            d = math.hypot(x - R, y - R)
            if c and c not in drop and d <= R - (2 if c in corona else 0):
                base[y][x] = c
# drop isolated corona specks
for y in range(1, 2 * R):
    for x in range(1, 2 * R):
        if base[y][x] in corona and sum(1 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if base[y + dy][x + dx]) < 2:
            base[y][x] = None
# solid corona: fill the holes left by the removed background
for y in range(2 * R + 1):
    for x in range(2 * R + 1):
        d = math.hypot(x - R, y - R)
        if base[y][x] is None and 22 < d <= R - 3:
            base[y][x] = H2('663300')
for k in range(3):
    rnd = random.Random(k * 7919)
    fr = [row[:] for row in base]
    for y in range(2 * R + 1):
        for x in range(2 * R + 1):
            c = fr[y][x]
            d = math.hypot(x - R, y - R)
            if c in corona and d > R - 8:
                r = rnd.random()
                if r < 0.1 and d > R - 4: fr[y][x] = None
                elif r < 0.36: fr[y][x] = H2('993300') if c == H2('663300') else H2('663300')
    save('Space_sun_%d' % k, fr)

w, h, earth = decode(os.path.join(ROOT, 'ArtSource', 'Mars', 'Space', 'Sprite-planet.aseprite'))
pts = [(x, y) for y in range(h) for x in range(w) if earth[y][x]]
ex0, ey0 = min(p[0] for p in pts), min(p[1] for p in pts)
for size in (8, 12):
    out = [[None] * size for _ in range(size)]
    cell = 64 / size
    for y in range(size):
        for x in range(size):
            if math.hypot(x + 0.5 - size / 2, y + 0.5 - size / 2) > size / 2: continue
            from collections import Counter
            cnt = Counter()
            for yy in range(int(y * cell), int((y + 1) * cell)):
                for xx in range(int(x * cell), int((x + 1) * cell)):
                    c = earth[ey0 + yy][ex0 + xx] if ey0 + yy < h and ex0 + xx < w else None
                    if c: cnt[c] += 1
            if cnt: out[y][x] = cnt.most_common(1)[0][0]
    save('Space_earth_%d' % size, out)

def moon(name, wd, ht, seed, craters):
    rnd = random.Random(seed)
    pal = [H2('2a1a2e'), H2('4a3a44'), H2('6e5a5c'), H2('927a74'), H2('b49c8c')]
    px = [[None] * wd for _ in range(ht)]
    a1, p1, a2, p2 = rnd.uniform(.05, .15), rnd.uniform(0, 6.3), rnd.uniform(.04, .1), rnd.uniform(0, 6.3)
    inside = lambda x, y: (lambda dx, dy: math.hypot(dx, dy) < 1 + a1 * math.cos(math.atan2(dy, dx) + p1) + a2 * math.cos(2 * math.atan2(dy, dx) + p2) - 0.12)((x + .5 - wd / 2) / (wd / 2), (y + .5 - ht / 2) / (ht / 2))
    for y in range(ht):
        for x in range(wd):
            if not inside(x, y): continue
            nx, ny = (x + .5 - wd / 2) / (wd / 2), (y + .5 - ht / 2) / (ht / 2)
            lit = -0.7 * nx - 0.7 * ny  # light from the upper left
            px[y][x] = pal[3] if lit > 0.35 else pal[2] if lit > -0.35 else pal[1]
            if lit > 0.75: px[y][x] = pal[4]
    for cx, cy, cr in craters:
        for y in range(ht):
            for x in range(wd):
                if px[y][x] and math.hypot(x - cx, y - cy) < cr:
                    px[y][x] = pal[1] if (x - cx) + (y - cy) < 0 else pal[3]
    for y in range(ht):
        for x in range(wd):
            if px[y][x] and any(not (0 <= x + dx < wd and 0 <= y + dy < ht) or not px[y + dy][x + dx] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = pal[0]
    save(name, px)

moon('Space_phobos', 22, 16, 3, [(8, 7, 2.6), (14, 9, 1.4), (11, 4, 1.0)])
moon('Space_deimos', 6, 5, 9, [])
streak = [[None] * 12 for _ in range(4)]
for i, c in enumerate(['3a2a5e', '5a4a8a', '7f72b8', 'a99de0', 'd6cff5', 'ffffff']):
    x = 2 * i
    for xx in (x, x + 1):
        streak[3 - xx // 4][xx] = H2(c)
save('Space_shootingstar', streak)
print('derived sprites written')
