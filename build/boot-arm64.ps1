# boot-arm64.ps1 -- End-to-end ARM64 UEFI boot pipeline
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Src,
    [switch]$NoBoot,
    [int]$MemMB = 4096,
    # Bound the QEMU run so this script can be an ARM. 0, the default, keeps
    # the interactive behaviour exactly: a foreground -nographic console with no
    # deadline, which is what a person driving it by hand wants. Any positive
    # value launches QEMU monitored, kills it at the deadline, sends the UART to
    # a file, and REPORTS whether it exited on its own or was killed.
    [int]$TimeoutSec = 0,
    # Override the derived host-forward port. 0 derives it from the workspace
    # name, and a port already held is a refusal rather than a silent hop.
    [int]$HostPort = 0
)

# Pipeline: source.codex -> IR -> ARM64 codegen -> PE plug (mode 2) -> GPT disk -> QEMU
# 
# Usage:
#   build/boot-arm64.ps1 -Src <source.codex>
#   build/boot-arm64.ps1 -Src <source.codex> -NoBoot   # build only, no QEMU
# 
# Prerequisites:
#   - ARM64 plug built:  codex/plugs/arm64/build.ps1
#   - PE plug built:     codex/plugs/pe/build.ps1


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $Repo 'build' 'vm-config.ps1')

$OutDir = Join-Path $Repo 'build' 'arm64-output'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$Arm64PlugCdx = Join-Path $Repo 'codex\plugs\arm64\build-output\arm64-plug.cdx'
$PePlugCdx = Join-Path $Repo 'codex\plugs\pe\build-output\pe-plug.cdx'
$QemuBin = 'D:\Program Files\qemu\qemu-system-aarch64.exe'
$UefiFw = 'D:\Program Files\qemu\share\edk2-aarch64-code.fd'

if ((-not (Test-Path -PathType Leaf $Arm64PlugCdx))) {
    throw "ARM64 plug not built. Run: codex/plugs/arm64/build.ps1"
}
if ((-not (Test-Path -PathType Leaf $PePlugCdx))) {
    throw "PE plug not built. Run: codex/plugs/pe/build.ps1"
}


