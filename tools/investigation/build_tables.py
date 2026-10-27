"""Build per-file table, anomaly inventory and mapping evidence (Markdown + CSV) from scanner JSON.
Usage: python -I build_tables.py <scan_json_dir> <wells.json> <out_dir>
"""
import json, os, sys, re, collections, csv
sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
D, WELLS, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(OUT, exist_ok=True)
R = [json.load(open(os.path.join(D, f), encoding='utf-8')) for f in sorted(os.listdir(D), key=str.lower)]
W = json.load(open(WELLS, encoding='utf-8'))

# ---- draft mapping (evidence-reviewed; see report section F/G) ----
ALIAS = {
 'GR':   ['GR', 'GR_EDTC', 'ECGR', 'ECGR_EDTC', 'GRGC', 'GAMMA', 'GR1', 'G1', 'GRSG', 'GMSG', 'GRGM', 'SGR', 'HSGR',
          'HGR', 'HGR_EDTC', 'EHGR_EDTC', 'GRTO', 'GAPI', 'GR_CAL'],
 'RES':  ['AT90', 'AF90', 'AO90', 'M2R9', 'M0R9', 'TBIT90', 'TBIF90', 'RT90', 'ILD', 'RILD', 'LLD', 'DDLL', 'RLLD',
          'RLA5', 'MLR4C', 'MLR4', 'RD', 'RT', 'RTAT', 'R85T', 'RT_HRLT'],
 'RHOB': ['RHOB', 'RHOZ', 'DEN', 'ZDNC', 'ZDEN', 'RHO8', 'RHOL'],
 'NPHI': ['TNPH', 'NPHI', 'NPRL', 'NPL', 'TNPH_LIM', 'NPHL', 'NPHC', 'CNC', 'CNCF', 'CNPOR', 'NPOR', 'HTNP', 'HNPO'],
}
UNITS = {
 'GR':   {'GAPI': 1, 'API': 1, 'AAPI': 'typo?', '': 'blank', 'UNKNOWN': 'blank', 'U': 'blank', '----': 'blank'},
 'RES':  {'OHMM': 1, 'OHM-M': 1, 'OHM.M': 1, 'M.OHM': 'label', '': 'blank'},
 'RHOB': {'G/C3': 1, 'G/CC': 1, 'G/CM3': 1, 'GM/CC': 1, 'K/M3': 0.001, 'KG/M3': 0.001, '': 'blank'},
 'NPHI': {'CFCF': 1, 'V/V': 1, 'FRAC': 1, 'DECP': 1, 'DECI': 1, 'FT3/FT3': 1, 'DEC': 1, 'PERC': 0.01, 'PU': 0.01, '%': 0.01, '': 'blank'},
}
DESC_EXCLUDE = re.compile(r'crossplot|sandstone|dolomite|correction|caliper|standoff|matrix|porosity from density', re.I)

def select(curves, concept):
    cands = []
    for rank, a in enumerate(ALIAS[concept]):
        for c in curves:
            if (c['mnem'] or '').upper() == a:
                cands.append((rank, c))
    notes = []
    good = []
    for rank, c in cands:
        u = (c['unit'] or '').upper()
        if u not in UNITS[concept]:
            notes.append(f"{c['mnem']}[{c['unit']}]:unit-rejected"); continue
        if DESC_EXCLUDE.search(c['desc'] or '') and concept == 'NPHI':
            notes.append(f"{c['mnem']}:desc-excluded('{c['desc'][:25]}')"); continue
        if c['n_valid'] == 0:
            notes.append(f"{c['mnem']}:all-null"); continue
        good.append(c)
    return (good[0] if good else None), cands, notes

def fmt(x, n=1):
    if x is None: return ''
    if abs(x) >= 1000: return f'{x:.0f}'
    return f'{x:.{n}f}'

per_file = []
anoms = collections.defaultdict(list)  # code -> [(file, detail)]
hashes = collections.defaultdict(list)
for r in R: hashes[r['sha256']].append(r['file'])

