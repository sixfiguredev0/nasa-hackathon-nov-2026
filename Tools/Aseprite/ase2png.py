"""Minimal .aseprite -> horizontal PNG strip (all frames, visible layers composited). Stdlib only.
usage: python -I ase2png.py in.aseprite out.png"""
import struct, sys, zlib, json

def read_ase(path):
    d = open(path, 'rb').read()
    _, magic, nframes, W, H, depth, flags, speed = struct.unpack_from('<IHHHHHIH', d, 0)
    assert magic == 0xA5E0, 'not aseprite'
    trans_idx = d[28]
    bpp = depth // 8
    pal = [(0, 0, 0, 0)] * 256
    layers, frames, tags = [], [], []
    off = 128
    for _f in range(nframes):
        fbytes, fmagic, old_chunks, dur = struct.unpack_from('<IHHH', d, off)
        assert fmagic == 0xF1FA
        new_chunks = struct.unpack_from('<I', d, off + 12)[0]
        nchunks = new_chunks or old_chunks
        p = off + 16
        cels = {}
        for _c in range(nchunks):
            csize, ctype = struct.unpack_from('<IH', d, p)
            b = p + 6
            if ctype == 0x2004:
                lflags, ltype, child, _, _, blend, opacity = struct.unpack_from('<HHHHHHB', d, b)
                layers.append({'visible': lflags & 1, 'type': ltype, 'opacity': opacity if (flags & 1) else 255})
            elif ctype == 0x2019:
                _, first, last = struct.unpack_from('<III', d, b)
                q = b + 20
                for i in range(first, last + 1):
                    eflags = struct.unpack_from('<H', d, q)[0]
                    pal[i] = tuple(d[q + 2:q + 6]); q += 6
                    if eflags & 1:
                        n = struct.unpack_from('<H', d, q)[0]; q += 2 + n
            elif ctype == 0x0004 and not any(pal):
                npk = struct.unpack_from('<H', d, b)[0]; q = b + 2; i = 0
                for _ in range(npk):
                    i += d[q]; n = d[q + 1] or 256; q += 2
                    for _ in range(n):
                        pal[i] = (d[q], d[q + 1], d[q + 2], 255); q += 3; i += 1
            elif ctype == 0x2005:
                li, x, y, op, ctyp = struct.unpack_from('<HhhBH', d, b)
                q = b + 16
                if ctyp == 1:
                    cels[li] = ('link', struct.unpack_from('<H', d, q)[0])
                else:
                    w, h = struct.unpack_from('<HH', d, q)
                    raw = d[q + 4:p + csize]
                    if ctyp == 2: raw = zlib.decompress(raw)
                    if ctyp in (0, 2): cels[li] = ('img', x, y, op, w, h, raw)
            elif ctype == 0x2018:
                n = struct.unpack_from('<H', d, b)[0]; q = b + 10
                for _ in range(n):
                    fr, to = struct.unpack_from('<HH', d, q); q += 17
                    sl = struct.unpack_from('<H', d, q)[0]
                    tags.append({'name': d[q + 2:q + 2 + sl].decode('utf8', 'replace'), 'from': fr, 'to': to}); q += 2 + sl
            p += csize
        frames.append({'dur': dur, 'cels': cels})
        off += fbytes

    def px(raw, i):
        if bpp == 4: return tuple(raw[i * 4:i * 4 + 4])
        if bpp == 2: v, a = raw[i * 2], raw[i * 2 + 1]; return (v, v, v, a)
        k = raw[i]
        return (0, 0, 0, 0) if k == trans_idx else pal[k]

    out = []
    for fi, fr in enumerate(frames):
        img = [(0, 0, 0, 0)] * (W * H)
        for li, L in enumerate(layers):
            if not L['visible'] or L['type'] != 0 or li not in fr['cels']: continue
            c = fr['cels'][li]
            if c[0] == 'link': c = frames[c[1]]['cels'][li]
            _, x, y, op, w, h, raw = c
            k = op * L['opacity'] / 65025.0
            for j in range(h):
                for i in range(w):
                    X, Y = x + i, y + j
                    if not (0 <= X < W and 0 <= Y < H): continue
                    s = px(raw, j * w + i)
                    a = s[3] / 255.0 * k
                    if a <= 0: continue
                    dst = img[Y * W + X]; da = dst[3] / 255.0
                    oa = a + da * (1 - a)
                    rgb = [round((s[n] * a + dst[n] * da * (1 - a)) / oa) for n in range(3)]
                    img[Y * W + X] = (*rgb, round(oa * 255))
        out.append(img)
    return W, H, out, [f['dur'] for f in frames], tags

def write_png(path, w, h, rows):
    raw = b''.join(b'\0' + bytes(c for p in row for c in p) for row in rows)
    def chunk(t, data): return struct.pack('>I', len(data)) + t + data + struct.pack('>I', zlib.crc32(t + data) & 0xffffffff)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))

if __name__ == '__main__':
    W, H, imgs, durs, tags = read_ase(sys.argv[1])
    n = len(imgs)
    rows = [[imgs[f][(H - 1 - y) * 0 + y * W + x] for f in range(n) for x in range(W)] for y in range(H)]
    write_png(sys.argv[2], W * n, H, rows)
    print(json.dumps({'w': W, 'h': H, 'frames': n, 'durations': durs, 'tags': tags}))
