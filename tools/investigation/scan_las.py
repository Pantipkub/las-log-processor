#!/usr/bin/env python3
"""READ-ONLY LAS inspection scanner (analysis only, not the deliverable).

Usage: python -I scan_las.py <input_dir> <output_dir>
Opens every file in <input_dir> read-only, writes one JSON summary per file to <output_dir>.
Does NOT use any LAS library: the parsing rules are hand-written so findings are independent.
"""
import sys, os, re, json, hashlib, math
from array import array
from collections import Counter
from multiprocessing import Pool

NUM_RE = re.compile(r'[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?')
FORTRAN_RE = re.compile(r'[+-]?(?:\d+\.?\d*|\.\d+)[dD][+-]?\d+')
COMMA_DEC_RE = re.compile(r'[+-]?\d+,\d+')
SPECIAL = {'nan', '+nan', '-nan', 'inf', '+inf', '-inf', 'infinity', '+infinity', '-infinity',
           '1.#inf', '-1.#inf', '1.#qnan', '-1.#ind', '1.#ind', 'null', 'n/a', 'na', '-', '--', '***', '****'}
SENTINEL_LIKE = {-999.25, -999.0, -9999.0, -99999.0, -9999.25, -99999.25, 999.25, 9999.25, 99999.0,
                 9999.0, -999.99, -9999.99, -1e30, 1e30, -32767.0, 32767.0, -32768.0, -2147483648.0,
                 -1.0e10, 1.0e10, -999.5, -99.0, -999.9}
NS = 8  # max samples per anomaly kind


def classify_section(title):
    t = title.strip().upper()
    words = t.split()
    first = words[0] if words else ''
    if '_' in first:
        if first.startswith('LOG_DEFINITION') or first == 'CURVE_DEFINITION':
            return 'C'
        if first.startswith('LOG_DATA'):
            return 'A'
        if first.startswith('LOG_PARAMETER'):
            return 'P'
        return 'X'
    if first and first[0] in 'VWCPOA':
        return first[0]
    return 'X'


def parse_hdr(s):
    """LAS 2.0 header line: MNEM.UNIT  DATA : DESCRIPTION (first dot, first space after dot, last colon)."""
    flags = []
    dot = s.find('.')
    colon_any = s.find(':')
    if dot < 0:
        return {'mnem': None, 'unit': '', 'value': '', 'desc': '', 'flags': ['no_dot']}
    if 0 <= colon_any < dot:
        flags.append('colon_before_dot')
    mnem = s[:dot].strip()
    rest = s[dot + 1:]
    m = re.match(r'\S*', rest)
    unit = m.group(0)
    rest2 = rest[len(unit):]
    if ':' in unit:
        flags.append('unit_touches_colon')
        k = unit.find(':')
        rest2 = unit[k:] + rest2
        unit = unit[:k]
    c = rest2.rfind(':')
    if c < 0:
        value, desc = rest2.strip(), ''
        flags.append('no_colon')
    else:
        value, desc = rest2[:c].strip(), rest2[c + 1:].strip()
        if ':' in rest2[:c]:
            flags.append('multi_colon')
    if mnem == '':
        flags.append('empty_mnem')
    if ' ' in mnem or '\t' in mnem:
        flags.append('space_in_mnem')
    return {'mnem': mnem, 'unit': unit, 'value': value, 'desc': desc, 'flags': flags}


def tofloat(s):
    s = (s or '').strip()
    if NUM_RE.fullmatch(s):
        return float(s)
    # tolerate a trailing unit/word e.g. "1234.5 FT"
    p = s.split()
    if p and NUM_RE.fullmatch(p[0]):
        return float(p[0])
    return None


def pct(sorted_vals, q):
    if not sorted_vals:
        return None
    i = min(len(sorted_vals) - 1, max(0, int(round(q * (len(sorted_vals) - 1)))))
    return sorted_vals[i]


