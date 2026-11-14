# Integration: Mars map v1 + Developer 1 simulation

Status: superseded by `feat/dev1-complete` (Developer 1 final). Current instructions: [HANDOFF](../planning/dev1-simulation/HANDOFF.md). GitHub `main` has not been created; the verified branch is the delivery reference until the team promotes it.

## Open, run, test, build

- Unity **2022.3.18f1** exactly (do not upgrade). Project folder: `Red Frontier/` in this repository.
- Combined scene: `Assets/RedFrontier/Scenes/Integration_MapV1.unity` (first in Build Settings). Press Play.
  - Mars map: seed 1337, `Use Random Seed` off. Restarting the mission never regenerates the map.
  - Functions panel (right side, ~420 px): open at start; **Functions** button or **Tab** toggles it (Tab is ignored while typing in the seed field). It reopens by itself for a new event or a mission outcome; you can still close it during an event. Closing only hides drawing.
  - Map panning (WASD / arrows / right-drag) is blocked while the panel is open and restored when it closes.
- Original scenes are unchanged: `Assets/Scenes/MarsOutpost.unity` (map owner), `Assets/RedFrontier/Scenes/Dev1_Debug.unity` (full-width debug HUD).
- Tests: Window > General > Test Runner, EditMode and PlayMode. .NET: `dotnet test game/sim/Tests/RedFrontier.Core.Tests.csproj`.
- WebGL (batch): `Unity -batchmode -quit -projectPath "Red Frontier" -buildTarget WebGL -executeMethod RedFrontier.Integration.IntegrationTools.BuildWebGL -buildPath <out>` (clean Development build, compression Disabled, integration scene only). Serve `<out>` over `http://127.0.0.1` with any static server.

## What was integrated

- Base: Developer 1 checkpoint `feat/dev1-core` `6ae1ed6`, plus a normal merge of `docs/red-frontier-game-plan` (`7de7220`, docs only).
- **Reviewed local map import, not a Git merge** (there is no map branch in Git): from the local Unity project `RED FRONTIER/Red Frontier/` the map scripts (`Assets/Scripts/Mars`), art (`Assets/Art/Mars`), scenes (`Assets/Scenes`), `ArtSource/`, `Tools/MarsArt`, `Docs/`, `Packages/manifest.json`, `Packages/packages-lock.json` and `ProjectSettings/`, with every original `.meta`. SHA-256 of every imported file: `map-v1-input-manifest.sha256` (Dev 1 files were already in Git and differ only in line endings).
- **Provenance limit:** that folder is a Unity Version Control (Plastic) workspace of `Red Frontier/Red Frontier@…@cloud`, `/main`. Its record shows an unfinished incoming update from changeset 1 to 2 with conflicts in `Packages/manifest.json` and `packages-lock.json`; the map files arrived through that partial update. The incoming package revisions could not be read (no Plastic CLI), so the imported package files are the local ones (changeset 1 plus the pinned MCP package), and **it is not confirmed that this snapshot equals any checked-in Plastic changeset**. Map scripts use only built-in Unity modules, so the local manifest compiles them. The Plastic workspace was not updated, resolved or checked in.
- Packages: unchanged from the snapshot, including `com.coplaydev.unity-mcp` pinned to `v10.0.0` (an Editor tool; each machine configures its own MCP endpoint, none is committed).
- Excluded: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, generated `.csproj/.sln`, `.plastic/`, `ignore.conf`, `__pycache__`, builds, SDKs, MCP client config.

## New code (temporary integration interface, not Developer 2's GUI)

- `Runtime/DebugHud.cs`: opt-in `panelMode` (default off, so Dev1_Debug looks as before). Reads session views and calls session commands only.
- `Integration/MapPanelBridge.cs` (default assembly, because `RedFrontier.Runtime` cannot reference the map's Assembly-CSharp): toggles `MarsEnvironmentController.allowPan`.
- `Integration/Editor/IntegrationTools.cs`: batch scene setup (copy of MarsOutpost, new GUID) and WebGL build. It never writes the original map scene.
- `Tests/PlayMode/PanelModeTests.cs`.

## Ownership

| Area | Owner role |
|---|---|
| Simulation/runtime: `Assets/RedFrontier/Core`, `Runtime`, `Tests`, `game/sim` | Developer 1 |
| Event/mission content: `Assets/RedFrontier/Content` (explicit exception inside `Assets/RedFrontier`) | Developer 3 (science/content) |
| Map: `Assets/Scripts/Mars`, `Assets/Art/Mars`, `Assets/Scenes/MarsOutpost.unity`, `ArtSource`, `Tools/MarsArt`, `Docs/` | Map owner |
| Production menu / GUI | Developer 2 |
| `Integration_MapV1`, Build Settings, `Packages/`, `ProjectSettings/` | Integration role (one person coordinates changes) |

Human names are not recorded here unless the team confirms them.

## Git main vs Plastic /main

GitHub `main` and Unity Version Control `/main` are different branches in different systems. Nothing synchronises them. After promotion, GitHub `main` is the shared baseline; do not copy back and forth automatically.

## Onboarding from the baseline

1. Back up your current work. Do not open the baseline over an existing folder.
2. Clone into a **new** folder; open `Red Frontier/` with Unity 2022.3.18f1.
3. Create your own branch from current `main` (`feat/<topic>`). One agent per branch/workspace.
4. Move only the files you own into it, always with their `.meta` files. Do not replace baseline files blindly; ask the integration role about scenes, `Packages/` or `ProjectSettings/`.
5. Never discard someone else's uncommitted work.

## Checks of the first integration candidate (historical, 2026-11-14; superseded by the final checks in DEVLOG)

Passed:
- Unity compiles; batch scene setup and build logged no compile errors. Integration scene: new GUID `5ed251bd6d48edb4a925b441c018abde` (MarsOutpost keeps `20665ddf…`), 1 camera, 1 AudioListener, 1 SimulationHost. MarsOutpost is byte-identical to the source; Dev1_Debug unchanged.
- No duplicate `.meta` GUIDs, missing or orphan metas in the imported inputs.
- Unity EditMode 52/52, PlayMode 5/5 (4 lifecycle + new `PanelModeTests`: toggling keeps session and 2 subscribers, events/outcome reopen the panel, closing during an event is allowed). .NET 47/47 (`_tools/dotnet`, SDK 8.0.428).
- Clean Development WebGL build of `Integration_MapV1`, compression Disabled: Succeeded, 23,967,392 bytes, 2:33 build (3:46 wall incl. platform switch). Output kept outside source (`_tools/webgl-builds/integration-map-v1`); the earlier `dev1-checkpoint1` build is untouched.
- Browser (Edge 157, http://127.0.0.1:8766, **CDP-injected trusted mouse/key events, not physical clicks**): map renders (seed 1337, hash `b6ed98ff`, generation 8.6 s in the development WebGL build); panel open at start; Functions button closes and reopens it; Tab closes and reopens it; Life support `off` changed the forecast and Air; Advance applied once (sol 1 -> 2); random event shown with choices; levels locked while pending; 0 console errors/exceptions.

Not verified yet (blocks promotion to `main`):
- Browser/Editor: success and failure runs, same-seed and new-seed restart by UI input, map unchanged after restart by UI, map panning when the panel is closed (CDP right-drag did not pan; keyboard panning not tried), Tab ignored while typing in the seed field.
- Editor Play Mode with real input (the clone's MCP bridge was not connected; batch mode was used instead).
- Fresh-checkout open/compile/test of the pushed commit.
- No integration test yet for the saved scene's seed/flags or map hash across a restart.
