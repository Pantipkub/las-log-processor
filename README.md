# LasLogs

Loads LAS well-log files with a hand-written parser, conditions the data, and plots each file as a three-track log: gamma ray | deep resistivity (log scale) | bulk density + neutron porosity. It also writes a conditioning report that says, per file, what was changed and what was only reported.

New to well logs? Read [docs/GUIDE.md](docs/GUIDE.md) first: it explains the curves, the pipeline and every decision in plain words.

## How to build and run

Requirements: Windows, the .NET SDK (any recent version; tested with 9.0.203), and the .NET Framework 4.8 runtime. The 4.8 reference assemblies come from NuGet, so the Developer Pack is not needed.

```bash
dotnet build LasLogs.sln
dotnet test LasLogs.sln
dotnet run --project src/LasLogs.App -- <input-folder> <output-folder>
```

For this dataset: `dotnet run --project src/LasLogs.App -- ../TestData/TestData output`. You can also open `LasLogs.sln` in Visual Studio 2022.

Every `.las`/`.LAS` file in the input folder is processed (the dataset has 203; the brief mentions 50). The output folder gets:

| Output | Content |
|---|---|
| `plots/<well key>/<file>.png` | One 1200 × 2400 log plot per input file. Well key = first 10 digits of the UWI/API header, else the file name. |
| `conditioning_summary.csv` | One row per file: status, reason, version, records, depth range and order, the four curves used, diagnostic counts, codes, plot path, time. |
| `conditioning_events.csv` | One row per diagnostic: file, well key, status, severity, code, curve, count, first/last source line, message. |
| `conditioning_report.md` | Dataset overview, curves used, out-of-scale counts, every code with its file count, duplicates, and the warnings/errors per file. |

Exit codes: 0 done (individual files may still be Failed; see the summary), 2 bad arguments or missing catalog, 3 an output file cannot be written (for example `conditioning_summary.csv` is open in Excel: close it and run again).

**Final run** (2026-10-27, Debug build via `dotnet run`, one thread, on the development laptop): 203 files in **33.1 s**, **peak working set 309 MB**. 200 Loaded, 3 Partial, 0 Failed. 203 PNGs (45 MB), 4,594 events.

Tests: 73 xUnit tests (`dotnet test`, about 1 s). One of them is opt-in and checks the investigation's numbers on the real dataset: set `LAS_TESTDATA` to the dataset folder and run `dotnet test --filter Category=RealData` (about 20 s); without the variable it returns at once.

## Design

```
LAS file ─► LasReader ─► Conditioner ─► CurveCatalog ─► LogPlotter ─► ReportWriter
            (parse)      (nulls, depth)  (pick 4 curves,  (PNG)         (3 report files,
                                          units, QC)                    rows streamed)
```

