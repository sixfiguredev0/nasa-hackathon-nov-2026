# game/sim: RED FRONTIER simulation core (plain C#)

Developer 1. Engine-free rules and state (`Core/`, namespace `RedFrontier.Core`) and NUnit tests (`Tests/`). Not a Unity project. When the combined Unity project exists, `Core/` moves to `Assets/RedFrontier/Core/` with `git mv` (see `docs/planning/dev1-simulation/ARCHITECTURE.md` section 8). The `.csproj` files stay outside `Assets/`.

## Commands

Needs the .NET 8 SDK (`dotnet --version` 8.x).

```bash
dotnet build Core/RedFrontier.Core.csproj
dotnet test Tests/RedFrontier.Core.Tests.csproj
```

`Core` builds as `netstandard2.1`, C# 9, warnings as errors, no packages, no `UnityEngine`: the constraints Unity 6 is expected to need (unverified against the real Editor).

## Layout

| Path | What |
|---|---|
| `Core/GameSession.cs` | The only authoritative object. Commands, queries, snapshot, forecast, report, history. |
| `Core/SolResolver.cs` | Sol steps 1 to 3 (facilities, crew needs, health). No randomness. |
| `Core/EventDirector.cs` | Step 7: major milestone first, else seeded random pool. Resolves variants once. |
| `Core/EffectApplier.cs` | Affordability check and immediate effects. |
| `Core/ContentValidator.cs` | Config and event checks, cost-free-choice lint. |
| `Core/Definitions.cs`, `State.cs`, `Reports.cs`, `Rng.cs` | Data shapes and the seeded generator. |
| `Content/example/` | Illustrative JSON in the proposed schema. Not final content or balance. |
| `Tests/` | Stage 1 acceptance tests, rule tests, hybrid-event tests, example-content tests. |