# --- Phase 1: Compile source to IR ---
$IrFile = Join-Path $OutDir 'arm64.ir'
$LogFile = Join-Path $OutDir 'compile-ir.log'
Write-Host "[boot-arm64] Phase 1: Compiling $Src to IR..."
& pwsh -NoProfile -File (Join-Path $Repo 'build' 'compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -MemMB $MemMB
if ((-not ($LASTEXITCODE -eq 0))) {
    throw "IR compile failed (exit $LASTEXITCODE); see $LogFile"
}


# --- Phase 2: ARM64 codegen via plug ---
$WireFile = Join-Path $OutDir 'arm64.wire.bin'
Write-Host '[boot-arm64] Phase 2: ARM64 codegen...'
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\arm64\run.ps1') -IrInput $IrFile -Out $WireFile
if ((-not ($LASTEXITCODE -eq 0))) {
    throw "ARM64 codegen failed (exit $LASTEXITCODE)"
}


Write-Host '[boot-arm64] Phase 3: Building ARM64 PE via plug (mode 2)...'
# Skip serial preamble (find wire header)
$wireBytes = [System.IO.File]::ReadAllBytes($WireFile)
$wireOff = 0
for ($wi = 0; $wi -lt [Math]::Min(64, $wireBytes.Length - 12); $wi++) { $cl = [BitConverter]::ToInt32($wireBytes, $wi); $dl = [BitConverter]::ToInt32($wireBytes, $wi + 4); $fc = [BitConverter]::ToInt32($wireBytes, $wi + 8); if ($cl -gt 0 -and $cl -lt 1000000 -and $dl -ge 0 -and $dl -lt 100000 -and $fc -gt 0 -and $fc -lt 10000) { $wireOff = $wi; break } }
$cleanWire = New-Object byte[] ($wireBytes.Length - $wireOff)
[Array]::Copy($wireBytes, $wireOff, $cleanWire, 0, $cleanWire.Length)

# Build payload: [mode=2] [wire bytes]
$payload = New-Object byte[] (1 + $cleanWire.Length)
$payload[0] = 2
[Array]::Copy($cleanWire, 0, $payload, 1, $cleanWire.Length)

try {
    # Start TCP listener
    $plugPort = 9128
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
    $listener.Start()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PePlugCdx, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $listener.Pending()) { if ([DateTime]::UtcNow -gt $deadline) { throw 'PE plug did not connect within 30s' }; Start-Sleep -Milliseconds 50 }
    $tcpClient = $listener.AcceptTcpClient()
    $tcpStream = $tcpClient.GetStream()
    $listener.Stop()
    Write-Host "[boot-arm64] PE plug connected, sending $($payload.Length) bytes..."

    # Send framed message (tag=4)
    $msgLen = $payload.Length + 1
    $tcpStream.Write([BitConverter]::GetBytes([int]$msgLen), 0, 4)
    $tcpStream.WriteByte(4)
    $tcpStream.Write($payload, 0, $payload.Length)
    $tcpStream.Flush()
    $tcpStream.ReadTimeout = 60000
    # A per-byte accumulate here costs 58.33 s per 8 MB against 0.015 s for
    # the bulk write. It is overlapped with the guest, so it buys a core and
    # not a second: at -Jobs 8 that is contention, not a faster single run.
    $allBytes = [System.IO.MemoryStream]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try { while ($true) { $n = $tcpStream.Read($readBuf, 0, $readBuf.Length); if ($n -le 0) { break }; $allBytes.Write($readBuf, 0, $n) } } catch {}
    if ($allBytes.Length -lt 512) {
        throw "PE output too small ($($allBytes.Length) bytes)"
    }

    # Fix inflated PE headers from plug bug with large binaries
    $peArr = $allBytes.ToArray()
    $maxExpected = $cleanWire.Length * 4
    if ($allBytes.Length -gt $maxExpected) {
        Write-Host "[boot-arm64] PE inflated ($($allBytes.Length) > $maxExpected), fixing headers..."
        $stubEnd = 512; for ($si = 512; $si -lt [Math]::Min($peArr.Length, 4096); $si += 4) { if ($peArr[$si] -eq $cleanWire[12] -and $peArr[$si+1] -eq $cleanWire[13] -and $peArr[$si+2] -eq $cleanWire[14] -and $peArr[$si+3] -eq $cleanWire[15]) { $stubEnd = $si; break } }
        $stubSize = $stubEnd - 512; $codeLen = [BitConverter]::ToInt32($cleanWire, 0); $dataLen = [BitConverter]::ToInt32($cleanWire, 4); $totalCode = $stubSize + $codeLen + $dataLen
        $codeRaw = (($totalCode + 511) -band (-bnot 511)); $codeSec = (($totalCode + 4095) -band (-bnot 4095)); $relocRva = 4096 + $codeSec; $imageSize = (($relocRva + 4096 + 4095) -band (-bnot 4095))
        [BitConverter]::GetBytes([int]$codeRaw).CopyTo($peArr, 128 + 28); [BitConverter]::GetBytes([int]$imageSize).CopyTo($peArr, 128 + 80)
        $secOff = 128 + 24 + 240; [BitConverter]::GetBytes([int]$totalCode).CopyTo($peArr, $secOff + 8); [BitConverter]::GetBytes([int]$codeRaw).CopyTo($peArr, $secOff + 16)
        $sec2Off = $secOff + 40; [BitConverter]::GetBytes([int]$relocRva).CopyTo($peArr, $sec2Off + 12); [BitConverter]::GetBytes([int]($codeRaw + 512)).CopyTo($peArr, $sec2Off + 20)
        [BitConverter]::GetBytes([int]$relocRva).CopyTo($peArr, 128 + 24 + 152)
        $fixedSize = 512 + $codeRaw + 512; $fixedPe = New-Object byte[] $fixedSize; $copyLen = [Math]::Min(512 + $totalCode, $peArr.Length); [Array]::Copy($peArr, 0, $fixedPe, 0, $copyLen)
        $fixedPe[$codeRaw + 512 + 4] = 8; $peArr = $fixedPe
        Write-Host "[boot-arm64] Fixed: stub=$stubSize code=$codeLen data=$dataLen total=$totalCode pe=$fixedSize"
    }

    $PeFile = Join-Path $OutDir 'BOOTAA64.EFI'
    [System.IO.File]::WriteAllBytes($PeFile, $peArr)
    Write-Host "[boot-arm64] PE: $PeFile ($($peArr.Length) bytes)"
    $tcpClient.Close()

} finally {
    if (($proc -and (-not $proc.HasExited))) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force -ErrorAction SilentlyContinue $stderrFile
}


# --- Phase 4: Build GPT disk image ---
$ImgFile = Join-Path $OutDir 'codex-arm64.img'
Write-Host '[boot-arm64] Phase 4: Building GPT disk image...'
& pwsh -NoProfile -File (Join-Path $Repo 'build' 'build-arm64-img.ps1') -PeInput $PeFile -Out $ImgFile
if ((-not ($LASTEXITCODE -eq 0))) {
    throw "Disk image build failed (exit $LASTEXITCODE)"
}


if ($NoBoot) {
    Write-Host "[boot-arm64] Done (NoBoot). Image: $ImgFile"
    exit 0
}


