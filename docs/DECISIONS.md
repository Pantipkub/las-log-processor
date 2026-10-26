# Data-conditioning decisions (confirmed)

The user confirmed these on 2026-10-25. Rules 5 and 8 were revised on 2026-10-26. Rules 1 and 5 were extended on 2026-10-27 (placeholder-only curves).
The evidence (file names, line numbers, counts) is in `../TestData/analysis/LAS_Investigation_Report.md`, §F and §G.

Diagnostic categories:
- **Observation**: no change was made.
- **Transformation**: data or naming changed.
- **Warning**: a human should look.
- **Error**: something could not be loaded.

## 1. Missing values
- The declared `~W NULL` value becomes `NaN`. Transformation `NULL_TO_MISSING`, counted per curve.
- If the declared NULL is **not** −999.25, exact −999.25 also becomes `NaN`. Warning `SENTINEL_MISMATCH`.
  *Evidence:* `4238336177MAIN.las` declares 0 but contains 11,840 × −999.25.
- `NULL = 0` is honoured. Warning `NULL_IS_ZERO`, with the count of zeros removed.
- Undeclared placeholder-looking values (−9999, −999, −999.2) stay as data. Warning `SUSPECTED_SENTINEL`, with counts.
  *Added 2026-10-27:* if **every** valid value of a candidate curve is such a placeholder, curve selection (§5) does not use it. The track label says "rejected: <curve> has only placeholder values (−999.2)". The values and the warning stay; a curve with only a few placeholders is used as before.
  *Evidence:* `4200343170_STATE_AQ_8_MUDLOG.las` GAMMA is −999.2 in all 10,781 records; before this rule the GR line was pinned to the 0 edge and the track looked empty with no label.
- Non-numeric tokens (`********`, `(null)`, `NaN`, `1.#INF`, `-1.#IND`) become `NaN`. Warning `NONNUMERIC_VALUE`.
- The original NULL text is kept in the model. The source file is the audit copy: diagnostics cite line numbers, not raw values.

## 2. Depth order
- Strictly decreasing depth: reverse all arrays together. Transformation `DEPTH_REVERSED`.
- Non-monotonic depth: **keep the file order**; do not sort or dedupe. Warning `DEPTH_NON_MONOTONIC`, with the first reversal lines and the number of repeated depths. The plot draws each one-directional segment separately.
- Repeated depths in otherwise monotonic data: keep them. Warning `DEPTH_DUPLICATES`.

## 3. Sampling
- Keep the original sample depths. **Never compute depths from STRT + i × STEP.** Always read the depth column.
- Report the modal step against the header STEP (Warning `STEP_MISMATCH`), STRT/STOP disagreements (Observation) and gaps larger than 1.5 × the modal step (Warning `DEPTH_GAP`).
- No resampling and no interpolation.

## 4. Units
- Convert only the four mapped concepts, using known unit spellings. Record the factor. Transformation `UNIT_CONVERTED`.

  | Concept | Canonical unit | Accepted ×1 | Converted |
  |---|---|---|---|
  | GammaRay | gAPI | GAPI, gAPI, API | n/a |
  | DeepResistivity | ohm·m | OHMM, OHM-M, OHM.M | `M.OHM` is ohm·m (alias, Warning) |
  | BulkDensity | g/cm³ | G/C3, G/CC, G/CM3 | K/M3, KG/M3 ×0.001 |
  | NeutronPorosity | v/v | CFCF, V/V, FRAC, DECP, DECI, FT3/FT3 | PERC, PU, % ×0.01 |

- **Trust the declared unit.** If the values contradict it, raise Warning `IMPLAUSIBLE_VALUES`; do not override.
- A blank or unknown-style unit (`''`, `UNKNOWN`, `U`, `----`) is accepted with Warning `UNIT_ASSUMED`, but only when the values are unambiguous. A spelling not in the table is not mapped: Warning `UNIT_UNRECOGNIZED`.
- Every curve keeps its original unit string. Unmapped curves are never converted.

