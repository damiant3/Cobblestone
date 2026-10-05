[CmdletBinding()]
param(
    [string]$TuringasDir = (Join-Path $env:TEMP 'sass-sm89-oracle/turingas'),
    [string]$Python = 'D:/AI/DiffusionForge/system/python/python.exe'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'f7c1a74ebe6bc2ac51920b0fee29e27767f81724'
if (!(Test-Path -PathType Leaf $Python)) { throw 'Oracle Python is unavailable' }
if (!(Test-Path $TuringasDir)) {
    & git clone --quiet https://github.com/daadaada/turingas.git $TuringasDir
    if ($LASTEXITCODE -ne 0) { throw 'turingas clone failed' }
    & git -C $TuringasDir checkout --quiet $commit
    if ($LASTEXITCODE -ne 0) { throw 'turingas checkout failed' }
}
$head = (& git -C $TuringasDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $commit) { throw 'turingas revision differs from pinned oracle' }
$dirty = @(& git -C $TuringasDir status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'turingas oracle has local changes' }
$scriptPath = Join-Path $env:TEMP ('sass89-hmma-oracle-' + $PID + '.py')
try {
    [IO.File]::WriteAllText($scriptPath, @'
import re
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, sys.argv[1])
from turingas.grammar import ProcessAsmLine, GenCode, grammar

def bar(n):
    return "-" if n % 7 == 0 else str(n % 7 - 1)

def reg(n):
    return "RZ" if n == 255 else f"R{n}"

for j in range(128):
    p = j % 8
    pred = "" if p == 7 else "@" + ("!" if (j // 8) % 2 else "") + f"P{p} "
    reuse = j % 8
    a = reg((j * 31 + 2) % 256) + (".reuse" if reuse & 1 else "")
    b = reg((j * 43 + 4) % 256) + (".reuse" if reuse & 2 else "")
    c = reg(255 if j % 7 == 0 else (j * 61 + 6) % 256) + (".reuse" if reuse & 4 else "")
    shape = "1688" if j % 2 == 0 else "16816"
    accum = "F16" if (j // 2) % 2 == 0 else "F32"
    # Wait masks are decimal in ReadCtrl; stalls are hexadecimal.
    ctrl = f"{j:02d}:{bar(j * 3)}:{bar(j)}:{'-' if j % 2 else 'Y'}:{j % 16:x}"
    text = f"{ctrl}  {pred}HMMA.{shape}.{accum} {reg((j * 17) % 256)}, {a}, {b}, {c};"
    if j >= 64:
        p = j - 88 if 88 <= j <= 95 else j - 96 if 96 <= j <= 102 else 7
        neg = 96 <= j <= 102
        pred = "" if p == 7 else "@" + ("!" if neg else "") + f"P{p} "
        reuse = j - 80 if 80 <= j <= 87 else 0
        a = reg(255 if j == 125 else 4) + (".reuse" if reuse & 1 else "")
        b = reg(255 if j == 126 else 8) + (".reuse" if reuse & 2 else "")
        c = reg(255 if j == 127 else 12) + (".reuse" if reuse & 4 else "")
        wr = str(j - 103) if 103 <= j <= 108 else "-"
        rd = str(j - 109) if 109 <= j <= 114 else "-"
        wait = 1 << (j - 115) if 115 <= j <= 120 else 0
        stall = j - 64 if 64 <= j <= 79 else 0
        ctrl = f"{wait:02d}:{rd}:{wr}:{'-' if j == 121 else 'Y'}:{stall:x}"
        shape = "1688" if j == 122 else "16816"
        accum = "F16" if j == 123 else "F32"
        text = f"{ctrl}  {pred}HMMA.{shape}.{accum} {reg(255 if j == 124 else 0)}, {a}, {b}, {c};"
    line = ProcessAsmLine(text, j)
    if line is None:
        raise RuntimeError(f"Unparsed vector {j}: {text}")
    matches = [(g, re.fullmatch(g['rule'], line['op'] + line['rest'])) for g in grammar[line['op']]]
    matches = [(g, m) for g, m in matches if m is not None]
    if len(matches) != 1:
        raise RuntimeError(f"Ambiguous or unmatched vector {j}: {text}")
    g, match = matches[0]
    code = GenCode(line['op'], g, match.groupdict(), line)
    if not 0 <= code < 1 << 128:
        raise RuntimeError(f"Non-128-bit result for vector {j}")
    # Match upstream cubin.GenerateText's word order without producing a cubin.
    print(f"{j} {code >> 64:016x} {code & ((1 << 64) - 1):016x}")
'@, [Text.UTF8Encoding]::new($false))
    $lines = @(& $Python $scriptPath $TuringasDir)
    if ($LASTEXITCODE -ne 0) { throw 'turingas oracle execution failed' }
    if ($lines.Count -ne 128) { throw 'turingas oracle returned an incomplete vector set' }
    for ($i = 0; $i -lt 128; $i++) {
        if ($lines[$i] -cnotmatch ('^' + $i + ' [0-9a-f]{16} [0-9a-f]{16}$')) { throw 'Malformed oracle output' }
    }
    $lines
} finally {
    Remove-Item -LiteralPath $scriptPath -ErrorAction SilentlyContinue
}
