#!/usr/bin/env python3
"""So sánh máy móc phần gọi adb giữa Inapp-Patch (PicsartToolGUI.cs) và ToolADB.

Cách dùng:  python3 tools/verify_port.py [đường_dẫn_repo_Inapp-Patch]   (mặc định ../Inapp-Patch)
Thoát mã 1 nếu có bất kỳ chỗ nào khác.

Hai lớp kiểm tra:
  1. Picsart: toàn bộ chuỗi lệnh shell của từng mode (deeplink/Pro/Ultra/Reset/Reset giữ đăng nhập/taint).
  2. Chess, Grammarly, quét offer: CHUỖI GỌI ADB THEO THỨ TỰ — từng lệnh (root/shell/pull/push),
     tham số device-side và timeout; cộng thêm SQL Chess và các hằng hex Grammarly.

Mã gốc truyền lệnh qua ProcessStartInfo.Arguments (chuỗi command line, shell "...") nên phải mô phỏng:
C#-unescape → string.Format → CommandLineToArgvW. ToolADB truyền qua ArgumentList nên chỉ cần C#-unescape.
Đường dẫn máy tính được quy về ký hiệu LOCAL(tên file) vì thư mục backup hai bên khác nhau (chủ đích).
"""
import re, sys, os

ID = "0123456789abcdef0123456789abcdef"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INAPP = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(ROOT), 'Inapp-Patch')
ORIG_FILE = os.path.join(INAPP, 'PicsartToolGUI.cs')
DB = '/data/data/com.android.vending/databases/sku_details.db'

def read(path):
    src = open(path, encoding='utf-8').read()
    return '\n'.join(l for l in src.split('\n') if not l.strip().startswith('//'))

def cs_unescape(s):
    out, i = [], 0
    while i < len(s):
        c = s[i]
        if c == '\\' and i + 1 < len(s):
            n = s[i + 1]
            if n == '\\': out.append('\\'); i += 2; continue
            if n == '"': out.append('"'); i += 2; continue
            if n == 'n': out.append('\n'); i += 2; continue
            if n == 'r': out.append('\r'); i += 2; continue
        out.append(c); i += 1
    return ''.join(out)

LITERAL = r'"((?:[^"\\]|\\.)*)"'
TOKEN = re.compile(LITERAL + r'|([A-Za-z_][A-Za-z_0-9]*)')

def assemble(expr, table):
    parts = []
    for lit, ident in TOKEN.findall(expr):
        if ident and ident in table: parts.append(table[ident])
        elif not ident: parts.append(cs_unescape(lit))
    return ''.join(parts)

def block(src, start_pat, end_pat=r';\s*\n'):
    m = re.search(start_pat + r'(.*?)' + end_pat, src, re.S)
    if not m: raise SystemExit('Không tìm thấy: ' + start_pat)
    return m.group(1)

def fmt(s): return s.replace('{{', '{').replace('}}', '}').replace('{0}', ID)
def win(s): return s.replace('\\"', '"')

results = []
def compare(name, a, b):
    results.append((name, a == b))
    print(f'{name:34s}: {"IDENTICAL" if a == b else "DIFFERENT"}')
    if a != b:
        a, b = str(a), str(b)
        i = next((i for i in range(min(len(a), len(b))) if a[i] != b[i]), min(len(a), len(b)))
        print('   orig:', repr(a[max(0, i-60):i+100]))
        print('   port:', repr(b[max(0, i-60):i+100]))

orig = read(ORIG_FILE)

# ====================== 1. Picsart: toàn bộ lệnh từng mode ======================
ot = {k: assemble(block(orig, rf'const string {k}\s*='), {}) for k in
      ('FixPermissionsSnippet', 'PatchMonthlySnippet', 'PatchUltraSnippet', 'OpenPaywallDeepLinkCmd')}
ot['GetCleanSubscriptionCacheSnippet'] = fmt(assemble(block(orig, r'return string\.Format\(', r',\s*\n\s*newDeviceId\);'), {}))
def orig_mode(var):
    cmd = win(fmt(assemble(block(orig, rf'string {var}\s*=\s*', r';\s*\n'), ot)))
    assert cmd.startswith('shell "') and cmd.endswith('"'), cmd[:40]
    return cmd[len('shell "'):-1]
