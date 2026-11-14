import struct, zlib, sys, os, glob, math, json, random

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ArtSource", "Mars")
TERRAIN = os.path.join(ROOT, "Assets", "Art", "Mars", "Terrain")
PLACE = os.path.join(ROOT, "Assets", "Art", "Mars", "Placeholders")

RAMP_STOPS = [(0.0, '1c0809'), (0.2, '3a0e12'), (0.4, '5e1612'), (0.6, '85201a'), (0.8, 'a8301f'), (1.0, 'c44430')]


def mars(c):
    r, g, b = c[:3]
    t = max(0.0, min(1.0, ((0.3 * r + 0.59 * g + 0.11 * b) - 20) / 125.0))
    for (t0, h0), (t1, h1) in zip(RAMP_STOPS, RAMP_STOPS[1:]):
        if t <= t1:
            f = (t - t0) / (t1 - t0)
            c0 = [int(h0[i:i + 2], 16) for i in (0, 2, 4)]
            c1 = [int(h1[i:i + 2], 16) for i in (0, 2, 4)]
            return tuple(int(round(c0[i] + (c1[i] - c0[i]) * f)) for i in range(3)) + ((c[3] if len(c) > 3 else 255),)


PAL = {k: mars(tuple(int(v[i:i + 2], 16) for i in (0, 2, 4))) for k, v in dict(
    d0='331313', d1='491313', d2='581818', d3='6e1515', d4='761919',
    m1='971b1b', m2='a72b2b', l1='ae4040', l2='c96b6b', l3='e56b6b').items()}
RAMP = ['d3', 'm1', 'm2', 'l1', 'l2']


def decode(path):
    d = open(path, 'rb').read()
    _, _, frames, w, h, _ = struct.unpack('<IHHHHH', d[:14])
    trans = d[28]
    pos, pal, img = 128, {}, [[None] * w for _ in range(h)]
    for _f in range(frames):
        fsize, _, oc, _, _, nc = struct.unpack('<IHHHHI', d[pos:pos + 16])
        p = pos + 16
        for _ in range(nc if nc else oc):
            csize, ctype = struct.unpack('<IH', d[p:p + 6])
            body = d[p + 6:p + csize]
            if ctype == 0x2019:
                _, first, last = struct.unpack('<III', body[:12])
                q = 20
                for i in range(first, last + 1):
                    flags = struct.unpack('<H', body[q:q + 2])[0]
                    pal[i] = tuple(body[q + 2:q + 6])
                    q += 6
                    if flags & 1:
                        q += 2 + struct.unpack('<H', body[q:q + 2])[0]
            elif ctype == 0x2005:
                _, x, y, _, ct = struct.unpack('<HhhBH', body[:9])
                if ct == 2:
                    cw, ch = struct.unpack('<HH', body[16:20])
                    raw = zlib.decompress(body[20:])
                    for yy in range(ch):
                        for xx in range(cw):
                            v = raw[yy * cw + xx]
                            if v != trans and 0 <= y + yy < h and 0 <= x + xx < w:
                                img[y + yy][x + xx] = pal[v]
            p += csize
        pos += fsize
    return w, h, img


def write_png(path, w, h, px):
    rows = b''
    for y in range(h):
        rows += b'\x00' + b''.join(bytes(px[y][x] or (0, 0, 0, 0)) for x in range(w))

    def ch(t, dt):
        return struct.pack('>I', len(dt)) + t + dt + struct.pack('>I', zlib.crc32(t + dt) & 0xffffffff)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) +
                           ch(b'IDAT', zlib.compress(rows, 9)) + ch(b'IEND', b''))


