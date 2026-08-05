# Report drift between each codex/build/*Script.codex generator and the
# script it claims to produce.
#
# Hand-written, deliberately, and it is the one script under build/ that
# must stay that way: it has to run when the generators are broken, which
# is exactly when a generated checker would be untrustworthy.
#
# There is NO -Write flag and that omission is load-bearing. Measured
# 2026-08-03, 39 of the 40 generators with a live target had drifted and
# in every case the SHIPPED script was the maintained side and the
# generator was the abandoned one. A bulk regenerate would therefore
# destroy the working scripts, including build.ps1 and test.ps1. Use
# -Diff to read a drift and port it back into the generator by hand.
#
# Usage:
#   check-generated-scripts.ps1                 # table for every generator
#   check-generated-scripts.ps1 -Only test      # one, by emitted name
#   check-generated-scripts.ps1 -Diff test      # show the actual drift
#
# Exit 1 if any generator has drifted, 0 if all match.
[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$Diff = '',
    [string]$OutRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

# The three generators whose target does not live under build/.
$AltTarget = @{
    'compile-arm64'  = 'codex\plugs\arm64\compile-arm64.ps1'
    'compile-riscv'  = 'codex\plugs\riscv\compile-riscv.ps1'
    'plug-build-lib' = 'codex\plugs\common\plug-build-lib.ps1'
}

$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) {
    Write-Host "MISSING: $Stage0"
    Write-Host "Run build/build.ps1, or stage the depot seed: Copy-Item seed\Codex.cdx $Stage0"
    exit 2
}
$digest = (Get-FileHash $Stage0 -Algorithm SHA256).Hash.Substring(0, 16)
Write-Host "compiler: build-output\bare-metal\Codex.cdx [$digest]"

if (-not $OutRoot) { $OutRoot = Join-Path ([System.IO.Path]::GetTempPath()) "genscripts-$PID" }
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null

# What each generator claims to emit, and where that lands.
$specs = @()
foreach ($g in (Get-ChildItem (Join-Path $Repo 'codex\build') -Filter '*Script.codex' -File)) {
    $text = Get-Content $g.FullName -Raw
    $m = [regex]::Match($text, 'sh-script\s+"([^"]+)"')
    if (-not $m.Success) { continue }
    $name = $m.Groups[1].Value
    $ext = if ($text -match 'emit-bash') { 'sh' } else { 'ps1' }
    $target = if ($ext -eq 'ps1' -and $AltTarget.ContainsKey($name)) { $AltTarget[$name] } else { "build\$name.$ext" }
    $specs += [pscustomobject]@{
        Generator = $g
        Emits     = $name
        Target    = $target
        Present   = (Test-Path -PathType Leaf (Join-Path $Repo $target))
    }
}

$wanted = if ($Diff) { $Diff } else { $Only }
if ($wanted) {
    $specs = @($specs | Where-Object { $_.Emits -eq $wanted })
    if ($specs.Count -eq 0) { Write-Host "no generator emits '$wanted'"; exit 2 }
}

# One VM boot compiles the whole set.
$live = @($specs | Where-Object Present)
if ($live.Count -eq 0) { Write-Host "nothing to check"; exit 0 }

$listFile = Join-Path $OutRoot 'generators.txt'
$live.Generator.FullName | Set-Content -Path $listFile -Encoding UTF8
& (Join-Path $PSScriptRoot 'test-compile-batch.ps1') -ListFile $listFile -OutRoot $OutRoot *> $null

$rows = @()
foreach ($s in $live) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($s.Generator.Name)
    $dir = Join-Path $OutRoot $stem
    $code = Join-Path $dir '.exitcode'
    if (-not (Test-Path -PathType Leaf $code) -or (Get-Content $code -Raw).Trim() -ne '0') {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = 'COMPILE FAILED'; Lines = 0; Drift = 0 }
        continue
    }
    $cdx = Get-ChildItem $dir -Filter '*.cdx' | Select-Object -First 1
    $emitted = Join-Path $dir 'emitted.txt'
    & (Join-Path $PSScriptRoot 'test-run.ps1') -Kernel $cdx.FullName -OutFile $emitted *> $null
    if (-not (Test-Path -PathType Leaf $emitted) -or (Get-Item $emitted).Length -eq 0) {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = 'EMITTED NOTHING'; Lines = 0; Drift = 0 }
        continue
    }

    # An emitter answers `# <unknown-cmd>` for a node it does not handle,
    # rather than failing, so a node added to ShellTypes and forgotten in
    # BashEmit or KshEmit produces a script that is silently wrong. Measured
    # 2026-08-03: all 45 nodes added that day are unhandled in both, and
    # nothing would have said so. This is the runner for that.
    $madeText = Get-Content $emitted -Raw
    $stubs = @([regex]::Matches($madeText, '<unknown-(?:cmd|expr)>')).Count
    if ($stubs -gt 0) {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "UNHANDLED NODES ($stubs)"; Lines = 0; Drift = $stubs }
        continue
    }

    $shipped = @(Get-Content (Join-Path $Repo $s.Target))
    $made = @(Get-Content $emitted)
    # Whitespace-only difference is not drift worth porting: the emitter's
    # blank-line and indent conventions differ from the hand-maintained
    # files across the board and would drown every real finding.
    $a = @($shipped | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    $b = @($made | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    $delta = @(Compare-Object $a $b).Count

    if ($Diff) {
        Write-Host ""
        Write-Host "$($s.Target)  <=  codex\build\$($s.Generator.Name)"
        Write-Host "  '<=' is in the shipped script only, '=>' is what the generator emits."
        Write-Host ""
        Compare-Object $a $b | Format-Table -AutoSize SideIndicator, InputObject | Out-String -Width 200 | Write-Host
        exit ($(if ($delta -gt 0) { 1 } else { 0 }))
    }

    $rows += [pscustomobject]@{
        Emits  = $s.Emits
        Status = if ($delta -eq 0) { 'match' } else { 'DRIFTED' }
        Lines  = $shipped.Count
        Drift  = $delta
    }
}

$rows | Sort-Object Drift -Descending | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$dead = @($specs | Where-Object { -not $_.Present })
if ($dead.Count -gt 0) {
    Write-Host "generators whose target does not exist ($($dead.Count)):"
    foreach ($d in $dead) { Write-Host "  $($d.Generator.Name) -> $($d.Target)" }
}

$drifted = @($rows | Where-Object Status -ne 'match').Count
Write-Host ""
Write-Host "Checked $($rows.Count) generators, $drifted drifted, $($dead.Count) with no target."
if ($drifted -gt 0) { exit 1 } else { exit 0 }