taint = win(assemble(block(orig, r'string check = RunAdb\(string\.Format\(', r',\s*serial\)'), {}))
taint = re.sub(r'^-s \{0\} shell "', '', taint).rstrip('"')

pics = read(os.path.join(ROOT, 'ToolADB/Features/Picsart/PicsartService.cs')).replace('" + newDeviceId + "', ID)
pt = {k: assemble(block(pics, rf'const string {k}\s*='), {}) for k in
      ('FixPermissionsSnippet', 'PatchMonthlySnippet', 'PatchUltraSnippet', 'OpenPaywallDeepLinkCmd')}
pt['GetCleanSubscriptionCacheSnippet'] = assemble(block(pics, r'GetCleanSubscriptionCacheSnippet\(string newDeviceId\) =>', r';\s*\n'), {})
def port_case(name):
    return assemble(block(pics, rf'case PicsartAction\.{name}:\s*\n\s*CaptureAdb\(serial, \d+, "shell",', r'\);\s*\n'), pt)

compare('Picsart deeplink', win(ot['OpenPaywallDeepLinkCmd']), pt['OpenPaywallDeepLinkCmd'])
compare('Picsart ultra', orig_mode('ultraCmd'), port_case('UltraMonthly'))
compare('Picsart monthly (Pro tháng)', orig_mode('patchCmd'), port_case('ProMonthly'))
compare('Picsart full (Xóa dữ liệu)', orig_mode('fullCmd'), port_case('Reset'))
compare('Picsart keep (Reset giữ đăng nhập)', orig_mode('keepCmd'), port_case('ResetKeepLogin'))
compare('Picsart taint check', taint, assemble(block(pics, r'string check = CaptureAdb\(serial, 5000, "shell",', r'\);\s*\n'), {}))

# ====================== 2. Chuỗi gọi adb theo thứ tự ======================
SYM = {'serial': 'SERIAL', 'playDb': 'DB', 'remoteSql': 'REMOTE',
       'dbBefore': 'LOCAL(sku_details.before.db)', 'dbAfter': 'LOCAL(sku_details.after.db)',
       'sqlFile': 'LOCAL(patch_chess_no_trial.sql)', 'tempFile': 'LOCAL(manakin.pb)', 'localSql': 'LOCAL(offer_scan.sql)'}

def sym(expr):
    expr = expr.strip()
    m = re.fullmatch(r'Path\.Combine\(backupDir,\s*"([^"]+)"\)', expr)
    if m: return 'LOCAL(' + m.group(1) + ')'
    m = re.fullmatch(r'playDb\s*\+\s*"([^"]+)"', expr)
    if m: return 'DB' + m.group(1)
    if expr in SYM: return SYM[expr]
    if expr.startswith('"'): return cs_unescape(''.join(re.findall(LITERAL, expr)))
    return '?' + expr

def win_split(cmdline):
    args, cur, i, inq = [], [], 0, False
    while i < len(cmdline):
        c = cmdline[i]
        if c == '\\' and i + 1 < len(cmdline) and cmdline[i+1] == '"': cur.append('"'); i += 2; continue
        if c == '"': inq = not inq; i += 1; continue
        if c == ' ' and not inq:
            if cur: args.append(''.join(cur)); cur = []
            i += 1; continue
        cur.append(c); i += 1
    if cur: args.append(''.join(cur))
    return args

def orig_calls(body):
    calls = []
    for m in re.finditer(r'RunAdb\((.*?),\s*(\d+)\);', body, re.S):
        expr, timeout = m.group(1), int(m.group(2))
        fm = re.match(r'string\.Format\(\s*((?:' + LITERAL + r'\s*\+?\s*)+),\s*(.*)\)\s*$', expr.strip(), re.S)
        if fm:
            fmtstr = cs_unescape(''.join(re.findall(LITERAL, fm.group(1))))
            argv = [sym(a) for a in re.split(r',(?![^()]*\))', fm.group(3))]
            for k in range(len(argv)): fmtstr = fmtstr.replace('{%d}' % k, '\x00%d\x00' % k)
            toks = [re.sub(r'\x00(\d+)\x00', lambda mm: argv[int(mm.group(1))], t) for t in win_split(fmtstr)]
        else:
            toks = win_split(cs_unescape(''.join(re.findall(LITERAL, expr))))
        calls.append((timeout, toks))
    return calls