for idx, r in enumerate(R, 1):
    f = r['file']
    ver = next((p['value'] for p in r['version'] if (p['mnem'] or '').upper() == 'VERS'), '?')
    codes = []
    def A(code, detail):
        codes.append(code); anoms[code].append((f, detail))
    b = r['bytes']
    if b['nul']: A('BINARY_JUNK', f"{b['nul']} NUL bytes; garbage line inside ~A (see record-integrity)")
    if b['ctrlz']: A('CTRL_Z_EOF', 'trailing 0x1A (DOS end-of-file marker) parsed as an extra 1-token line')
    if b['nonascii']:
        A('NON_ASCII', f"{b['nonascii']} byte(s) >127, utf8_valid={b['utf8_valid']}, first: line {r['nonascii_lines'][0][0] if r['nonascii_lines'] else '?'}")
    if b['lf_only']: A('LF_ONLY_EOL', f"{b['lf_only']} LF-only line endings (CRLF elsewhere={b['crlf']})")
    if b['cr_only']: A('LONE_CR', f"{b['cr_only']} lone CR byte(s)")
    if not b['ends_with_newline']: A('NO_FINAL_NEWLINE', 'last line not terminated')
    if len(hashes[r['sha256']]) > 1: A('DUPLICATE_FILE', 'byte-identical to ' + ', '.join(x for x in hashes[r['sha256']] if x != f))
    if r['n_preamble']: A('PREAMBLE_COMMENTS', f"{r['n_preamble']} comment line(s) before ~V")
    lay = ''.join(k for _, _, k in r['sections'])
    if 'X' in lay: A('LAS3_EXTRA_SECTIONS', ', '.join(t for _, t, k in r['sections'] if k == 'X'))
    if len(r['datasets']) > 1: A('MULTIPLE_DATASETS', f"{len(r['datasets'])} ~C/~A pairs; ~A at lines {[d['a_line'] for d in r['datasets']]}")
    if r['dlm'] != 'SPACE': A('NON_SPACE_DELIMITER', f"DLM={r['dlm']}")
    for kind, ln, s, fl in r['hdr_bad']:
        if 'empty_mnem' in fl: A('EMPTY_MNEMONIC', f"~{kind} line {ln}: '{s[:45]}'")
        elif kind in 'WV': A('HEADER_LINE_MALFORMED', f"~{kind} line {ln}: '{s[:45]}' {fl}")
    for i, cs in enumerate(r['curve_sets']):
        c = collections.Counter((x[0] or '').upper() for x in cs)
        d = {k: v for k, v in c.items() if v > 1}
        if d: A('DUPLICATE_MNEMONIC', f"set {i}: " + ', '.join(f'{k}x{v}' for k, v in list(d.items())[:6]) + (' ...' if len(d) > 6 else ''))
    # null
    if r['null'] == 0.0: A('NULL_IS_ZERO', 'declared NULL=0: genuine zero readings become indistinguishable from missing')
    elif r['null'] != -999.25: A('NULL_NONSTANDARD', f"declared NULL={r['null']}")
    ds0 = r['datasets'][0]
    tot_null = sum(c['n_null'] for c in ds0['curves'])
    if tot_null == 0 and ds0['n_records']: A('NULL_NEVER_USED', f"declared NULL {r['null']} never appears in data")
    sent = collections.Counter()
    for ds in r['datasets']:
        for c in ds['curves'][1:]:
            for k, v in c['sentinel_vals'].items():
                if float(k) < 0: sent[k] += v
    if sent: A('UNDECLARED_SENTINEL', 'exact values ' + ', '.join(f'{k} x{v}' for k, v in sent.most_common(3)) + f' present as data (declared NULL {r["null"]})')
    alln = [c['mnem'] for c in ds0['curves'][1:] if c['n_valid'] == 0]
    if alln: A('ALL_NULL_CURVES', f"{len(alln)}/{len(ds0['curves'])-1}: {', '.join(alln[:5])}{' ...' if len(alln) > 5 else ''}")
    # record integrity
    for i, ds in enumerate(r['datasets']):
        tag = f'ds{i}: ' if len(r['datasets']) > 1 else ''
        real_mis = [m for m in ds['mismatch_samples'] if not (m[1] == 1 and (m[2] in ('\x1a',) or '\x00' in m[2]))]
        n_mis = ds['n_mismatch_lines'] - (len(ds['mismatch_samples']) - len(real_mis) if ds['n_mismatch_lines'] <= len(ds['mismatch_samples']) else 0)
        if ds['n_records'] == 0 and ds['data_lines']:
            A('UNDECLARED_WRAP', f"{tag}WRAP=NO declared but every depth step spans several lines (token-count histogram {ds['tokcount_hist']}); 0 records parsed as unwrapped")
        elif real_mis:
            A('COLUMN_COUNT_MISMATCH', f"{tag}{ds['n_mismatch_lines']} line(s) with token count != {ds['n_curves']}; first at line {real_mis[0][0]} ({real_mis[0][1]} tokens); histogram {ds['tokcount_hist']}")
        if ds['truncated_tail']: A('TRUNCATED_WRAPPED_RECORD', f"{tag}{ds['truncated_tail']}")
        if ds['comment_lines']: A('COMMENT_IN_DATA', f"{tag}{ds['comment_lines']} '#' line(s) inside ~A, first line {ds['comment_samples'][0][0]}")
        if ds['blank_lines']: A('BLANK_IN_DATA', f"{tag}{ds['blank_lines']} blank line(s) inside ~A at {ds['blank_samples'][:3]}")
        nbad = sum(c['n_bad'] + c['n_commadec'] for c in ds['curves'])
        if nbad: A('NONNUMERIC_TOKEN', f"{tag}{nbad} token(s), e.g. " + '; '.join(f"line {l} col {c+1} '{t}'" for l, c, t in ds['bad_samples'][:2]))
        nsp = sum(c['n_special'] for c in ds['curves'])
        if nsp: A('NAN_INF_TOKEN', f"{tag}{nsp} token(s), e.g. " + '; '.join(f"line {l} col {c+1} '{t}'" for l, c, t in ds['special_samples'][:2]))
        if ds['tab_lines']: A('TAB_DELIMITED', f"{tag}{ds['tab_lines']} data lines use TAB")
        if ds['dup_consecutive_records'] > 5: pass  # mostly null padding rows; summarised in table only
        d = ds['depth']
        if not d.get('n'): continue
        direction = 'inc' if d['n_pos'] and not d['n_neg'] else 'dec' if d['n_neg'] and not d['n_pos'] else 'MIXED'
        if direction == 'dec': A('DEPTH_DECREASING', f"{tag}{d['first']} -> {d['last']} (STEP {r['step']})")
        if direction == 'MIXED': A('DEPTH_NON_MONOTONIC', f"{tag}{d['reversals']} direction reversal(s), e.g. line {d['reversal_samples'][0][0]}: {d['reversal_samples'][0][1]} -> {d['reversal_samples'][0][2]}; {d['n_global_duplicates']} repeated depth values")
        elif d['n_global_duplicates']: A('DEPTH_DUPLICATES', f"{tag}{d['n_global_duplicates']}")
        step = r['step']
        mode = d.get('mode_step')
        if step and mode and abs(abs(mode) - abs(step)) > 1e-6 + 10 ** -(d['decimals'] or 0):
            A('STEP_HEADER_WRONG', f"{tag}STEP={step} but {d['diff_top'][0][1]} of {d['n']-1} intervals are {mode}")
        ng = d.get('n_gaps_gt_1p5_mode', 0)
        if ng and direction != 'MIXED': A('DEPTH_GAP', f"{tag}{ng} gap(s); largest {d['max_gap'][0]} ft at line {d['max_gap'][1]}")
        tol = abs(step or 0) * 0.5 + 1e-6
        if d.get('last_minus_stop') is not None and abs(d['last_minus_stop']) > tol:
            A('STOP_MISMATCH', f"{tag}last depth {d['last']} vs STOP {r['stop']}")
        if d.get('first_minus_strt') is not None and abs(d['first_minus_strt']) > tol:
            A('STRT_MISMATCH', f"{tag}first depth {d['first']} vs STRT {r['strt']}")
        if d['n_negative']: A('NEGATIVE_DEPTH', f"{tag}{d['n_negative']} sample(s), min {d['min']}")
    # well id
    w = W[f]
    if w['hdr10'] and w['fn10'] and w['hdr10'] != w['fn10']:
        A('WELL_ID_MISMATCH', f"header API/UWI {w['hdr10']} vs filename {w['fn10']}")
    if not w['hdr10']: A('WELL_ID_MISSING', f"no 10-digit UWI/API in header (WELL='{w['well'][:30]}')")
    # concepts
    sel = {}
    for k in ALIAS:
        c, cands, notes = select(ds0['curves'], k)
        sel[k] = (c, cands, notes)
        if c:
            u = (c['unit'] or '').upper()
            fac = UNITS[k][u]
            if fac not in (1,):
                A('UNIT_ATTENTION', f"{k}<-{c['mnem']} unit '{c['unit']}' -> {'x'+str(fac) if isinstance(fac, float) else fac}")
            if k == 'RES' and (c.get('n_neg', 0) or 0) + (c.get('n_zero', 0) or 0):
                A('RES_NONPOSITIVE', f"{c['mnem']}: {c['n_neg']} negative + {c['n_zero']} zero value(s) cannot be drawn on a log axis")
            if c['min'] == c['max']:
                A('CONSTANT_CURVE', f"{k}<-{c['mnem']} constant {c['min']}")
    absent = [k for k in ALIAS if not sel[k][0]]
    if absent: A('CONCEPT_UNAVAILABLE', ', '.join(absent))
    d = ds0['depth']
    direction = '' if not d.get('n') else ('inc' if d['n_pos'] and not d['n_neg'] else 'dec' if d['n_neg'] and not d['n_pos'] else 'MIXED')
    def cell(k):
        c = sel[k][0]
        if c:
            return f"{c['mnem']} [{c['unit'] or '—'}] p50={fmt(c.get('p50'), 2)}"
        cands = sel[k][1]
        return ('✗ (' + '; '.join(sel[k][2])[:40] + ')') if cands else '✗'
    per_file.append(dict(
        idx=idx, file=f, mb=r['size'] / 1e6, ver=ver, wrap='Y' if r['wrap'] else 'N', dlm=r['dlm'], layout=lay,
        null=r['null'], unit=(ds0['curves'][0]['unit'] if ds0['curves'] else ''), direction=direction,
        strt=r['strt'], stop=r['stop'], step=r['step'], first=d.get('first'), last=d.get('last'), mode=d.get('mode_step'),
        recs=ds0['n_records'], ncurves=ds0['n_curves'], well_key=w['key'], well=w['well'],
        GR=cell('GR'), RES=cell('RES'), RHOB=cell('RHOB'), NPHI=cell('NPHI'), codes=sorted(set(codes)),
        sel={k: (sel[k][0]['mnem'] if sel[k][0] else None) for k in ALIAS},
        sel_unit={k: (sel[k][0]['unit'] if sel[k][0] else None) for k in ALIAS}))

