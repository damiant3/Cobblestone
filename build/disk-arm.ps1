# Calibrate the compiler's DISK ladder: run every arm, including the ones that
# must FAIL.
#
# The compiler reports a metal compile's progress as a screen colour, because
# the ASUS has no serial port and ConOut has never been shown to render a
# character there. A colour ladder nobody has watched fail is worth nothing, so
# this forces each state and prints which stage was last painted.
#
#   build/disk-arm.ps1                   # build the payloads, then all six arms
#   build/disk-arm.ps1 -SkipBuild        # reuse build-output/disk-arm/*.efi
#   build/disk-arm.ps1 -Only pass        # one arm
#   build/disk-arm.ps1 -Keep             # leave the working images
#
# Expected, and a differing row is a defect in the ladder, not in the arm:
#
#   pass        wrote     WHITE      everything worked
#   nomode      entered   CYAN       no mode line on stdin
#   badbpb      mode      YELLOW     a sector comes back, bytes-per-sector is not 512
#   nosource    volume    MAGENTA    the volume mounted, SOURCE.SRC is not on it
#   badsource   source    ORANGE     the source read and would not compile
#   nowrite     compiled  BLUE       the compile finished, the write failed
#
# The colour is not read here; the trace is. That is a choice and no longer a
# necessity: `-screenshot` DOES render a payload's paint, and the claim that it
# did not was a bug in the BMP reader (PowerShell shifts a [byte] at byte
# width, so every pixel decoded as its blue channel alone). See
# OperatorsManual, "READING A COLOUR OUT OF -screenshot".
#
# The trace is still what this reads, because it names the rung and the ok=
# value rather than only the last colour standing, and because a rung that
# paints without tracing is invisible to a screenshot taken at one instant.
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$Keep,
    [string]$Only = '',
    [int]$Seconds = 180
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
if (-not (Test-Path -PathType Leaf $Vm)) { Write-Host "FAIL: $Vm missing"; exit 1 }

$Work = Join-Path $Repo 'build-output\disk-arm'
New-Item -ItemType Directory -Force $Work | Out-Null

$Concat  = Join-Path $Repo 'build\concat-codex-self.ps1'
$Compile = Join-Path $Repo 'build\compile.ps1'
$ToPe    = Join-Path $Repo 'build\cdx-to-pe.ps1'
$MkImg   = Join-Path $Repo 'build\build-img.ps1'

$src      = Join-Path $Work 'Codex.codex'
$cdx      = Join-Path $Work 'compiler-uefi.cdx'
$efiIn    = Join-Path $Work 'compiler-stdin.efi'
$efiNoIn  = Join-Path $Work 'compiler-nostdin.efi'

# The sources that go on the volume. LF ONLY: the DISK path does not apply
# utf8-to-cce and CR has no CCE code point, so a CRLF file fails at the first
# line with CDX2000 -- which is the badsource arm's mechanism, and would
# silently become the pass arm's too.
function Write-Lf([string]$path, [string]$text) {
    if (Test-Path $path) { Set-ItemProperty $path -Name IsReadOnly -Value $false }
    [IO.File]::WriteAllText($path, ($text -replace "`r`n", "`n"), [Text.UTF8Encoding]::new($false))
}
$goodSrc = Join-Path $Work 'GOOD.codex'
$badSrc  = Join-Path $Work 'BAD.codex'
Write-Lf $goodSrc @'
Chapter: DiskArmGood

Section: Entry

  opening : [Console] Nothing = act
   print-line-uni "disk arm"
  end
'@
Write-Lf $badSrc @'
Chapter: DiskArmBad

Section: Entry

  opening : [Console] Nothing = act
   print-line-uni (no-such-name 1)
  end
'@

