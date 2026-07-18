# Compile a program out of the fact store, and run it.
#
# BACKLOG 6.1 -- "Codex stores its own source" -- has been the largest
# unrealized piece of the founding vision since day one. This script is the
# first thing that makes the sentence literally true, end to end, with no
# Perforce anywhere in the chain:
#
#   1. codex/test/apps/checkout-emit boots with a blank disk. It stores a real
#      Codex program into DiskFacts as a signed, content-addressed fact; then
#      REVISES it and stores the revision; then rebuilds the work index from the
#      disk, walks the tree, fetches the current version of every path, and
#      prints the checked-out source.
#
#   2. That output -- source code that existed nowhere but a block device, in a
#      log of signed facts -- is compiled.
#
#   3. The binary is run. If it prints its greeting, a program was compiled out
#      of the fact store.
#
# The first edition of the stored program is WRONG ON PURPOSE: it prints
# "THE SUPERSEDED EDITION -- checkout is broken". So if the checkout ever takes
# the superseded version rather than the current one, this script does not
# quietly pass -- the binary itself says what went wrong.
#
# Why this is a script and not a BVT test: it is two compiles with a VM boot in
# between, and the output of stage one is the INPUT of stage two. The BVT runs
# one compile and one boot per test. checkout-emit is in the BVT on its own,
# where its expected output is the checked-out source, which pins the checkout
# byte for byte; this script is what proves the bytes are a program.
#
# Usage: build/test-compile-from-store.ps1

[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$expected = 'I was compiled out of the fact store.'
$work = Join-Path 'test-output' 'compile-from-store'
New-Item -ItemType Directory -Force -Path $work | Out-Null

$emitSrc  = 'codex\test\apps\checkout-emit.codex'
$emitCdx  = Join-Path $work 'checkout-emit.cdx'
$emitLog  = Join-Path $work 'checkout-emit.log'
$disk     = Join-Path $work 'store.disk'
$checkout = Join-Path $work 'checked-out.codex'
$progCdx  = Join-Path $work 'from-store.cdx'
$progLog  = Join-Path $work 'from-store.log'
$progOut  = Join-Path $work 'from-store.out'

function Fail([string]$msg) { Write-Host "FAIL: $msg" -ForegroundColor Red; exit 1 }

Write-Host '--- 1. store a program, revise it, check the tree back out ---'
& pwsh -NoProfile -File 'build\compile.ps1' -Src $emitSrc -Out $emitCdx -Log $emitLog | Out-Null
if (-not (Test-Path $emitCdx)) { Fail "checkout-emit did not compile; see $emitLog" }

# A blank disk. The store starts with nothing in it -- that is the point.
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 1048576))

& pwsh -NoProfile -File 'build\test-run.ps1' -Kernel $emitCdx -OutFile $checkout -DiskFile $disk 2>$null | Out-Null
if (-not (Test-Path $checkout)) { Fail 'the store produced no checkout' }

$src = Get-Content $checkout -Raw
if ([string]::IsNullOrWhiteSpace($src)) { Fail 'the checkout was empty' }
if ($src -match 'THE SUPERSEDED EDITION') {
    Fail 'the checkout took the SUPERSEDED edition. The tree is not following the last write for a path.'
}
Write-Host "  checked out $((Get-Item $checkout).Length) bytes of source from a block device"

Write-Host '--- 2. compile what the store handed back ---'
& pwsh -NoProfile -File 'build\compile.ps1' -Src $checkout -Out $progCdx -Log $progLog | Out-Null
if (-not (Test-Path $progCdx)) { Fail "the checked-out source did not compile; see $progLog" }
Write-Host '  it compiled'

Write-Host '--- 3. run it ---'
& pwsh -NoProfile -File 'build\test-run.ps1' -Kernel $progCdx -OutFile $progOut 2>$null | Out-Null
if (-not (Test-Path $progOut)) { Fail 'the binary produced no output' }
$actual = ((Get-Content $progOut -Raw) -replace "`r", '').Trim()

if ($actual -ne $expected) {
    Write-Host "  got:      '$actual'" -ForegroundColor Red
    Write-Host "  expected: '$expected'" -ForegroundColor Red
    Fail 'the program compiled out of the store did not say what it should'
}

Write-Host ''
Write-Host "  $actual" -ForegroundColor Green
Write-Host ''
Write-Host 'PASS: a program was compiled out of the fact store.' -ForegroundColor Green
exit 0
