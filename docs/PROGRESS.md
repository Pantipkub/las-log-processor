# Progress

## Part 0: Skeleton ✅ (2026-10-26)
- Solution `LasLogs.sln` with three SDK-style projects:
  - `src/LasLogs.Core`: library, empty for now.
  - `src/LasLogs.App`: console exe, prints usage only.
  - `tests/LasLogs.Tests`: xUnit 2.9, no tests yet.
- Shared settings live in `Directory.Build.props`: `net48`, C# 7.3, and the `Microsoft.NETFramework.ReferenceAssemblies` package (so the build doesn't need the Developer Pack).
- `Prefer32Bit=false` on the App.
- `.gitattributes` keeps `*.las` bytes exact for fixtures. `.gitignore` excludes `bin/`, `obj/`, `output/` and `TestData/`.
- VS Code: `.vscode/tasks.json` (build) and `.vscode/launch.json` (`clr` debugger, runs over `../TestData/TestData`). The C# Dev Kit extension is installed.

**Environment:** .NET SDK 9.0.203, .NET Framework 4.8.1 runtime, VS Community 2022 (fallback debugger), Git 2.46, VS Code 1.143.

**Not verified:** F5 debugging of the net48 exe in VS Code. Try it once; if it fails, debug in VS 2022 (open `LasLogs.sln`).

## Next: Part 1 (LAS reader)
See `docs/PLAN.md` Part 1. Start from report §F.1–F.4 (section/header/data findings) and §I (test list).