# ---------------- write outputs ----------------
SHORT = {'BINARY_JUNK': 'BIN', 'CTRL_Z_EOF': '^Z', 'NON_ASCII': 'NONASCII', 'LF_ONLY_EOL': 'LF', 'LONE_CR': 'CR',
         'NO_FINAL_NEWLINE': 'NOEOL', 'DUPLICATE_FILE': 'DUPFILE', 'PREAMBLE_COMMENTS': 'PRE#', 'LAS3_EXTRA_SECTIONS': 'LAS3X',
         'MULTIPLE_DATASETS': 'MULTI-DS', 'NON_SPACE_DELIMITER': 'DLM', 'EMPTY_MNEMONIC': 'EMPTYMNEM',
         'HEADER_LINE_MALFORMED': 'HDRBAD', 'DUPLICATE_MNEMONIC': 'DUPMNEM', 'NULL_IS_ZERO': 'NULL0', 'NULL_NONSTANDARD': 'NULL≠',
         'NULL_NEVER_USED': 'NULLUNUSED', 'UNDECLARED_SENTINEL': 'SENTINEL', 'ALL_NULL_CURVES': 'ALLNULL',
         'UNDECLARED_WRAP': 'WRAP?', 'COLUMN_COUNT_MISMATCH': 'COLS', 'TRUNCATED_WRAPPED_RECORD': 'TRUNCW', 'COMMENT_IN_DATA': '#IN~A',
         'BLANK_IN_DATA': 'BLANK', 'NONNUMERIC_TOKEN': 'BADTOK', 'NAN_INF_TOKEN': 'NAN/INF', 'TAB_DELIMITED': 'TAB',
         'DEPTH_DECREASING': 'DEC', 'DEPTH_NON_MONOTONIC': 'NONMONO', 'DEPTH_DUPLICATES': 'DUPDEPTH', 'STEP_HEADER_WRONG': 'STEP≠',
         'DEPTH_GAP': 'GAP', 'STOP_MISMATCH': 'STOP≠', 'STRT_MISMATCH': 'STRT≠', 'NEGATIVE_DEPTH': 'NEGDEPTH',
         'WELL_ID_MISMATCH': 'APIdiff', 'WELL_ID_MISSING': 'NOAPI', 'UNIT_ATTENTION': 'UNIT', 'RES_NONPOSITIVE': 'RES≤0',
         'CONSTANT_CURVE': 'CONST', 'CONCEPT_UNAVAILABLE': 'MISSING'}
