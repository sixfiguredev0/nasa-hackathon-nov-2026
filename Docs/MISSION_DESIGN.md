# Hard mission, seeded events and the short opening

## What changed
- **Crew can die.** Oxygen out for 25 s, water out for 60 s or food out for 100 s kills one astronaut (`ColonyEconomy.DeathAfter`). Both dead = mission failed.
- **A mission with an end.** Survive 7 sols (100 s each, about 12 min). A day/night cycle stops solar power at night (`MissionPlan.DaylightFactor`), so batteries matter.
- **Seeded random events** (`MissionPlan`, run by `MissionDirector`). Every mission has the NASA-based solar particle burst and a dust storm, plus one or two of: equipment failure, resupply delay. Order, timing and severity come from the seed. The seed also builds the map. Same seed = same mission.
- **Debrief** (`MissionDebrief`) on success and failure: what happened, why a crew member died, what each event teaches, seed, NASA attribution. Buttons: same seed, new mission, title.
- **Solar burst choice now costs more**: shelter stops building for 25 s; working costs 15 fitness points; no answer in 25 s means the crew kept working.
- **Harder economy**: crew upkeep x1.75; starting stock 30/40/50/50/60 (the scene's serialized stock is no longer used); Lander Metal income 1.0/1.8/3.5 per second (was 0.4/1/2) because the old rate made a viable base impossible to build in time once deaths existed.
- **Opening cutscene**: now about 15 s, any key or click skips. The original 30 s version is kept compiled in `Scripts/Outpost/Archive/OpeningCutsceneLegacy.cs`; tick `Colony.useLegacyOpening` to play it.

## Economy simulation
Run it: Unity menu **Red Frontier > Outpost > Simulate Economy** (writes `Logs/economy-sim.txt`), or without Unity:
`dotnet run --project Tools/EconomySim -- 1000` (see the csproj for the Unity path).
It runs the real `ColonyEconomy` and `MissionPlan`; the players are crude bots (fixed or adaptive build order, one crew job at a time, no site choice).

Result, 1000 seeds, final rules:

```
== mission rules | start stock E/W/F/O/M = 30/40/50/50/60 | upkeep x1.75 | crew 2 | 1000 seeds x 7 sols (100 s each) ==
Smart  survive  83.1% | all crew  74.9% | lost one   8.2% | wiped out  16.9% | first loss from O2/water/food 76/175/0 | median wipe-out sol 6
Guided survive  74.7% | all crew  48.3% | lost one  26.4% | wiped out  25.3% | first loss from O2/water/food 504/13/0 | median wipe-out sol 3
Naive  survive   0.0% | all crew   0.0% | lost one   0.0% | wiped out 100.0% | first loss from O2/water/food 0/1000/0 | median wipe-out sol 2
Idle   survive   0.0% | all crew   0.0% | lost one   0.0% | wiped out 100.0% | first loss from O2/water/food 1000/0/0 | median wipe-out sol 2
PASS: idle loses 100% (need >=99), naive loses 100% (need >=90), smart wins 83% (need 70-95), smart keeps full crew 75%
```

How to read it: *Smart* builds adaptively (life support, then solar and batteries sized to the night, Gym, upgrades) and shelters during the burst. *Guided* follows Marlonis's tutorial order exactly. *Naive* builds a Greenhouse and Workshop first, forgets the Gym and batteries and keeps working. *Idle* builds nothing.
Findings that drove the tuning: before this work the economy collapsed around minute 11 even for decent players (fitness decay halves output, which starves power); once deaths exist the Lander's 0.4 Metal/s made a viable base impossible in time; the scene's own starting stock (O2 20) was unwinnable.

## Not verified
- Nothing here has been played in the Unity editor. All runtime and editor code compiles against Unity 6000.6.6f1 assemblies (0 errors, with a stub for the 2D pixel-perfect camera) and the economy self-test passes (`ALL PASS`, including new checks), but the HUD layout, debrief screen, new cutscene timing and crew-death animation have not been looked at.
- `CriteriaVerification` (the editor integration suite) was updated for the new flow (director disabled, solar burst triggered explicitly) but has not been run.
- The bots are not people. 75-83% for a competent bot and 0% for careless play says the rules are in a sane range; real win rates need human playtests.
- Project version is 2022.3.18f1; the compile check used 6000.6.6f1.