# --- Phase 5: Boot in QEMU ---
Write-Host '[boot-arm64] Phase 5: Booting in QEMU aarch64...'
if (($TimeoutSec -lt 1)) {
    Write-Host '[boot-arm64] UART output on serial console. Press Ctrl+C to stop.'
}
# Create varstore if it does not exist (64 MB, zeroed)
$VarStore = Join-Path $OutDir 'efi-varstore.img'
if ((-not (Test-Path -PathType Leaf $VarStore))) {
    $fs = [System.IO.File]::Create($VarStore); $fs.SetLength(64 * 1024 * 1024); $fs.Close()
    Write-Host "[boot-arm64] Created EFI varstore: $VarStore"
}

# The hostfwd was a fixed 8080. Two agents booting arm64 on this box forwarded
# the same host port, so whichever guest bound it first answered the other
# agent's requests -- the same class as the fixed monitor port that
# build/boot/test-ovmf.ps1 derives per workspace for exactly this reason
# (L-SHARED). Derived here in 18080..18279 from the workspace name.
# 
# A HELD port is a REFUSAL rather than a hop to the next free number. A run
# that quietly moves its port answers on an address the caller was never told,
# which is the same failure one step later.
$fwdPort = $HostPort
if ($fwdPort -eq 0) {
    $tag = (Split-Path $Repo -Leaf) -replace '[^A-Za-z0-9]',''
    $portHash = 0
    foreach ($ch in $tag.ToCharArray()) { $portHash = ($portHash * 31 + [int]$ch) % 200 }
    $fwdPort = 18080 + $portHash
    $held = @(Get-NetTCPConnection -LocalPort $fwdPort -State Listen -ErrorAction SilentlyContinue)
    if ($held.Count -gt 0) {
        throw "hostfwd port $fwdPort (workspace '$tag') is already listening, PID $($held[0].OwningProcess). An arm64 boot from THIS workspace is still alive; refusing to start, because its guest would answer requests meant for this run."
    }
}
Write-Host "[boot-arm64] hostfwd tcp::$fwdPort -> guest :80"
# The firmware path contains a space, and Start-Process does not re-quote a
# space inside a single argument token, so a -drive whose file= sits under
# 'Program Files' reaches QEMU split and it dies on 'Could not open D:\Program'.
# The foreground `& $exe @args` call quotes per element and never met this; the
# -TimeoutSec path below uses Start-Process and does. Copy to a space-free
# path, which is what build/boot/test-ovmf.ps1 already does for the same reason.
$FwCopy = Join-Path $env:TEMP ("arm64-code-" + ((Split-Path $Repo -Leaf) -replace '[^A-Za-z0-9]','') + ".fd")
if ((-not (Test-Path $FwCopy)) -or (Get-Item $FwCopy).Length -ne (Get-Item $UefiFw).Length) { Copy-Item -Force $UefiFw $FwCopy }
$qemuArgs = @('-machine', 'virt,gic-version=3', '-cpu', 'cortex-a72', '-m', '1024', '-drive', "if=pflash,format=raw,file=$FwCopy,readonly=on", '-drive', "if=pflash,format=raw,file=$VarStore", '-drive', "file=$ImgFile,format=raw,if=virtio", '-device', 'virtio-net-pci,netdev=net0,mac=52:54:00:12:34:56', '-netdev', "user,id=net0,hostfwd=tcp::$fwdPort-:80")
if ($TimeoutSec -le 0) {
    & $QemuBin @($qemuArgs + '-nographic')
} else {
    # A bounded run has to be READABLE or the bound buys nothing: -nographic
    # puts the UART on a stdout that Start-Process discards, so serial goes to
    # a file and the path is printed.
    $SerialLog = Join-Path $OutDir 'arm64-serial.log'
    Remove-Item $SerialLog -Force -ErrorAction SilentlyContinue
    $qproc = Start-Process -FilePath $QemuBin -ArgumentList @($qemuArgs + @('-display', 'none', '-serial', "file:$SerialLog")) -PassThru -WindowStyle Hidden
    Write-Host "[boot-arm64] QEMU pid $($qproc.Id), deadline ${TimeoutSec}s, serial -> $SerialLog"
    $qproc.WaitForExit($TimeoutSec * 1000)
    if ($qproc.HasExited) {
        Write-Host "[boot-arm64] QEMU exited on its own, code $($qproc.ExitCode)"
    } else {
        Stop-Process -Id $qproc.Id -Force -ErrorAction SilentlyContinue
        # An unfinished run is not a verdict, so the report says which this was.
        Write-Host "[boot-arm64] TIMEOUT at ${TimeoutSec}s: QEMU was killed and the run did NOT finish. Treat the serial log as a partial."
    }
}
