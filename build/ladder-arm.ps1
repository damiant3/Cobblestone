# Calibrate the metal ladder: run every arm, including the ones that must FAIL.
#
# The ladder reports a payload's progress as a screen colour, because the ASUS
# has no serial port and ConOut has never been shown to render a character
# there. A colour ladder nobody has watched fail is worth nothing, so this
# forces each state and prints which stage was last painted.
#
#   build/ladder-arm.ps1                 # all four arms
#   build/ladder-arm.ps1 -Keep           # leave the working images for inspection
#
# Expected, and a differing row is a defect in the ladder, not in the arm:
#
#   pass      wrote      WHITE     everything worked
#   nodisk    systab     YELLOW    no EFI_BLOCK_IO to locate
#   badbpb    read       MAGENTA   sector came back, bytes-per-sector is not 512
#   small     bpb        ORANGE    volume too small for the scratch LBA
#
# CYAN (the SystemTable cell reading zero) is NOT forced here: the stub primes
# that cell and nothing in the bed can unprime it. It is the one rung on the
# ladder that has never been seen to fire.
[CmdletBinding()]
param(
    [switch]$Keep,
    [int]$Seconds = 25
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
$Img  = Join-Path $Repo 'build\boot\blockladder.img'
foreach ($f in @($Vm, $Img)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "FAIL: $f missing"; exit 1 }
}

# REFUSE TO CALIBRATE A STALE IMAGE. build-option-a.ps1 leaves the previous
# .img in place when the compile fails, so running this straight after a failed
# build calibrated the OLD payload and printed a green result for source that
# had never compiled. A pass has to be a pass for the source on disk.
$srcs = @(
    (Join-Path $Repo 'apps\works\BlockLadderProbe.codex'),
    (Join-Path $Repo 'apps\works\MetalLadder.codex')
)
$imgTime = (Get-Item $Img).LastWriteTimeUtc
foreach ($s in $srcs) {
    if (-not (Test-Path $s)) { Write-Host "FAIL: source $s missing"; exit 1 }
    if ((Get-Item $s).LastWriteTimeUtc -gt $imgTime) {
        Write-Host "STALE: $(Split-Path $s -Leaf) is newer than blockladder.img."
        Write-Host "       Rebuild first (and check the compile actually succeeded):"
        Write-Host "       build/boot/build-option-a.ps1 -Src apps/works/BlockLadderProbe.codex -Out build/boot/blockladder.img -Kernel seed/Codex.cdx -Uefi"
        exit 1
    }
}

# Derived from the workspace, never a fixed path: two agents running this at
# once must not read each other's images (L-SHARED).
$Work = Join-Path ([IO.Path]::GetTempPath()) ("ladder-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

function New-Copy([string]$name) {
    $dst = Join-Path $Work $name
    Copy-Item $Img $dst -Force
    Set-ItemProperty $dst -Name IsReadOnly -Value $false
    return $dst
}

function Invoke-Arm([string]$name, [string]$kernel, [string]$disk) {
    $err = Join-Path $Work "$name.err"
    $a = @('-kernel', $kernel, '-uefi', '-headless', '-output', (Join-Path $Work "$name.out"))
    if ($disk) { $a += @('-disk', $disk) }
    $p = Start-Process -FilePath $Vm -ArgumentList $a -NoNewWindow -PassThru `
            -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$name.stdout")
    # The probe holds its colour by repainting, so it never exits. -timeout is
    # not reliable here. Kill the PID we started; never by name, other agents
    # are running their own VMs.
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 300

    # The guest's console goes to the -output file. stderr carries codex-vm's own
    # trace, and reading only that is how this reported "(nothing painted)" for
    # four healthy arms at once. Both are read, so neither stream going quiet
    # silently zeroes the result.
    $text = @()
    foreach ($f in @((Join-Path $Work "$name.out"), $err)) {
        if (Test-Path $f) { $text += @(Get-Content $f -ErrorAction SilentlyContinue) }
    }
    # The last stage that PASSED, not the last that painted: a failed stage
    # repaints the previous colour, so its own tag names a rung the screen is
    # not showing.
    $painted = @($text | Select-String -Pattern 'ok=1 painted fb=1' | ForEach-Object { $_.Line })
    if ($painted.Count -eq 0) { return '(nothing painted)' }
    return (($painted[-1] -split 'LADDER ')[-1] -replace ' ok=.*$', '')
}

$expected = [ordered]@{ pass = 'wrote'; nodisk = 'systab'; badbpb = 'read'; small = 'bpb' }
$actual   = [ordered]@{}

$k = New-Copy 'k-pass.img'
$actual['pass'] = Invoke-Arm 'pass' $k $k

$k = New-Copy 'k-nodisk.img'
$actual['nodisk'] = Invoke-Arm 'nodisk' $k ''

# A sector comes back but its bytes-per-sector is not 512. The BAD disk is
# passed as -disk only: codex-vm reads the BPB of -kernel to find BOOTX64.EFI,
# so corrupting the one it boots from would stop the guest before it starts.
$k = New-Copy 'k-badbpb.img'
$d = New-Copy 'd-badbpb.img'
$bytes = [IO.File]::ReadAllBytes($d)
$bytes[2048 * 512 + 11] = 0
$bytes[2048 * 512 + 12] = 0
[IO.File]::WriteAllBytes($d, $bytes)
$actual['badbpb'] = Invoke-Arm 'badbpb' $k $d

# A volume with no LBA 30000, so the scratch write and its readback fail.
$k = New-Copy 'k-small.img'
$d = Join-Path $Work 'd-small.img'
& pwsh -NoProfile -File (Join-Path $Repo 'build\build-img.ps1') `
    -PeInput (Join-Path $Repo 'build\boot\blockladder.efi') -Out $d -TotalSectors 16384 2>&1 | Out-Null
if (-not (Test-Path $d)) {
    # No stashed .efi beside the image: rebuild the small volume from the image
    # itself by truncating is not valid FAT, so report rather than pretend.
    $actual['small'] = '(skipped: build/boot/blockladder.efi missing)'
} else {
    $actual['small'] = Invoke-Arm 'small' $k $d
}

$bad = 0
Write-Host ''
Write-Host 'arm      expected   actual'
Write-Host '-------  ---------  ---------'
foreach ($name in $expected.Keys) {
    $e = $expected[$name]; $a = $actual[$name]
    $mark = if ($a -eq $e) { '' } else { '   <-- MISMATCH'; }
    if ($a -ne $e) { $bad++ }
    Write-Host ("{0,-8} {1,-10} {2}{3}" -f $name, $e, $a, $mark)
}
Write-Host ''
if (-not $Keep) { Remove-Item (Join-Path $Work '*.img') -Force -ErrorAction SilentlyContinue }
if ($bad -gt 0) { Write-Host "LADDER NOT CALIBRATED: $bad arm(s) disagree"; exit 1 }
Write-Host 'Ladder calibrated: every arm painted the stage it should have.'
exit 0
