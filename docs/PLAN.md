# Plan

One part per Claude session. Each part ends with: tests green → `docs/PROGRESS.md` updated → one commit (or a few).
Expected numbers come from the investigation (`../TestData/analysis/LAS_Investigation_Report.md`) and are the acceptance oracle.

## Part 0: Skeleton (≈0.5 h) ✅
Solution: `LasLogs.Core`, `LasLogs.App` and `LasLogs.Tests` (all net48). Also `.gitignore`/`.gitattributes`, `CLAUDE.md`, `docs/`, a README with the required headings, and VS Code build/launch config.
**Done when:** `dotnet build` and `dotnet test` succeed on a clean clone.

## Part 1: LAS reader (≈4 h): report roadmap phases 1–2 ✅
- `HeaderLine` parser: the first dot ends the mnemonic, the first space after it ends the unit, the last colon starts the description. LAS 1.2 keeps `~W` values in the description field (except STRT/STOP/STEP/NULL).
- Section state machine (only the first letter after `~` matters; LAS 3.0 names per DECISIONS §9). Model: `LasFile` and `CurveDef` (by position), plus `Diagnostic`.
- Data: unwrapped, wrapped and `DLM` (comma/tab). Comment/blank/Ctrl-Z/NUL lines are handled. Wrong-count rows are rejected; truncated records are rejected; undeclared wrap is recovered (DECISIONS §6).
- `Read(TextReader, name)` overload for tests; synthetic tests from report §I.2 (S1–S9, S19, S22, S24, S25).
- **Done when** a quick full-folder run gives:

  | Check | Expected |
  |---|---|
  | Exceptions across 203 files | 0 |
  | VERS | 2.0 ×140, 1.2 ×52, 3.0 ×11 |
  | `39399.LAS` (wrapped) | 23,435 records |
  | `4200340122MAINCOR.las` | 20,645 records + `UNDECLARED_WRAP` |
  | `4246135949_MAIN.las` | 17,307 records + 3,054 rejected (lines 8247–11300) |
  | `4200340123MAIN.las` | 13,341 records + 1 rejected (line 13447), Partial |
  | `4200340740PnrUniv7-23_1.las` | 20,817 records; line 10508 rejected |
  | `4200345990_20140426_LAS(masmp).LAS` | 125 curves (2 empty mnemonics kept) × 79,033 records |
  | `4200346370_20150224_LAS(UA_3001HL_lateral_hole_Mud_log).las` | 40 curves × 8,565 records (comma) |
  | `…9365ST2COMBINED_2_0).las` | 8,616 records (tab) |

## Part 2: Batch runner + conditioning + catalog (≈4 h): phases 3–5
- `Program`: enumerate files, isolate each file in try/catch, record status and timing, write a summary CSV. Synthetic broken files (empty, binary, locked) end as Failed.
- `Conditioner`: null policy, depth direction, step/gap/STRT/STOP analysis (DECISIONS §1–3).
- `CurveCatalog` (CSV data file, seeded from report §F.7) + unit table + QC (DECISIONS §4, §5, §8).
- **Done when:**

  | Check | Expected |
  |---|---|
  | Concept coverage | ≈ GR 182 / RES 109 / RHOB 100 / NPHI 124 files (+1 each once MAINCOR is recovered) |
  | Depth reversed | 26 files |
  | Non-monotonic | 1 file (`4200343170_STATE_AQ_8_RUN2.las`) |
  | `NULL_IS_ZERO` | 5 files |
  | K/M3 conversion | 1 file (`4238337564_…triplecmi_004_MD.las`, median ≈ 2.518) |
  | `IMPLAUSIBLE_VALUES` | raised for density in `4246135949_MAIN.las` |
  | Duplicate `NPRL` in `4246135292_MAIN.las` | "Limestone" one chosen |

## Part 3: Plotting (≈2.5 h): phase 6
- 30-minute OxyPlot spike first: reversed depth axis, `LogarithmicAxis`, two top axes in one track, NaN gaps, PNG export on net48. Fallback: one PlotModel per track, stitched with System.Drawing.
- `LogPlotter` per DECISIONS §7–8. Output: `output/plots/<wellKey>/<file>.png`.
- **Done when** these look right, checked by eye:

  | File | What to look at |
  |---|---|
  | `39399.LAS` | all 4 concepts |
  | `4200340122MAINCOR.las` | recovered wrap |
  | `4200343170_STATE_AQ_8_RUN2.las` | non-monotonic depth segments |
  | a CNL-only file | missing tracks |
  | a mud log | 0 concepts |
  | `4238337564_…triplecmi_004_MD.las` | unit conversion |

## Part 4: Report + README + delivery (≈2 h): phases 7–8
- `ReportWriter`: `conditioning_report.md`, `conditioning_events.csv` and `conditioning_summary.csv` (design in report §J).
- README sections: build & run; design; conditioning decisions (with one real example each); known limitations; how AI was used (**the user writes or approves this**). Include timing and memory numbers from a full run.
- Final full run. Decide on committing `output/` (≈50 MB or less) or attaching a zip.

**If time runs short, cut in this order:** report Markdown detail (keep the CSV) → some synthetic tests.
**Never compromise on:** no crashes, no silent data loss, depth alignment, correct units, an honest report.
