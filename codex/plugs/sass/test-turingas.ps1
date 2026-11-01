[CmdletBinding()]
param(
    [string]$TuringasDir = '',
    [string]$Python = 'D:/AI/DiffusionForge/system/python/python.exe'
)

# Grades the sass plug's SM 8.9 listing of test/first-slice.codex with
# turingas (f7c1a74): every instruction line, control notation included,
# through turingas's own ProcessAsmLine and GenCode, whose two words must
# equal the plug's. It also requires every kernel of the slice to be listed;
# gpu-divide, gpu-divide-var, gpu-loop, gpu-narrow, gpu-shared, gpu-float, gpu-async, gpu-warp and the two gpu-refuse- kernels are refused only by
# SM 5.2. What a
# kernel computes is not graded here. It also lists codex/foreword/gpu/
# GemmKernels.codex, where kernel-gemm-wmma and the four kernels that divide by a variable must list and every listed line is
# graded the same way. The plug is rebuilt from source first.
#
#   pwsh codex/plugs/sass/test-turingas.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = 'f7c1a74ebe6bc2ac51920b0fee29e27767f81724'
if (-not (Test-Path -PathType Leaf $Python)) { Write-Host "MISSING: $Python"; exit 2 }
if (-not $TuringasDir) {
    $ws = Split-Path -Leaf (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    $TuringasDir = Join-Path $env:TEMP "sass-sm89-oracle-$ws/turingas"
}
if (-not (Test-Path $TuringasDir)) {
    $staging = "$TuringasDir.partial-$PID"
    if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    & git clone --quiet https://github.com/daadaada/turingas.git $staging
    if ($LASTEXITCODE -ne 0) { Write-Host 'turingas clone failed'; exit 2 }
    & git -C $staging checkout --quiet $commit
    if ($LASTEXITCODE -ne 0) { Write-Host "turingas checkout of $commit failed"; exit 2 }
    Move-Item -LiteralPath $staging -Destination $TuringasDir
}
$head = @(& git -C $TuringasDir rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1) { Write-Host "turingas oracle at $TuringasDir is not a git clone (an interrupted clone?); delete it and rerun"; exit 2 }
if ($head[0].Trim() -ne $commit) { Write-Host 'turingas revision differs from the pinned oracle'; exit 2 }
$dirty = @(& git -C $TuringasDir status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { Write-Host 'turingas oracle has local changes'; exit 2 }

$out = Join-Path $PSScriptRoot 'build-output'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: plug build'; exit 1 }
$listing = Join-Path $out 'first-slice-89.sass'
if (Test-Path $listing) { [IO.File]::Delete($listing) }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src (Join-Path $PSScriptRoot 'test\first-slice.codex') -Out $listing -Sm 89 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $listing)) { Write-Host 'FAIL: plug run'; exit 1 }

$text = [IO.File]::ReadAllText($listing)
$failed = 0
foreach ($k in 'gpu-scale-add', 'gpu-global-index', 'gpu-nested', 'gpu-divide', 'gpu-divide-var', 'gpu-loop', 'gpu-narrow', 'gpu-shared', 'gpu-wmma', 'gpu-wmma-shared', 'gpu-float', 'gpu-async', 'gpu-warp', 'gpu-refuse-recursion', 'gpu-refuse-multiply') {
    if (-not $text.Contains(".kernel $k`n")) { Write-Host "FAIL: kernel $k not listed"; $failed++ }
}
if ($text.Contains('REFUSED')) { Write-Host 'FAIL: a kernel was refused'; $failed++ }
if ($text.Contains('REFUSED-INSN')) { Write-Host 'FAIL: the encoder refused a selected instruction'; $failed++ }

# The GEMM the diffusion engine uses: kernel-gemm-wmma must list, every line graded.
$gemm = Join-Path $out 'gemm-89.sass'
if (Test-Path $gemm) { [IO.File]::Delete($gemm) }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src (Join-Path $PSScriptRoot '..\..\foreword\gpu\GemmKernels.codex') -Out $gemm -Sm 89 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $gemm)) { Write-Host 'FAIL: plug run on GemmKernels'; exit 1 }
$gtext = [IO.File]::ReadAllText($gemm)
foreach ($k in 'kernel-gemm-wmma', 'kernel-conv2d-wmma', 'kernel-conv2d-fast', 'kernel-conv-weight-khwc', 'kernel-attention-fused-128') {
    if (-not $gtext.Contains(".kernel $k`n")) { Write-Host "FAIL: $k not listed"; $failed++ }
}
if ($gtext.Contains('REFUSED-INSN')) { Write-Host 'FAIL: the encoder refused a selected GEMM instruction'; $failed++ }

