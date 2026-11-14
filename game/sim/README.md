# game/sim: .NET build and tests for the RED FRONTIER simulation core

Developer 1. **The authoritative sources now live in the Unity project** at `Red Frontier/Assets/RedFrontier/` (decision 0006). This folder keeps only the plain .NET projects, which include those sources by path, and the .NET-only tests.

| Path | What |
|---|---|
| `Core/RedFrontier.Core.csproj` | Compiles `Red Frontier/Assets/RedFrontier/Core/**/*.cs` as `netstandard2.1`, C# 9, warnings as errors. |
| `Tests/RedFrontier.Core.Tests.csproj` | NUnit (NuGet). Includes the shared tests from `Assets/RedFrontier/Tests/EditMode/Shared/` and the content JSON from `Assets/RedFrontier/Content/Resources/`. |
| `Tests/ExampleContentTests.cs` | .NET-only: parses the content with System.Text.Json. Unity checks the same files with JsonUtility in `ContentLoaderTests`. |

## Commands

Needs the .NET 8 SDK. On this workstation a workspace-local SDK (8.0.428) is in `_tools/dotnet` (not on PATH; set `DOTNET_CLI_HOME` and `NUGET_PACKAGES` under `_tools` to keep caches out of the repo). Result on 2026-11-14: build 0 warnings, tests 47/47 passed.

```bash
dotnet build Core/RedFrontier.Core.csproj
dotnet test Tests/RedFrontier.Core.Tests.csproj
```

In Unity: Window > General > Test Runner > EditMode > `RedFrontier.Tests.EditMode`.
