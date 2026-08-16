# Does the UEFI stub pick the largest GOP mode the firmware enumerates, and
# fall through when it cannot? Six bed arms, one PE, read from codex-vm's own
# "GOP: SetMode" line and the screenshot's geometry (delay 2000: 5000+ writes
# no BMP at all on a UEFI probe image, measured by blu 2026-08-15, and reads
# exactly like a killed GOP).
#
#   build/gop-mode-arm.ps1                       # default payload: seed/Codex.cdx
#   build/gop-mode-arm.ps1 -Cdx build\output\Sut.cdx
#
# The payload never matters: the selection runs inside GopAcquire before the
# payload's first instruction. Each arm's expected outcome is written HERE,
# before it runs. -uefi-conout-remode is the ASUS-shaped arm: the console's
# ClearScreen puts the glass at 1024x768 and the stub must climb back to the
# panel's largest mode; the old stub (main 15393) stays at 1024x768 under it,
# which is the ablation.
param(
    [string]$Cdx = 'seed/Codex.cdx',
    [int]$TimeoutSec = 90
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vm = Join-Path $repo 'tools/codex-vm.exe'
$cdxAbs = if ([IO.Path]::IsPathRooted($Cdx)) { $Cdx } else { Join-Path $repo $Cdx }
foreach ($f in @($vm, $cdxAbs)) { if (-not (Test-Path $f)) { Write-Host "FAIL: $f missing"; exit 1 } }
$work = Join-Path ([IO.Path]::GetTempPath()) ("gopmode-" + (Split-Path $repo -Leaf))
New-Item -ItemType Directory -Force $work | Out-Null

$efi = Join-Path $work 'gopmode.efi'
& pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') -CdxInput $cdxAbs -Out $efi -HeapPages 32768 *> (Join-Path $work 'pe.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $efi)) { Write-Host "FAIL: cdx-to-pe"; Get-Content (Join-Path $work 'pe.log') | Select-Object -Last 5; exit 1 }
$img = Join-Path $work 'gopmode.img'
& pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') -PeInput $efi -Out $img -TotalSectors 32768 *> (Join-Path $work 'img.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $img)) { Write-Host "FAIL: build-img"; Get-Content (Join-Path $work 'img.log') | Select-Object -Last 5; exit 1 }

# name, extra flags, expected SetMode target (-1 = none), expected BMP geometry ('' = no framebuffer)
$arms = @(
    @('default',   @(),                                                        2, '1024x768'),
    @('desk1600',  @('-gop-width','1600','-gop-height','900'),                -1, '1600x900'),
    @('maxmode1',  @('-gop-max-mode','1'),                                    -1, ''),
    @('remode',    @('-uefi-conout-remode','-gop-width','1600','-gop-height','900'), 3, '1600x900'),
    @('gop800',    @('-gop-width','800','-gop-height','600'),                  2, '1024x768'),
    @('gop',       @('-gop'),                                                  2, '1024x768')
)
$bad = 0
foreach ($arm in $arms) {
    $name = $arm[0]; $flags = $arm[1]; $wantMode = $arm[2]; $wantBmp = $arm[3]
    $copy = Join-Path $work "$name.img"; Copy-Item $img $copy -Force
    $bmp = Join-Path $work "$name.bmp"; $err = Join-Path $work "$name.err"
    Remove-Item $bmp, $err -ErrorAction SilentlyContinue
    $a = @('-kernel', $copy, '-uefi', '-headless', '-screenshot', $bmp, '-screenshot-delay', '2000') + $flags
    $p = Start-Process -FilePath $vm -ArgumentList $a -NoNewWindow -PassThru -RedirectStandardError $err -RedirectStandardOutput (Join-Path $work "$name.stdout")
    $p.WaitForExit($TimeoutSec * 1000) | Out-Null
    if (-not $p.HasExited) { $p.Kill(); Start-Sleep -Milliseconds 500 }
    $lines = @(Get-Content $err -ErrorAction SilentlyContinue)
    $crash = @($lines | Where-Object { $_ -match 'HOST CRASH' }).Count -gt 0
    $setLine = @($lines | Where-Object { $_ -match 'GOP: SetMode (\d+)' })
    $gotMode = -1
    if ($setLine.Count -gt 0 -and $setLine[-1] -match 'GOP: SetMode (\d+)') { $gotMode = [int]$Matches[1] }
    $gotBmp = ''
    if (Test-Path $bmp) { $b = [IO.File]::ReadAllBytes($bmp); $gotBmp = [BitConverter]::ToInt32($b,18).ToString() + 'x' + [Math]::Abs([BitConverter]::ToInt32($b,22)) }
    $ok = (-not $crash) -and ($gotMode -eq $wantMode) -and ($gotBmp -eq $wantBmp)
    if (-not $ok) { $bad++ }
    Write-Host ("  {0,-9} setmode={1,-2} bmp={2,-9} expected setmode={3,-2} bmp={4,-9} {5}" -f $name, $gotMode, $(if ($gotBmp) { $gotBmp } else { 'none' }), $wantMode, $(if ($wantBmp) { $wantBmp } else { 'none' }), $(if ($crash) { 'HOST CRASH' } elseif ($ok) { 'ok' } else { 'MISMATCH' }))
}
if ($bad -gt 0) { Write-Host "gop-mode-arm: FAIL ($bad of $($arms.Count) arms; artifacts in $work)"; exit 1 }
Write-Host "gop-mode-arm: OK ($($arms.Count) arms)"
exit 0