## 5. Curve names
- Use the alias catalog (data file, editable without recompiling): concept, mnemonic and priority. The initial content comes from report §F.7.
- Matching is exact on the mnemonic (case-insensitive, trimmed). The unit must belong to the concept's family. For **neutron porosity only**, the description must not hit the exclusion list (`crossplot`, `sandstone`, `dolomite`, `correction`, `caliper`, `standoff`, `mud`, `matrix`). The candidate must have at least one valid value that is not a suspected placeholder (§1, 2026-10-27).
  *Revised 2026-10-26:* applied to all four concepts, the list rejected good curves: `ZDNC` "Borehole size/mud weight corrected density" (11 files) and `MLR4C` "…adaptive borehole correction" (2 files). The other concepts are already protected by the exact mnemonic and the unit check.
- Pick the highest priority. If one mnemonic appears twice, take the first and raise Warning `AMBIGUOUS_CURVE`.
- Medium/Low-confidence names (`GR_1`, `GKUT`, `REST`, `RHOB_SHIFT`, `NPHM`, …) stay **unmapped** and are listed in the report.
- Duplicate mnemonics must never crash the program: curves are identified by **column position**, not by a dictionary keyed on the name.

## 6. Malformed data
- A row with the wrong value count is rejected, never guessed. Error `RECORD_REJECTED`, with the count and first/last line.
- A truncated final record is rejected and the file status is **Partial**.
- NUL/control-only lines are rejected (Error). A trailing Ctrl-Z (0x1A) is ignored (Observation).
- Comment (`#`) and blank lines inside `~A` are skipped (Observation).
- **Undeclared wrap recovery:** if `WRAP NO` but most lines don't match the curve count, re-read as wrapped. Accept the result only if the tokens divide evenly into records and the depth step is consistent. Warning `UNDECLARED_WRAP`. Otherwise the file fails with that reason.
- File status:
  - **Loaded**: all records parsed (value-level warnings allowed).
  - **Partial**: some records rejected or the file was truncated.
  - **Failed**: no usable data. Report the reason and continue with the next file.

## 7. Missing curves
- Always draw all three tracks. An empty track gets a centred reason label: "not in file", "present but all null", or "rejected: <reason>".
- A file with none of the four concepts still gets a plot. Observation `NO_REQUIRED_CURVES`.

## 8. Plot rules (visual only; stored data never changes). Revised 2026-10-26
- Fixed display scales, kept as constants in one place:

  | Track | Scale |
  |---|---|
  | GR | 0–150 gAPI |
  | Resistivity | 0.2–20,000 ohm·m, log axis |
  | Bulk density | 1.95–2.95 g/cm³ |
  | Neutron porosity | 0.45 → −0.15 v/v, reversed, same track as density |

- Depth is the vertical axis and increases downward. The axis spans the file's shallowest to deepest loaded depth.
- `NaN` breaks the line. Nothing is interpolated across gaps.
- **Nothing is hidden.** Values outside the display scale are drawn running to the track edge, clipped by the axis, so unexpected excursions stay visible. Out-of-scale points are counted per curve, shown in the plot footer, and listed in the report.
- The only exception: values ≤ 0 cannot be drawn on the log resistivity axis. They are counted and reported.
- **QC typical ranges** (warnings only; they never change data or the plot):

  | Concept | Median expected within |
  |---|---|
  | GR | 5–300 gAPI |
  | Resistivity | 0.1–10,000 ohm·m |
  | Density | 1.5–3.1 g/cm³ |
  | Neutron | 0.005–0.6 v/v |

  A median outside the range raises Warning `IMPLAUSIBLE_VALUES`; it catches the column-order problem in `4246135949_MAIN.las`. The scales and ranges are display/QC conventions; the README says a petrophysicist should tune them.

## 9. Scope
- **One plot per input file.** Well grouping (by header API/UWI) is informational only, in the report.
- Process every `.las`/`.LAS` file in the input folder (the dataset has 203 files, while the brief says 50). Byte-identical duplicates are processed and flagged (Observation `DUPLICATE_FILE`).
- Deep resistivity only (no shallow overlay at first).
- Depth axis label uses the file's own unit; no metric conversion.
- LAS 3.0: support the LAS 2.0-compatible subset (`~Version/~Well/~Parameter/~Curve/~Ascii`, `DLM`, `~Log_Definition`, `~Log_Data`). Skip other sections (Observation). If a file has several datasets, load the first and report the others.
