# LasLogs

Loads LAS well-log files with a hand-written parser, conditions the data, and plots each file as a three-track log: gamma ray | resistivity (log scale) | bulk density + neutron porosity. It also writes a per-file conditioning report.

## How to build and run

Requirements: Windows, the .NET SDK (any recent version; tested with 9.0.203), and the .NET Framework 4.8 runtime. The 4.8 reference assemblies come from NuGet, so the Developer Pack is not needed.

```bash
dotnet build LasLogs.sln
dotnet test LasLogs.sln
dotnet run --project src/LasLogs.App -- <input-folder> <output-folder>
```

You can also open `LasLogs.sln` in Visual Studio 2022.

## Design

*To be written (Part 4).*

## Data-conditioning decisions

See [docs/DECISIONS.md](docs/DECISIONS.md). A summary with real examples will be added here in Part 4.

## Known limitations

*To be written (Part 4).*

## How I used AI

*To be written (Part 4).*
