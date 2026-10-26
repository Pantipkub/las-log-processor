# Test fixtures

Byte-exact excerpts of real files from the dataset (`../TestData/TestData/`), cut with `sed -b -n`.
Line numbers in the tests are excerpt line numbers.

| Fixture | Source file | Source lines | What it covers |
|---|---|---|---|
| `39399_excerpt.las` | `39399.LAS` | 1–104 | LAS 2.0 `WRAP YES`, 44 curves, 3 records |
| `4200340122MAINCOR_excerpt.las` | `4200340122MAINCOR.las` | 1–204 | `WRAP NO` but wrapped (6 lines per record), 5 records |
| `4200340740PnrUniv7-23_1_excerpt.las` | `4200340740PnrUniv7-23_1.las` | 1–101, 4969–4971, 10506–10510, 20915–20917 | `********` in DEN (excerpt line 103), NUL junk line (excerpt line 107), final blank line, mixed CRLF/LF |
| `4200345990_masmp_excerpt.las` | `4200345990_20140426_LAS(masmp).LAS` | 1–230 | Two empty mnemonics, 125 curves, 21 `#` lines inside `~A`, wrapped, 2 records |
| `4200346370_mudlog_comma_excerpt.las` | `4200346370_20150224_LAS(UA_3001HL_lateral_hole_Mud_log).las` | 1–90, 8650–8668 | LAS 3.0 `DLM COMMA`, comment and blank lines at the end of `~Ascii`, LAS 3.0 sections after the data |
