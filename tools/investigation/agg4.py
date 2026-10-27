import json, os, sys, collections, csv, re
sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
D, OUTCSV = sys.argv[1], sys.argv[2]
R = [json.load(open(os.path.join(D, f), encoding='utf-8')) for f in sorted(os.listdir(D))]
rows = []
for r in R:
    for i, ds in enumerate(r['datasets']):
        for c in ds['curves']:
            rows.append(dict(file=r['file'], ds=i, pos=c['pos'], mnem=c['mnem'], unit=c['unit'], api=c['api'], desc=c['desc'],
                             line=c['line'], n_valid=c['n_valid'], n_null=c['n_null'], n_bad=c['n_bad'] + c['n_special'],
                             min=c.get('min'), p01=c.get('p01'), p50=c.get('p50'), p99=c.get('p99'), max=c.get('max'),
                             n_neg=c.get('n_neg'), n_zero=c.get('n_zero'), run=c['longest_const_run']))
with open(OUTCSV, 'w', newline='', encoding='utf-8') as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
    w.writeheader(); w.writerows(rows)
print('curve definitions total', len(rows))
print('unique mnemonics (exact)', len({r['mnem'] for r in rows}), 'case-insensitive', len({(r['mnem'] or '').upper() for r in rows}))
print('unique (mnem,unit)', len({(r['mnem'], r['unit']) for r in rows}))
# candidate concept detection by keyword on desc/mnemonic/unit - for REVIEW, not final mapping
pat = {
 'GR': re.compile(r'gamma|\bGR\b|GAPI', re.I),
 'RES': re.compile(r'resist|laterolog|induction|\bOHMM\b|ohm', re.I),
 'RHOB': re.compile(r'bulk dens|density|RHOB|RHOZ|G/C3|G/CC|g/cm3|K/M3', re.I),
 'NPHI': re.compile(r'neutron|NPHI|TNPH|NPOR|CNC|NPRL', re.I),
}
for k, p in pat.items():
    cnt = collections.Counter()
    for r in rows:
        if p.search(r['desc'] or '') or p.search(r['mnem'] or '') or p.search(r['unit'] or ''):
            cnt[(r['mnem'], r['unit'], (r['desc'] or '')[:45])] += 1
    print(f'\n===== {k}: {len(cnt)} distinct (mnem,unit,desc) candidates =====')
    for (m, u, d), n in sorted(cnt.items(), key=lambda x: (-x[1], x[0])):
        print(f'{n:>3} | {m:<14} | {u:<10} | {d}')
