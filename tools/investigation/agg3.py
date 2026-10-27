import json, os, sys, collections
sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
D = sys.argv[1]
R = [json.load(open(os.path.join(D, f), encoding='utf-8')) for f in sorted(os.listdir(D))]
C = collections.Counter
print('== index curve mnemonic/unit ==')
print(C((r['datasets'][0]['curves'][0]['mnem'], r['datasets'][0]['curves'][0]['unit']) for r in R if r['datasets'][0]['curves']))
print('== depth unit STRT vs index curve ==')
for r in R:
    w = {p['mnem'].upper(): p for p in r['well'] if p['mnem']}
    c0 = r['datasets'][0]['curves'][0] if r['datasets'][0]['curves'] else None
    su = w.get('STRT', {}).get('unit', '')
    if c0 and su.upper()[:1] != (c0['unit'] or '').upper()[:1]:
        print('  unit mismatch', r['file'], 'STRT unit', su, 'index', c0['mnem'], c0['unit'])
print('== depth behaviour ==')
dirc = C()
for r in R:
    for i, ds in enumerate(r['datasets']):
        d = ds['depth']
        if not d.get('n'):
            print('NO DEPTHS', r['file']); continue
        direction = 'inc' if d['n_pos'] and not d['n_neg'] else 'dec' if d['n_neg'] and not d['n_pos'] else 'mixed'
        dirc[direction] += 1
        flags = []
        if direction == 'mixed': flags.append(f"MIXED pos={d['n_pos']} neg={d['n_neg']} rev={d['reversals']} {d['reversal_samples'][:3]}")
        if d['n_zero']: flags.append(f"dupdepth consec={d['n_zero']} {d['dup_consecutive_samples'][:3]}")
        if d['n_global_duplicates'] != d['n_zero']: flags.append(f"global dups={d['n_global_duplicates']}")
        step = r['step']
        if step and direction == 'inc' and step < 0: flags.append(f'STEP sign {step} but data increasing')
        if step and direction == 'dec' and step > 0: flags.append(f'STEP sign {step} but data decreasing')
        if d.get('mode_step') is not None and step and abs(d['mode_step'] - step) > 1e-6 * max(1, abs(step)) + 10 ** -(d['decimals'] or 0):
            flags.append(f"mode step {d['mode_step']} != STEP {step}")
        if d.get('n_dev_from_declared_step'):
            flags.append(f"dev-from-STEP={d['n_dev_from_declared_step']} of {d['n']-1} diffs; top diffs {d['diff_top'][:4]}; samples {d['dev_samples'][:2]}")
        if d.get('n_gaps_gt_1p5_mode'):
            flags.append(f"gaps={d['n_gaps_gt_1p5_mode']} max={d['max_gap']}")
        tol = abs(step or 0) * 0.5 + 1e-6
        if d.get('first_minus_strt') is not None and abs(d['first_minus_strt']) > tol:
            flags.append(f"first depth {d['first']} vs STRT {r['strt']}")
        if d.get('last_minus_stop') is not None and abs(d['last_minus_stop']) > tol:
            flags.append(f"last depth {d['last']} vs STOP {r['stop']}")
        if d.get('expected_records') and d['expected_records'] != ds['n_records']:
            flags.append(f"records {ds['n_records']} vs expected {d['expected_records']}")
        if d['n_negative']: flags.append(f"negative depths {d['n_negative']} min={d['min']}")
        if flags:
            print(r['file'], f'[ds{i} {direction} n={d["n"]} {d["first"]}->{d["last"]} STEP={step} mode={d.get("mode_step")} dec={d["decimals"]}]')
            for f in flags: print('     ', f[:330])
print(dirc)
