# cost-corpus.ps1 -- run the Cost Model 3.3 kill-rate corpus and check it against its answer key.
#
# ON DEMAND, NOT A GATE. Damian's 2026-07-27 ruling is that harnesses are built
# and not put in the standard battery (docs/ExaminersAssay.md, "Build the
# instrument; do not gate it"), so codex\test\cost is deliberately absent from
# build/test.ps1's $allDirs and must stay absent. Without this script the corpus
# would be a recorded measurement that nothing ever re-runs, which is the same
# assertion-with-no-runner defect the corpus itself exists to avoid -- one level
# up, with the corpus as the assertion.
#
# What it checks is not "did it pass". It is the two properties the corpus is
# FOR: every positive still measures quadratic, every negative still measures
# linear, and the two populations still do not touch. A future change to the
# allocator can move every number in the table without breaking either, and the
# ratios are what say whether the threshold of 8 is still in the gap.
#
# Usage:
#   pwsh build/cost-corpus.ps1
#   pwsh build/cost-corpus.ps1 -Kernel build/output/Sut.cdx   # against a built SUT

[CmdletBinding()]
param(
    [string]$Kernel = 'seed/Codex.cdx',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$src = 'codex/test/cost/accumulator-corpus.codex'
$key = 'codex/test/cost/accumulator-corpus.expected'
$out = 'build-output'
New-Item -ItemType Directory -Force $out | Out-Null
$cdx = "$out/cost-corpus.cdx"
$got = "$out/cost-corpus.out"

Write-Host "cost-corpus: compiling with $Kernel"
& (Join-Path $PSScriptRoot 'compile.ps1') -Src $src -Out $cdx -Log "$cdx.log" -Kernel $Kernel | Out-Null
if (-not (Test-Path $cdx)) { Write-Host "  FAIL  $src did not compile"; exit 1 }

& (Join-Path $PSScriptRoot 'test-run.ps1') -Kernel $cdx -OutFile $got | Out-Null

$lines = @(Get-Content $got | Where-Object { $_.Trim() -ne '' })
if ($lines.Count -eq 0) { Write-Host "  FAIL  the corpus printed nothing"; exit 1 }

$fail = 0
$pos = @()
$neg = @()

foreach ($l in $lines) {
    if ($l -notmatch '^\s*(\S+)\s+.*x(\d+)\.(\d+)\s+(quadratic|linear)\s*$') {
        Write-Host "  FAIL  unparsable row: $l"; $fail++; continue
    }
    $name = $matches[1]
    $ratio = [double]"$($matches[2]).$($matches[3])"
    $verdict = $matches[4]

    # The name prefix is the DECLARED intent; the verdict is what was MEASURED.
    # Disagreement between them is the finding, in either direction.
    $want = if ($name.StartsWith('p-')) { 'quadratic' } elseif ($name.StartsWith('n-')) { 'linear' } else { $null }
    if ($null -eq $want) { Write-Host "  FAIL  $name has no p-/n- prefix, so nothing declares its intent"; $fail++; continue }

    if ($verdict -ne $want) {
        Write-Host "  FAIL  $name declared $want, measured $verdict (x$ratio)"; $fail++
    } else {
        Write-Host "  PASS  $name $verdict x$ratio"
    }
    if ($want -eq 'quadratic') { $pos += $ratio } else { $neg += $ratio }
}

if ($pos.Count -eq 0 -or $neg.Count -eq 0) {
    Write-Host "  FAIL  the corpus must carry BOTH directions; got $($pos.Count) positive and $($neg.Count) negative"
    $fail++
} else {
    $worstPos = ($pos | Measure-Object -Minimum).Minimum
    $bestNeg = ($neg | Measure-Object -Maximum).Maximum
    Write-Host ""
    Write-Host "  separation: worst positive x$worstPos, best negative x$bestNeg (threshold 8)"
    if ($worstPos -le $bestNeg) {
        Write-Host "  FAIL  the populations overlap -- the corpus can no longer separate them"; $fail++
    } elseif ($worstPos -le 8 -or $bestNeg -ge 8) {
        Write-Host "  FAIL  the threshold of 8 is no longer inside the gap"; $fail++
    } else {
        Write-Host "  PASS  the threshold sits in the gap"
    }
}

# The answer key is compared last and reported separately, because a moved
# number is not the same event as a broken property. New numbers with the
# properties intact means re-record the key; properties broken means stop.
$keyText = (Get-Content $key -Raw) -replace "`r`n", "`n"
$gotText = (Get-Content $got -Raw) -replace "`r`n", "`n"
if ($keyText.Trim() -ne $gotText.Trim()) {
    Write-Host ""
    Write-Host "  NOTE  the numbers differ from $key."
    Write-Host "        If every row above PASSED, the properties hold and the key is stale: re-record it."
    Write-Host "        If any row FAILED, the key is not the problem."
}

if (-not $KeepArtifacts) { Remove-Item -Force $cdx -ErrorAction SilentlyContinue }

Write-Host ""
if ($fail -gt 0) { Write-Host "cost-corpus: $fail FAILED"; exit 1 }
Write-Host "cost-corpus: all checks passed"
exit 0
