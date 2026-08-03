# The installed stick boots.
#
# `codex/test/apps/install-to-drive` proves the BYTES cross: three files, three
# matching digests, read back off the target through a different filesystem on
# a different drive. It cannot prove the result is bootable, because its source
# fixture carries a synthetic 4096-byte blob where BOOTX64.EFI belongs -- a
# stand-in chosen so the fixture stays small and the digests stay stable.
#
# This harness closes that gap the only way it can be closed: build a REAL boot
# image with build-boot-img.ps1, install from it onto a blank disk inside a
# guest, and then boot the disk the guest wrote. codex-vm -uefi scans a GPT
# image for the EFI system partition and loads EFI/BOOT/BOOTX64.EFI out of it,
# so the firmware doing the reading is not our FAT driver -- which is the whole
# point. Our reader agreeing with our writer is what the in-battery test
# already establishes.
#
# On demand: it boots several VMs and builds a boot image, so it is not in
# build/build.ps1.
#
#   pwsh build/install-boot-test.ps1
#   pwsh build/install-boot-test.ps1 -Kernel build/output/Sut.cdx
#   pwsh build/install-boot-test.ps1 -KeepArtifacts
[CmdletBinding()]
param(
    [string]$Kernel = '',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) { Write-Host "FAIL: kernel $Kernel missing"; exit 1 }

$work = Join-Path $Repo 'build-output\install-boot'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

$vm      = Join-Path $Repo 'tools\codex-vm.exe'
$compile = Join-Path $PSScriptRoot 'compile.ps1'

$fail = 0
function Check ([string]$what, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "  PASS  $what" }
    else { Write-Host "  FAIL  $what"; if ($detail) { Write-Host "        $detail" }; $script:fail++ }
}

# ------------------------------------------------------------- the source stick
# A real boot image: the UEFI dev console as BOOTX64.EFI, the seed, and the
# concatenated source. This is the artifact a person is handed.
#
# The payload is named explicitly rather than taken from the default. This
# harness asserts the 'Codex Dev Console' banner below, so it is testing
# UefiBoot specifically; the default became GopBoot when B5.4 step 3 flipped
# it, and a harness that checks for one payload must not silently follow a
# default that names another.
Write-Host '[install-boot-test] building a real source stick...'
$srcImg = Join-Path $work 'source.img'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-boot-img.ps1') -Out $srcImg -BootSource 'apps\works\UefiBoot.codex' -Uefi 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $srcImg)) {
    Write-Host 'FAIL: could not build the source boot image'
    exit 1
}
$srcSize = (Get-Item $srcImg).Length
Write-Host "  source stick: $srcSize bytes"

# The target has to be able to hold the ESP the installer will size for it, and
# the payload that goes in it. Same size as the source is the honest choice: it
# is the case a person actually has, two sticks of the same kind.
$tgtImg = Join-Path $work 'target.img'
[System.IO.File]::WriteAllBytes($tgtImg, [byte[]]::new($srcSize))

# ------------------------------------------------------------------ the install
Write-Host '[install-boot-test] installing, guest to guest...'
$guestSrc = Join-Path $Repo 'codex\test\apps\install-to-drive.codex'
$guestCdx = Join-Path $work 'install.cdx'
$guestLog = Join-Path $work 'install-compile.log'
& pwsh -NoProfile -File $compile -Src $guestSrc -Out $guestCdx -Log $guestLog -Kernel $Kernel 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $guestCdx)) {
    Write-Host 'FAIL: install guest did not compile'
    Get-Content $guestLog -TotalCount 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
    exit 1
}