def dark_sides(img, w, h):
    pts = [(x, y) for y in range(h) for x in range(w) if img[y][x]]
    maxy = max(p[1] for p in pts)
    minx = min(x for x, y in pts if y == maxy)
    data = []
    for x, y in pts:
        b = (maxy - y + 0.5) / 13.0
        a = (x - minx - 12 * b + 0.5) / 24.0
        c = img[y][x]
        data.append((a, b, (c[0] + c[1] + c[2]) / 3))
    mean = sum(d[2] for d in data) / len(data)

    def band(f):
        v = [d[2] for d in data if f(d)]
        return sum(v) / len(v)

    flags = 0
    if mean - band(lambda d: d[0] < 0.3) >= 12: flags |= 1
    if mean - band(lambda d: d[0] > 0.7) >= 12: flags |= 2
    if mean - band(lambda d: d[1] < 0.3) >= 12: flags |= 4
    if mean - band(lambda d: d[1] > 0.7) >= 12: flags |= 8
    return flags


def convert_tiles():
    manifest = {}
    sides = {}
    CW, CH = 40, 16
    sources = [(f, None) for f in sorted(glob.glob(os.path.join(SRC, 'Sprite-gro*.aseprite')))]
    sources += [(f, 1) for f in sorted(glob.glob(os.path.join(SRC, 'Scatter', 'Sprite-gro*-Scatter-*.aseprite')))]
    for f, scatter in sources:
        w, h, img = decode(f)
        pts = [(x, y) for y in range(h) for x in range(w) if img[y][x]]
        minx, maxy = min(p[0] for p in pts), max(p[1] for p in pts)
        cv = [[None] * CW for _ in range(CH)]
        for x, y in pts:
            cv[14 - (maxy - y)][1 + x - minx] = mars(img[y][x])
        b = os.path.basename(f).replace('.aseprite', '').replace('Sprite-groud', 'ground').replace('Sprite-ground', 'ground')
        name = 'MarsTile_' + b.replace('-Scatter-', '_')
        write_png(os.path.join(TERRAIN, name + '.png'), CW, CH, cv)
        manifest[name] = {'pivot': [19 / CW, 7.5 / CH], 'ppu': 16}
        sides[name] = dark_sides(img, w, h)
    fam = {}
    for k, v in sides.items():
        fam[k.split('_')[1]] = fam.get(k.split('_')[1], 0) | v
    for k in sides:
        sides[k] = fam[k.split('_')[1]]
    with open(os.path.join(ROOT, 'Assets', 'Art', 'Mars', 'tile_sides.txt'), 'w') as f:
        for k, v in sorted(sides.items()):
            f.write('%s,%d\n' % (k, v))
    return manifest


class Cv:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [[None] * w for _ in range(h)]

    def set(self, x, y, c, a=255):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.px[y][x] = c[:3] + (a,)


def clamp(v, a, b):
    return max(a, min(b, v))


