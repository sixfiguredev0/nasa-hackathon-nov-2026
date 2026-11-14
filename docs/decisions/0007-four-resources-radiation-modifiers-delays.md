# 0007: Four resources, radiation, timed modifiers and delayed effects

- Status: Proposed (Developer 1; Developers 2 and 3 to review). Supersedes the Stage 5 "rejected" rows in ARCHITECTURE section 7 and the earlier exclusion of Water and radiation.
- Date: 2026-11-14

## Decision

All values are **illustrative placeholder game values**, not NASA data, physical dose units or medical thresholds.

**Resources.** Still data-driven (`mission.json`); no new resource system. `power` = Energy, `air` = Oxygen (air no longer stands in for water), plus `water` and `food`. Each has start/capacity, crew need with a health penalty, facility costs and outputs. Example facilities: `water_recycler` (power -> water), `greenhouse` (power + water -> food), `radiation_shelter` (power, `shielding`). Facilities that cannot pay in full drop a level (unchanged rule), so production is never unaffordable.

**Radiation (step 3a of sol resolution).** `exposure = mission.radiationPerSol + state.radiationPending` (pending = units added today by `radiation` effects). `blocked = min(exposure, sum of shielding of levels that actually ran)`. `net = exposure - blocked`; Crew Health loses `net x radiationDamagePerUnit` once. `radiationPending` is consumed by that resolution; `radiationTotal` accumulates `net`. Forecasts run on a copy, so they show but never consume exposure.

**Health (step 3b).** Shortage penalties and radiation damage apply; recovery happens only on a sol with neither, so recovery never cancels damage. Health 0 after step 5 fails the mission (existing rule).

**Timed modifier** `{type: modifier, target: facility, percent, durationSols}`: active on sols S..S+d-1, where S is the sol it takes effect (choice sol, or due sol if delayed). Scales facility **output only**: `floor(amount * (100 + sum) / 100)`, minimum 0. Modifiers on the same facility **stack additively**; each expires independently in step 4 (removed when `lastSol < new sol`). Percent must be non-zero and >= -100.

**Delayed effect** (`delaySols` k >= 1 on any effect except a resource cost): queued at choice time with `dueSol = sol + k`; in step 5 of the advance that enters `dueSol` it is applied once, in queue order, removed from the queue, logged (`delayed_effect`) and listed in that `SolReport.delayedApplied`. Resource costs cannot be delayed (costs are paid when chosen; validator error).

**Order of an advance (amended after review of 416e005):** 1 facilities (with modifiers) -> 2 crew needs -> 3a radiation -> 3b health -> 5 delayed effects due on the next sol -> 4 sol+1 and modifier expiry -> 6 failure/success -> 7 events. Step 5 runs inside the shared resolution, so the forecast and `SolReport` include it (`ResourceReport.fromEffects`, health cause `delayed_effect`). A delayed modifier still starts on its due sol.

**Death latch:** once Crew Health is 0 in steps 2-3b or after any delayed effect, no further delayed effect applies (no revival); the mission fails in step 6. Unapplied queued effects stay in the ended state and never apply.

**Restart** builds a fresh state: modifiers, scheduled effects and radiation are cleared.

No RNG is used by any of this; determinism and hybrid-event rules are unchanged.

## Consequences

- Developer 2: `SimState.modifiers`, `scheduled`, `radiationPending`, `radiationTotal`; `SolReport.radiationExposure/Blocked/Net`, `delayedApplied`; `FacilityReport.modifierPercent/shielding`. Health cause code `radiation`.
- Developer 3: effect types `modifier` and `radiation`, `delaySols`, facility level `shielding`, mission `radiationPerSol`/`radiationDamagePerUnit`. Every event still needs a fallback choice; a radiation-only choice counts as a fallback (it has no resource cost).
