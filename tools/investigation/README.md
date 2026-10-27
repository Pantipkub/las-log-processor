# Investigation scripts (analysis only, not part of the C# program)

Read-only Python 3 scripts used before the C# work started, to scan all 203 dataset files and write the
investigation report (`LAS_Investigation_Report.md`, kept outside this repo). The numbers in `docs/PLAN.md`
(record counts, coverage, depth order, nulls) come from them and serve as acceptance checks for the C# program.

- They never write to the LAS folder. No LAS library is used: the parsing rules are hand-written, so the findings
  are independent of the C# reader.
- The C# program does not use or need them. They are kept here so the evidence behind the decisions can be re-run.
- Run with `python -I` (isolated mode); all paths are arguments. `scan_las.py` uses several processes.

| Step | Command | Output |
|---|---|---|
| 1 | `python -I scan_las.py <las_dir> <json_dir>` | one JSON summary per LAS file: bytes, sections, headers, data, per-curve stats, depth analysis |
| 2 | `python -I agg4.py <json_dir> <curve_inventory.csv>` | curve inventory CSV and concept candidate lists |
| 3 | `python -I build_tables.py <json_dir> <wells.json> <out_dir>` | per-file table, anomaly inventory, `per_file_summary.csv` |
| - | `agg1.py`, `agg2.py`, `agg3.py`, `agg5.py` | cross-file queries: bytes/versions/nulls, record integrity, depth, unit/value evidence |

`wells.json` (step 3) came from a small ad-hoc well-identity script that is not kept: LAS 1.2 value-in-description rule,
then UWI → API → file name (similar to `LasFile.WellKey()`).

These scripts were written with AI help (see "How I used AI" in the main README).