def rock(w, h, seed):
    r = random.Random(seed)
    padb, padr = max(2, h // 5), max(3, w // 3)
    cv = Cv(w + padr, h + padb)
    ph1, ph2 = r.uniform(0, 6.28), r.uniform(0, 6.28)
    inside = [[False] * w for _ in range(h)]
    cx, cy = w / 2, h / 2
    for y in range(h + padb):
        for x in range(w + padr):
            ex, ey = (x + .5 - (w * 0.62)) / (w * 0.5), (y + .5 - (h + padb * 0.2)) / max(1, padb * 0.8)
            if ex * ex + ey * ey <= 1 and (x + .5) > w * 0.2:
                cv.set(x, y, PAL['d1'], 105)
    for y in range(h):
        for x in range(w):
            ex, ey = (x + .5 - cx) / (w / 2), (y + .5 - cy) / (h / 2)
            th = math.atan2(ey, ex)
            rr = 1 + .12 * math.sin(3 * th + ph1) + .07 * math.sin(5 * th + ph2)
            sup = abs(ex) ** 2.5 + abs(ey) ** 2.5
            if sup <= rr ** 2.5:
                inside[y][x] = True
    for y in range(h):
        for x in range(w):
            if not inside[y][x]:
                continue
            ex, ey = (x + .5 - cx) / (w / 2), (y + .5 - cy) / (h / 2)
            lum = .5 - .5 * (ex * .55 + ey * .75) + r.uniform(-.14, .14)
            c = PAL[RAMP[clamp(int(lum * 5), 0, 4)]]
            below = y + 1 >= h or not inside[y + 1][x]
            right = x + 1 >= w or not inside[y][x + 1]
            up = y == 0 or not inside[y - 1][x]
            left = x == 0 or not inside[y][x - 1]
            if below:
                c = PAL['d1']
            elif right:
                c = PAL['d3']
            elif up or left:
                c = PAL['l2']
            cv.set(x, y, c)
    return cv, ((w / 2) / (w + padr), padb / (h + padb), padb)


def rock_fix(w, h, seed):
    cv, (px, py, padb) = rock(w, h, seed)
    return cv, (px, py)


def crater(R, seed):
    r = random.Random(seed)
    ex_x, ex_y = int(26.9 * R) + 3, int(12 * R) + 3
    cw, ch = ex_x * 2 + 1, ex_y * 2 + 1
    cv = Cv(cw, ch)
    L = (.55, .75)
    for y in range(ch):
        for x in range(cw):
            sx, sy = x + .5 - cw / 2, y + .5 - ch / 2
            b = -sy / 12
            a = (sx - 12 * b) / 24
            rr = math.sqrt(a * a + b * b) / R
            wob = 1 + .06 * math.sin(math.atan2(b, a) * 3 + seed)
            rr /= wob
            if rr > 1.0:
                continue
            ln = math.hypot(sx, sy) or 1
            lit = (sx * L[0] + sy * L[1]) / ln
            n = r.uniform(-.25, .25)
            if rr >= .8:
                c = PAL['l2'] if lit < n - .1 else (PAL['m2'] if lit < .5 else PAL['m1'])
                if rr > .94 and lit > 0:
                    c = PAL['d3']
            elif rr >= .5:
                c = PAL['l1'] if lit + n > 0 else PAL['d1']
                if abs(lit + n) < .15:
                    c = PAL['m1']
            else:
                c = PAL['m1'] if n > -.1 else PAL['d4']
                if n > .18:
                    c = PAL['m2']
            cv.set(x, y, c)
    return cv, (.5, .5)


def poly_in(p, q):
    s = 0
    for i in range(len(p)):
        x1, y1 = p[i]
        x2, y2 = p[(i + 1) % len(p)]
        c = (x2 - x1) * (q[1] - y1) - (y2 - y1) * (q[0] - x1)
        if c < 0:
            s -= 1
        elif c > 0:
            s += 1
    return abs(s) == len(p)


def mesa(na, nb, hpx, seed):
    r = random.Random(seed)
    W, D = int(24 * na), int(12 * nb)
    cw, ch = W + D + 10, hpx + D + 4
    cv = Cv(cw, ch)
    bl = (1, 2)

    def P(x, y):
        return (bl[0] + x, ch - 1 - (bl[1] + y))

    faces = [
        ('shadow', [(W, 0), (W + 9, 0), (W + D + 9, D), (W + D, D)], None),
        ('right', [(W, 0), (W + D, D), (W + D, D + hpx), (W, hpx)], ['d1', 'd3', 'd4']),
        ('front', [(0, 0), (W, 0), (W, hpx), (0, hpx)], ['d3', 'm1', 'm2']),
        ('top', [(0, hpx), (W, hpx), (W + D, hpx + D), (D, hpx + D)], ['m2', 'l1', 'l2']),
    ]
    for name, pts, ramp in faces:
        pp = [P(*p) for p in pts]
        for y in range(ch):
            for x in range(cw):
                if poly_in(pp, (x + .5, y + .5)):
                    if name == 'shadow':
                        cv.set(x, y, PAL['d1'], 95)
                        continue
                    k = r.random()
                    base = 1 if name == 'front' and (y // 3) % 3 else 0
                    idx = clamp(int(k * 2.2) + (1 if name == 'top' else 0) + base - (1 if name == 'right' else 0), 0, 2)
                    cv.set(x, y, PAL[ramp[idx]])
    for x in range(cw):
        for y in range(ch):
            c = cv.px[y][x]
            if c and c[3] == 255 and (y + 1 >= ch or cv.px[y + 1][x] is None):
                cv.set(x, y, PAL['d1'])
    return cv, ((bl[0] + W / 2) / cw, bl[1] / ch)


def cluster(seed):
    r = random.Random(seed)
    cv = Cv(40, 22)
    for (w, h, ox, oy) in [(16, 12, 4, 7), (12, 9, 20, 5), (9, 7, 14, 1)]:
        rc, _ = rock_fix(w, h, r.randint(0, 9999))
        for y in range(rc.h):
            for x in range(rc.w):
                c = rc.px[y][x]
                if c:
                    X, Y = ox + x, 21 - oy - (rc.h - 1) + y
                    if 0 <= Y < 22 and 0 <= X < 40 and (c[3] == 255 or cv.px[Y][X] is None):
                        cv.set(X, Y, c, c[3])
    return cv, (.5, 1 / 22)


def pebbles(seed):
    r = random.Random(seed)
    cv = Cv(14, 7)
    for _ in range(r.randint(3, 5)):
        x, y = r.randint(1, 11), r.randint(1, 4)
        cv.set(x + 1, y + 1, PAL['d1'], 120)
        cv.set(x, y, PAL['m2'])
        cv.set(x - 1, y - 1, PAL['l2']) if r.random() < .5 else None
        cv.set(x, y + 1, PAL['d3'])
    return cv, (.5, .25)


def dust(seed):
    r = random.Random(seed)
    cv = Cv(34, 7)
    for _ in range(4):
        y, x0 = r.randint(1, 5), r.randint(0, 10)
        for x in range(x0, x0 + r.randint(10, 22)):
            cv.set(x, y + (1 if r.random() < .15 else 0), PAL['l2'] if r.random() < .6 else PAL['d1'], 70)
    return cv, (.5, .5)


def build_placeholders():
    m = {}

    def add(name, res):
        cv, piv = res
        write_png(os.path.join(PLACE, 'PLACEHOLDER_' + name + '.png'), cv.w, cv.h, cv.px)
        m['PLACEHOLDER_' + name] = {'pivot': list(piv), 'ppu': 16}

    for i, (w, h) in enumerate([(7, 6), (8, 6), (9, 7)]):
        add('rock_s_%d' % i, rock_fix(w, h, 100 + i))
    for i, (w, h) in enumerate([(12, 9), (13, 9), (14, 10)]):
        add('rock_m_%d' % i, rock_fix(w, h, 200 + i))
    for i, (w, h) in enumerate([(19, 14), (21, 14)]):
        add('rock_l_%d' % i, rock_fix(w, h, 300 + i))
    add('rock_xl_0', rock_fix(28, 20, 400))
    for i in range(3):
        add('pebbles_%d' % i, pebbles(500 + i))
    for i, R in enumerate([.4, .45]):
        add('crater_s_%d' % i, crater(R, 600 + i))
    for i, R in enumerate([.7, .8]):
        add('crater_m_%d' % i, crater(R, 700 + i))
    add('crater_l_0', crater(1.15, 800))
    add('formation_mesa_0', mesa(1.0, 1.0, 14, 900))
    add('formation_mesa_1', mesa(2.0, 1.0, 11, 901))
    add('formation_mesa_2', mesa(1.5, 1.5, 18, 902))
    add('formation_cluster_0', cluster(910))
    add('formation_cluster_1', cluster(911))
    for i in range(2):
        add('dust_streak_%d' % i, dust(1000 + i))
    return m


if __name__ == '__main__':
    man = {}
    man.update(convert_tiles())
    man.update(build_placeholders())
    with open(os.path.join(ROOT, 'Assets', 'Art', 'Mars', 'sprite_manifest.txt'), 'w') as f:
        for k, v in sorted(man.items()):
            f.write('%s,%.6f,%.6f\n' % (k, v['pivot'][0], v['pivot'][1]))
    print(len(man), 'sprites written')
