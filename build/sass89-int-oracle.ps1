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
$scriptPath = Join-Path $env:TEMP ('sass89-int-oracle-' + $PID + '.py')
try {
    [IO.File]::WriteAllText($scriptPath, @'
import re
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, sys.argv[1])
from turingas.grammar import ProcessAsmLine, GenCode, grammar

CASES = [
    "MOV R1, R2;",
    "MOV R3, 0x12345678;",
    "MOV R4, c[0x0][0x160];",
    "@P1 MOV R5, R6.reuse;",
    "MOV R7, -0x1;",
    "IADD3 R0, R2, R4, RZ;",
    "IADD3 R0, P0, R2, R4, RZ;",
    "IADD3.X R1, R3, R5, RZ, P0, !PT;",
    "IADD3 R0, -R2, R4, R6;",
    "IADD3 R0, R2, -R4, RZ;",
    "IADD3 R8, R9, 0x100, R10;",
    "IADD3 R8, R9, c[0x0][0x168], RZ;",
    "IADD3 R0, P1, P2, R2.reuse, R4.reuse, R6.reuse;",
    "IADD3.X R1, R3, -R5, RZ, !P0, P3;",
    "@!P2 IADD3.X R11, P4, R12, R13, R14, P5, !P6;",
    "ISETP.LT.S32.AND P0, PT, R2, R4, PT;",
    "ISETP.EQ.U32.OR P1, PT, R3, 0x7, !P2;",
    "ISETP.LE.S32.XOR P2, PT, R5, c[0x0][0x0], P3;",
    "ISETP.GT.U32.AND P3, PT, R6.reuse, R7.reuse, PT;",
    "ISETP.NE.S32.AND P4, PT, R8, RZ, PT;",
    "ISETP.GE.U32.AND P6, PT, R9, 0xffffffff, PT;",
    "SHF.L.U32 R0, R2, 0x3, RZ;",
    "SHF.L.S64.HI R1, R2, 0x3, R3;",
    "SHF.R.S32.HI R4, RZ, 0x1f, R5;",
    "SHF.R.U32 R6, R7, R8, R9;",
    "SHF.R.U32.HI R10, R11.reuse, R12.reuse, R13.reuse;",
    "S2R R0, SR_TID.X;",
    "S2R R1, SR_CTAID.X;",
    "S2R R2, SR_LANEID;",
    "@P3 S2R R3, SR_TID.Z;",
    "LDC R0, c[0x0][0x0];",
    "LDC.64 R2, c[0x0][0x160];",
    "LDC.32 R4, c[0x3][0xfffc];",
    "LDG.E.64 R2, [R4.64+URZ];",
    "LDG.E R6, [R8.64+URZ+0x10];",
    "LDG.E.64.CONSTANT.CTA R2, [R4.64+URZ+-0x8];",
    "LDG.E.64.CTA R2, [R4.64+URZ+0x7ffff8];",
    "LDG.E.64.STRONG.GPU R2, [R4.64+URZ];",
    "LDG.E.64 R10, [R12.64+URZ+-0x800000];",
    "LDG.E.64.SYS R10, [R12.64+URZ];",
    "@!P1 LDG.E R0, [R2.64+URZ+0x4];",
    "STG.E.64 [R4+0x10], R2;",
    "STG.E [R6], R8;",
    "STG.E.64.CTA.CONSTANT [R4+-0x8], R2;",
    "STG.E.64.GPU.STRONG [R4], R2;",
    "STG.E.CTA [R4+0x7ffffc], R9;",
    "STG.E.64.SYS [R10], R12;",
    "@P6 STG.E [R2], R3;",
    "EXIT;",
    "@P0 EXIT;",
    "@!P5 EXIT;",
    "IMAD R0, R2, R4, R6;",
    "IMAD.U32 R1, R3, R5, RZ;",
    "IMAD.S32 R8, R9, 0x10, R10;",
    "IMAD.U32 R8, R9, c[0x0][0x160], RZ;",
    "@P2 IMAD R3, R4.reuse, R7.reuse, R3.reuse;",
    "IMAD.WIDE.U32 R2, R4, R6, RZ;",
    "IMAD.WIDE R10, R12, R14, R16;",
    "IMAD.WIDE.U32 R2, R4, 0x8, R6;",
    "IMAD.WIDE.S32 R2, R4, c[0x0][0x168], RZ;",
    "@!P0 IMAD.WIDE.U32 R20, R21, R22, R24;",
    "BRA 0x10;",
    "@P0 BRA -0x80;",
    "@!P3 BRA 0x7ffffff0;",
    "BRA -0x80000000;",
    "BRA 0x0;",
    "@P6 BRA -0x10;",
    "LDG.E.U16 R3, [R4.64+URZ+0x2];",
    "STG.E.U16 [R4+-0x2], R7;",
    "@P1 LDG.E.U16.CTA R9, [R10.64+URZ];",
    "LDS R3, [R4+0x10];",
    "LDS.64 R6, [R8];",
    "@P2 LDS R9, [R10+-0x4];",
    "STS [R4+URZ+0x10], R7;",
    "STS.U16 [R4+URZ], R7;",
    "STS.64 [R6+URZ+-0x8], R8;",
    "BAR.SYNC 0x0;",
    "@P1 BAR.SYNC 0x0;",
    "PRMT R0, R2, 0x5410, R3;",
    "PRMT R4, R5, R6, R7;",
    "@P1 PRMT R8, R9, 0x7632, RZ;",
    "LOP3.LUT R0, R2, R3, RZ, 0xc0, !PT;",
    "LOP3.LUT R1, R4, 0x3, RZ, 0xc0, !PT;",
    "LOP3.LUT R5, R6, c[0x0][0x10], R7, 0xfe, !PT;",
    "@!P2 LOP3.LUT R8, R9.reuse, R10.reuse, R11.reuse, 0x96, !PT;",
    "LDS.U16 R3, [R4+0x2];",
]

def bar(n):
    return "-" if n % 7 == 0 else str(n % 7 - 1)

for j, body in enumerate(CASES):
    # Wait masks are decimal in ReadCtrl; stalls are hexadecimal.
    ctrl = f"{(j * 5) % 64:02d}:{bar(j * 3)}:{bar(j)}:{'-' if j % 2 else 'Y'}:{j % 16:x}"
    text = f"{ctrl}  {body}"
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
    if ($lines.Count -ne 86) { throw 'turingas oracle returned an incomplete vector set' }
    for ($i = 0; $i -lt 86; $i++) {
        if ($lines[$i] -cnotmatch ('^' + $i + ' [0-9a-f]{16} [0-9a-f]{16}$')) { throw 'Malformed oracle output' }
    }
    $lines
} finally {
    Remove-Item -LiteralPath $scriptPath -ErrorAction SilentlyContinue
}
