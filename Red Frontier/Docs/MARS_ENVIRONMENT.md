# Procedural Mars environment

Scene: `Assets/Scenes/MarsOutpost.unity` (open it, press Play). Pan with WASD / arrows / right-mouse drag.

## How it works
The ground is no longer a Tilemap. `MarsTerrain` builds a height / sand / rock model on the artist's oblique lattice
(cell = 24 x 12 px parallelogram) and bakes it into one point-filtered texture shown by the `Ground` SpriteRenderer:
- Oblique rendering: a point with height h is drawn h px higher, so slopes that face the camera become cliff faces (layered strata on mesas).
- Craters (bowl, raised rim, ejecta, central peak on big ones), mesas, rolling hills, dark basaltic sand with ripples, boulders, talus and pebbles.
- Pixel-art style for ages 7-12: small hue-shifted palette (`Dust`, `Sand`, `Rock`, `Outline` in `MarsTerrain.cs`), no dithering, lighting in 5 flat bands, 1 px outlines on rocks and cliffs, horizontal strata on mesa cliffs.
- Light from the upper left with ray-marched cast shadows.
- The artist grain (`ground8` + scatter A/B/C) gives sparse specks on the ground.
- Outpost zones are flattened to height 0 and kept free of craters and rocks.

## Space and horizon (`Mars Sky` object, `MarsSky.cs`)
- Above the ground's far edge: distant hills and peaks, a thin banded atmosphere glow, then open space (camera background = `MarsSky.SpaceColor`, so space never ends).
- Star field: 512 px wide tile repeated horizontally, with a Milky Way band; faint stars fade near the horizon. Extra stars twinkle.
- Sun (artist sprite, background removed, 3 shimmer frames), Earth (artist sprite reduced to 8 px), Phobos (crosses west to east every `phobosCrossSeconds`), Deimos, and a shooting star every 5-12 s.
- Sizes follow the real view from Mars loosely: the Sun looks about 2/3 the size we see from Earth, Earth is a small bright dot (really ~1/27 of the Sun; 8 px keeps it recognisable; swap in `Space_earth_12` for a bigger one), Phobos is about half the Sun, Deimos is star-like.
- Space layers follow the camera horizontally (`spaceFollow` 0.85, hills 0.4) for parallax. Positions are in pixels above the horizon (`sunPos`, `earthPos`, ...).
- The camera can pan up to `spaceHeight` px above the horizon.
- Art: `ArtSource/Mars/Space/*.aseprite` -> `Tools/MarsArt/make_space.py` -> `Assets/Art/Mars/Space/`.

## Change the seed
Select **Mars Map**, `MarsMapGenerator`:
- `Use Random Seed` ON: new map on every Play. OFF + `Seed` = N: same map every time.
- Right-click the component header: *Generate (current seed)* / *Generate (new random seed)* (Edit mode too).
- Code: `generator.Generate(seed)`.

## Tuning (`MarsMapGenerator > Settings`)
`width` / `height` (cells), `terrainVariation`, `mesaAmount`, `duneAmount`, `craterDensity`, `maxCraterRadius`, `rockDensity`, `pebbleDensity` (densities per 100 cells).
Outpost zones live in `Placement` (green outlines when selected).

## Placing buildings
`LatticeToWorld(i, j)` gives the ground position of lattice point (i, j) (cell centre = i + 0.5, j + 0.5); outpost zones are flat, so use it there.
`SurfaceToWorld(i, j)` adds the terrain height for props outside the outpost. Sort sprites with `SortingOrderFor(worldY)`; the ground draws at -1000.

## Art
- `ArtSource/Mars/*.aseprite`: copies of the artist's originals.
- `Tools/MarsArt/make_grain.py`: extracts the grain sheet `Assets/Art/Mars/Terrain/MarsGrain.png` (must stay Read/Write enabled; *Import Art Settings* sets it).
- The old tile PNGs and `PLACEHOLDER_*` sprites are no longer used by the scene.
- Editor menu `Red Frontier > Mars`: *Import Art Settings*, *Build Outpost Scene*, *Run Self Test*, *Build WebGL Test*.

## Backups
- Before the terrain rebuild: `D:\Projects\Red Frontier\Backups\pre-mars-rebuild-20261114-173420` (Mars scripts, art, scenes, tools, docs).
- Before any Mars work: `D:\Projects\Red Frontier\Backups\pre-mars-20261114-161357`.