with open(os.path.join(OUT, 'per_file_table.md'), 'w', encoding='utf-8') as o:
    o.write('| # | File | MB | VERS | Wrap | Sections | NULL | Index unit | Depth order | Header STRT→STOP / STEP | Observed first→last / modal step | Records | Curves | Well key | Gamma ray | Resistivity (deep) | Bulk density | Neutron porosity | Flags |\n')
    o.write('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n')
    for p in per_file:
        flags = ' '.join(SHORT[c] for c in p['codes'])
        hdr = f"{fmt(p['strt'])}→{fmt(p['stop'])} / {p['step']}"
        obs = f"{fmt(p['first'])}→{fmt(p['last'])} / {p['mode']}" if p['first'] is not None else '— (0 records)'
        fn = p['file'].replace('|', '\\|')
        o.write(f"| {p['idx']} | `{fn}` | {p['mb']:.2f} | {p['ver']} | {p['wrap']}{'' if p['dlm']=='SPACE' else ','+p['dlm']} | {p['layout']} | {p['null']} | {p['unit']} | {p['direction'] or '—'} | {hdr} | {obs} | {p['recs']} | {p['ncurves']} | {p['well_key']} | {p['GR']} | {p['RES']} | {p['RHOB']} | {p['NPHI']} | {flags} |\n")
