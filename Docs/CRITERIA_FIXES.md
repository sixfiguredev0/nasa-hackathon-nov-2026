# NASA exercise and gradual guidance

## Historical observation

`Assets/Resources/Nasa/donki-sep-curated.json` is the unchanged offline fixture from the 15 November 2026 handoff. `HistoricalTraining` loads it with `Resources.Load<TextAsset>` and selects one record by its original ID:

- NASA CCMC DONKI / M2M_CATALOG, Moon to Mars Space Weather Analysis Office.
- ID: `2024-05-10T13:35:00-SEP-001`.
- Observation time: `2024-05-10T13:35Z` (UTC), distinct from submission time.
- Instrument: `GOES-P: SEISS >10 MeV`.
- [Original NASA record](https://ccmc.gsfc.nasa.gov/DONKI/view/SEP/30677/-1).
- [Archive query](https://ccmc.gsfc.nasa.gov/DONKI-API/get/SEP?startDate=2024-05-01&endDate=2024-05-20). The supplied retrieval manifest is retained alongside the fixture.

The loaded observation supplies the exercise's identity, displayed date and source details. This is archived data, with no runtime network access or API key. It is not a claim that this event struck the fictional Mars base, a live warning, a Mars dose measurement or a severity ranking based on an energy-band label. Marlonis's **NASA source** view states these boundaries.

## Authored game rules

After the basic supply objectives, a four-second preparation interval leads to one two-choice warning. These timings and consequences are educational game rules, not quantities supplied by NASA:

- **Shelter crew:** interrupt the existing work coroutine and walk into the lander using its existing entrance behavior. Preserve the complete job queue and accumulated build time. Once everyone is inside, hold construction for 20 game seconds, prevent the exercise's extra fitness dip, then resume saved work. Buildings already operating keep operating; normal upkeep and fitness changes continue.
- **Continue working:** keep queued construction moving, with up to an eight-percentage-point temporary fitness dip through the existing fitness system. After 20 game seconds, return the amount removed, clamped to the normal fitness range. Normal fitness decay/training continues independently, so this does not reset fitness to an earlier snapshot.

Both choices end in one short resolution, then free sandbox play continues. Pause freezes the game clock and consequences. Disabling/leaving the exercise clears its temporary consequence and releases shelter; a new mission starts fresh. Missing or invalid archive data produces an honest unavailable message and leaves sandbox building available.

## Staged teaching

The objective tracker and reopenable Marlonis guide teach Solar Panels, Ice Drill, Greenhouse, sufficient extra solar capacity, Habitat Dome, then the historical exercise. A building counts when commissioned, not when its blueprint is placed or a dialogue button is pressed. The extra power check includes actual site yields, fitness, existing electrical loads and the habitat's upcoming demand. Already completed milestones are recognized even when built in a different order.

All five resources, costs, normal production, upgrades and free building remain in place. The crash opening is unchanged, and the exercise cannot start during it. No additional hazards, resources or progression systems are introduced.

## Verification

Use Unity **2022.3.18f1**. Run **Red Frontier > Outpost > Verify Criteria Fixes** in the editor. This first runs the existing economy self-test, then uses the real menu buttons, opening, outpost, crew jobs and pause-menu scene transitions. Its report is written to the ignored `Logs/criteria-results.txt`.

The integration checks provision resource stock and accelerate construction portions with `Time.timeScale`; they do not establish normal-economy balance or long-session usability. They check provenance and invalid data, milestone progression, reopening guidance, UI raycasts/input exclusion, both choices, saved partial work and multiple queued jobs, pause, recovery, post-event building and fresh missions after menu transitions.

On 15 November 2026, Unity 2022.3.18f1 compiled the scripts, the existing economy self-test reported **ALL PASS**, and the graphical editor integration suite passed **46 assertions** (including repeated fresh-mission checks). The first menu-to-outpost run played the complete opening; later runs used its existing skip/finish path. Source and choice screenshots were inspected, as were the running guide and shelter resolution. The fixture's SHA-256 matched the supplied handoff byte for byte. The task diff was checked for whitespace, scope and common credential patterns.

Remaining limitations: no exported Windows/WebGL player build, child usability study, broad resolution matrix, long-session balance or reliability test. The editor's free-aspect Game view showed Unity's odd-resolution Pixel Perfect Camera warning at 1280 x 619; render settings and the active HUD were outside this change. The integration suite invokes the real UI button listeners and checks their raycasts/input exclusion, rather than simulating an entire mouse-driven building session.