if (-not $SkipBuild) {
    Write-Host '[disk-arm] concat + compile (UEFI exit mode)...'
    & pwsh -NoProfile -File $Concat -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $src
    & pwsh -NoProfile -File $Compile -Src $src -Out $cdx -Log (Join-Path $Work 'compile.log') -Uefi
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdx)) { Write-Host 'FAIL: compiler compile failed'; exit 1 }
    Write-Host '[disk-arm] PE payloads...'
    & pwsh -NoProfile -File $ToPe -CdxInput $cdx -Out $efiIn   -EntryStart -HeapPages 32768 -Stdin "DISK`nSOURCE.SRC`n" | Out-Null
    & pwsh -NoProfile -File $ToPe -CdxInput $cdx -Out $efiNoIn -EntryStart -HeapPages 32768 | Out-Null
}
foreach ($f in @($efiIn, $efiNoIn)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "FAIL: $f missing (run without -SkipBuild)"; exit 1 }
}

# REFUSE TO CALIBRATE A STALE PAYLOAD. A failed compile leaves the previous
# .efi in place, and calibrating it prints a green result for source that has
# never compiled.
foreach ($s in @((Join-Path $Repo 'codex\compiler\opening.codex'),
                 (Join-Path $Repo 'codex\compiler\Core\BootPaint.codex'))) {
    if ((Get-Item $s).LastWriteTimeUtc -gt (Get-Item $efiIn).LastWriteTimeUtc) {
        Write-Host "STALE: $(Split-Path $s -Leaf) is newer than $(Split-Path $efiIn -Leaf). Re-run without -SkipBuild."
        exit 1
    }
}

function New-Image([string]$name, [string]$pe, [string]$source) {
    $out = Join-Path $Work "$name.img"
    $a = @('-PeInput', $pe, '-Out', $out, '-TotalSectors', '32768')
    if ($source) { $a += @('-Source', $source) }
    & pwsh -NoProfile -File $MkImg @a | Out-Null
    if (-not (Test-Path $out)) { throw "build-img produced nothing for $name" }
    # build-img accepts a -Source that does not exist and writes 0 bytes, so a
    # typo in a path silently turns the pass arm into the nosource arm.
    if ($source -and -not (Test-Path -PathType Leaf $source)) { throw "source $source missing for $name" }
    Set-ItemProperty $out -Name IsReadOnly -Value $false
    return $out
}

# Mark every FREE cluster bad in both FAT copies. The BPB field offsets are
# FAT16's, not ours: bytes-per-sector at +11, sectors-per-cluster at +13,
# reserved sectors at +14, FAT count at +16, root entries at +17, total
# sectors at +19 (16-bit) or +32 (32-bit), sectors-per-FAT at +22.
function Set-FatFull([string]$img) {
    $partLba = 2048
    $b = [IO.File]::ReadAllBytes($img)
    $p = $partLba * 512
    $bps      = [BitConverter]::ToUInt16($b, $p + 11)
    $spc      = $b[$p + 13]
    $reserved = [BitConverter]::ToUInt16($b, $p + 14)
    $nFats    = $b[$p + 16]
    $rootEnt  = [BitConverter]::ToUInt16($b, $p + 17)
    $tot16    = [BitConverter]::ToUInt16($b, $p + 19)
    $spf      = [BitConverter]::ToUInt16($b, $p + 22)
    $tot32    = [BitConverter]::ToUInt32($b, $p + 32)
    $total    = if ($tot16 -ne 0) { [int]$tot16 } else { [int]$tot32 }
    if ($bps -ne 512 -or $spc -lt 1 -or $spf -lt 1) { throw "Set-FatFull: BPB does not parse (bps=$bps spc=$spc spf=$spf)" }
    $rootSectors = [int][math]::Ceiling(($rootEnt * 32) / $bps)
    $dataSectors = $total - $reserved - ($nFats * $spf) - $rootSectors
    $clusters    = [int][math]::Floor($dataSectors / $spc)
    $filled = 0
    for ($f = 0; $f -lt $nFats; $f++) {
        $fatBase = $p + ($reserved + $f * $spf) * $bps
        for ($c = 2; $c -lt ($clusters + 2); $c++) {
            $o = $fatBase + $c * 2
            if ($b[$o] -eq 0 -and $b[$o + 1] -eq 0) {
                $b[$o] = 0xF7; $b[$o + 1] = 0xFF
                if ($f -eq 0) { $filled++ }
            }
        }
    }
    [IO.File]::WriteAllBytes($img, $b)
    Write-Host "  [nowrite] $clusters clusters, $filled free marked bad"
    if ($filled -eq 0) { throw "Set-FatFull: nothing was free, the arm would not force anything" }
}

