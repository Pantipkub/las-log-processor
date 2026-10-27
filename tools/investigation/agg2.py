import json, os, sys, collections
sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
D = sys.argv[1]
R = [json.load(open(os.path.join(D, f), encoding='utf-8')) for f in sorted(os.listdir(D))]
print('== null-declared value files (non -999.25) ==')
for r in R:
    if r['null'] != -999.25:
        ds = r['datasets'][0]
        print(r['file'], 'NULL=', r['null'], 'nulls found per curve:', sum(c['n_null'] for c in ds['curves']), 'sentinel-like:', {c['mnem']: c['sentinel_vals'] for c in ds['curves'] if c['n_sentinel_like'] and c['pos']})
print('== datasets: record integrity ==')
for r in R:
    for i, ds in enumerate(r['datasets']):
        issues = []
        if ds['n_mismatch_lines']: issues.append(('mismatch', ds['n_mismatch_lines'], ds['tokcount_hist'], ds['mismatch_samples'][:3]))
        if ds['wrap_overflow']: issues.append(('wrap_overflow', ds['wrap_overflow'], ds['wrap_overflow_samples'][:3]))
        if ds['wrap'] and ds['wrap_first_line_multi']: issues.append(('wrap_first_line_multi', ds['wrap_first_line_multi']))
        if ds['truncated_tail']: issues.append(('truncated_tail', ds['truncated_tail']))
        if ds['blank_lines']: issues.append(('blank_in_data', ds['blank_lines'], ds['blank_samples'][:4]))
        if ds['comment_lines']: issues.append(('comment_in_data', ds['comment_lines'], ds['comment_samples'][:3]))
        if ds['bad_samples']: issues.append(('bad_tokens', sum(c['n_bad'] for c in ds['curves']), sum(c['n_commadec'] for c in ds['curves']), ds['bad_samples'][:5]))
        if ds['special_samples']: issues.append(('special', sum(c['n_special'] for c in ds['curves']), ds['special_samples'][:4]))
        nf = sum(c['n_fortran'] for c in ds['curves'])
        if nf: issues.append(('fortran', nf))
        if ds['n_records'] == 0: issues.append(('NO_RECORDS', ds['data_lines'], ds['tokcount_hist']))
        if ds['tab_lines']: issues.append(('tabs', ds['tab_lines']))
        if ds['comma_lines'] and ds['dlm'] != 'COMMA': issues.append(('commas_in_space_delim', ds['comma_lines']))
        if ds['n_depth_null'] or ds['n_depth_bad']: issues.append(('depth_null/bad', ds['n_depth_null'], ds['n_depth_bad']))
        if ds['dup_consecutive_records']: issues.append(('dup_records', ds['dup_consecutive_records']))
        if issues:
            print(r['file'], f'[ds{i} wrap={ds["wrap"]} dlm={ds["dlm"]} ncurv={ds["n_curves"]} recs={ds["n_records"]}]')
            for x in issues: print('    ', str(x)[:400])