class Dataset:
    def __init__(self, curves, a_title, a_line, null, wrap, dlm):
        self.curves = curves
        self.n = len(curves)
        self.a_title = a_title
        self.a_line = a_line
        self.null = null
        self.wrap = wrap
        self.dlm = dlm
        n = self.n
        self.nnull = [0] * n
        self.nbad = [0] * n
        self.nspecial = [0] * n
        self.nfortran = [0] * n
        self.ncommadec = [0] * n
        self.nsent = [0] * n
        self.sent_vals = [Counter() for _ in range(n)]
        self.vals = [array('d') for _ in range(n)]
        self.vdepth_first = [None] * n
        self.vdepth_last = [None] * n
        self.run_cur = [0] * n
        self.run_max = [0] * n
        self.run_max_val = [None] * n
        self.last_v = [None] * n
        self.depths = array('d')
        self.depth_lines = array('l')
        self.depth_decimals = 0
        self.n_depth_null = 0
        self.n_depth_bad = 0
        self.bad_samples = []
        self.special_samples = []
        self.tokcount_hist = Counter()
        self.mismatch_samples = []
        self.n_records = 0
        self.n_mismatch_lines = 0
        self.rows_all_null = 0
        self.rows_partial_null = 0
        self.rows_full = 0
        self.first_row_with_data = None
        self.last_row_with_data = None
        self.dup_consecutive_records = 0
        self.prev_toks = None
        self.blank_lines = 0
        self.comment_lines = 0
        self.comment_samples = []
        self.blank_samples = []
        self.tab_lines = 0
        self.comma_lines = 0
        self.max_line_len = 0
        self.pending = []
        self.pending_line = None
        self.wrap_first_line_multi = 0
        self.wrap_overflow = 0
        self.wrap_overflow_samples = []
        self.truncated_tail = None
        self.data_lines = 0
        self.first_data_line = None
        self.last_data_line = None

    def feed_line(self, raw, lineno):
        s = raw.strip()
        L = len(raw.rstrip('\n'))
        if L > self.max_line_len:
            self.max_line_len = L
        if not s:
            self.blank_lines += 1
            if len(self.blank_samples) < NS:
                self.blank_samples.append(lineno)
            return
        if s[0] == '#':
            self.comment_lines += 1
            if len(self.comment_samples) < NS:
                self.comment_samples.append((lineno, s[:80]))
            return
        self.data_lines += 1
        if self.first_data_line is None:
            self.first_data_line = lineno
        self.last_data_line = lineno
        if '\t' in raw:
            self.tab_lines += 1
        if ',' in s:
            self.comma_lines += 1
        if self.dlm == 'COMMA':
            toks = [t.strip() for t in s.split(',')]
        elif self.dlm == 'TAB':
            toks = [t.strip() for t in s.split('\t')]
        else:
            toks = s.split()
        self.tokcount_hist[len(toks)] += 1
        if self.n == 0:
            return
        if not self.wrap:
            if len(toks) != self.n:
                self.n_mismatch_lines += 1
                if len(self.mismatch_samples) < NS:
                    self.mismatch_samples.append((lineno, len(toks), s[:100]))
                return
            self.record(toks, lineno)
        else:
            if not self.pending:
                self.pending_line = lineno
                if len(toks) > 1:
                    self.wrap_first_line_multi += 1
            self.pending.extend(toks)
            if len(self.pending) >= self.n:
                if len(self.pending) > self.n:
                    self.wrap_overflow += 1
                    if len(self.wrap_overflow_samples) < NS:
                        self.wrap_overflow_samples.append((self.pending_line, lineno, len(self.pending)))
                    self.pending = []
                    return
                self.record(self.pending, self.pending_line)
                self.pending = []

    def record(self, toks, lineno):
        self.n_records += 1
        if self.prev_toks is not None and toks == self.prev_toks:
            self.dup_consecutive_records += 1
        self.prev_toks = list(toks)
        null = self.null
        depth = None
        nvalid_nondepth = 0
        nnull_nondepth = 0
        for j, t in enumerate(toks):
            v = None
            if NUM_RE.fullmatch(t):
                v = float(t)
                if math.isinf(v):
                    self.nspecial[j] += 1
                    v = None
                    if len(self.special_samples) < NS:
                        self.special_samples.append((lineno, j, t))
            else:
                tl = t.lower()
                if tl in SPECIAL:
                    self.nspecial[j] += 1
                    if len(self.special_samples) < NS:
                        self.special_samples.append((lineno, j, t))
                elif FORTRAN_RE.fullmatch(t):
                    self.nfortran[j] += 1
                    v = float(t.replace('d', 'e').replace('D', 'E'))
                elif COMMA_DEC_RE.fullmatch(t):
                    self.ncommadec[j] += 1
                    if len(self.bad_samples) < NS:
                        self.bad_samples.append((lineno, j, t))
                else:
                    self.nbad[j] += 1
                    if len(self.bad_samples) < NS:
                        self.bad_samples.append((lineno, j, t))
            is_null = v is not None and null is not None and v == null
            if j == 0:
                if v is None:
                    self.n_depth_bad += 1
                elif is_null:
                    self.n_depth_null += 1
                else:
                    depth = v
                    self.depths.append(v)
                    self.depth_lines.append(lineno)
                    k = t.find('.')
                    if k >= 0:
                        e = t.lower().find('e')
                        dec = (e if e > 0 else len(t)) - k - 1
                        if dec > self.depth_decimals:
                            self.depth_decimals = dec
            if is_null:
                self.nnull[j] += 1
                if j:
                    nnull_nondepth += 1
                self.run_cur[j] = 0
                self.last_v[j] = None
                continue
            if v is None:
                if j:
                    nnull_nondepth += 1
                self.run_cur[j] = 0
                self.last_v[j] = None
                continue
            if v in SENTINEL_LIKE:
                self.nsent[j] += 1
                self.sent_vals[j][v] += 1
            self.vals[j].append(v)
            if j:
                nvalid_nondepth += 1
            if self.vdepth_first[j] is None:
                self.vdepth_first[j] = depth
            self.vdepth_last[j] = depth
            if self.last_v[j] == v:
                self.run_cur[j] += 1
            else:
                self.run_cur[j] = 1
                self.last_v[j] = v
            if self.run_cur[j] > self.run_max[j]:
                self.run_max[j] = self.run_cur[j]
                self.run_max_val[j] = v
        if self.n > 1:
            if nvalid_nondepth == 0:
                self.rows_all_null += 1
            else:
                if self.first_row_with_data is None:
                    self.first_row_with_data = (self.n_records, lineno, depth)
                self.last_row_with_data = (self.n_records, lineno, depth)
                if nnull_nondepth:
                    self.rows_partial_null += 1
                else:
                    self.rows_full += 1

    def finish(self, step_decl, strt, stop):
        if self.pending:
            self.truncated_tail = {'start_line': self.pending_line, 'tokens': len(self.pending), 'expected': self.n}
        out = {
            'a_title': self.a_title, 'a_line': self.a_line, 'n_curves': self.n,
            'wrap': self.wrap, 'dlm': self.dlm,
            'data_lines': self.data_lines, 'first_data_line': self.first_data_line, 'last_data_line': self.last_data_line,
            'n_records': self.n_records, 'blank_lines': self.blank_lines, 'blank_samples': self.blank_samples,
            'comment_lines': self.comment_lines, 'comment_samples': self.comment_samples,
            'tab_lines': self.tab_lines, 'comma_lines': self.comma_lines, 'max_line_len': self.max_line_len,
            'tokcount_hist': dict(self.tokcount_hist.most_common(12)), 'n_tokcount_distinct': len(self.tokcount_hist),
            'n_mismatch_lines': self.n_mismatch_lines, 'mismatch_samples': self.mismatch_samples,
            'wrap_first_line_multi': self.wrap_first_line_multi, 'wrap_overflow': self.wrap_overflow,
            'wrap_overflow_samples': self.wrap_overflow_samples, 'truncated_tail': self.truncated_tail,
            'bad_samples': self.bad_samples, 'special_samples': self.special_samples,
            'rows_all_null': self.rows_all_null, 'rows_partial_null': self.rows_partial_null, 'rows_full': self.rows_full,
            'first_row_with_data': self.first_row_with_data, 'last_row_with_data': self.last_row_with_data,
            'dup_consecutive_records': self.dup_consecutive_records,
            'n_depth_null': self.n_depth_null, 'n_depth_bad': self.n_depth_bad,
        }
        # depth analysis
        d = self.depths
        dl = self.depth_lines
        dep = {'n': len(d), 'decimals': self.depth_decimals}
        if len(d):
            dep['first'] = d[0]
            dep['last'] = d[-1]
            dep['min'] = min(d)
            dep['max'] = max(d)
            dep['n_negative'] = sum(1 for x in d if x < 0)
            npos = nneg = nzero = 0
            diffs = Counter()
            rev_samples, dup_samples, gap_samples, dev_samples = [], [], [], []
            reversals = 0
            last_sign = 0
            rnd = min(self.depth_decimals + 3, 8)
            for i in range(1, len(d)):
                df = d[i] - d[i - 1]
                if df > 0:
                    npos += 1; sg = 1
                elif df < 0:
                    nneg += 1; sg = -1
                else:
                    nzero += 1; sg = 0
                    if len(dup_samples) < NS:
                        dup_samples.append((dl[i], d[i]))
                if sg and last_sign and sg != last_sign:
                    reversals += 1
                    if len(rev_samples) < NS:
                        rev_samples.append((dl[i], d[i - 1], d[i]))
                if sg:
                    last_sign = sg
                if len(diffs) < 5000 or round(df, rnd) in diffs:
                    diffs[round(df, rnd)] += 1
                else:
                    diffs['other'] += 1
            dep.update(n_pos=npos, n_neg=nneg, n_zero=nzero, reversals=reversals,
                       reversal_samples=rev_samples, dup_consecutive_samples=dup_samples)
            dep['n_unique'] = len(set(d))
            dep['n_global_duplicates'] = len(d) - dep['n_unique']
            dep['diff_top'] = [(k, v) for k, v in diffs.most_common(6)]
            dep['n_distinct_diffs'] = len(diffs)
            num_diffs = [(k, v) for k, v in diffs.most_common() if k != 'other' and k != 0]
            mode = num_diffs[0][0] if num_diffs else None
            dep['mode_step'] = mode
            tol_print = 1.01 * 10 ** (-self.depth_decimals) if self.depth_decimals else 0.0
            if mode:
                ngap = 0
                maxgap = (0, None)
                ndev_mode = 0
                for i in range(1, len(d)):
                    df = d[i] - d[i - 1]
                    if abs(df - mode) > max(tol_print, abs(mode) * 1e-3) + 1e-9:
                        ndev_mode += 1
                    if mode and df / mode > 1.5:
                        ngap += 1
                        if len(gap_samples) < NS:
                            gap_samples.append((dl[i], d[i - 1], d[i]))
                        if abs(df) > abs(maxgap[0]):
                            maxgap = (df, dl[i])
                dep.update(n_dev_from_mode=ndev_mode, n_gaps_gt_1p5_mode=ngap, gap_samples=gap_samples, max_gap=maxgap)
            if step_decl not in (None, 0.0):
                ndev = 0
                tol = max(tol_print, abs(step_decl) * 1e-3) + 1e-9
                for i in range(1, len(d)):
                    df = d[i] - d[i - 1]
                    if abs(df - step_decl) > tol:
                        ndev += 1
                        if len(dev_samples) < NS:
                            dev_samples.append((dl[i], d[i - 1], d[i]))
                dep.update(n_dev_from_declared_step=ndev, dev_samples=dev_samples, step_tol=tol)
                if strt is not None and stop is not None:
                    dep['expected_records'] = int(round(abs(stop - strt) / abs(step_decl))) + 1
            if strt is not None:
                dep['first_minus_strt'] = d[0] - strt
            if stop is not None:
                dep['last_minus_stop'] = d[-1] - stop
        out['depth'] = dep
        curves = []
        for j, c in enumerate(self.curves):
            v = sorted(self.vals[j])
            n = len(v)
            st = {'pos': j, 'mnem': c['mnem'], 'unit': c['unit'], 'api': c['value'], 'desc': c['desc'],
                  'line': c['line'], 'n_valid': n, 'n_null': self.nnull[j], 'n_bad': self.nbad[j],
                  'n_special': self.nspecial[j], 'n_fortran': self.nfortran[j], 'n_commadec': self.ncommadec[j],
                  'n_sentinel_like': self.nsent[j],
                  'sentinel_vals': {str(k): x for k, x in self.sent_vals[j].most_common(4)},
                  'valid_depth_first': self.vdepth_first[j], 'valid_depth_last': self.vdepth_last[j],
                  'longest_const_run': self.run_max[j], 'longest_const_val': self.run_max_val[j]}
            if n:
                st.update(min=v[0], max=v[-1], p01=pct(v, 0.01), p50=pct(v, 0.5), p99=pct(v, 0.99),
                          mean=sum(v) / n, n_neg=sum(1 for x in v if x < 0), n_zero=sum(1 for x in v if x == 0),
                          n_distinct_capped=min(len(set(v[:200000])), 200000))
            curves.append(st)
        out['curves'] = curves
        return out


