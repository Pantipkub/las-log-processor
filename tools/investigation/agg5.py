"""Draft alias table -> per-file selection + value-range evidence per (mnemonic, unit)."""
import csv, sys, collections, json, os
sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8')))
OUT = sys.argv[2]
ALIAS = {  # priority order (first = preferred). Draft for evidence review.
 'GR':   ['GR', 'GR_EDTC', 'ECGR', 'ECGR_EDTC', 'GRGC', 'GAMMA', 'GR1', 'G1', 'SGR', 'HSGR', 'GRSG', 'GMSG', 'GRGM',
          'HGR', 'HGR_EDTC', 'EHGR_EDTC', 'GRTO', 'GAPI', 'GR_CAL'],
 'RES_DEEP': ['AT90', 'AF90', 'AO90', 'M2R9', 'M0R9', 'TBIT90', 'TBIF90', 'RT90', 'ILD', 'RILD', 'LLD', 'DDLL', 'RLLD',
              'RLA5', 'MLR4C', 'MLR4', 'RD', 'RT', 'REST', 'RTAT', 'RT_HRLT', 'R85T'],
 'RHOB': ['RHOB', 'RHOZ', 'DEN', 'ZDNC', 'ZDEN', 'RHO8', 'RHOL'],
 'NPHI': ['TNPH', 'NPHI', 'NPRL', 'NPL', 'TNPH_LIM', 'NPHL', 'NPHC', 'CNC', 'CNPOR', 'NPOR', 'HTNP', 'HNPO'],
}
by_file = collections.defaultdict(list)
for r in rows:
    if r['ds'] == '0':
        by_file[r['file']].append(r)
stats = collections.defaultdict(list)
sel = {}
for f, cs in by_file.items():
    sel[f] = {}
    for k, al in ALIAS.items():
        cands = []
        for a in al:
            for c in cs:
                if (c['mnem'] or '').upper() == a:
                    cands.append(c)
        for c in cands:
            stats[(k, c['mnem'].upper(), c['unit'].upper())].append(c)
        valid = [c for c in cands if int(c['n_valid']) > 0]
        sel[f][k] = (valid[0]['mnem'], valid[0]['unit'], int(valid[0]['n_valid'])) if valid else (('ALL-NULL:' + cands[0]['mnem']) if cands else None)
def fl(x):
    try: return float(x)
    except: return None
print('== value evidence per (concept, mnemonic, unit): files | median of p01 / p50 / p99 | min / max | files w/ n_valid=0 ==')
for key in sorted(stats):
    L = stats[key]
    def med(field):
        v = sorted(fl(c[field]) for c in L if fl(c[field]) is not None)
        return round(v[len(v)//2], 4) if v else None
    mins = [fl(c['min']) for c in L if fl(c['min']) is not None]
    maxs = [fl(c['max']) for c in L if fl(c['max']) is not None]
    print(f"{key[0]:<9}{key[1]:<10}{key[2]:<9} n={len(L):<3} p01={med('p01')} p50={med('p50')} p99={med('p99')} min={min(mins) if mins else None} max={max(maxs) if maxs else None} empty={sum(1 for c in L if int(c['n_valid'])==0)}")
cov = collections.Counter()
for f in sel:
    for k in ALIAS:
        s = sel[f][k]
        cov[(k, 'ok' if isinstance(s, tuple) else 'allnull' if s else 'absent')] += 1
print(cov)
json.dump(sel, open(OUT, 'w'), indent=0)
print('== files missing a concept ==')
for f in sorted(sel):
    miss = [k for k in ALIAS if not isinstance(sel[f][k], tuple)]
    if miss: print(f'{f[:95]:<95} missing={miss}')
