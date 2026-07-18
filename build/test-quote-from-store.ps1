# Compile a program whose quotation is resolved out of the fact store.
#
# BACKLOG 6.1 -- "Codex stores its own source" -- reaches the compiler here.
# Until now a quoted work travelled beside the source in a %%QUOTED-WORKS%%
# text blob, because the one place that resolves quotations -- the compiler's
# import gate -- could not read the disk that holds the works. This is the
# first time the compiler resolves a quotation from the repository itself,
# with no work handed alongside the source:
#
#   1. codex/test/apps/store-quoted-work boots with a blank disk and writes one
#      signed, content-addressed work into DiskFacts. The work, its hash, its
#      signer and its signature are the exact quotation codex/test/quotes-gate
#      proves through the blob -- so the only new thing under test is that the
#      compiler reads from the disk what the app-side writer wrote to it.
#
#   2. codex/test/quote-from-store QUOTES that work by its digest, with NO
#      %%QUOTED-WORKS%% blob, and is compiled with the store disk attached
#      (compile.ps1 -DiskFile). The compiler reads the store, resolves the
#      quotation through the four guards, splices the work in, and compiles.
#
#   3. The binary is run. sort-ascending comes only from the quoted work, so if
#      it prints sorted=105 the quotation was resolved out of the fact store.
#
# A crossing test: the work is WRITTEN by the app-side writer (RepoProtocolPersist
# over DiskFacts) and READ by the compiler-side reader (foreword FactDisk). A
# drift between the two on-disk formats is a refused quotation here, not a quiet
# miscompile somewhere downstream.
#
# Usage: build/test-quote-from-store.ps1  [-Kernel seed\Codex.cdx]

[CmdletBinding()]
param([string]$Kernel = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$work = Join-Path 'test-output' 'quote-from-store'
New-Item -ItemType Directory -Force -Path $work | Out-Null

$writerSrc = 'codex\test\apps\store-quoted-work.codex'
$writerCdx = Join-Path $work 'store-quoted-work.cdx'
$writerLog = Join-Path $work 'store-quoted-work.log'
$disk      = Join-Path $work 'store.disk'
$quoteSrc  = 'codex\test\quote-from-store.codex'
$quoteCdx  = Join-Path $work 'quote-from-store.cdx'
$quoteLog  = Join-Path $work 'quote-from-store.log'
$quoteOut  = Join-Path $work 'quote-from-store.out'

$kArg = @(); if ($Kernel) { $kArg = @('-Kernel', $Kernel) }
function Fail([string]$msg) { Write-Host "FAIL: $msg" -ForegroundColor Red; exit 1 }

Write-Host '--- 1. write one signed work into the store on a blank disk ---'
& pwsh -NoProfile -File 'build\compile.ps1' -Src $writerSrc -Out $writerCdx -Log $writerLog @kArg | Out-Null
if (-not (Test-Path $writerCdx)) { Fail "the store writer did not compile; see $writerLog" }
# A blank store, and codex-vm attached to it DIRECTLY: its IDE writes are
# flushed durably to this file, so the work is still here when stage two
# compiles against it. test-run.ps1 copies the disk to a throwaway temp, so
# it cannot be used to write a store that must survive the boot.
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 1048576))
$writerOut = Join-Path $work 'writer.out'
$vmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
$wproc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel', $writerCdx, '-output', $writerOut, '-disk', $disk, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden
$wproc.WaitForExit(120000) | Out-Null
if (-not $wproc.HasExited) { try { $wproc.Kill() } catch {} ; Fail 'the store writer VM did not exit' }
if ((Get-Content $writerOut -Raw -ErrorAction SilentlyContinue) -notmatch 'stored') { Fail 'the store writer did not report it stored the work' }
Write-Host '  a signed work is on the disk'

Write-Host '--- 2. compile a source that quotes it, with no blob, against that disk ---'
& pwsh -NoProfile -File 'build\compile.ps1' -Src $quoteSrc -Out $quoteCdx -Log $quoteLog -DiskFile $disk @kArg | Out-Null
if (-not (Test-Path $quoteCdx)) { Fail "the quoting source did not compile; the quotation was not resolved from the store. See $quoteLog" }
Write-Host '  it compiled -- the quotation resolved from the store'

Write-Host '--- 3. run it ---'
& pwsh -NoProfile -File 'build\test-run.ps1' -Kernel $quoteCdx -OutFile $quoteOut 2>$null | Out-Null
$actual = ((Get-Content $quoteOut -Raw -ErrorAction SilentlyContinue) -replace "`r", '').Trim()
if ($actual -ne 'sorted=105') {
    Write-Host "  got:      '$actual'" -ForegroundColor Red
    Write-Host "  expected: 'sorted=105'" -ForegroundColor Red
    Fail 'the program compiled from the store did not compute with the quoted function'
}

Write-Host ''
Write-Host "  $actual" -ForegroundColor Green
Write-Host ''
Write-Host 'PASS: a quotation was resolved out of the fact store.' -ForegroundColor Green
exit 0
