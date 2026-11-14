# Extracts the artist ground grain (ground8 + scatter A/B/C) into Assets/Art/Mars/Terrain/MarsGrain.png (24x12 per variant).
import sys, glob, os
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_mars_art import decode, write_png
src=os.path.join(ROOT, "ArtSource", "Mars")
fs=[src+"/Sprite-ground8.aseprite"]+sorted(glob.glob(src+"/Scatter/Sprite-ground8-Scatter-*.aseprite"))
print(fs)
tiles=[]
for f in fs:
    w,h,img=decode(f)
    pts=[(x,y) for y in range(h) for x in range(w) if img[y][x]]
    minx=min(x for x,y in pts if y==max(p[1] for p in pts)); maxy=max(p[1] for p in pts)
    t=[[None]*24 for _ in range(12)]
    for ly in range(12):
        for lx in range(24):
            x=minx+ly+lx; y=maxy-ly
            c=img[y][x] if 0<=y<h and 0<=x<w else None
            t[11-ly][lx]=c
    miss=sum(1 for r in t for c in r if c is None); print(os.path.basename(f),'missing',miss)
    tiles.append(t)
out=[[None]*(24*len(tiles)) for _ in range(12)]
for k,t in enumerate(tiles):
    for y in range(12):
        for x in range(24): out[y][k*24+x]=t[y][x] or (160,40,40,255)
write_png(os.path.join(ROOT, "Assets", "Art", "Mars", "Terrain", "MarsGrain.png"),24*len(tiles),12,out)
