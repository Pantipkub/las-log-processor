# LasLogs system guide

This guide is for someone who is new to well logs and needs to understand, explain and defend this program. It uses simple words. Every example names a real file from the dataset and a number from the final run (2026-10-27), so you can open the file or the reports and check it yourself.

Where the rules live:
- `docs/DECISIONS.md`: the binding rules (short).
- This guide: what the rules mean and **why** they are right.
- `../TestData/analysis/LAS_Investigation_Report.md` (outside the repo): the full evidence; sections C (basics), F (findings) and G (decision options).

## 1. The basics

### 1.1 Well, log, depth sample
- A **well** is a hole drilled into the ground, often several kilometres deep. Depth is measured along the hole from a point on the drilling rig.
- A **well log** is a record of measurements against depth. A tool is pulled up the hole (wireline) or sits in the drill string (while drilling) and measures the rock every few inches. A **mud log** is different: a geologist records rock cuttings and gas readings while drilling, so it usually has none of the four curves we plot.
- A **depth sample** is one row of data: one depth plus one value for every curve at that depth. The depth is the **key**: if a value moves to the wrong row or column, it describes the wrong rock. That is why most of the rules below protect the link between depth and value.
- One well can have many files (main pass, repeat pass, different tools, a mud log). The dataset has 203 files for 64 well keys.

### 1.2 The LAS file
LAS (Log ASCII Standard) is a plain-text format. It has sections that start with `~`:

| Section | What it holds |
|---|---|
| `~V` Version | `VERS` (1.2, 2.0 or 3.0) and `WRAP` (whether one row is split over several lines) |
| `~W` Well | well name, API/UWI number, `STRT`/`STOP`/`STEP` (depth range and spacing), `NULL` (the "no value" number) |
| `~C` Curve | one line per data column, in column order: mnemonic, unit, description |
| `~P` Parameter | constants such as mud weight |
| `~A` ASCII | the numbers |

A header line looks like `MNEMONIC.UNIT  VALUE : DESCRIPTION`, for example `RHOZ.G/C3 : Standard Resolution Formation Density`. The **mnemonic** is a short column name. The same measurement has different mnemonics from different companies and tools (density is `RHOB`, `RHOZ`, `DEN` or `ZDNC`), and one mnemonic can mean two things (`NPRL` is both "Limestone Neutron Por." and "Crossplot Porosity" in `4246135292_MAIN.las`).

Example: `39399.LAS` lines 8–11 are `STRT.F 164.0000:`, `STOP.F 11881.0000:`, `STEP.F 0.5000:`, `NULL. -999.25:`. So the log runs from 164 to 11,881 ft, every 0.5 ft, and −999.25 means "no value". It is **wrapped**: each depth is on its own line and its 43 values follow on the next lines.

### 1.3 The four curves we plot

| Curve (concept) | Unit | What it measures | How to read it |
|---|---|---|---|
| **Gamma ray (GR)** | gAPI | natural radioactivity of the rock | high (≈ 100+) = usually shale; low (≈ 20–40) = clean sand or limestone |
| **Deep resistivity** | ohm·m | how hard it is to push electric current through the rock, far from the hole | salty water conducts (low, ≈ 0.1–5); oil, gas and tight rock do not (high, 10–10,000+). The values span many powers of ten, so the axis is **logarithmic** |
| **Bulk density** | g/cm³ | mass per volume of rock plus the fluid in its pores | sandstone 2.65, limestone 2.71, dolomite 2.87; most logs are 2.0–2.9. Lower = more pore space (or gas) |
| **Neutron porosity** | v/v (fraction) | hydrogen content, read as "apparent porosity" on a limestone scale | 0.20 = 20 % pore space. Shale reads high (bound water); gas reads low |

**Why density and neutron share a track:** on the standard paired scales (density 1.95–2.95 left to right, neutron 0.45 → −0.15 right to left) the two lines lie on top of each other in water-filled limestone, separate in shale and cross over in gas zones. Interpreters read rock type and gas from the gap between them. The assignment asks for this layout.

## 2. The pipeline

```
LAS file ─► LasReader ─► Conditioner ─► CurveCatalog ─► LogPlotter ─► ReportWriter
```