function Invoke-Arm([string]$name, [string]$kernel, [string]$disk, [bool]$roDisk) {
    $err = Join-Path $Work "$name.err"
    $a = @('-kernel', $kernel, '-uefi', '-headless', '-mem', '3072',
           '-output', (Join-Path $Work "$name.vmout"))
    if ($disk) { $a += @('-disk', $disk) }
    if ($roDisk -and $disk) { Set-ItemProperty $disk -Name IsReadOnly -Value $true }
    $p = Start-Process -FilePath $Vm -ArgumentList $a -NoNewWindow -PassThru `
            -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$name.stdout")

    # DO NOT READ THE VM'S STREAMS WHILE IT IS RUNNING. Watching them to stop as
    # soon as a rung reported cost a day on the sink ladder: the pass arm died
    # seconds after the same rung on every polled run, with no fault line in
    # either stream, while by-hand runs of the identical image with no polling
    # reached the last rung. Opening the files share-write was not enough.
    # Wait, then read. See OperatorsManual, "TAILING A LIVE VM LOG".
    #
    # The payload HOLDS its colour by repainting, so a healthy run never exits
    # and -timeout is not reliable. Kill the PID we started; never by name,
    # other agents are running their own VMs.
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 400
    if ($roDisk -and $disk) { Set-ItemProperty $disk -Name IsReadOnly -Value $false }

    # The guest's ConOut echoes to codex-vm's STDERR. Reading only -output is
    # how an earlier harness reported five timeouts against five completed runs.
    # Both are read, so neither stream going quiet silently zeroes the result.
    $text = @()
    foreach ($f in @((Join-Path $Work "$name.vmout"), $err)) {
        if (Test-Path $f) { $text += @(Get-Content $f -ErrorAction SilentlyContinue) }
    }
    # The last stage that PASSED, not the last that painted: a failed stage
    # repaints the previous colour, so its own tag names a rung the screen is
    # not showing.
    $passed = @($text | Select-String -Pattern 'DISK-LADDER \S+ ok=1' | ForEach-Object { $_.Line })
    $failed = @($text | Select-String -Pattern 'DISK-LADDER \S+ ok=0' | ForEach-Object { $_.Line })
    if ($passed.Count -eq 0) { return '(nothing painted)' }
    $last = (($passed[-1] -split 'DISK-LADDER ')[-1] -replace ' ok=.*$', '')
    # A run that neither failed a rung nor reached the last one is a DEADLINE,
    # not a verdict, and it reports the same last-passed rung a legitimately
    # failing arm does.
    if ($failed.Count -eq 0 -and $last -ne 'wrote' -and $name -ne 'badsource') {
        return "(no verdict in ${Seconds}s: unfinished after '$last')"
    }
    return $last
}

# The source rung paints silently, so 'source' never appears in the trace on
# the way through. The two arms that stop there are named by what did NOT
# happen next: badsource prints CODEGEN-HALTED, nowrite prints DISK-OUT FAILED.
function Read-Halt([string]$name, [string]$pattern) {
    foreach ($f in @((Join-Path $Work "$name.vmout"), (Join-Path $Work "$name.err"))) {
        if ((Test-Path $f) -and (Select-String -Path $f -Pattern $pattern -Quiet)) { return $true }
    }
    return $false
}

$expected = [ordered]@{
    pass      = 'wrote'
    nomode    = 'entered'
    badbpb    = 'mode'
    nosource  = 'volume'
    badsource = 'source'
    nowrite   = 'compiled'
}
$actual = [ordered]@{}
$names  = if ($Only) { @($Only) } else { @($expected.Keys) }
foreach ($n in $names) { if (-not $expected.Contains($n)) { Write-Host "FAIL: no arm '$n'"; exit 1 } }

foreach ($name in $names) {
    Write-Host "[disk-arm] $name..."
    switch ($name) {
        'pass' {
            $k = New-Image 'pass' $efiIn $goodSrc
            $actual['pass'] = Invoke-Arm 'pass' $k $k $false
        }
        'nomode' {
            $k = New-Image 'nomode' $efiNoIn $goodSrc
            $actual['nomode'] = Invoke-Arm 'nomode' $k $k $false
        }
        'badbpb' {
            # The BAD volume goes in as -disk only: codex-vm reads the BPB of
            # -kernel to find BOOTX64.EFI, so corrupting the one it boots from
            # would stop the guest before it starts.
            $k = New-Image 'badbpb-k' $efiIn $goodSrc
            $d = New-Image 'badbpb-d' $efiIn $goodSrc
            $bytes = [IO.File]::ReadAllBytes($d)
            $bytes[2048 * 512 + 11] = 0
            $bytes[2048 * 512 + 12] = 0
            [IO.File]::WriteAllBytes($d, $bytes)
            $actual['badbpb'] = Invoke-Arm 'badbpb' $k $d $false
        }
        'nosource' {
            $k = New-Image 'nosource' $efiIn ''
            $actual['nosource'] = Invoke-Arm 'nosource' $k $k $false
        }
        'badsource' {
            $k = New-Image 'badsource' $efiIn $badSrc
            $r = Invoke-Arm 'badsource' $k $k $false
            $actual['badsource'] = if (Read-Halt 'badsource' 'CODEGEN-HALTED') { 'source' } else { $r }
        }
        'nowrite' {
            # Every free cluster marked bad, so the allocator has nowhere to put
            # OUT.CDX. The compile itself is untouched, which is what makes this
            # arm stop at BLUE and not earlier.
            #
            # A READ-ONLY MEDIUM DOES NOT FORCE THIS, and that was the first
            # version. codex-vm serves the guest from an in-memory image and
            # only flushes to the host file, so 833 `WARN: cannot reopen disk
            # ... for write` lines went past while the guest wrote, re-read and
            # verified OUT.CDX at 84,561 bytes and painted WHITE. The medium the
            # guest sees was never read-only.
            $k = New-Image 'nowrite' $efiIn $goodSrc
            Set-FatFull $k
            $r = Invoke-Arm 'nowrite' $k $k $false
            $actual['nowrite'] = if (Read-Halt 'nowrite' 'DISK-OUT: FAILED') { 'compiled' } else { $r }
        }
    }
}

$bad = 0
Write-Host ''
Write-Host 'arm        expected   actual'
Write-Host '---------  ---------  ---------'
foreach ($name in $names) {
    $e = $expected[$name]; $a = $actual[$name]
    if ($a -ne $e) { $bad++ }
    $mark = if ($a -eq $e) { '' } else { '   <-- MISMATCH' }
    Write-Host ("{0,-10} {1,-10} {2}{3}" -f $name, $e, $a, $mark)
}
Write-Host ''
if (-not $Keep) { Remove-Item (Join-Path $Work '*.img') -Force -ErrorAction SilentlyContinue }
if ($bad -gt 0) { Write-Host "DISK LADDER NOT CALIBRATED: $bad arm(s) disagree"; exit 1 }
Write-Host 'Disk ladder calibrated: every arm painted the stage it should have.'
exit 0
