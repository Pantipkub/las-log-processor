# LasLogs: project rules for Claude

C# console app for the PetroRes take-home: load LAS well-log files with a hand-written parser, condition the data, and plot one 3-track log (GR | resistivity on a log axis | bulk density + neutron porosity) per input file. It also writes a conditioning report. Deadline: 2026-10-28 20:00 ICT.

## Read first
- `docs/PLAN.md`: the parts and their acceptance criteria. Work on **one part per session**.
- `docs/PROGRESS.md`: what is done and the notes left for the next part. **Update it and commit at the end of every part.**
- `docs/DECISIONS.md`: the confirmed data-conditioning policy. It is binding: don't re-decide; ask the user before changing it.
- Full investigation (outside the repo, about 200 KB; read only the section you need): `../TestData/analysis/LAS_Investigation_Report.md`. The useful sections are §F (findings per topic), §F.7 (curve catalog), §I (test cases) and §J (report design).

## Commands
```bash
dotnet build LasLogs.sln
dotnet test LasLogs.sln
dotnet run --project src/LasLogs.App -- ../TestData/TestData output
```

## Data
- Real dataset: `../TestData/TestData/` (203 LAS files, 1.07 GB). **Read-only. Never modify or commit it.**
- Tests use small fixtures in `tests/LasLogs.Tests/Fixtures/` (excerpts of real files; name the source file). `.gitattributes` keeps `*.las` bytes exact.

## Hard rules
- Target `net48`, **C# 7.3**: no records, switch expressions, nullable reference types, `Span<T>` or `System.Text.Json`.
- **No LAS-parsing libraries** (assignment rule). OxyPlot is allowed for plotting (added in Part 3).
- Parse numbers with `CultureInfo.InvariantCulture` and `NumberStyles.Float`. Then check for NaN/Infinity: in .NET Framework, `"1E400"` fails to parse.
- Read files with `Encoding.GetEncoding(28591)` (Latin-1). **Never use `Encoding.Default`** (it is Thai cp874 on the dev machine).
- Stream line by line. Keep one file in memory at a time. A bad file must never crash the batch.
- Never resample, interpolate, sort overlapping depths, or clamp stored values.

## Style
- Keep it small: three projects (Core = parsing, conditioning and reporting with no dependencies; App = console + plotting; Tests). No interfaces with only one implementation, no DI containers.
- Diagnostics have stable string codes (e.g. `NULL_IS_ZERO`); tests assert codes, not message text.
- Code, comments, docs and commit messages are in English. **Chat with the user in Thai** (simple Thai; keep technical terms in English).
