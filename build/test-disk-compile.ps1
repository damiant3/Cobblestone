# test-disk-compile.ps1 -- Test DISK compile mode end-to-end
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$SampleSrc = 'codex\test\field-range-proven.codex',
    [string]$Expected = '12',
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$root = Split-Path $PSScriptRoot
$outDir = Join-Path $root 'build-output\disk-compile-test'
if ((-not (Test-Path -PathType Container $outDir))) {
    New-Item -ItemType Directory -Force $outDir | Out-Null
}
$compile = Join-Path $PSScriptRoot 'compile.ps1'
$runScript = Join-Path $PSScriptRoot 'test-run.ps1'
Write-Host '=== DISK compile test ===' -ForegroundColor Cyan


function Export-Fat16File {
    param([string]$Image, [string]$Name, [string]$Out)
    $fs = [System.IO.File]::OpenRead($Image)
    try {
        $bpb = New-Object byte[] 512
        $fs.Seek((2048 * 512), 'Begin') | Out-Null
        if ($fs.Read($bpb, 0, 512) -ne 512) { throw 'FAT16: cannot read the BPB at LBA 2048' }
        if (($bpb[510] -ne 85) -or ($bpb[511] -ne 170)) { throw 'FAT16: no boot signature at LBA 2048' }
        $bps = [BitConverter]::ToUInt16($bpb, 11)
        $spc = $bpb[13]
        $rsvd = [BitConverter]::ToUInt16($bpb, 14)
        $nfat = $bpb[16]
        $rent = [BitConverter]::ToUInt16($bpb, 17)
        $spf = [BitConverter]::ToUInt16($bpb, 22)
        if (($bps -le 0) -or ($spc -le 0) -or ($spf -le 0) -or ($nfat -le 0)) { throw "FAT16: implausible BPB bps=$bps spc=$spc spf=$spf nfat=$nfat" }
        $fatSec = 2048 + $rsvd
        $rootSec = $fatSec + ($nfat * $spf)
        $rootSecs = [int][Math]::Ceiling(($rent * 32) / $bps)
        $dataSec = $rootSec + $rootSecs
        $rd = New-Object byte[] ($rootSecs * $bps)
        $fs.Seek(($rootSec * $bps), 'Begin') | Out-Null
        $fs.Read($rd, 0, $rd.Length) | Out-Null
        $clus = 0
        $size = 0
        for ($e = 0; $e -lt $rent; $e++) {
            $o = $e * 32
            if ($rd[$o] -eq 0) { break }
            if (($rd[$o] -eq 229) -or ($rd[$o + 11] -eq 15)) { continue }
            if ([System.Text.Encoding]::ASCII.GetString($rd, $o, 11) -eq $Name) {
                $clus = [BitConverter]::ToUInt16($rd, $o + 26)
                $size = [BitConverter]::ToUInt32($rd, $o + 28)
                break
            }
        }
        if ($clus -lt 2) { throw "FAT16: $Name is not in the root directory" }
        $fat = New-Object byte[] ($spf * $bps)
        $fs.Seek(($fatSec * $bps), 'Begin') | Out-Null
        $fs.Read($fat, 0, $fat.Length) | Out-Null
        $bytes = New-Object byte[] $size
        $got = 0
        $cl = $clus
        while ((($got -lt $size) -and ($cl -ge 2)) -and ($cl -lt 65528)) {
            $take = [Math]::Min(($spc * $bps), ($size - $got))
            $fs.Seek((($dataSec + ($cl - 2) * $spc) * $bps), 'Begin') | Out-Null
            if ($fs.Read($bytes, $got, $take) -ne $take) { throw "FAT16: short read in $Name at cluster $cl" }
            $got += $take
            $cl = [BitConverter]::ToUInt16($fat, $cl * 2)
        }
        if ($got -ne $size) { throw "FAT16: $Name chain yielded $got of $size bytes" }
        [System.IO.File]::WriteAllBytes($Out, $bytes)
        return $size
    } finally {
        $fs.Close()
    }
}


Write-Host 'Step 1a: Compile source to CDX...'
$imgOut = Join-Path $outDir 'disk-test.img'
$cdxOut = Join-Path $outDir 'disk-test.cdx'
$cdxLog = Join-Path $outDir 'cdx-build.log'
& pwsh -NoProfile -File $compile -Src $SampleSrc -Out $cdxOut -Log $cdxLog -PCore $PCore
if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $cdxOut)))) {
    Write-Host 'FAIL: CDX compile failed' -ForegroundColor Red
    if ((Test-Path -PathType Leaf $cdxLog)) {
        Get-Content $cdxLog | Write-Host
    }
    exit 1
}