| Step | Code | What it does |
|---|---|---|
| Read | `src/LasLogs.Core/LasReader.cs` | Reads the text line by line (Latin-1 bytes, invariant number format). Builds the header items, the curve list (by column position) and one `double[]` per column. Rejects bad rows. Never throws on bad content. |
| Condition | `src/LasLogs.Core/Conditioner.cs` | Turns null values into `NaN`, removes rows without a depth, checks and fixes depth order, reports step, gaps and header mismatches. |
| Select curves | `src/LasLogs.Core/CurveCatalog.cs` + `curve_catalog.csv` | Picks one file curve for each of the four concepts, checks and converts its unit, runs the QC check on the median. |
| Plot | `src/LasLogs.App/LogPlotter.cs` | Draws the 3-track PNG with fixed scales; counts out-of-scale points. |
| Report | `src/LasLogs.Core/ReportWriter.cs` | Writes the summary row, one event row per diagnostic, and at the end the Markdown report. |
| Batch | `src/LasLogs.App/Program.cs` | Lists the files, checks duplicates (SHA-256), runs the steps for each file inside its own try/catch, prints time and memory. |

Every step records what it did as a **diagnostic**: a stable code (e.g. `NULL_TO_MISSING`), a severity, a count and the first/last line in the source file. The severities are:

| Severity | Meaning |
|---|---|
| Observation | a fact; nothing was changed |
| Transformation | data, order, units or naming changed |
| Warning | a human should look |
| Error | something could not be loaded |

The **file status** follows from them: **Loaded** (all rows read), **Partial** (some rows rejected: any Error), **Failed** (no usable data).

Only one LAS file is in memory at a time. A bad file can never stop the batch: any exception becomes a Failed row with the code `FILE_FAILED`, and the next file starts.

## 3. Every decision, and why

The principle behind all of them: **never invent data, never hide data, and say what you did.** The source LAS file is the audit copy; the reports point to its line numbers.

