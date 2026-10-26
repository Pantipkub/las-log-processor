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

## Part 2: Batch runner + conditioning + catalog ✅ (2026-10-26)
- `Program` (App): enumerates `*.las`/`*.LAS` (filtered by extension, not the `*.las` pattern, which also matches `.lasx`), sorted by name. Each file runs in its own try/catch; an exception (I/O, locked file, anything) gives a Failed row with `ExceptionType: message`. Writes `output/conditioning_summary.csv` (one row per file: status, first error, version, wrap, records, depth unit/top/base, original depth order, modal/header step, the four chosen sources, diagnostic counts per severity, distinct codes, elapsed ms). Prints one line per file plus totals and peak working set. `Program` is public so the tests can run a whole batch.
- `Conditioner.Run(file, catalog)` (Core) works in place on the loaded file, then calls the catalog. Order: declared NULL → reject rows with a missing depth → null policy on the other curves → depth checks → curve selection → `UpdateStatus()`.
  - Nulls: declared NULL → NaN (`NULL_TO_MISSING` per curve). Exact −999.25 is always missing; when NULL is something else (or missing/non-numeric: `NULL_MISSING`) that gives one `SENTINEL_MISMATCH` per file. `NULL_IS_ZERO` whenever NULL is 0. −9999/−999/−999.2 stay as data with `SUSPECTED_SENTINEL` per curve.
  - Depth: NaN/NULL depth → row rejected (`DEPTH_INVALID`, Error, so Partial). Strictly decreasing (repeats allowed) → all arrays and `RecordLines` reversed (`DEPTH_REVERSED`). Up and down → file order kept (`DEPTH_NON_MONOTONIC`, count = direction changes, plus repeated values). Repeats in monotonic data → `DEPTH_DUPLICATES`. `NEGATIVE_DEPTH` (Observation).
  - Steps: modal step = most common absolute interval (rounded to 1e-6). `STEP_MISMATCH` if it differs from |STEP| by more than 1 %. STEP 0 → `STEP_IRREGULAR` (Observation) and no gap check. `DEPTH_GAP` for intervals > 1.5 × modal (monotonic files only). `STRT_MISMATCH`/`STOP_MISMATCH` (Observations) against the file order, tolerance half a step.
  - Results on `LasFile`: `DepthOrder`, `ModalStep`, `Concepts` (null for files with no records). `LasFile.Add(...)` and `UpdateStatus()` are shared with the reader.
- `CurveCatalog` (Core) + `src/LasLogs.Core/curve_catalog.csv` (copied next to the exe and the tests). The CSV is the §F.7 tables (including their M/L rows such as `GAMMA`, `GR_CAL`); the "unresolved" names are left out. The unit table, blank-unit rules and QC ranges are constants in `CurveCatalog`.
  - Selection: catalog order (priority, then row order), then column order. Checks: unit in the concept's table (else `UNIT_UNRECOGNIZED`), neutron description exclusion (DECISIONS §5, revised), at least one valid value, blank unit decided from the 99th percentile (GR/RES always canonical; density p99 ≤ 5; neutron p99 ≤ 1.5 → v/v, ≤ 150 → percent). The same mnemonic passing twice → first one + `AMBIGUOUS_CURVE`.
  - `ConceptCurve`: `Source` (the file curve, never renamed or converted), `Values` in canonical units (the source array itself when the factor is 1), `Factor`, `Median`, and `Missing` for the plot label ("not in file", "present but all null", "rejected: …").
  - Codes: `CURVE_MAPPED`, `UNIT_CONVERTED` (Transformations); `UNIT_ALIAS` (`m.ohm`), `UNIT_ASSUMED`, `IMPLAUSIBLE_VALUES` (median outside the §8 range), `RES_NONPOSITIVE` (Warnings); `CONCEPT_UNAVAILABLE`, `NO_REQUIRED_CURVES` (Observations).
- Tests: 69 in total. `ConditionerTests` covers S10–S18, S20, S21 and the catalog rules; `ProgramTests` runs a batch with empty, binary and locked files (S2, S3, S23). `RealDataTests` now also checks the Part 2 table (about 20 s).

**Full-folder result** (203 files, about 20–30 s, peak working set 285 MB, Debug): 200 Loaded, 3 Partial, 0 exceptions.

| Check | Oracle | Now | Why different |
|---|---|---|---|
| Coverage GR / RES / RHOB / NPHI | 182 / 109 / 100 / 124 | 182 / 110 / 101 / 125 | +1 each from MAINCOR; GR also −1: `4246137956_UNIV_NG_5021H_LAS.las` GR unit `aapi` is not in the DECISIONS §4 table (`UNIT_UNRECOGNIZED`) |
| Depth reversed | 26 | 27 | MAINCOR (recovered in Part 1) is decreasing |
| Non-monotonic | 1 | 1 | `4200343170_STATE_AQ_8_RUN2.las`: 4 changes, first line 4465, 200 repeated depths |
| `NULL_IS_ZERO` | 5 | 5 | |
| K/M3 conversion | 1 file, median ≈ 2.518 | same | |
| `IMPLAUSIBLE_VALUES` on density in `4246135949_MAIN.las` | yes | yes (DEN median 0.194; GRGC 3.3 gAPI also flagged) | |
| `NPRL` in `4246135292_MAIN.las` | Limestone | Limestone | |

Other counts, for reference: `STEP_MISMATCH` 2, `STRT_MISMATCH` 2, `STOP_MISMATCH` 3, `DEPTH_GAP` 1 (`4246135949_MAIN`, made by the rejected rows), `NEGATIVE_DEPTH` 5, `SENTINEL_MISMATCH` 2 (the `4238336177` pair, 11,840 each), `SUSPECTED_SENTINEL` 8 (the report's 9 included that pair, which is now `SENTINEL_MISMATCH`, and had no −999.2 mud-log case), `RES_NONPOSITIVE` 19 (the report's 20 minus the `4238336177` pair, whose negatives were −999.25; plus the OPAL `MLR4C`, not investigated), `IMPLAUSIBLE_VALUES` 13 files, `NO_REQUIRED_CURVES` 20 files.

