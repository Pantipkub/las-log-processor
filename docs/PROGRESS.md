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

## Part 1: LAS reader ✅ (2026-10-26)
- Core files: `Diagnostic.cs` (`Severity`, aggregated `Diagnostic`), `LasFile.cs` (`LasFile`, `HeaderItem` + `HeaderItem.Parse`, `CurveDef`, `FileStatus`), `LasReader.cs`.
- `LasReader.Read(path)` / `Read(TextReader, name)`. Latin-1, no BOM detection (a stray FF FE must not switch to UTF-16); a UTF-8 BOM is stripped by hand. It never throws on bad content. I/O errors from `Read(path)` still throw: the Part 2 runner catches them.
- **Header split rule (differs from the PLAN wording):** the description starts at the **first `:` that follows whitespace**, otherwise at the **last `:`**. A plain last-colon rule breaks real lines (`EKB .F 3000.0 : Elevation: K.B.`, mud-log `CUTTYP … :27 Type of Cut: (…)`, `{YYYY/MM/DD hh:mm}`). The first-colon rule breaks time values. This rule handles every multi-colon line in the dataset. Tests: `HeaderItemTests`.
- Sections: LAS 1.2/2.0 use the first letter after `~`. LAS 3.0 uses the name (`~Comment_Definition` is not `~Curve`). Everything after the first `~A` is skipped (`SECTION_SKIPPED`, `MULTIPLE_DATASETS`).
- Data:
  - Unwrapped rows need exactly one value per curve, otherwise `RECORD_REJECTED`.
  - Wrapped records start with the depth alone on its line. An overflowing record is rejected and reading resumes at the next 1-token line. An incomplete last record gives `TRUNCATED_RECORD`. Known ceiling (`ponytail:` comment): a record short by exactly one value swallows the next depth line, and the record after it is then rejected.
  - Undeclared wrap: if fewer than half of the first 20 data lines fit `WRAP NO`, the reader reads the file as wrapped. It accepts only with zero rejects and a constant depth step; otherwise an `UNDECLARED_WRAP` Error and status Failed.
  - `DLM COMMA` splits on commas; space and tab mean any whitespace.
- **Values are raw.** The NULL policy is not applied (Part 2). Non-numeric tokens (`********`, `(null)`, `NaN`, `1.#INF`, `1E400`, `1,5`, `1.0D+02`) are already `NaN`, with `NONNUMERIC_VALUE` per column (the message names the column, because `VS` appears 3 times in one file).
- Status: Failed = 0 records; Partial = any Error diagnostic (rejected rows, binary line, truncated record); otherwise Loaded.
- Reader diagnostic codes:
  - Observation: `HEADER_NO_DOT`, `HEADER_NO_COLON`, `SECTION_SKIPPED`, `MULTIPLE_DATASETS`, `WRAP_MISSING`, `BLANK_LINE`, `COMMENT_LINE`, `CTRL_Z`.
  - Warning: `EMPTY_MNEMONIC` (named `CURVE_<n>`), `DLM_UNKNOWN`, `NONNUMERIC_VALUE`, `UNDECLARED_WRAP`.
  - Error: `RECORD_REJECTED`, `TRUNCATED_RECORD`, `BINARY_LINE`, `UNDECLARED_WRAP`, `DATA_BEFORE_CURVES`, `NO_CURVES`, `NO_DATA_SECTION`, `NO_RECORDS`.
- `LasFile.RecordLines[i]` is the source line of record i, so Part 2 can cite lines (non-monotonic depth, etc.).
- Tests: 36 unit tests (S1–S9, S19, S22, S24, S25, LAS 3.0 names, 5 real-file excerpts in `tests/LasLogs.Tests/Fixtures/`, see its README). One opt-in test checks the whole PLAN table; set `LAS_TESTDATA` to the dataset folder and run `dotnet test --filter Category=RealData` (about 18 s).

**Full-folder result:**
- 203 files, 0 exceptions. VERS 2.0 ×140, 1.2 ×52, 3.0 ×11. 200 Loaded, 3 Partial (`4200340123MAIN`, `4200340740PnrUniv7-23_1`, `4246135949_MAIN`).
- Record and curve counts match `per_file_summary.csv` for all 203 files, except MAINCOR, which is now recovered (20,645 records).
- About 18 s and a 260 MB peak working set (Debug build).
- Ctrl-Z: 7 files, not the report's 8. The report's 8th (`PnrUniv7-23_1`) is the 0x1A inside the junk line 10508, which is now `BINARY_LINE`.

## Next: Part 2 (batch runner + conditioning + catalog)
See `docs/PLAN.md` Part 2 and DECISIONS §1–5, §8.
- Read NULL from `WellItems` (mnemonic `NULL`, raw text in `Value`). The reader has not touched sentinel values.
- Still to do from report §I.2: S10–S18, S20 (NaN/null depth rows), S21, S23 (locked file → Failed in the runner).
