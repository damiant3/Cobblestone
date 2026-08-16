# jonquil-test.ps1 -- positive and negative control for build/jonquil.ps1.
#
# The jonquil runs in the standing gate and scans the compiler's own IR for a
# self-reproducing definition (docs/Test/Active/DDC-QUINE-ARM.md). The gate only
# ever exercises its NEGATIVE side: a clean tree, every build, passing. Nothing
# proves the scanner still FIRES when it should, so a change that silently breaks
# the scan would leave the gate green forever and let a real quine ship. This is
# that missing positive control, plus negative controls that pin the scanner's
# discrimination (L-CONTROL, L-FALSIF).
#
# Hermetic: it feeds jonquil hand-built IR logs and checks only the exit code and
# the named def. No compile, no seed, no VM. The live compile path (QuineToy.codex
# -> jonquil FAIL) is covered by the Reproduce section of DDC-QUINE-ARM.md.
#
# SCOPE, same as the jonquil itself: this tests the tractable single-def form. It
# does NOT assert the known misses (a header split across adjacent text-lits, or a
# mutual "A embeds B, B embeds A" pair) -- those are the undecidable tail the
# jonquil's own header disclaims, and asserting them would claim a completeness the
# tripwire does not have.

param(
    [string]$WorkDir = ''
)

$ErrorActionPreference = 'Stop'
$jonquil = Join-Path $PSScriptRoot 'jonquil.ps1'
if (-not (Test-Path -PathType Leaf $jonquil)) {
    Write-Host "jonquil-test: FAIL -- build/jonquil.ps1 not found"; exit 1
}
if (-not $WorkDir) { $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) 'jonquil-test' }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# POSITIVE: a self-reproducing def carries its OWN escaped header as text data.
# This is the exact def build/output QuineToy.codex compiles to (D=63, emitted=128).
$posLine = '  (def "quine-self" "QuineToy" (params) text (text-lit "(def \"quine-self\" \"QuineToy\" (params) text (text-lit @QQ@) 0 0)") 0 0)'
$pos = @"
IR-BEGIN
(chapter "Program"
  (defs
$posLine))
IR-END
"@

# NEGATIVE: honest defs the scanner must NOT flag.
#   square       -- an ordinary def, no header as data.
#   ir-emit-def  -- the honest emitter pattern: its text-lit is "(def " (space,
#                   then the closing quote), which is `(def "` not the escaped
#                   `(def \"ir-emit-def\"`. This is the real compiler code that
#                   could false-positive, so it is the load-bearing control.
#   foo          -- carries `(def \"bar\"`, ANOTHER name's header. The scanner
#                   keys on SELF-reference, so foo carrying bar's header is not a
#                   quine and must pass. This pins that it is not a blind grep for
#                   any `(def \"`.
$neg = @'
IR-BEGIN
(chapter "Program"
  (defs
  (def "square" "Test" (params (param "x" int-default)) (fn int-default int-default) (binary mul-int (name "x" int-default) (name "x" int-default) int-default) 0 0)
  (def "ir-emit-def" "Test" (params) text (text-lit "(def ") 0 0)
  (def "foo" "Test" (params) text (text-lit "(def \"bar\" is another name, not foo") 0 0)))
IR-END
'@

$posFile = Join-Path $WorkDir 'quine.ir.log'
$negFile = Join-Path $WorkDir 'clean.ir.log'
[System.IO.File]::WriteAllText($posFile, $pos, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($negFile, $neg, [System.Text.UTF8Encoding]::new($false))

$fail = 0

# --- positive control: jonquil MUST fire (exit 1) and name quine-self ---
$posOut = & pwsh -NoProfile -File $jonquil -IrFile $posFile 2>&1
$posExit = $LASTEXITCODE
if ($posExit -eq 1 -and ($posOut -join "`n").Contains('quine-self')) {
    Write-Host "  positive control: PASS (jonquil fired on the quine, named quine-self)"
} else {
    Write-Host "  positive control: FAIL -- expected exit 1 naming quine-self, got exit $posExit"
    Write-Host ($posOut -join "`n")
    $fail = 1
}

# --- negative control: jonquil MUST pass (exit 0) on honest defs ---
$negOut = & pwsh -NoProfile -File $jonquil -IrFile $negFile 2>&1
$negExit = $LASTEXITCODE
if ($negExit -eq 0) {
    Write-Host "  negative control: PASS (honest defs, incl. the emitter pattern and a foreign header, did not fire)"
} else {
    Write-Host "  negative control: FAIL -- expected exit 0, got exit $negExit (false positive)"
    Write-Host ($negOut -join "`n")
    $fail = 1
}

if ($fail -eq 0) {
    Write-Host "jonquil-test: OK (positive fires, negative clean)"
    exit 0
}
Write-Host "jonquil-test: FAIL"
exit 1
