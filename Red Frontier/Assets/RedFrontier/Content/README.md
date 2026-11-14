# Placeholder content (illustrative)

**Placeholder for the Developer 1 slice. Not final content, not final balance, not NASA observations.** Developer 3 owns real event content.

- `Resources/mission.json`: mission config (Power/Energy, Air/Oxygen, Water, Food, Crew Health, radiation, five facilities, 10 sols).
- `Resources/Events/<id>.json`: one event per file. Two milestones (sols 3 and 7, the first with three seeded variants; the dust storm fallback adds a timed modifier, the resupply a delayed food delivery) and three random events (one is the radiation solar flare).

Loaded at runtime by `Runtime/ContentLoader.cs` (JsonUtility, `Resources.Load`). The same files are checked by the Unity EditMode `ContentLoaderTests` and by the .NET `ExampleContentTests`.

Rule (decision 0006): every event and variant needs a **fallback choice**: no `requires` and no negative `resource` effect. A health loss is allowed. Content without one fails validation.

Schema: `docs/planning/dev1-simulation/ARCHITECTURE.md` section 7 and decisions 0005, 0006 and 0007.