### 3.1 Missing values (nulls)
**Rule.** The `NULL` value declared in `~W` becomes `NaN` ("not a number", the computer's "no value") in every curve (`NULL_TO_MISSING`, counted per curve). −999.25, the LAS convention, also becomes `NaN` even if the file declares a different NULL (`SENTINEL_MISMATCH`). If the file declares `NULL = 0`, zeros become `NaN` with a warning (`NULL_IS_ZERO`). Tokens that are not numbers (`********`, `(null)`, `1.#INF`) become `NaN` (`NONNUMERIC_VALUE`).

**Why NaN and not 0.** A GR of 0 says "no radioactivity"; a missing GR says "not measured". A fake 0 creates false geology. `NaN` is skipped by the plot (the line breaks) and cannot be mistaken for a measurement.

**Why not delete the row.** A row holds many curves. If GR is missing at 5,000 ft, density at 5,000 ft can still be good. Deleting the row would throw away good values. Only the single value becomes `NaN`.

**Why −999.25 is always missing.** `4238336177MAIN.las` declares `NULL = 0` but contains −999.25 11,840 times. Plotting −999.25 as a density would be nonsense, and the value is the well-known LAS placeholder.

**Why honour `NULL = 0`.** The file says so. Five files do this (e.g. `4231736403_UNIV_7_3_5_MUDLOG_LAS.las`, 95,482 zeros). We cannot tell a real 0 from "missing", so we follow the file and warn that real zeros are lost.

**Undeclared placeholders (−9999, −999, −999.2) stay as data**, with a warning (`SUSPECTED_SENTINEL`). We are not sure they are placeholders: −999 could be a real reading in some curves, and the file does not say. Changing data on a guess breaks the "never invent" rule, so we keep them and show them (they run to the track edge, see 3.10). Example: `4238338546_University_2505_3HM_.las`, `RD` has 400 × −9999.

**But a curve with only placeholders is not plotted** (added 2026-10-27). In `4200343170_STATE_AQ_8_MUDLOG.las` the gamma curve `GAMMA` is −999.2 in all 10,781 records. That is clearly "no data", and drawing it made the GR track look empty without saying why. Now the track says "rejected: GAMMA has only placeholder values (−999.2)". The values themselves are not changed and the warning stays.

**What is never removed.** A value is only made `NaN` by the rules above. A row is only removed when it cannot be placed (3.2). Out-of-range values, spikes, negative depths and duplicate files are all kept.

### 3.2 Rejected rows
**Rule.** A row with the wrong number of values is rejected whole, never guessed (`RECORD_REJECTED`). A cut-off last record (`TRUNCATED_RECORD`), a line of binary junk (`BINARY_LINE`) and a row without a usable depth (`DEPTH_INVALID`) are rejected too. Any rejection makes the file **Partial**.

**Why.** If a row has 28 values but 29 curves are declared, we cannot know which column is missing. Guessing would shift every value after the gap into the wrong curve. Example: `4246135949_MAIN.las` lines 8247–11300 (3,054 rows) have 28 values. They are rejected; the rest of the file (17,307 rows) is loaded and plotted. Other examples: `4200340123MAIN.las` line 13447 has 12 of 23 values (the file was cut off); `4200340740PnrUniv7-23_1.las` line 10508 is binary junk.

Only 3 files are Partial; the report lists them with line numbers.

### 3.3 Undeclared wrap
**Rule.** If the file says `WRAP NO` but most data lines do not have one value per curve, the reader tries reading it as wrapped. It accepts the result only if it fits perfectly (no rejected records and a constant depth step), with `UNDECLARED_WRAP`. Otherwise the file fails with that reason.

**Why.** `4200340122MAINCOR.las` is wrapped but says it is not. Read as declared, every row would be rejected. The strict check makes sure we only "fix" the header when the data proves it: 20,645 records with a constant step.

### 3.4 Depth order
**Rule.**
- Depth only decreasing: all arrays (depth and every curve) are reversed together, so depth increases (`DEPTH_REVERSED`). 27 files, e.g. `4200340122HBCmsf3.las`, 10,383 → 555.5 ft, 19,656 records.
- Depth going up **and** down: the file order is kept, nothing is sorted or removed (`DEPTH_NON_MONOTONIC`). One file: `4200343170_STATE_AQ_8_RUN2.las`, 4 direction changes, the first at line 4465 (7,763.5 → 7,664 ft), 200 repeated depths.
- Repeated depths in otherwise ordered data: kept (`DEPTH_DUPLICATES`).

**Why reversing is safe.** Reversing every array together keeps each value with its depth. It changes only the order, not a single number, and it is reported.

**Why we do not sort a non-monotonic file.** When the depth goes back, the tool logged the same interval twice (for example a repeat section). Sorting would mix two passes into one zig-zag line; removing one pass means choosing which one is right, and we do not know. So we keep both, report it, and the plot draws each one-direction piece separately (no line is drawn across the jump).

### 3.5 Sampling (step and gaps)
**Rule.** We always read the depth column and keep the original depths. We never compute depths from `STRT + i × STEP`, never resample and never interpolate. We report a header `STEP` that does not match the data (`STEP_MISMATCH`), `STRT`/`STOP` that do not match the first/last depth (Observations), and gaps bigger than 1.5 × the usual step (`DEPTH_GAP`).

**Why.** The header can be wrong: `4200340125MAIN.las` declares `STEP 0.75`, but every one of its 20,608 intervals is 0.5. Computing depths from the header would put every value at the wrong depth. A plot does not need a regular grid: each sample is drawn at its own depth. Interpolation would invent values that were never measured.

**Gaps.** `4246135949_MAIN.las` has one gap of 1,527.5 ft, left by the rejected rows (3.2). The plot breaks the line there instead of drawing a straight line across 1,500 ft of rock that has no data.

### 3.6 Units
**Rule.** Only the four plotted curves are converted, and only from units we know (the table in `DECISIONS.md` §4). The factor is recorded (`UNIT_CONVERTED`). Every curve keeps its original unit text. Examples: density in `K/M3` × 0.001 → g/cm³; neutron in `PERC`/`PU`/`%` × 0.01 → v/v.

- **We trust the declared unit.** If the values do not fit it, we warn (`IMPLAUSIBLE_VALUES`) but do not "fix" it.
- **A blank unit** (`''`, `UNKNOWN`, …) is accepted only when the values make the unit obvious (`UNIT_ASSUMED`): density whose 99th percentile is ≤ 5 is g/cm³; neutron with p99 ≤ 1.5 is v/v and ≤ 150 is percent. Example: `4200340122MAIN.las`, `RLLD` has a blank unit → ohm·m.
- **An unknown spelling** is not used (`UNIT_UNRECOGNIZED`): `4246137956_UNIV_NG_5021H_LAS.las` GR unit `aapi` → the GR track says "rejected: GR unit 'aapi' not recognised".

**Real example.** `4238337564_UNIV_10_13_5H_triplecmi_004_MD.las`: `DEN` is in `K/M3` (median 2,518). × 0.001 gives 2.518 g/cm³, a normal rock density.

**Why trust the label instead of the numbers.** A wrong conversion changes the meaning by a factor of 100 or 1,000. `39399.LAS` has a neutron curve labelled `%` whose values look like fractions, and `4238338546_University_2505_3HM_NMR250_DT2i.las` has `CNC` labelled `frac` with a median of 22.55. Silently "fixing" units from statistics would sometimes be wrong and nobody would know. A warning puts a human in charge.

### 3.7 Curve selection (curve names)
**Rule.** `curve_catalog.csv` lists, for each concept, the accepted mnemonics with a priority and a confidence (H/M/L). For each concept the program tries the catalog names in priority order and takes the first file curve that passes all checks:
1. exact mnemonic match (case-insensitive);
2. its unit belongs to the concept (3.6);
3. for neutron only: the description does not contain `crossplot`, `sandstone`, `dolomite`, `correction`, `caliper`, `standoff`, `mud` or `matrix`;
4. at least one real value that is not a placeholder.

The choice and the curves not used are recorded (`CURVE_MAPPED`). If the same mnemonic appears twice and both pass, the first is used (`AMBIGUOUS_CURVE`). Curves are identified by **column position**, never by name alone, because names repeat.

**Examples.** `39399.LAS`: GR is `GR`; `ECGR` and `HGR` are not used (lower priority). `4246135292_MAIN.las`: neutron is `NPRL` "Limestone Neutron Por." (column 17); the second `NPRL` "Crossplot Porosity" (column 25) is excluded by the word `crossplot`.

**Why an explicit list, not fuzzy matching.** Many names look alike but are different measurements: `DRHO` is a density *correction* (≈ 0), `DPHI` is density *porosity*, `GRTH` is the thorium part of GR. A reviewable list with priorities is predictable, and a petrophysicist can edit it without recompiling.

**Why the neutron word check.** `NPRS`/`NPRD` are the same neutron measurement on a sandstone/dolomite scale, and "crossplot porosity" is a computed curve. Mixing scales between wells would make them disagree. The check is for neutron only: applied to all curves it rejected good ones, e.g. `ZDNC` "Borehole size/mud weight corrected density" in 11 files (revised 2026-10-26).

**Confidence.** M and L entries (e.g. `GAMMA`, `GRSG`, `CNC`) are used, but the report lists each file where one was chosen, so a human can check them.

**Missing curve.** If no curve passes, the track is still drawn with a reason: "not in file", "present but all null" or "rejected: …" (`CONCEPT_UNAVAILABLE`). A file with none of the four still gets a plot (`NO_REQUIRED_CURVES`, 21 files, mostly mud logs).

### 3.8 Why the median is used only for QC and unit guessing
The **median** is the middle value after sorting. It is used in exactly two places:
1. **QC:** if the median of a chosen curve is outside a typical range (GR 5–300 gAPI, resistivity 0.1–10,000 ohm·m, density 1.5–3.1 g/cm³, neutron 0.005–0.6 v/v), we warn `IMPLAUSIBLE_VALUES`.
2. **Blank units:** the 99th percentile (a similar "sorted position" value) decides the unit when the file gives none (3.6).

**Why the median and not the mean.** One spike (e.g. a −9999 or a 100,000 saturation reading) moves the mean a lot but hardly moves the median. The median tells us what most of the curve looks like.

**Why it never changes data.** A typical range is a rule of thumb, not physics. Real rock can be outside it; a statistic cannot prove a unit is wrong. It catches real problems: `4246135949_MAIN.las` `DEN` has a median of 0.194 g/cm³ (no rock is that light): the bad rows in this file shifted the columns. The warning tells a human; the program does not guess which column is right.

### 3.9 Why the plot scales are fixed, not computed
GR 0–150 gAPI, resistivity 0.2–20,000 ohm·m (log), density 1.95–2.95 g/cm³, neutron 0.45 → −0.15 v/v. The same on all 203 plots; they are constants at the top of `LogPlotter.cs`.

**Why.**
- These are industry display conventions; a geologist reads a log by its shape on known scales. The density/neutron crossover (1.3) only works on the paired standard scales.
- Fixed scales make plots **comparable** between wells: the same GR value is at the same place on every page.
- Auto-scaling would **hide problems**: the bad density in `4246135949_MAIN.las` (median 0.194) would fill the track and look like a normal curve. On the fixed scale it sits on the edge and is obviously wrong.
- The resistivity range goes to 20,000 (not the common 2,000) because this dataset's deep curves often read above 2,000.

They are display conventions, not truths; the README says a petrophysicist should tune them.

### 3.10 Why out-of-scale values are drawn at the track edge
**Rule.** A value outside the scale is drawn **at the edge** of the track (the plot copy only; stored data does not change). Each curve's count is shown under the track (the "footer") and in the report (`OUT_OF_SCALE`). The only exception: resistivity ≤ 0 cannot exist on a log axis, so it is not drawn; it is counted ("n <= 0 not drawn") and reported (`RES_NONPOSITIVE`).

**Why.** "Nothing is hidden." With plain axis clipping, a curve that is completely outside the scale simply vanished, and the track looked empty, as if there were no data. Drawn on the edge, the excursion is visible and the footer says how many points are there. Hiding or removing them would be silent data loss; changing them would be inventing data.

### 3.11 Gaps in the plot
The line breaks (nothing is drawn between two points) when:
- a value is `NaN` (missing);
- the depth turns back in a non-monotonic file (3.4);
- the depth jumps by more than 1.5 × the usual step in a file with a `DEPTH_GAP` (3.5).

We never draw a line across missing data, because a line suggests "we measured something here".

### 3.12 Duplicate files, file count, grouping
- **Duplicates.** The program hashes each file (SHA-256) and flags a byte-identical copy of an earlier file with `DUPLICATE_FILE`. It still processes and plots it: skipping is a decision for the data owner, and the brief asks for every file. The dataset has 11 such pairs (22 files), e.g. `4238336177MAIN.las` = `4238336177_MAIN.las`.
- **203 files, not 50.** The brief says 50; the folder has 203. The program processes every `.las` file it is given.
- **One plot per file.** Files of the same well are different runs and passes; splicing them is a petrophysical job. The plots are only **grouped** in folders by well key (first 10 digits of the API/UWI number).

## 4. How to read a plot

Open `output/plots/4200339399/39399.png` next to this section.

- **Title:** the file name. **Subtitle:** well name | loaded depth range and unit | status.
- **Left axis:** depth, in the file's own unit, **increasing downward**, from the shallowest to the deepest loaded depth.
- **Tracks:** GR (green) | deep resistivity (black, log scale) | bulk density (red) + neutron porosity (blue, reversed: 0.45 on the left).
- **Top of each track:** the curve name, the **file's mnemonic** in brackets, the unit, and the scale. Neutron's scale is one row higher than density's.
- **Bottom of each track:** the same scale again and the **footer**: "all points within scale", or "drawn at the track edge: n < min, m > max", plus "n <= 0 not drawn" for resistivity.
- **A label in the middle of a track** means no curve: "not in file", "present but all null", or "rejected: <reason>".
- **A line running along the edge** means the values are outside the scale there (the footer gives the count). It is real data, not a drawing error.

**Examples to look at:**
- `39399.LAS` (`plots/4200339399/39399.png`): above about 5,800 ft the resistivity sits on the 0.2 edge (11,303 points below 0.2) and the density flips between both edges (2,345 below 1.95, 8,478 above 2.95). This looks noisy, but it is **real data**: the raw `RHOZ` values there swing between about 1.6 and 3.5 every few feet, and the shallow resistivity is about 0.018 ohm·m. A likely explanation (not confirmed) is a **cased hole** interval: steel casing conducts electricity (very low resistivity) and disturbs the density tool. GR looks normal there. We show it because nothing is hidden.
- `4246135949_MAIN.las`: the density runs down the 1.95 edge (17,099 points below), the `IMPLAUSIBLE_VALUES` case; there is a break at the 1,527.5 ft gap.
- `4200343170_STATE_AQ_8_MUDLOG.las` (`plots/4300343170/`): four labels; GR says "rejected: GAMMA has only placeholder values (−999.2)". (Its folder is `4300343170` because its header says `API 43-003-43170`.)
- `4200343170_STATE_AQ_8_RUN2.las`: the non-monotonic file; repeated intervals are drawn over each other.
- `4238337564_UNIV_10_13_5H_triplecmi_004_MD.las`: density around 2.5 after the K/M3 conversion.

## 5. How to read the reports

All three are in the output folder.
- `conditioning_summary.csv`: one row per file. Start here: `status`, `reason` (the first error), `*_source` (which file curve was used for each concept; empty = none), the counts per severity, `codes` (every code for that file) and `plot_file`.
- `conditioning_events.csv`: one row per diagnostic, with `first_line`/`last_line` in the source file. Filter by `code` or `file` in Excel.
- `conditioning_report.md`: the overview. "Curves used for the plots" (coverage and out-of-scale counts per concept), "What was changed and what was only reported" (every code, grouped by severity, with a real first example), the duplicates, the wells, and the warnings/errors per file.

## 6. Where to change things (for the live change in the interview)

| Change | Where |
|---|---|
| Add or reorder a curve name | `src/LasLogs.Core/curve_catalog.csv` (no recompiling; it is copied next to the exe) |
| Accept a new unit spelling (e.g. `aapi`) | `Units` table at the top of `src/LasLogs.Core/CurveCatalog.cs` |
| Change a display scale or colour | `Min`/`Max`/`Colors` arrays at the top of `src/LasLogs.App/LogPlotter.cs` |
| Change a QC range | `TypicalMedian` in `CurveCatalog.cs` |
| Add a placeholder value | `SuspectedSentinels` in `src/LasLogs.Core/Conditioner.cs` |
| Neutron exclusion words | `ExcludedWords` in `CurveCatalog.cs` |
| Add a report column | `SummaryColumns` + `SummaryRow` in `src/LasLogs.Core/ReportWriter.cs` |

After a change: `dotnet build LasLogs.sln`, `dotnet test LasLogs.sln`, then run the app.

## 7. Questions a reviewer may ask

**Why did you write your own parser?** The assignment forbids LAS-parsing libraries. The reader is one file (`LasReader.cs`) with unit tests for each format case found in the data (wrapped, comma/tab delimiter, LAS 3.0 names, junk lines, Ctrl-Z).

**How do you know the parser is right?** An investigation scanned all 203 files first and counted records, curves and problems per file. The program's record and curve counts match it for every file except `4200340122MAINCOR.las`, which the program recovers as an undeclared wrap (3.3). One opt-in test checks the key numbers on the real data. Every format case has a small unit test, several made from real file excerpts.

**Why `NaN` and not 0 or deleting rows?** 0 is a real measurement; deleting a row throws away the other curves at that depth. `NaN` means "no value" and breaks the plot line.

**Why keep −9999 as data?** The file does not declare it as null, and we do not change data on a guess. It is flagged and visible at the track edge. Only a curve with **nothing but** placeholders is left out of the plot, because then there is clearly no data.

**Why not sort the non-monotonic file?** It holds repeated passes over the same interval; sorting would mix them, removing one means guessing which is right. We keep the file order and report it.

**Why not resample to a regular step?** Resampling creates values that were not measured. A plot does not need a regular grid. The header `STEP` can even be wrong (`4200340125MAIN.las`: 0.75 declared, 0.5 real).

**Why trust a unit that looks wrong?** Guessing units from values is sometimes wrong by × 100 or × 1,000, silently. We convert by the label and warn when the values disagree, so a human decides.

**Why fixed scales instead of auto-scaling?** They are the industry convention, they make wells comparable, and auto-scaling would make a broken curve look normal.

**Why draw out-of-scale values at the edge?** So nothing disappears. Pure clipping made whole curves vanish. The footer and the report give the counts.

**Why the median for QC?** It is robust to spikes. It only raises warnings; it never changes data.

**Why is the shallow part of `39399.LAS` so noisy?** Those are the values in the file (checked against the raw lines). It is probably a cased-hole interval. We show it rather than hide it.

**Why are 3 files Partial?** They have rows that cannot be read safely: 3,054 short rows in `4246135949_MAIN.las`, a truncated last row in `4200340123MAIN.las`, a binary line in `4200340740PnrUniv7-23_1.las`. Everything else in them is loaded and plotted.

**What about duplicate files?** Detected by SHA-256 and flagged (11 pairs). They are still processed, because skipping files is the data owner's decision.

**How fast is it, and would it scale?** 203 files (1.07 GB) in about 33 s with a 309 MB peak, one thread, one file in memory at a time. For thousands of files, process several files in parallel; memory grows with the number of parallel files, not with the total.

**Why .NET Framework 4.8 and C# 7.3?** The assignment requires .NET 4.8. C# 7.3 is that framework's default language version, so the code builds without special settings.

**Why one plot per file and not per well?** Files of one well are different runs; joining them (splicing) needs a petrophysicist's choices. The plots are grouped by well in folders.

**How did you use AI?** See the README section "How I used AI".