$grade = Join-Path $env:TEMP ('sass89-plug-grade-' + $PID + '.py')
try {
    [IO.File]::WriteAllText($grade, @'
import re
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, sys.argv[1])
from turingas.grammar import ProcessAsmLine, GenCode, grammar

insts = bad = naks = 0
index = 0
for n, raw in enumerate(open(sys.argv[2], encoding="utf-8").read().split("\n")):
    if raw.startswith(".kernel "):
        index = 0
    m = re.fullmatch(r"([IN]) (\S+) ([0-9a-f]{16}) ([0-9a-f]{16}) (.*)", raw)
    if not m:
        continue
    kind, ctrl, lo, hi, text = m.groups()
    # Every branch the plug emits is a loop's back edge: it must go backward to
    # an instruction of its own kernel.
    b = re.search(r"\bBRA (-?)0x([0-9a-f]+);", text)
    if b:
        off = int(b.group(2), 16) * (-1 if b.group(1) else 1)
        target = index + 1 + off // 16
        if off >= 0 or off % 16 or target < 0:
            print(f"BRANCH to {target} from {index}: {text}")
            bad += 1
    index += 1
    if kind == "N":
        # turingas has no WARPSYNC. NAK (Mesa 26.2.4 sm70_encode.rs, OpWarpSync and
        # encode_alu_base): opcode 0x148 in form 4, the mask at 32..63, PT at 87..89.
        # The guard and control bits are turingas's own, taken from a NOP with them.
        w = re.fullmatch(r"(@!?P\d )?WARPSYNC 0x([0-9a-fA-F]+);", text)
        # turingas and NAK have no BPT. ptxas (CUDA 13.2, `trap;` at sm_89, read
        # with nvdisasm -hex) writes BPT.TRAP 0x1 as 0x000000040000795c
        # 0x000fea0000300000: opcode 0x95C, the code at 34, the mode 3 at 84..85.
        t = re.fullmatch(r"(@!?P\d )?BPT\.TRAP 0x1;", text)
        if w is None and t is None:
            raise RuntimeError(f"not a NAK-graded form: {raw}")
        nop = ProcessAsmLine(f"{ctrl}  {(w or t).group(1) or ''}NOP;", n)
        g = grammar['NOP'][0]
        code = GenCode('NOP', g, re.fullmatch(g['rule'], nop['op'] + nop['rest']).groupdict(), nop)
        if w is not None:
            want_lo = ((code >> 64) & ~0xFFF) | 0x948 | (int(w.group(2), 16) << 32)
            want_hi = (code & ((1 << 64) - 1)) | (7 << 23)
        else:
            want_lo = ((code >> 64) & ~0xFFF) | 0x95c | (1 << 34)
            want_hi = (code & ((1 << 64) - 1)) | (3 << 20)
            unguarded = (want_lo & ~(0xF << 12)) | (7 << 12)
            if unguarded != 0x000000040000795c or want_hi & ((1 << 41) - 1) != 0x0000300000:
                raise RuntimeError(f"BPT grade disagrees with the ptxas words: {unguarded:016x} {want_hi:016x}")
        naks += 1
        if f"{want_lo:016x} {want_hi:016x}" != f"{lo} {hi}":
            print(f"NAK WORD {want_lo:016x} {want_hi:016x} plug {lo} {hi}: {text}")
            bad += 1
        continue
    line = ProcessAsmLine(f"{ctrl}  {text}", n)
    if line is None:
        raise RuntimeError(f"turingas cannot parse: {raw}")
    rows = [(g, re.fullmatch(g['rule'], line['op'] + line['rest'])) for g in grammar[line['op']]]
    rows = [(g, r) for g, r in rows if r is not None]
    if len(rows) != 1:
        raise RuntimeError(f"no single turingas grammar row matches: {raw}")
    g, r = rows[0]
    code = GenCode(line['op'], g, r.groupdict(), line)
    insts += 1
    want = f"{code >> 64:016x} {code & ((1 << 64) - 1):016x}"
    if want != f"{lo} {hi}":
        print(f"WORD {want} plug {lo} {hi}: {text}")
        bad += 1
print(f"turingas: {insts} instructions, NAK fields: {naks}, {bad} mismatches")
sys.exit(1 if bad or insts == 0 else 0)
'@, [Text.UTF8Encoding]::new($false))
    & $Python $grade $TuringasDir $listing
    if ($LASTEXITCODE -ne 0) { $failed++ }
    & $Python $grade $TuringasDir $gemm
    if ($LASTEXITCODE -ne 0) { $failed++ }
} finally {
    Remove-Item -LiteralPath $grade -ErrorAction SilentlyContinue
}

if ($failed) { Write-Host "FAIL: $failed"; exit 1 }
Write-Host 'PASS'
