# A real source file, stored into the fact store by a tool and checked back
# out of it -- byte for byte, with no Perforce anywhere in the chain.
#
# BACKLOG 6.1(d): "nothing writes the store but a test." This is the answer.
# cdx-store is a real tool a person runs on a real file; here it stores an
# actual file from the tree, and cdx-checkout reads the store back and prints
# the source. If what comes out equals what went in, the repository held a
# real file and handed it back.
#
# The file crosses UTF-8 -> CCE on the way in (hashing is not an I/O function,
# so the store addresses a work by its CCE bytes) and CCE -> Unicode on the way
# out, so a clean round trip also exercises that the crossing is invertible.
#
# Usage: build/test-store-real-file.ps1  [-Src codex/test/factorial.codex] [-Kernel seed\Codex.cdx]
[CmdletBinding()]
param([string]$Src = 'codex/test/factorial.codex', [string]$Kernel = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$work = Join-Path 'test-output' 'store-real-file'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$disk = Join-Path $work 'store.disk'
$coCdx = Join-Path $work 'cdx-checkout.cdx'
$coOut = Join-Path $work 'checkout.out'
$kArg = @(); if ($Kernel) { $kArg = @('-Kernel', $Kernel) }
function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; exit 1 }

if (-not (Test-Path -PathType Leaf $Src)) { Fail "source not found: $Src" }

Write-Host "--- 1. store $Src into a blank fact store ---"
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $disk), (New-Object byte[] 1048576))
& pwsh -NoProfile -File 'build/store-source.ps1' -Src $Src -Disk $disk @kArg
if ($LASTEXITCODE -ne 0) { Fail 'the store tool did not store the file' }

Write-Host '--- 2. check the store back out ---'
& pwsh -NoProfile -File 'build/compile.ps1' -Src 'tools/cdx-checkout.codex' -Out $coCdx -Log (Join-Path $work 'cdx-checkout.log') @kArg | Out-Null
if (-not (Test-Path $coCdx)) { Fail 'cdx-checkout did not compile' }
$vm = Join-Path 'tools' 'codex-vm.exe'
$p = Start-Process -FilePath $vm -ArgumentList @('-kernel', $coCdx, '-output', $coOut, '-disk', $disk, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden
if (-not $p.WaitForExit(90000)) { try { $p.Kill() } catch {} ; Fail 'checkout VM did not exit' }

$expected = (([System.IO.File]::ReadAllText((Resolve-Path $Src))) -replace "`r",'').TrimEnd()
$actual = (((Get-Content $coOut -Raw -ErrorAction SilentlyContinue)) -replace "`r",'' -replace "^[\x00-\x08\x0e-\x1f]+",'').TrimEnd()
if ($actual -ne $expected) {
    $work2 = Join-Path $work 'checked-out.codex'
    [System.IO.File]::WriteAllText($work2, $actual)
    Write-Host "  checked-out bytes differ from the original; see $work2" -ForegroundColor Red
    Write-Host "  original: $($expected.Length) chars, checkout: $($actual.Length) chars" -ForegroundColor Red
    Fail 'the file did not round-trip through the store'
}

Write-Host ''
Write-Host "  $($actual.Length) characters round-tripped through the fact store, byte for byte" -ForegroundColor Green
Write-Host ''
Write-Host 'PASS: a real file was stored and checked back out of the fact store.' -ForegroundColor Green
exit 0
