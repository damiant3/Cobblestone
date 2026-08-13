# The T3ISA gate.
#
# Compile one Codex program two ways and require the same stdout: through the
# existing x86-64 path under codex-vm, and through this plug to balanced
# ternary words run on the external emulator. The claim under test is README
# claim 7, that a new target is a plug and not a compiler change, and the
# point of choosing THIS target is that it shares almost none of the
# assumptions every other plug was built under.
#
# Requires the external toolchain, which is on this machine only. Nothing in
# build/build.ps1 reaches this script.
#
# Two oracles exist. target-v13 is built from t3isa-spec-v1.3, the version this
# plug is written against, and is the default. target\release is the v1.0 build
# the encoding was originally derived against; it is kept so a disagreement can
# be pinned on the emulator rather than on us, and is selected with -Manitc.
param(
  [string]$Src    = (Join-Path $PSScriptRoot 'test\gate.codex'),
  [string]$Manitc = 'D:\Toolchain-Ternary\target-v13\release\manitc.exe',
  [switch]$Sabotage
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$outDir = Join-Path $PSScriptRoot 'build-output'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (-not (Test-Path $Manitc)) { throw "the oracle is not built: $Manitc. See docs/Designs/Done/Compiler/T3IsaPlug.md." }

function Get-OracleOutput([string]$t3b) {
  $raw = & $Manitc run-t3 $t3b 2>&1 | Out-String
  # Drop the emulator's own banner; keep the program's output.
  ($raw -split "`r?`n" | Where-Object { $_ -notmatch '^\[T3ISA\] running' }) -join "`n"
}
function Normalize([string]$s) { (($s -replace "`r", '') -split "`n" | Where-Object { $_.Trim() -ne '' }) -join "`n" }

# --- arm 1: the existing x86-64 path ---
$nativeCdx = Join-Path $outDir 'gate.cdx'
$nativeOut = Join-Path $outDir 'gate.native.txt'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $nativeCdx -Log (Join-Path $outDir 'native.log') *> $null
if (-not (Test-Path $nativeCdx)) { throw "native compile produced nothing; see $outDir\native.log" }
& pwsh -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') -Kernel $nativeCdx -OutFile $nativeOut *> $null
$native = Normalize (Get-Content $nativeOut -Raw)

# --- arm 2: this plug, to balanced ternary ---
$t3b = Join-Path $outDir 'gate.t3b'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src $Src -Out $t3b *> $null
if ($LASTEXITCODE -ne 0) { throw "the plug refused or failed (exit $LASTEXITCODE); run run.ps1 directly for the reason" }
$ternary = Normalize (Get-OracleOutput $t3b)

Write-Output "--- x86-64 (codex-vm) ---"
Write-Output $native
Write-Output "--- T3ISA (balanced ternary, external emulator) ---"
Write-Output $ternary
Write-Output ""
$agree = $native -eq $ternary
if ($agree) { Write-Output "GATE PASS: both paths produced identical output" }
else { Write-Output "GATE FAIL: the two paths disagree" }

if (-not $Sabotage) { exit ([int](-not $agree)) }

# --- negative control ---
# A comparison that has never been shown to fail is not evidence. Corrupt one
# arithmetic instruction in the emitted assembly, reassemble, and require the
# outputs to diverge. The mutation is at the assembly level so it needs no
# plug rebuild, and it proves the comparison is actually reading the ternary
# machine's answers rather than reporting agreement by construction.
Write-Output ""
Write-Output "--- negative control: TADD -> TSUB in the emitted assembly ---"
. (Join-Path $PSScriptRoot 'spec\t3isa-assembler.ps1')
$asmFile = [System.IO.Path]::ChangeExtension($t3b, '.t3s')
$mutAsm = Join-Path $outDir 'gate.sabotage.t3s'
$mutBin = Join-Path $outDir 'gate.sabotage.t3b'
$text = Get-Content $asmFile -Raw
if ($text -notmatch 'TADD  R9, R9, R10') { Write-Output "  ARM IS STALE: no 'TADD  R9, R9, R10' in the emitted assembly"; exit 1 }
($text -replace 'TADD  R9, R9, R10', 'TSUB  R9, R9, R10') | Set-Content -LiteralPath $mutAsm -NoNewline
$a = Assemble $mutAsm
$bytes = New-Object byte[] ($a.words.Count * 8)
for ($i = 0; $i -lt $a.words.Count; $i++) { [BitConverter]::GetBytes([long]$a.words[$i]).CopyTo($bytes, $i * 8) }
[System.IO.File]::WriteAllBytes($mutBin, $bytes)
[System.IO.File]::WriteAllLines([System.IO.Path]::ChangeExtension($mutBin, '.t3d'), $a.sidecar, [System.Text.UTF8Encoding]::new($false))
$mutated = Normalize (Get-OracleOutput $mutBin)
Write-Output $mutated
$fired = $mutated -ne $native
Write-Output ""
if ($fired) { Write-Output "CONTROL FIRES: the sabotaged build disagrees with x86-64, so the comparison bites" }
else { Write-Output "CONTROL DID NOT FIRE: the comparison would pass a corrupted program" }
exit ([int]((-not $agree) -or (-not $fired)))