- **Three projects.** `LasLogs.Core` (no dependencies): `LasReader`, `Conditioner`, `CurveCatalog` + `curve_catalog.csv`, `ReportWriter`, and the model (`LasFile`, `CurveDef`, `Diagnostic`). `LasLogs.App`: `Program` (batch loop) and `LogPlotter` (OxyPlot 2.1.2, PNG through GDI+). `LasLogs.Tests`: xUnit.
- **Hand-written parser**, as the assignment requires. It streams line by line, reads bytes as Latin-1 (never the machine's code page), parses numbers with the invariant culture, and never throws on bad content: problems become diagnostics.
- **One file in memory at a time.** Each file is read, conditioned, plotted and written to the reports, then dropped. Report rows are streamed; only counts and the warning lines for the Markdown are kept.
- **A bad file never stops the batch.** Each file runs in its own try/catch; an exception becomes a Failed row with a `FILE_FAILED` error. The three report files are opened before the batch, so a locked output stops the run at once with a clear message.
- **Curves are identified by column position**, not by name, because mnemonics repeat (`VS` three times in one file, `NPRL` twice in `4246135292_MAIN.las`).
- **Missing = `NaN`.** The depth column is the index every other array is aligned with. Nothing is resampled, interpolated, sorted or clamped.
- **Diagnostics** have a stable code, a severity (Observation, Transformation, Warning, Error), a count and the first/last source line. They are aggregated (one per file + code + curve), not one per value. The code that makes a change is the code that records it, so the report never claims a change that did not happen. Tests assert codes, not message text.
- **The curve catalog is data** (`src/LasLogs.Core/curve_catalog.csv`: concept, mnemonic, priority, confidence), so a petrophysicist can edit it without recompiling. The unit table and QC ranges are constants in `CurveCatalog`; the display scales are constants at the top of `LogPlotter`.
- **Duplicates:** the SHA-256 of each file's bytes is compared with the earlier files. A copy gets a `DUPLICATE_FILE` observation but is still processed and plotted.

## Data-conditioning decisions

The full, binding rules are in [docs/DECISIONS.md](docs/DECISIONS.md); [docs/GUIDE.md](docs/GUIDE.md) explains why. One real example each (from the final run):

| Decision | What the program does | Real example |
|---|---|---|
| Declared NULL | becomes `NaN` (`NULL_TO_MISSING`, counted per curve) | `39399.LAS`: 10 of 23,435 `RHOZ` values equal −999.25 → missing |
| −999.25 when NULL is something else | also `NaN` (`SENTINEL_MISMATCH`) | `4238336177MAIN.las` declares NULL 0 but holds 11,840 × −999.25 |
| NULL = 0 | honoured, with a warning that real zeros are lost (`NULL_IS_ZERO`) | `4231736403_UNIV_7_3_5_MUDLOG_LAS.las`: 95,482 zeros → missing |
| Undeclared placeholders (−9999, −999, −999.2) | kept as data (`SUSPECTED_SENTINEL`) | `4238338546_University_2505_3HM_.las`: `RD` has 400 × −9999 |
| A curve that has **only** placeholders | not used for the plot; the track says why (added 2026-10-27) | `4200343170_STATE_AQ_8_MUDLOG.las`: `GAMMA` is −999.2 in all 10,781 records |
| Non-numeric tokens | `NaN` (`NONNUMERIC_VALUE`) | `4200340740PnrUniv7-23_1.las`: 12 × `********` in `DEN` |
| Rows with the wrong value count | rejected, never guessed; file is Partial (`RECORD_REJECTED`) | `4246135949_MAIN.las`: lines 8247–11300 have 28 values, 29 declared (3,054 rows) |
| Truncated last record / binary junk line | rejected; file is Partial | `4200340123MAIN.las` line 13447 (12 of 23 values); `4200340740PnrUniv7-23_1.las` line 10508 |
| Undeclared wrap | re-read as wrapped only if fully consistent (`UNDECLARED_WRAP`) | `4200340122MAINCOR.las` → 20,645 records |
| Decreasing depth | all arrays reversed together (`DEPTH_REVERSED`, 27 files) | `4200340122HBCmsf3.las`: 10,383 → 555.5 ft, 19,656 records |
| Depth that goes up and down | file order kept, not sorted (`DEPTH_NON_MONOTONIC`) | `4200343170_STATE_AQ_8_RUN2.las`: 4 direction changes, first at line 4465, 200 repeated depths |
| Sampling | original depths kept; header STEP and gaps reported (`STEP_MISMATCH`, `DEPTH_GAP`) | `4200340125MAIN.las`: STEP 0.75 declared, every interval is 0.5 |
| Units | only the four plotted curves are converted, by the declared unit (`UNIT_CONVERTED`) | `4238337564_UNIV_10_13_5H_triplecmi_004_MD.las`: `DEN` K/M3 × 0.001 → median 2.518 g/cm³ |
| Blank unit | accepted only when the values are unambiguous (`UNIT_ASSUMED`) | `4200340122MAIN.las`: `RLLD` unit blank → ohm·m |
| Unknown unit spelling | curve not used (`UNIT_UNRECOGNIZED`) | `4246137956_UNIV_NG_5021H_LAS.las`: GR unit `aapi` |
| Values that contradict the unit | warned, never "fixed" (`IMPLAUSIBLE_VALUES`, median outside a typical range) | `4246135949_MAIN.las`: `DEN` median 0.194 g/cm³ (columns shifted by the bad rows) |
| Curve selection | catalog priority, unit family, neutron description check, at least one real value | `39399.LAS`: `GR` over `ECGR`/`HGR`; `4246135292_MAIN.las`: `NPRL` "Limestone" over `NPRL` "Crossplot" |
| Missing curve | the track is drawn with a reason label (`CONCEPT_UNAVAILABLE`) | `4231736403_UNIV_7_3_5_MUDLOG_LAS.las`: four labels, no curves |
| Plot scales | fixed: GR 0–150 gAPI, resistivity 0.2–20,000 ohm·m (log), density 1.95–2.95 g/cm³, neutron 0.45 → −0.15 v/v | same scales on all 203 plots |
| Out-of-scale values | drawn at the track edge and counted in the footer and the report (`OUT_OF_SCALE`); stored values unchanged | `39399.LAS`: `RHOZ` 2,345 below 1.95 and 8,478 above 2.95 |
| Byte-identical files | processed and flagged (`DUPLICATE_FILE`) | 11 pairs, e.g. `4238336177MAIN.las` = `4238336177_MAIN.las` |

## Known limitations

- **Placeholders are only flagged.** Undeclared −9999/−999/−999.2 stay as data unless a curve has nothing else. About 45 Schlumberger files have one junk value near minus the depth at the tool's first sample (e.g. `4200340686_ChkUniv5-26_1_Final_LAS.las` line 141); it is drawn at the track edge and counted, not removed.
- **Column-order problems are detected, not fixed.** `4246135949_MAIN.las` is plotted with a density median of 0.194 and an `IMPLAUSIBLE_VALUES` warning.
- **Units are trusted.** A unit that is wrong in the file stays wrong (only a QC warning). Spellings outside the table (`aapi`) are not mapped.
- **`NULL = 0` loses real zeros** in 5 files; the source format cannot tell them apart.
- **Fixed display scales and QC ranges are conventions**, not computed from the data. A petrophysicist should tune them for this basin. A curve far outside the scale shows as a line on the track edge (e.g. `39399.LAS` above about 5,800 ft, probably a cased-hole interval: resistivity on the 0.2 edge, density flipping between both edges).
- **Plot details:** in the non-monotonic file the repeated intervals are drawn over each other in the same colour; a long resistivity footer can run into the gap between tracks.
- **Scope:** deep resistivity only (no shallow overlay); one plot per file (no splicing of runs or wells); depth in the file's own unit (no metric conversion); no resampling.
- **LAS 3.0:** the LAS 2.0-compatible subset only; extra sections are skipped and only the first data set is loaded.
- **Wrapped files:** a record short by exactly one value swallows the next depth line, and the record after it is rejected (not seen in this dataset).
- **Well grouping** uses the header UWI/API as written. A typo moves a file to another group: `4200343170_STATE_AQ_8_MUDLOG.las` says `API 43-003-43170`, so its plot is under `plots/4300343170/`.
- **File count:** the brief says 50 files; all 203 in the folder are processed. Duplicates are processed twice (and flagged).
- **Windows only:** .NET Framework 4.8 and a GDI+ PNG exporter. One thread: about 33 s for 1.07 GB. For thousands of files, run files in parallel (memory ≈ threads × largest file).

## How I used AI

I used Claude Code (Anthropic's coding assistant) as a pair programmer, in small steps with my review at each one:

- **Investigation:** Claude wrote read-only Python scripts that scanned all 203 files and wrote an investigation report (not part of the submission). The numbers in `docs/PLAN.md` come from it and serve as acceptance checks.
- **Decisions:** Claude proposed the conditioning rules with evidence from the files; I chose and confirmed each one (dates in `docs/DECISIONS.md`). For example, after seeing an empty GR track in a mud log, I approved the rule for curves with only placeholder values.
- **Code and tests:** Claude wrote most of the C# code and the unit tests, one part per session (`docs/PLAN.md`, `docs/PROGRESS.md`), under the rules in `CLAUDE.md` (net48 and C# 7.3, no LAS libraries, Latin-1 reading, no resampling). I ran the build and the tests, reviewed the changes, and checked the plots by eye.
- **Checks against the raw data:** when something looked odd I asked for it to be traced back to the source file, e.g. the noisy shallow part of `39399.LAS` (real values in the file, not nulls).
- **Docs:** Claude drafted this README and `docs/GUIDE.md`; I reviewed and approved them.
