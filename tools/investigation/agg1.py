import json, os, sys, collections
sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
D = sys.argv[1]
files = sorted(os.listdir(D))
R = [json.load(open(os.path.join(D, f), encoding='utf-8')) for f in files]
C = collections.Counter
def wv(r, m):
    for p in r['well']:
        if (p['mnem'] or '').upper() == m:
            return p
    return None
def vv(r, m):
    for p in r['version']:
        if (p['mnem'] or '').upper() == m:
            return p
    return None
print('files', len(R), 'total bytes', sum(r['size'] for r in R), 'total lines', sum(r['lines'] for r in R))
# duplicates by hash
h = collections.defaultdict(list)
for r in R: h[r['sha256']].append(r['file'])
print('== identical-content groups ==')
for k, v in h.items():
    if len(v) > 1: print(v)
print('== bytes ==')
print('bom', [r['file'] for r in R if r['bytes']['bom']])
print('lf_only files', [(r['file'], r['bytes']['lf_only']) for r in R if r['bytes']['lf_only']][:20])
print('cr_only files', [(r['file'], r['bytes']['cr_only']) for r in R if r['bytes']['cr_only']])
print('mixed EOL', [r['file'] for r in R if r['bytes']['lf_only'] and r['bytes']['crlf']])
print('nul', [(r['file'], r['bytes']['nul']) for r in R if r['bytes']['nul']])
print('ctrlz', [(r['file'], r['bytes']['ctrlz']) for r in R if r['bytes']['ctrlz']])
print('nonascii', [(r['file'], r['bytes']['nonascii'], r['bytes']['utf8_valid'], r['nonascii_lines'][:3]) for r in R if r['bytes']['nonascii']])
print('no final newline', [r['file'] for r in R if not r['bytes']['ends_with_newline']])
print('== versions ==')
print(C((vv(r,'VERS') or {}).get('value') for r in R))
print('WRAP', C(str(r['wrap']) for r in R), 'DLM', C(r['dlm'] for r in R))
print('missing VERS', [r['file'] for r in R if not vv(r,'VERS')])
print('missing WRAP', [r['file'] for r in R if not vv(r,'WRAP')])
print('n_version_sections>1', [(r['file'], r['n_version_sections']) for r in R if r['n_version_sections'] != 1])
print('== section layouts ==')
lay = C(''.join(k for _, _, k in r['sections']) for r in R)
for k, v in lay.most_common(): print(v, k)
print('X sections', C(t for r in R for _, t, k in r['sections'] if k == 'X'))
print('multi A', [(r['file'], len(r['datasets'])) for r in R if len(r['datasets']) != 1])
print('preamble', [(r['file'], r['preamble'][:2]) for r in R if r['n_preamble']])
print('== null ==')
print(C(r['null'] for r in R))
print('no NULL item', [r['file'] for r in R if not wv(r,'NULL')])
print('NULL raw variants', C((wv(r,'NULL') or {}).get('value') for r in R))
print('== step ==')
print(C(r['step'] for r in R))
print('STRT unit', C((wv(r,'STRT') or {}).get('unit') for r in R))
print('missing STRT/STOP/STEP', [(r['file'], [m for m in ('STRT','STOP','STEP') if not wv(r,m)]) for r in R if not all(wv(r,m) for m in ('STRT','STOP','STEP'))])
print('== hdr_bad ==')
for r in R:
    if r['hdr_bad']: print(r['file'], r['hdr_bad'][:6])
