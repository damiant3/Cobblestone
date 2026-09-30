# build-snapshot.ps1 -- regenerate apps/erp/ErpSnapshot.codex from a native run.
#
# The dashboard page (ErpPage.codex) cannot run the scenario in the browser:
# the GL is backed by the Data quire, whose pages need alloc-bytes, which the
# HTML runtime does not have. So ErpSnapshotMain.codex runs ErpScenario's
# run-month natively and prints its numbers, and this script writes them into a
# data-only chapter the page cites, naming the compiler and depot change that
# produced them. codex/test/apps/erp-snapshot-fresh fails when the scenario and
# the snapshot disagree; rerun this script then.
#
# Usage: pwsh apps/erp/build-snapshot.ps1 [-Kernel <compiler cdx>]

param([string]$Kernel = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Set-Location $Repo
if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
$Out  = Join-Path $Repo 'test-output\erp-snapshot'
New-Item -ItemType Directory -Force $Out | Out-Null
$cdx  = Join-Path $Out 'erp-snapshot.cdx'
$run  = Join-Path $Out 'erp-snapshot.out'
foreach ($f in @($cdx, $run)) { if (Test-Path $f) { Remove-Item $f } }

& (Join-Path $Repo 'build\compile.ps1') -Src (Join-Path $PSScriptRoot 'ErpSnapshotMain.codex') -Out $cdx -Log (Join-Path $Out 'compile.log') -Kernel $Kernel | Out-Null
if (-not (Test-Path $cdx)) { Write-Host "build-snapshot: FAIL -- ErpSnapshotMain did not compile; see $Out\compile.log"; exit 1 }
& pwsh -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') -Kernel $cdx -OutFile $run | Out-Null
if (-not (Test-Path $run)) { Write-Host 'build-snapshot: FAIL -- the run wrote nothing'; exit 1 }

$kv = @{}; $tb = @()
foreach ($line in Get-Content $run) {
    if ($line -match '^tb\|([^|]*)\|([^|]*)\|(-?\d+)\|(-?\d+)$') { $tb += , @($Matches[1], $Matches[2], $Matches[3], $Matches[4]) }
    elseif ($line -match '^([a-z-]+)=(-?\d+)$') { $kv[$Matches[1]] = $Matches[2] }
}
$keys = 'revenue', 'expenses', 'net', 'cash', 'assets', 'liabilities', 'equity', 'ap-open', 'ar-open'
$missing = @($keys + 'end' | Where-Object { -not $kv.ContainsKey($_) })
if ($missing.Count -gt 0 -or $tb.Count -eq 0) { Write-Host "build-snapshot: FAIL -- the run is incomplete (missing: $($missing -join ', '); $($tb.Count) trial-balance lines)"; exit 1 }
foreach ($r in $tb) { if ($r[0] -match '"' -or $r[1] -match '"') { Write-Host "build-snapshot: FAIL -- a quote in account $($r[0])"; exit 1 } }

$seed = (Get-FileHash $Kernel -Algorithm SHA256).Hash.Substring(0, 16)
$change = ((p4 -ztag -F '%change%' changes -m 1 '...#have' 2>$null) | Select-Object -First 1)
if (-not $change) { $change = 'unknown' }
$build = "compiler $seed, depot change $change"

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('Chapter: ErpSnapshot')
$lines.Add('')
$lines.Add(' Written by apps/erp/build-snapshot.ps1 from a native run of ErpScenario')
$lines.Add(' run-month. Regenerate it; do not edit the numbers by hand.')
$lines.Add('')
$lines.Add('Section: Snapshot')
$lines.Add('')
$lines.Add('  ErpSnapLine = record { esl-account : Text, esl-name : Text, esl-debit : Integer, esl-credit : Integer }')
$lines.Add('')
$lines.Add("  erp-snap-build : Text = `"$build`"")
foreach ($k in $keys) {
    $lines.Add('')
    $lines.Add("  erp-snap-$k : Integer = $($kv[$k])")
}
$lines.Add('')
$lines.Add('  erp-snap-tb : List ErpSnapLine = [')
for ($i = 0; $i -lt $tb.Count; $i++) {
    $r = $tb[$i]
    $sep = if ($i -lt $tb.Count - 1) { ',' } else { '' }
    $lines.Add("    ErpSnapLine { esl-account = `"$($r[0])`", esl-name = `"$($r[1])`", esl-debit = $($r[2]), esl-credit = $($r[3]) }$sep")
}
$lines.Add('  ]')
$lines.Add('')
$target = Join-Path $PSScriptRoot 'ErpSnapshot.codex'
[System.IO.File]::WriteAllText($target, ($lines -join "`r`n"))
Write-Host "build-snapshot: wrote $target ($($tb.Count) trial-balance lines; $build)"
exit 0