def scan(args):
    path, outdir = args
    name = os.path.basename(path)
    res = {'file': name, 'size': os.path.getsize(path)}
    # ---- pass 1: raw bytes
    h = hashlib.sha256()
    crlf = lf = cr = nul = nonascii = ctrlz = 0
    first_nonascii = None
    bom = False
    prev_last = b''
    offset = 0
    with open(path, 'rb') as f:
        head = f.read(4)
        bom = head.startswith(b'\xef\xbb\xbf')
        f.seek(0)
        tail_byte = b''
        while True:
            chunk = f.read(1 << 22)
            if not chunk:
                break
            h.update(chunk)
            buf = prev_last + chunk
            c_crlf = buf.count(b'\r\n')
            crlf += c_crlf
            lf += chunk.count(b'\n')
            cr += chunk.count(b'\r')
            prev_last = chunk[-1:]
            nul += chunk.count(b'\x00')
            ctrlz += chunk.count(b'\x1a')
            if not chunk.isascii():
                cnt = sum(1 for b in chunk if b > 127)
                nonascii += cnt
                if first_nonascii is None:
                    for i, b in enumerate(chunk):
                        if b > 127:
                            first_nonascii = offset + i
                            break
            offset += len(chunk)
            tail_byte = chunk[-1:]
    res['sha256'] = h.hexdigest()
    res['bytes'] = {'bom': bom, 'crlf': crlf, 'lf_only': lf - crlf, 'cr_only': cr - crlf, 'nul': nul,
                    'ctrlz': ctrlz, 'nonascii': nonascii, 'first_nonascii_offset': first_nonascii,
                    'ends_with_newline': tail_byte in (b'\n', b'\r')}
    utf8_ok = True
    if nonascii:
        try:
            with open(path, 'rb') as f:
                f.read().decode('utf-8')
        except UnicodeDecodeError:
            utf8_ok = False
    res['bytes']['utf8_valid'] = utf8_ok
    # ---- pass 2: structure (latin-1 maps every byte; universal newlines)
    sections = []
    hdr = {'V': [], 'W': [], 'C': [], 'P': []}
    cur_curves = []
    sec_stats = []
    preamble = []
    nonascii_lines = []
    datasets = []
    ds = None
    kind = None
    o_lines = 0
    x_lines = 0
    hdr_bad = []
    hdr_comments = 0
    hdr_blank = 0
    hdr_tabs = 0
    hdr_maxlen = 0
    lineno = 0
    null = None
    wrap = None
    dlm = 'SPACE'
    step = strt = stop = None
    v_seen = 0
    with open(path, 'r', encoding='latin-1', newline=None) as f:
        for raw in f:
            lineno += 1
            s = raw.strip()
            if nonascii and len(nonascii_lines) < NS and not raw.isascii():
                nonascii_lines.append((lineno, s[:80]))
            if s.startswith('~'):
                if ds is not None:
                    datasets.append(ds.finish(step, strt, stop))
                    ds = None
                title = s[1:]
                kind = classify_section(title)
                sections.append((lineno, s[:60], kind))
                if kind == 'V':
                    v_seen += 1
                if kind == 'C':
                    cur_curves = []
                    hdr['C'].append(cur_curves)
                if kind == 'A':
                    # resolve null/wrap/step from the latest header data
                    ds = Dataset(cur_curves, s[:200], lineno, null, bool(wrap), dlm)
                continue
            if kind is None:
                if s:
                    preamble.append((lineno, s[:80]))
                continue
            if kind == 'A':
                ds.feed_line(raw, lineno)
                continue
            # header-ish sections
            hdr_maxlen = max(hdr_maxlen, len(raw.rstrip('\n')))
            if '\t' in raw:
                hdr_tabs += 1
            if not s:
                hdr_blank += 1
                continue
            if s[0] == '#':
                hdr_comments += 1
                continue
            if kind == 'O':
                o_lines += 1
                continue
            if kind == 'X':
                x_lines += 1
                continue
            p = parse_hdr(s)
            p['line'] = lineno
            if p['mnem'] is None or set(p['flags']) & {'no_colon', 'colon_before_dot', 'empty_mnem'}:
                if len(hdr_bad) < 20:
                    hdr_bad.append((kind, lineno, s[:100], p['flags']))
            if kind == 'C':
                if p['mnem'] is not None:
                    cur_curves.append(p)
                continue
            hdr[kind].append(p)
            mu = (p['mnem'] or '').upper()
            if kind == 'V':
                if mu == 'WRAP':
                    wrap = p['value'].upper().startswith('Y')
                    if p['value'] == '':
                        wrap = None
                elif mu == 'DLM':
                    dlm = p['value'].upper() or 'SPACE'
            if kind == 'W':
                if mu == 'NULL':
                    null = tofloat(p['value'])
                elif mu == 'STEP':
                    step = tofloat(p['value'])
                elif mu == 'STRT':
                    strt = tofloat(p['value'])
                elif mu == 'STOP':
                    stop = tofloat(p['value'])
    if ds is not None:
        datasets.append(ds.finish(step, strt, stop))
    res['lines'] = lineno
    res['sections'] = sections
    res['preamble'] = preamble[:NS]
    res['n_preamble'] = len(preamble)
    res['nonascii_lines'] = nonascii_lines
    res['version'] = hdr['V']
    res['well'] = hdr['W']
    res['n_param'] = len(hdr['P'])
    res['params'] = [(p['mnem'], p['unit'], p['value'][:40], p['desc'][:50]) for p in hdr['P']]
    res['curve_sets'] = [[(c['mnem'], c['unit'], c['value'], c['desc'], c['line'], c['flags']) for c in cs] for cs in hdr['C']]
    res['o_lines'] = o_lines
    res['x_lines'] = x_lines
    res['hdr_bad'] = hdr_bad
    res['hdr_comments'] = hdr_comments
    res['hdr_blank'] = hdr_blank
    res['hdr_tabs'] = hdr_tabs
    res['hdr_maxlen'] = hdr_maxlen
    res['null'] = null
    res['wrap'] = wrap
    res['dlm'] = dlm
    res['strt'], res['stop'], res['step'] = strt, stop, step
    res['n_version_sections'] = v_seen
    res['datasets'] = datasets
    with open(os.path.join(outdir, name + '.json'), 'w', encoding='utf-8') as o:
        json.dump(res, o, default=str)
    return name, lineno


if __name__ == '__main__':
    indir, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    files = sorted(os.path.join(indir, f) for f in os.listdir(indir) if os.path.isfile(os.path.join(indir, f)))
    files.sort(key=lambda p: -os.path.getsize(p))  # biggest first for better load balancing
    with Pool(min(16, os.cpu_count() or 4)) as pool:
        for name, n in pool.imap_unordered(scan, [(p, outdir) for p in files]):
            print(f'{n:>10} lines  {name}', flush=True)
    print('DONE', len(files))
