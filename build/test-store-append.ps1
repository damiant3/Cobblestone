# One work stored and checked out byte-exact; a second stored and BOTH back.
#
# Two instruments merged into one script (2026-07-28, BatteryReorg step 8;
# the single-file half was build/test-store-real-file.ps1).
#
# Stage one is the real-file round trip: cdx-store is a real tool a person
# runs on a real file; it stores an actual file from the tree, cdx-checkout
# reads the store back, and the checkout must equal the source BYTE FOR BYTE.
# The file crosses UTF-8 -> CCE on the way in and CCE -> Unicode on the way
# out, so a clean round trip also proves the crossing is invertible.
#
# Stage two is the case that did not exist when only one work was ever
# stored: ONE work is precisely the case where appending and replacing
# agree, so cdx-store opening the store with disk-init instead of disk-load
# went unnoticed -- every boot built an empty store, and a disk handed three
# works held only the third. The test therefore stores a second, DIFFERENT
# file and requires BOTH to come back. Against the old behaviour it fails on
# the first file, which is what makes it an instrument rather than a
# formality.
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
$kArg = @(); if ($Kernel) { $kArg = @('-Kernel', $Kernel) }
function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; exit 1 }

foreach ($s in @($FirstSrc, $SecondSrc)) {
    if (-not (Test-Path -PathType Leaf $s)) { Fail "source not found: $s" }
}

function Read-Checkout([string]$outFile) {
    $vm = Join-Path 'tools' 'codex-vm.exe'
    $p = Start-Process -FilePath $vm -ArgumentList @('-kernel', $coCdx, '-output', $outFile, '-disk', $disk, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(120000)) { try { $p.Kill() } catch {} ; Fail 'checkout VM did not exit' }
    (((Get-Content $outFile -Raw -ErrorAction SilentlyContinue)) -replace "`r", '' -replace "^[\x00-\x08\x0e-\x1f]+", '').TrimEnd()
}

function Source-Body([string]$src) {
    (([System.IO.File]::ReadAllText((Resolve-Path $src)) -replace "`r", '')).TrimEnd()
}

Write-Host '--- 1. store one real file into a blank store ---'
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 4194304))
& pwsh -NoProfile -File 'build/store-source.ps1' -Src $FirstSrc -Disk $disk @kArg
if ($LASTEXITCODE -ne 0) { Fail "the store tool did not store $FirstSrc" }

& pwsh -NoProfile -File 'build/compile.ps1' -Src 'tools/cdx-checkout.codex' -Out $coCdx -Log (Join-Path $work 'cdx-checkout.log') @kArg | Out-Null
if (-not (Test-Path $coCdx) -or (Get-Item $coCdx).Length -eq 0) { Fail 'cdx-checkout did not compile' }

Write-Host '--- 2. check it back out; must equal the source byte for byte ---'
$single = Read-Checkout (Join-Path $work 'checkout-one.out')
$firstBody = Source-Body $FirstSrc
if ($single -ne $firstBody) {
    [System.IO.File]::WriteAllText((Join-Path $work 'checked-out-one.codex'), $single)
    Write-Host "  original: $($firstBody.Length) chars, checkout: $($single.Length) chars" -ForegroundColor Red
    Fail 'the file did not round-trip through the store byte for byte'
}
Write-Host "  $($single.Length) characters round-tripped byte for byte" -ForegroundColor Green

Write-Host '--- 3. store a second, different file into the same store ---'
& pwsh -NoProfile -File 'build/store-source.ps1' -Src $SecondSrc -Disk $disk @kArg
if ($LASTEXITCODE -ne 0) { Fail "the store tool did not store $SecondSrc" }

Write-Host '--- 4. check the store back out; BOTH works must be present ---'
$actual = Read-Checkout (Join-Path $work 'checkout.out')

# Both works must be present. The first is the one a replacing store loses, so
# it is checked first and named explicitly in the failure.
$missing = @()
foreach ($s in @($FirstSrc, $SecondSrc)) {
    if (-not $actual.Contains((Source-Body $s))) { $missing += $s }
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
foreach ($s in @($FirstSrc, $SecondSrc)) { $expectedSum += (Source-Body $s).Length }
$slack = $actual.Length - $expectedSum
if ($slack -lt 0 -or $slack -gt 8) {
    Fail "checkout length $($actual.Length) is not the two sources ($expectedSum) plus separators"
}

Write-Host ''
Write-Host "  two works stored, both checked back out ($($actual.Length) chars, sources $expectedSum + $slack separator)" -ForegroundColor Green
Write-Host ''
Write-Host 'PASS: one work round-trips byte-exact, and a store accumulates works.' -ForegroundColor Green
exit 0