## Part 3: Plotting ✅ (2026-10-26)
- **Library:** `OxyPlot.WindowsForms` 2.1.2 (pulls in `OxyPlot.Core` 2.1.2), App project only; Core stays dependency-free. 2.1.2 is from Dec 2022 and targets net462+. Its `PngExporter` draws with GDI+ (System.Drawing, part of .NET Framework), so there are no native binaries (unlike the SkiaSharp/ImageSharp exporters). The spike confirmed on net48: reversed depth axis (`StartPosition=1, EndPosition=0`), `LogarithmicAxis`, two top axes in one track (`StartPosition`/`EndPosition` fractions + `PositionTier`), NaN breaks a `LineSeries`, PNG export. One `PlotModel` holds all three tracks, so the System.Drawing stitching fallback was not needed.
- `LogPlotter` (App, one file): `Plot(file, outputFolder)` writes `output/plots/<wellKey>/<file stem>.png` (1200 × 2400) and returns the relative path for the new `plot_file` column (between `codes` and `elapsed_ms`). Called inside `Program.ProcessFile`'s try/catch after `Conditioner.Run`, so a plotting exception gives a Failed row.
  - **wellKey:** first 10 digits (state + county + well) of the `UWI`, else `API`, header (value, then description, because some LAS 1.2 files keep the number in the other field); otherwise the file name without extension. So `42-003-40122`, `4200340122` and `42003401220000` share one folder. 64 folders for 203 files.
  - **Layout:** depth axis on the left (file's own unit, shallowest to deepest loaded depth). Tracks: GR 0–150 gAPI (green) | deep resistivity 0.2–20,000 ohm·m log (black) | bulk density 1.95–2.95 g/cm³ (red) + neutron 0.45 → −0.15 v/v (blue, one tier higher). Scales, colours and track positions are the arrays at the top of `LogPlotter`. Title = file name; subtitle = well name, depth range, status. Each track has its scale on top and the same scale at the bottom, with the out-of-scale counts as the bottom axis title (the "footer").
  - **Out of scale:** pure axis clipping made a curve that is entirely out of scale vanish (the spike showed it), so the plot copy **pins** such values to the track edge (stored values unchanged). `4246135949_MAIN.las` density (median 0.194) now runs down the 1.95 edge. Counts per curve in the footer, plus one `OUT_OF_SCALE` Observation per curve (count = below + above) so Part 4 can list them; 170 files have at least one. Resistivity ≤ 0 is a gap on the log axis, shown in the footer as "n <= 0 not drawn" (already reported as `RES_NONPOSITIVE` by the catalog).
  - **Line breaks:** NaN; the depth turning back (non-monotonic file: the jump itself is not drawn and the next step sets the new direction); and depth jumps > 1.5 × modal step in files where the Conditioner reported `DEPTH_GAP` (only `4246135949_MAIN.las`: its 3,054 rejected rows left a 4,150–5,700 ft hole that was first drawn as a straight line).
  - **Missing concepts:** all three tracks are always drawn; an empty track gets a centred label from `ConceptCurve.Missing` (density label above the middle, neutron below). Mud logs get a plot with four labels.
  - Paths use the `\\?\` prefix: the long dataset file names went over 260 characters under a deep output folder (16 files Failed in the first run).
- Tests: 71 (2 new in `LogPlotterTests`): a synthetic file with a null, a missing density, a value ≤ 0 and a depth reversal plots to a non-empty PNG under `plots/4200340122/`; `Points` pins, counts and breaks correctly.

**Full-folder result** (Debug, run into a scratch folder because `output/conditioning_summary.csv` was open in Excel): 203 files in 29.6 s, 200 Loaded, 3 Partial, 0 Failed, peak working set 349 MB (Part 2: 285 MB). 203 PNGs, 47 MB. Slowest file: `4200345990_20140426_LAS(masmp).LAS` (79,033 records × 125 curves) at 2.2 s.

**Checked by eye:** `39399.LAS`, `4200340122MAINCOR.las`, `4200343170_STATE_AQ_8_RUN2.las`, `4246135297UNIV_3_27_2_CNL.LAS` (CNL-only), `4231736403_UNIV_7_3_5_MUDLOG_LAS.las` (0 concepts), `4238337564_UNIV_10_13_5H_triplecmi_004_MD.las` (density ≈ 2.5 after K/M3 conversion), `4246135949_MAIN.las`, `4246137956_UNIV_NG_5021H_LAS.las` ("rejected: GR unit 'aapi' not recognised").

**Worth a look by the user:**
- `39399.LAS` above ~5,800 ft: resistivity sits on the 0.2 edge and density flips between both edges (11,303 and 8,478 points). That is the data (probably readings above the logged interval), drawn because nothing is hidden. It looks noisy.
- Non-monotonic file: the repeated intervals are drawn over each other in the same colour.
- A long resistivity footer can run a little into the gap between tracks.

## Next: Part 4 (report + README + delivery)
See `docs/PLAN.md` Part 4 and report §J.
- Out-of-scale counts are in the diagnostics (`OUT_OF_SCALE`, Observation, per curve); `plot_file` is in the summary.
- Still open: `DUPLICATE_FILE` (needs SHA-256) and the events CSV / Markdown report.
- Re-run into `output/` once `output/conditioning_summary.csv` is closed in Excel (the app cannot open it while it is locked, and that error is not caught in `Main`).
