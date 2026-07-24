# Two works into one store, both checked back out.
#
# This is the case that did not exist. build/test-store-real-file.ps1 stores
# exactly ONE work into a blank disk, and one is precisely the case where
# appending and replacing agree, so cdx-store opening the store with disk-init
# instead of disk-load went unnoticed: every boot built an empty store and
# wrote one definition into it, so a disk handed three works held only the
# third. The post-submit hook is the first caller that stores more than one
# thing, and it is how the defect surfaced.
#
# The test therefore stores two DIFFERENT files and requires BOTH to come back.
# Against the old behaviour it fails on the first file, which is what makes it
# an instrument rather than a formality.
#
# Usage: build/test-store-append.ps1 [-Kernel seed\Codex.cdx]
[CmdletBinding()]
param(
    [string]$FirstSrc = 'codex/test/factorial.codex',
    [string]$SecondSrc = 'codex/test/stringbuilder-independence.codex',
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$work = Join-Path 'test-output' 'store-append'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$disk = Join-Path $work 'store.disk'
$coCdx = Join-Path $work 'cdx-checkout.cdx'
$coOut = Join-Path $work 'checkout.out'
$kArg = @(); if ($Kernel) { $kArg = @('-Kernel', $Kernel) }
function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; exit 1 }

foreach ($s in @($FirstSrc, $SecondSrc)) {
    if (-not (Test-Path -PathType Leaf $s)) { Fail "source not found: $s" }
}

Write-Host '--- 1. store two different files into one blank store ---'
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 4194304))
foreach ($s in @($FirstSrc, $SecondSrc)) {
    & pwsh -NoProfile -File 'build/store-source.ps1' -Src $s -Disk $disk @kArg
    if ($LASTEXITCODE -ne 0) { Fail "the store tool did not store $s" }
}

Write-Host '--- 2. check the store back out ---'
& pwsh -NoProfile -File 'build/compile.ps1' -Src 'tools/cdx-checkout.codex' -Out $coCdx -Log (Join-Path $work 'cdx-checkout.log') @kArg | Out-Null
if (-not (Test-Path $coCdx)) { Fail 'cdx-checkout did not compile' }
$vm = Join-Path 'tools' 'codex-vm.exe'
$p = Start-Process -FilePath $vm -ArgumentList @('-kernel', $coCdx, '-output', $coOut, '-disk', $disk, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden
if (-not $p.WaitForExit(120000)) { try { $p.Kill() } catch {} ; Fail 'checkout VM did not exit' }

$actual = (((Get-Content $coOut -Raw -ErrorAction SilentlyContinue)) -replace "`r", '' -replace "^[\x00-\x08\x0e-\x1f]+", '').TrimEnd()

# Both works must be present. The first is the one a replacing store loses, so
# it is checked first and named explicitly in the failure.
$missing = @()
foreach ($s in @($FirstSrc, $SecondSrc)) {
    $body = ([System.IO.File]::ReadAllText((Resolve-Path $s)) -replace "`r", '').TrimEnd()
    if (-not $actual.Contains($body)) { $missing += $s }
}
if ($missing.Count -gt 0) {
    [System.IO.File]::WriteAllText((Join-Path $work 'checked-out.txt'), $actual)
    foreach ($m in $missing) { Write-Host "  missing from the store: $m" -ForegroundColor Red }
    Write-Host "  checkout held $($actual.Length) chars; see $work\checked-out.txt" -ForegroundColor Red
    Fail 'a work stored earlier did not survive a later store'
}

# The length is checked as well as the presence, so a store that somehow held
# both bodies plus something else does not pass quietly.
$expectedSum = 0
foreach ($s in @($FirstSrc, $SecondSrc)) {
    $expectedSum += (([System.IO.File]::ReadAllText((Resolve-Path $s)) -replace "`r", '').TrimEnd()).Length
}
$slack = $actual.Length - $expectedSum
if ($slack -lt 0 -or $slack -gt 8) {
    Fail "checkout length $($actual.Length) is not the two sources ($expectedSum) plus separators"
}

Write-Host ''
Write-Host "  two works stored, both checked back out ($($actual.Length) chars, sources $expectedSum + $slack separator)" -ForegroundColor Green
Write-Host ''
Write-Host 'PASS: a store accumulates works rather than replacing them.' -ForegroundColor Green
exit 0