PIECE = re.compile(LITERAL.replace('(', '(?:', 1) + r'|Path\.Combine\(backupDir,\s*"[^"]+"\)|[A-Za-z_][\w]*')
def port_calls(body):
    calls = []
    for m in re.finditer(r'CaptureAdb\(serial,\s*(\d+),\s*(.*?)\);\n', body, re.S):
        timeout, rest = int(m.group(1)), m.group(2)
        parts, cur, depth, inq, i = [], [], 0, False, 0
        while i < len(rest):
            c = rest[i]
            if inq and c == '\\': cur.append(c); cur.append(rest[i+1]); i += 2; continue
            if c == '"': inq = not inq
            if not inq:
                if c == '(': depth += 1
                if c == ')': depth -= 1
                if c == ',' and depth == 0: parts.append(''.join(cur)); cur = []; i += 1; continue
            cur.append(c); i += 1
        parts.append(''.join(cur))
        toks = ['-s', 'SERIAL']
        for p in parts:
            s = ''
            for piece in PIECE.findall(p):
                if piece.startswith('"'): s += cs_unescape(piece[1:-1])
                elif piece.startswith('Path.Combine'): s += 'LOCAL(' + re.search(r'"([^"]+)"', piece).group(1) + ')'
                else: s += SYM.get(piece, '?' + piece)
            toks.append(s)
        calls.append((timeout, toks))
    return calls

def norm(calls):
    return [(t, [re.sub(r'/data/local/tmp/(?:chess_no_trial|offer_scan)_[^ ;]*\.sql', 'REMOTE', a) for a in toks]) for t, toks in calls]

def compare_sequence(name, orig_body, port_body):
    oc, pc = norm(orig_calls(orig_body)), norm(port_calls(port_body))
    ok = oc == pc
    results.append((name, ok))
    print(f'{name:34s}: {"IDENTICAL" if ok else "DIFFERENT"}  ({len(oc)} lệnh adb gốc / {len(pc)} port)')
    if not ok:
        for i in range(max(len(oc), len(pc))):
            o = oc[i] if i < len(oc) else None; p = pc[i] if i < len(pc) else None
            if o != p: print(f'   #{i+1} orig: {o}\n   #{i+1} port: {p}')

oc_body = block(orig, r'private void PatchChessNoTrial\(string serial\)', r'private void CheckAndWarnIfAccountTainted')
pc_body = read(os.path.join(ROOT, 'ToolADB/Features/InappTrial/InappTrialService.Chess.cs'))
compare_sequence('Chess: chuỗi gọi adb', oc_body, pc_body)
def sql_of(src, const):
    body = re.sub(r'"\s*\+\s*' + const + r'\s*\+\s*"', '4102444800000', block(src, r'string sql =\s*', r';\s*\n\s*File\.WriteAllText'))
    return assemble(body, {})
compare('Chess: SQL', sql_of(oc_body, 'farFutureExpiration'), sql_of(pc_body, 'farFutureExpiration'))

og_body = block(orig, r'private void PatchGrammarlyNoTrial\(string serial\)', r'private void PatchChessNoTrial')
pg_body = read(os.path.join(ROOT, 'ToolADB/Features/InappTrial/InappTrialService.Grammarly.cs'))
compare_sequence('Grammarly: chuỗi gọi adb', og_body, pg_body)
for h in ('1A660A12616E64726F69645F667265655F747269616C', '1A690A12616E64726F69645F667265655F747269616C',
          '2A06746573745F31', '2A09636F6E74726F6C5F31'):
    compare('Grammarly: hex ' + h[:8], h if h in og_body else '', h if h in pg_body else '')

os_body = block(orig, r'private List<BillingOfferInfo> ScanOffersForDevice\(', r'private static string Csv\(')
ps_body = read(os.path.join(ROOT, 'ToolADB/Features/InappTrial/InappTrialService.OfferScanner.cs'))
compare_sequence('Quét offer: chuỗi gọi adb', os_body, ps_body)
def scan_sql(src):
    return assemble(block(src, r'string sql =\s*', r';\s*\n\s*File\.WriteAllText'), {'packageName': 'PACKAGE'})
compare('Quét offer: SQL', scan_sql(os_body), scan_sql(ps_body))

bad = [n for n, ok in results if not ok]
print('\nKẾT QUẢ:', 'TẤT CẢ KHỚP' if not bad else 'KHÁC: ' + ', '.join(bad))
sys.exit(1 if bad else 0)
