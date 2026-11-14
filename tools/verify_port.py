#!/usr/bin/env python3
"""So sánh máy móc chuỗi lệnh device-side giữa Inapp-Patch (PicsartToolGUI.cs) và ToolADB.

Cách dùng:  python3 tools/verify_port.py [đường_dẫn_repo_Inapp-Patch]
(mặc định ../Inapp-Patch). Thoát mã 1 nếu có bất kỳ chuỗi nào khác nhau.

Mã gốc truyền lệnh qua ProcessStartInfo.Arguments (chuỗi command line, shell "...")
nên phải mô phỏng: C#-unescape → string.Format → CommandLineToArgvW (\" → ").
ToolADB truyền qua ArgumentList nên chỉ cần C#-unescape.
"""
import re, sys, os

ID = "0123456789abcdef0123456789abcdef"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INAPP = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(ROOT), 'Inapp-Patch')
ORIG_FILE = os.path.join(INAPP, 'PicsartToolGUI.cs')

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
        out.append(c); i += 1
    return ''.join(out)

TOKEN = re.compile(r'"((?:[^"\\]|\\.)*)"|([A-Za-z_][A-Za-z_0-9]*)')

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
    print(f'{name:28s}: {"IDENTICAL" if a == b else "DIFFERENT"}  ({len(a)} chars)')
    if a != b:
        i = next((i for i in range(min(len(a), len(b))) if a[i] != b[i]), min(len(a), len(b)))
        print('   orig:', repr(a[max(0, i-60):i+80]))
        print('   port:', repr(b[max(0, i-60):i+80]))

orig = read(ORIG_FILE)

# ---------------- Picsart ----------------
ot = {k: assemble(block(orig, rf'const string {k}\s*='), {}) for k in
      ('FixPermissionsSnippet', 'PatchMonthlySnippet', 'PatchUltraSnippet', 'OpenPaywallDeepLinkCmd')}
ot['GetCleanSubscriptionCacheSnippet'] = fmt(assemble(block(orig, r'return string\.Format\(', r',\s*\n\s*newDeviceId\);'), {}))
def orig_mode(var):
    cmd = win(fmt(assemble(block(orig, rf'string {var}\s*=\s*', r';\s*\n'), ot)))
    assert cmd.startswith('shell "') and cmd.endswith('"'), cmd[:40]
    return cmd[len('shell "'):-1]
taint = win(assemble(block(orig, r'string check = RunAdb\(string\.Format\(', r',\s*serial\)'), {}))
taint = re.sub(r'^-s \{0\} shell "', '', taint).rstrip('"')

port = read(os.path.join(ROOT, 'ToolADB/Features/Picsart/PicsartService.cs')).replace('" + newDeviceId + "', ID)
pt = {k: assemble(block(port, rf'const string {k}\s*='), {}) for k in
      ('FixPermissionsSnippet', 'PatchMonthlySnippet', 'PatchUltraSnippet', 'OpenPaywallDeepLinkCmd')}
pt['GetCleanSubscriptionCacheSnippet'] = assemble(block(port, r'GetCleanSubscriptionCacheSnippet\(string newDeviceId\) =>', r';\s*\n'), {})
def port_case(name):
    return assemble(block(port, rf'case PicsartAction\.{name}:\s*\n\s*CaptureAdb\(serial, \d+, "shell",', r'\);\s*\n'), pt)

compare('Picsart deeplink', win(ot['OpenPaywallDeepLinkCmd']), pt['OpenPaywallDeepLinkCmd'])
compare('Picsart ultra', orig_mode('ultraCmd'), port_case('UltraMonthly'))
compare('Picsart monthly', orig_mode('patchCmd'), port_case('ProMonthly'))
compare('Picsart full (Reset)', orig_mode('fullCmd'), port_case('Reset'))
compare('Picsart keep (ResetKeepLogin)', orig_mode('keepCmd'), port_case('ResetKeepLogin'))
compare('Picsart taint check', taint, assemble(block(port, r'string check = CaptureAdb\(serial, 5000, "shell",', r'\);\s*\n'), {}))

# ---------------- Chess ----------------
DB = '/data/data/com.android.vending/databases/sku_details.db'
oc = block(orig, r'private void PatchChessNoTrial\(string serial\)', r'private void CheckAndWarnIfAccountTainted')
pc = read(os.path.join(ROOT, 'ToolADB/Features/InappTrial/InappTrialService.Chess.cs'))
def sql_of(src, const):
    body = re.sub(r'"\s*\+\s*' + const + r'\s*\+\s*"', '4102444800000', block(src, r'string sql =\s*', r';\s*\n\s*File\.WriteAllText'))
    return assemble(body, {})
compare('Chess SQL', sql_of(oc, 'farFutureExpiration'), sql_of(pc, 'farFutureExpiration'))
oshell = [re.sub(r'\{2\}', 'REMOTE', win(s).replace('{1}', DB)) for s in re.findall(r'shell \\"(.*?)\\"', oc)]
pshell = []
for m in re.finditer(r'CaptureAdb\(serial,\s*\d+,\s*"shell",\s*(.*?)\);\n', pc, re.S):
    pshell.append(assemble(m.group(1).replace('playDb', f'"{DB}"').replace('remoteSql', '"REMOTE"'), {}))
compare('Chess shell commands', '\n'.join(oshell), '\n'.join(pshell))

# ---------------- Grammarly ----------------
og = block(orig, r'private void PatchGrammarlyNoTrial\(string serial\)', r'private void PatchChessNoTrial')
pg = read(os.path.join(ROOT, 'ToolADB/Features/InappTrial/InappTrialService.Grammarly.cs'))
oshell = [win(s) for s in re.findall(r'shell \\"(.*?)\\""', og)]
pshell = [assemble(m.group(1), {}) for m in re.finditer(r'CaptureAdb\(serial,\s*\d+,\s*"shell",\s*(.*?)\);\n', pg, re.S)]
compare('Grammarly shell commands', '\n'.join(oshell), '\n'.join(pshell))
for h in ('1A660A12616E64726F69645F667265655F747269616C', '1A690A12616E64726F69645F667265655F747269616C',
          '2A06746573745F31', '2A09636F6E74726F6C5F31'):
    compare('Grammarly hex ' + h[:8], h if h in og else '', h if h in pg else '')

bad = [n for n, ok in results if not ok]
print('\nKẾT QUẢ:', 'TẤT CẢ KHỚP' if not bad else 'KHÁC: ' + ', '.join(bad))
sys.exit(1 if bad else 0)