with open(os.path.join(OUT, 'anomaly_inventory.md'), 'w', encoding='utf-8') as o:
    for code in sorted(anoms, key=lambda c: (-len({x[0] for x in anoms[c]}), c)):
        L = anoms[code]
        o.write(f"\n#### `{code}` (flag `{SHORT[code]}`) — {len({x[0] for x in L})} file(s)\n\n")
        lim = 60 if code not in ('CONCEPT_UNAVAILABLE', 'DEPTH_DECREASING', 'CTRL_Z_EOF') else 200
        for f, det in L[:lim]:
            o.write(f"- `{f}` — {det}\n")
        if len(L) > lim: o.write(f"- … {len(L)-lim} more (see per_file_table.md)\n")
with open(os.path.join(OUT, 'per_file_summary.csv'), 'w', newline='', encoding='utf-8') as o:
    wcsv = csv.writer(o)
    wcsv.writerow(['idx', 'file', 'MB', 'VERS', 'WRAP', 'DLM', 'sections', 'NULL', 'index_unit', 'depth_order', 'STRT', 'STOP', 'STEP',
                   'first_depth', 'last_depth', 'modal_step', 'records', 'curves', 'well_key', 'GR', 'RES', 'RHOB', 'NPHI', 'flags'])
    for p in per_file:
        wcsv.writerow([p['idx'], p['file'], f"{p['mb']:.3f}", p['ver'], p['wrap'], p['dlm'], p['layout'], p['null'], p['unit'], p['direction'],
                       p['strt'], p['stop'], p['step'], p['first'], p['last'], p['mode'], p['recs'], p['ncurves'], p['well_key'],
                       p['sel']['GR'], p['sel']['RES'], p['sel']['RHOB'], p['sel']['NPHI'], ' '.join(p['codes'])])
# coverage summary
cov = collections.Counter()
for p in per_file:
    n = sum(1 for k in ALIAS if p['sel'][k])
    cov[n] += 1
print('concept coverage: files with N of 4 concepts:', dict(sorted(cov.items())))
for k in ALIAS:
    print(k, 'available in', sum(1 for p in per_file if p['sel'][k]), 'files; selected mnemonics:', collections.Counter(p['sel'][k] for p in per_file if p['sel'][k]).most_common())
    print('   units:', collections.Counter(p['sel_unit'][k] for p in per_file if p['sel'][k]).most_common())
print('anomaly codes:', {c: len({x[0] for x in L}) for c, L in sorted(anoms.items(), key=lambda kv: -len(kv[1]))})
wells_all = {p['well_key'] for p in per_file}
wells_any = {p['well_key'] for p in per_file if any(p['sel'].values())}
print('well keys', len(wells_all), 'with >=1 concept', len(wells_any))
print('files with 0 concepts:', [p['file'] for p in per_file if not any(p['sel'].values())])
