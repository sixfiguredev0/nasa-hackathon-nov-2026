# Developer 1 handoff

Keep this short and current. Replace sections rather than appending history; history belongs in [DEVLOG.md](../../../DEVLOG.md).

Last updated: 2026-11-14, Opus checkpoint 1 (verify and preserve the Unity prototype). No gameplay was added in this checkpoint.

## Stage 5 candidate: `feat/dev1-complete` (2026-11-14, read this first)

Not the shared baseline. Based on `integration/map-v1-dev1` (`5f7d232`). Contracts: [decision 0007](../../decisions/0007-four-resources-radiation-modifiers-delays.md).

**Play:** Unity 2022.3.18f1, project `Red Frontier/`, scene `Assets/RedFrontier/Scenes/Integration_MapV1.unity`, press Play. Functions panel (button or Tab) shows Power (Energy), Air (Oxygen), Water, Food, Crew Health, radiation (baseline, today's event units, unshielded total), active modifiers, queued delayed effects, five facilities (solar array, life support, water recycler, greenhouse, radiation shelter), forecast with radiation and modifier lines, events with effect previews, Advance, outcome, restarts.

**Done (Developer 1 simulation):**
- Four resources through existing data-driven content; Water separate from Oxygen; greenhouse (power + water -> food); water recycler; shortage penalties per resource; recovery only on a sol without shortage or radiation damage.
- Radiation: baseline + event exposure, facility shielding (shelter costs power), damage applied once per sol, consumed pending exposure, forecast/report/panel fields.
- Timed modifiers (additive stacking, output only, expiry in step 4) and delayed effects (step 5, once, logged, reported); restart clears both. Validator accepts `modifier`, `radiation`, `delaySols`, `shielding` with bounds; rejects delayed costs.
- Example content (placeholder): dust storm fallback adds solar -50% for 2 sols; resupply adds +6 food in 2 sols; new random `random_solar_flare` (radiation, shelter choice costs power).

**Not done / limits:**
- Balance is placeholder; no NASA-sourced values (Developer 3).
- No save/load; no production GUI (Developer 2).
- No Plastic transfer yet.

**Checks:**
- Passed: .NET build + tests 56/56 (47 existing + 9 Stage 5). Unity EditMode 63/63 (52 + 9 Stage 5 + 2 saved-scene checks). Unity PlayMode 5/5 (rerun after the final DebugHud fix). No duplicate GUIDs.
- Passed: clean Development WebGL build of the final code, compression Disabled, 23,988,371 bytes, 1:40 build. Edge 157 over http://127.0.0.1:8767 with CDP-injected mouse/keyboard (not physical): four resources, radiation line and five facilities shown; shelter on changed forecast power use 8 -> 11 and radiation to 1 blocked; Life support + Water recycler off changed forecast Air 12 -> 8, Water 12 -> 7; panel button and Tab toggle; ArrowRight pans with the panel closed, ArrowLeft does nothing with it open; Tab inside the seed field is ignored (a defect found and fixed in this pass: focus was read before the field registered its name). Map hash `b6ed98ff` (seed 1337), generation 4.6-5.7 s in the dev build. 0 console errors.
- NOT RUN in browser/Editor UI: Advance with the new content, event choices (solar flare, dust-storm modifier, delayed food), success, failure, same/new-seed restart, map unchanged after restart. These are covered only by automated tests (session API), which do not count as UI verification.
- NOT RUN: fresh-checkout Unity open/test of the pushed commit; Editor Play Mode with real input.
- Known: placeholder balance (power capacity 24 wastes solar output at start); delayed effects due after the last sol never land (mission ends first).

## Integration candidate (2026-11-14, supersedes the Git/Plastic notes below)
- Branch `integration/map-v1-dev1` holds a complete openable project: Packages, ProjectSettings, map and Dev 1 code. Play `Assets/RedFrontier/Scenes/Integration_MapV1.unity`.
- DebugHud gained an opt-in `panelMode` (Functions button, Tab, auto-open on event/outcome); Dev1_Debug keeps the default full-width HUD. Core and content unchanged.
- Checks, provenance limits and ownership: [MAP-V1-DEV1](../../integration/MAP-V1-DEV1.md). Promotion to GitHub `main` only after its checks pass.

## Workspace and branch

- Git checkout: the workspace root `RED FRONTIER/`, branch `feat/dev1-core`, based on `origin/feat/dev1-core` at `61f1ec3`. Checkpoint 1 is a **local commit** on top of it. Not pushed, merged or opened as a PR.
- Unity project: `RED FRONTIER/Red Frontier/`, Unity **2022.3.18f1** (do not upgrade). It is also a Unity Version Control (Plastic) workspace. Plastic metadata and local tool folders are excluded through `.git/info/exclude`.
- **Authoritative source:** `Red Frontier/Assets/RedFrontier/`. `game/sim/` holds the .NET projects, which include those files by path, plus .NET-only tests. See [0006](../../decisions/0006-event-fallback-and-unity-layout.md).
- **Committed:** `Assets/RedFrontier/**` (with every `.meta`), `game/sim`, and the docs.
- **Left untracked in Git:** `ProjectSettings/`, `Packages/` (contains the local Unity MCP package entry) and `Assets/Scenes/SampleScene.unity`. They belong to the Plastic project and the integrator, so the Git branch alone is not an openable Unity project yet.
- `_review/dev1-core` is untouched.

## How to play

Open `Assets/RedFrontier/Scenes/Dev1_Debug.unity` and press Play. The debug HUD (IMGUI) has:
- Power, Air, Crew Health and the sol
- facility level buttons and the next-sol forecast
- Advance
- the pending event, with its choices and why a choice is unavailable
- the end panel
- Restart with the same seed or a typed seed
- history

The browser build is `_tools/webgl-builds/dev1-checkpoint1/` (local only). Serve that folder over `http://127.0.0.1` with any static server.

## Changed in checkpoint 1

- **Fix: null session after a script reload in Play Mode** (reproduced; it matches Codex's report).
  - Cause: Unity's default "recompile and continue playing" rebuilds MonoBehaviours, calls `OnEnable` but not `Awake`, and drops the non-serialisable `GameSession`. `LoadError` gets its `""` initialiser, so the host looked healthy but had no session.
  - Fix: `SimulationHost` now initialises in `OnEnable` (idempotent). After a reload it rebuilds the session from the current seed and logs a warning that progress was lost. The `seed` field tracks restarts.
  - `DebugHud` binds idempotently: it unsubscribes in `OnDisable` and retries the bind from `OnGUI`.
  - Restoring progress across a reload would need save/restore, which is out of scope.
- New PlayMode test assembly `RedFrontier.Tests.PlayMode`, with lifecycle tests: one session; one HUD refresh per accepted command and none on rejection; repeated disable/enable of the host, the HUD and the GameObject; restart keeps the subscriptions; a second host is refused; destroy and re-create.
- No changes to Core, content or contracts in this checkpoint.

## Checks run (2026-11-14)

- **Unity EditMode:** 52/52 passed, job `ccf5a73c…`.
- **Unity PlayMode:** 4/4 passed, job `830add8f…`.
- **Script reload in Play Mode** (`EditorUtility.RequestScriptReload`, no source edit):
  - Before the fix: `Session == null`, `LoadError == ""`.
  - After the fix: session rebuilt, 2 subscribers, warning logged.
  - Repeated Play/Stop worked.
- **.NET:** SDK 8.0.428 installed workspace-locally (`_tools/dotnet`, official `dotnet-install.ps1`, Microsoft-signed; nothing machine-wide). Caches are in `_tools`.
  - `dotnet build Core/RedFrontier.Core.csproj`: 0 warnings, 0 errors.
  - `dotnet test Tests/RedFrontier.Core.Tests.csproj`: **47/47 passed**. These are the 45 shared tests plus 2 .NET-only content tests, which proves the by-path source and content includes work.
  - The seed→variant output is identical to Unity's.
- **Editor HUD with real Windows mouse input** (`SetCursorPos`/`mouse_event` into the Game View, only while Unity was the foreground window):
  - Life support `full` / `off` / `full`: the forecast changed each time; with `off` it showed Air 12→8.
  - Advance; answered a random event with the fallback and a milestone (sol 3, variant `long`) with the paid choice.
  - Clicked through to **MISSION SUCCESS** (seed 1).
  - **Restart with new seed** (2).
  - The session stopped when focus left Unity. **Failure and same-seed restart were not clicked in the Editor**; both were clicked in the browser.
- **Browser:** WebGL Build Support installed for 2022.3.18f1 (official Unity installer, UAC approved).
  - Build: Development, uncompressed, `Dev1_Debug` only, 89 s, 23.3 MB.
  - Run in Edge 157 over `http://127.0.0.1:8765`. Cold load to player-ready took 0.26 s (localhost, warm browser process).
  - With CDP-injected trusted mouse events (not physical clicks) and screenshots at each step: facility level → forecast; Advance; random and milestone events; success; restart with new seed; failure through fallback choices; same-seed restart with an identical replay.
  - Browser console: 0 errors, 0 exceptions.
  - Details: `_tools/evidence/checkpoint-1/browser-webgl-2026-11-14.md`.
- Evidence (local, untracked): `_tools/evidence/checkpoint-1/` holds the test summaries, `dotnet-tests.trx`, browser screenshots and console logs.

## Known issues and limitations

- **Environment noise, not ours:** after a script reload in Play Mode, Unity logs 8 to 10 "The referenced script (Unknown) on this Behaviour is missing!" errors without a stack trace. They also appear in `SampleScene`, which has no RedFrontier objects, and not after an Edit Mode reload. The MCP bridge logs WebSocket reconnect warnings after each reload.
- After the Editor restart, the MCP bridge needed a manual Connect (Window > MCP for Unity).
- A WebGL build attempted in the same Editor session that installed the module fails ("Build target 'WebGL' not supported"), but its build report still says Succeeded. The following incremental build then reused stale player data and produced a broken `.data`. **Use a clean build after first enabling WebGL.**
- `ProjectSettings/EditorSettings.asset` differs from the Plastic base (re-saved when a build started; not edited by hand). The integrator should review it in Unity Version Control's pending changes.
- `ProjectSettings.asset` was restored byte-identical to the Plastic base after each build. The Burst settings files the build created were removed.
- Unity Hub is installed as a Microsoft Store app. A first search missed it; the official module installer was used instead.
- Not verified: physical mouse clicks for Editor failure and same-seed restart; physical (non-CDP) clicks in a browser; other browsers, devices and network hosting.

## Public API

Unchanged in this checkpoint. Changes from the previous pass are in [0006](../../decisions/0006-event-fallback-and-unity-layout.md):
- the fallback-choice validator rule
- the end reason `content_error_no_available_choice` (outcome Failure)
- facility `uses` summed per resource
- `SimulationHost.Session`/`LoadError` and `ContentLoader`

[ARCHITECTURE](ARCHITECTURE.md) section 6 describes the rest.

## Next

1. Codex review of the checkpoint commit. Yasen authorises any push.
2. Fable's architecture review before the simulation expansion.
3. Integrator decisions:
   - Git vs Unity Version Control as the source of truth for `Assets/RedFrontier/`
   - whether `ProjectSettings/` and `Packages/` (and the MCP package) go into Git
   - the `Red Frontier/` path versus `game/`
   - review of `EditorSettings.asset`
4. Developers 2 and 3: accept or amend 0002, 0004, 0005 and 0006.