Write-Host 'Step 1b: CDX -> PE via plug...'
$peOut = Join-Path $outDir 'disk-test.efi'
$pePlug = Join-Path $root 'codex\plugs\pe\run.ps1'
& pwsh -NoProfile -File $pePlug -CdxInput $cdxOut -Out $peOut
if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $peOut)))) {
    Write-Host 'FAIL: PE plug failed'
    exit 1
}


Write-Host 'Step 1c: PE + CDX -> FAT16 IMG via plug...'
$imgPlug = Join-Path $root 'codex\plugs\img\run.ps1'
& pwsh -NoProfile -File $imgPlug -PeInput $peOut -CdxInput $cdxOut -Out $imgOut -Fat16 -Source $SampleSrc
if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $imgOut)))) {
    Write-Host 'FAIL: IMG plug failed'
    exit 1
}
Write-Host "  IMG: $((Get-Item $imgOut).Length) bytes"


Write-Host 'Step 2: DISK compile...'
$Stage0 = Join-Path $root 'build-output\bare-metal\Codex.cdx'
if ((-not (Test-Path -PathType Leaf $Stage0))) {
    Write-Host "FAIL: no compiler CDX at $Stage0"
    exit 1
}
$diskArgs = @('-drive', "file=$imgOut,format=raw,if=ide,index=0")
$run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 10 -MemMB 3072 -PCore $PCore -ExtraArgs $diskArgs
if ((-not $run)) {
    Write-Host 'FAIL: VM did not start'
    exit 1
}


try {
    if ((-not (Read-VmReady -Conn $run.Conn -TimeoutSec 30))) {
        Write-Host 'FAIL: no READY'
        exit 1
    }


    $stream = $run.Conn.Data.GetStream()
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("DISK`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $path = [System.Text.Encoding]::UTF8.GetBytes("SOURCE.SRC`n")
    $stream.Write($path, 0, $path.Length)
    $stream.Flush()
    $binSize = 0
    $status = ''
    $logLines = @()


    :read_loop while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if ($null -eq $line) {
            break
        }
        $logLines += $line
        if ($line.StartsWith('SIZE:')) {
            if (($line -match '^\D*(\d+)')) {
                $binSize = [int]$matches[1]
            }
            $status = 'size'
        }
        if ($line.StartsWith('DISK-OUT:')) {
            $status = 'disk-out'
            break
        }
        if ($line.StartsWith('CODEGEN-HALTED')) {
            $status = 'error'
            break
        }
        if ($line.StartsWith('CODEGEN-ERRORS')) {
            $status = 'error'
        }
        if ($logLines.Count -ge 400) {
            break
        }
    }


    if ((($status -ne 'disk-out') -or $binSize -le 0)) {
        Write-Host "FAIL: DISK compile failed (status=$status)" -ForegroundColor Red
        foreach ($ll in $logLines) {
            Write-Host "  $ll"
        }
        exit 1
    }
    Write-Host "  Compiled: $binSize bytes"
    foreach ($ll in $logLines) {
        if ($ll.StartsWith('DISK-OUT:')) {
            Write-Host "  $ll"
        }
    }

} finally {
    Close-Vm -Conn $run.Conn -Process $run.Process
}


Write-Host 'Step 2b: Extract OUT.CDX from the image...'
$cdxOut = Join-Path $outDir 'disk-compiled.cdx'
$extracted = Export-Fat16File -Image $imgOut -Name 'OUT     CDX' -Out $cdxOut
if ($extracted -ne $binSize) {
    Write-Host "FAIL: OUT.CDX is $extracted bytes, the guest declared $binSize" -ForegroundColor Red
    exit 1
}
Write-Host "  Written: $cdxOut ($extracted bytes)"


Write-Host 'Step 3: Run compiled CDX...'
$actual = Join-Path $outDir 'runtime.actual'
& pwsh -NoProfile -File $runScript -Kernel $cdxOut -OutFile $actual -PCore $PCore
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: runtime failed'
    exit 1
}
$output = if (Test-Path $actual) { (Get-Content -Raw $actual).TrimEnd() } else { '' }
Write-Host "  Output: '$output'"
Write-Host "  Expected: '$Expected'"
if (($output -eq $Expected)) {
    Write-Host 'PASS: DISK compile mode works' -ForegroundColor Green
} else {
    Write-Host 'FAIL: output mismatch' -ForegroundColor Red
    exit 1
}
