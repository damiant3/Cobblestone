# Boot codex/test/apps/factdisk-cap on the image mint-factlog-fixture.ps1 -Big
# mints, and grade it. The image is about 8.4 MB and is minted fresh every run
# rather than kept in the depot, which is why the chapter carries a .skip and
# this runner rather than a .disk.
#
#   entry 1  cap000, a record of exactly fd-max-content-len bytes   admitted
#   entry 2  pst001, one byte past it, well-formed                  refused
#   entry 3  fed789, small and signed                               arrives
#
# Usage: build/factdisk-cap-test.ps1 [-Kernel seed\Codex.cdx]

[CmdletBinding()]
param([string]$Kernel = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
if (-not $Kernel) { $Kernel = 'seed\Codex.cdx' }

$work = Join-Path 'test-output' 'factdisk-cap'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$disk = Join-Path $work 'factdisk-cap.disk'
$src  = 'codex\test\apps\factdisk-cap.codex'
$cdx  = Join-Path $work 'factdisk-cap.cdx'
$log  = Join-Path $work 'factdisk-cap.log'
$out  = Join-Path $work 'factdisk-cap.out'

function Fail([string]$msg) { Write-Host "FAIL: $msg" -ForegroundColor Red; exit 1 }

# cap000's content length: 4,194,304 less its 35-byte prefix, the 7 digits of
# the length field and that field's separator.
$expected = @(
    'count=2',
    'at-cap=4194261/val/77',
    'past-cap=absent',
    'tail=4/val/88'
) -join "`n"

& pwsh -NoProfile -File 'build\mint-factlog-fixture.ps1' -Big -Out $disk
if (-not (Test-Path $disk)) { Fail 'the image was not minted' }

& pwsh -NoProfile -File 'build\compile.ps1' -Src $src -Out $cdx -Log $log -Kernel $Kernel | Out-Null
if (-not (Test-Path $cdx)) { Fail "the chapter did not compile; see $log" }

$vm = Join-Path (Get-Location) 'tools\codex-vm.exe'
$p = Start-Process -FilePath $vm -ArgumentList @('-kernel', $cdx, '-output', $out, '-disk', $disk, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden
$p.WaitForExit(300000) | Out-Null
if (-not $p.HasExited) { try { $p.Kill() } catch {} ; Fail 'the VM did not exit within 300 s' }

$actual = ((Get-Content $out -Raw -ErrorAction SilentlyContinue) -replace "`r", '').Trim()
if ($actual -ne $expected) {
    Write-Host "  got:`n$actual" -ForegroundColor Red
    Write-Host "  expected:`n$expected" -ForegroundColor Red
    Fail 'factdisk-cap'
}
Write-Host $actual
Write-Host 'PASS: factdisk-cap' -ForegroundColor Green
exit 0