$installOut = Join-Path $work 'install.out'
# Bounded, like every other VM here. An unbounded run is not merely slow when it
# goes wrong: this one holds both disk images open, so a survivor blocks the boot
# steps below on files they need, and the failure presents as the NEXT stage
# hanging rather than as this one.
$installErr = Join-Path $work 'install.err'
$ip = Start-Process -FilePath $vm -PassThru -WindowStyle Hidden -RedirectStandardError $installErr `
    -ArgumentList @('-kernel', $guestCdx, '-disk', $srcImg, '-disk2', $tgtImg,
                    '-headless', '-output', $installOut, '-mem', '3072')
# 25 minutes, and it needs most of them. The guest copies ~5.9 MB a sector at a
# time and SHA-256s both ends, which measured ~8 KB/s: about 12 minutes of copy
# plus verification. A budget sized by intuition (180 s) reports a working
# install as a hang, which is what it did on the first run of this bound.
if (-not $ip.WaitForExit(1500000)) {
    try { Stop-Process -Id $ip.Id -Force -ErrorAction Stop } catch { }
    Start-Sleep -Milliseconds 400
    Write-Host 'FAIL: the install guest did not finish inside its budget'
    exit 1
}
$installText = if (Test-Path $installOut) { (Get-Content $installOut -Raw) -replace '[^\x20-\x7E\r\n]', '' } else { '' }
Write-Host ($installText.TrimEnd())
Check 'the install reports success' ($installText -like '*install: installed*') ''

# ------------------------------------------------------------------- the boot
# The target is booted by the firmware, not by our FAT driver. A guest that
# reaches its own console has been loaded out of a volume this project wrote.
# codex-vm reports what its firmware emulation found on STDERR, not on the
# guest's serial. The first cut of this harness discarded stderr and read the
# guest's silence as a verdict, which was wrong twice over: it blamed the
# installer for something the reference stick does too, and it threw away the
# one line that says whether the volume was recognised at all.
Write-Host '[install-boot-test] booting the installed target...'

# A working dev console NEVER EXITS -- it sits in its input loop redrawing, and
# headless there is no keyboard to end it. So the boot must be given a wall
# budget and then stopped, and the guest's serial read out of the file it was
# already streaming to. Running the VM to completion is only viable while the
# guest crashes, which is precisely the state this harness exists to leave.
$BootBudgetMs = 25000

function Invoke-Uefi ([string]$img, [string]$tag) {
    $o = Join-Path $work "$tag.out"; $e = Join-Path $work "$tag.err"
    $args = @('-uefi', '-kernel', $img, '-headless', '-output', $o, '-mem', '3072')
    $proc = Start-Process -FilePath $vm -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardError $e
    $exited = $proc.WaitForExit($BootBudgetMs)
    if (-not $exited) {
        # Still alive at the budget. For a console that is the PASS shape, so
        # this is not an error -- take what it has said and stop it.
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
        Start-Sleep -Milliseconds 400
    }
    $outText = ''
    if (Test-Path $o) {
        # A looping console can write megabytes of redraws. Only the first
        # screenful is ever asserted on, and reading the whole file costs
        # seconds and hundreds of MB for nothing.
        $fs = [System.IO.File]::Open($o, 'Open', 'Read', 'ReadWrite')
        try {
            $n = [int][Math]::Min(65536L, $fs.Length)
            $buf = New-Object byte[] $n
            [void]$fs.Read($buf, 0, $n)
            $outText = ([System.Text.Encoding]::ASCII.GetString($buf)) -replace '[^\x20-\x7E\r\n]', ''
        } finally { $fs.Dispose() }
    }
    $errText = ''
    if (Test-Path $e) { $errText = (Get-Content $e -Raw -ErrorAction SilentlyContinue) }
    return @{ Out = $outText; Err = $errText; StillRunning = (-not $exited) }
}

$tgt = Invoke-Uefi $tgtImg 'boot-target'
$src = Invoke-Uefi $srcImg 'boot-source'

# The blank control: without it, a firmware that found our volume and one that
# found any volume at all read the same.
$blankImg = Join-Path $work 'blank.img'
[System.IO.File]::WriteAllBytes($blankImg, [byte[]]::new($srcSize))
$blank = Invoke-Uefi $blankImg 'boot-blank'

# The installed volume is FAT32 because the installer formats it so; the source
# stick is FAT16 because build-img.ps1 makes it so. Naming the filesystem in
# the assertion is what keeps this honest: codex-vm's extractor read the BPB as
# FAT16 unconditionally until 2026-07-27, so a FAT32 ESP was passed over in
# silence and the image loaded as a raw kernel instead.
Check 'the installed target is recognised as a FAT32 ESP' `
    ($tgt.Err -match 'extracted BOOTX64\.EFI \(\d+ bytes, FAT32\)') $tgt.Err
Check 'the source stick is recognised as a FAT16 ESP' `
    ($src.Err -match 'extracted BOOTX64\.EFI \(\d+ bytes, FAT16\)') ''
Check 'the blank control is recognised as nothing' `
    ($blank.Err -notmatch 'extracted BOOTX64') ''
Check 'both images yield the same payload size' `
    (($tgt.Err -replace '(?s).*extracted BOOTX64\.EFI \((\d+) bytes.*', '$1') -eq
     ($src.Err -replace '(?s).*extracted BOOTX64\.EFI \((\d+) bytes.*', '$1')) ''

# The dev console boots, as of 2026-07-27. Until then it never had: the PE stub
# did not program IA32_LSTAR, so the first `syscall` -- and every disk read is
# one -- jumped through an unprogrammed MSR into zeroed memory and triple-faulted.
# With that fixed and the SystemTable published at uefi-systab-addr, the console
# reaches its input loop and stays there.
#
# So the assertion is what it prints, not merely that it survives. "Ran at all"
# was satisfiable by a guest that emitted one byte and died, and for months it
# was satisfied by BOTH sticks failing identically, which is the shape of a test
# that cannot fail.
$banner = 'Codex Dev Console'
Write-Host "  target serial: $($tgt.Out.Trim().Length) chars   source serial: $($src.Out.Trim().Length) chars"

Check 'the reference stick reaches the dev console' `
    ($src.Out -like "*$banner*") "source serial was: $($src.Out.Trim())"
Check 'the installed target reaches the dev console' `
    ($tgt.Out -like "*$banner*") "target serial was: $($tgt.Out.Trim())"

# A console that is alive is a console still running at the budget. A guest that
# printed the banner and then died would pass the two checks above; this is what
# separates "it booted" from "it booted and stayed up".
Check 'the installed target is still running at the boot budget' `
    ($tgt.StillRunning) 'the target printed the banner and then exited'

# The blank control must NOT print it, or the banner is coming from somewhere
# other than the volume under test.
Check 'the blank control reaches no console' `
    ($blank.Out -notlike "*$banner*") ''

Write-Host ''
if ($fail -gt 0) {
    Write-Host "install-boot-test: $fail assertion(s) FAILED"
    Write-Host "artifacts kept in $work"
    exit 1
}
if (-not $KeepArtifacts) { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
Write-Host 'install-boot-test: PASS'
exit 0
