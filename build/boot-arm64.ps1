# End-to-end ARM64 UEFI boot pipeline.
#
# Pipeline: source.codex -> IR -> ARM64 codegen -> PE plug (mode 2) -> GPT disk -> QEMU
#
# Usage:
#   build/boot-arm64.ps1 -Src <source.codex>
#   build/boot-arm64.ps1 -Src <source.codex> -NoBoot   # build only, no QEMU
#
# Prerequisites:
#   - ARM64 plug built:  codex/plugs/arm64/build.ps1
#   - PE plug built:     codex/plugs/pe/build.ps1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [switch]$NoBoot,
    [int]$MemMB = 4096
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $Repo 'build' 'vm-config.ps1')

$OutDir = Join-Path $Repo 'build' 'arm64-output'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Arm64PlugCdx = Join-Path $Repo 'codex\plugs\arm64\build-output\arm64-plug.cdx'
$PePlugCdx    = Join-Path $Repo 'codex\plugs\pe\build-output\pe-plug.cdx'
$QemuBin      = 'D:\Program Files\qemu\qemu-system-aarch64.exe'
$UefiFw       = 'D:\Program Files\qemu\share\edk2-aarch64-code.fd'

if (-not (Test-Path $Arm64PlugCdx)) { throw "ARM64 plug not built. Run: codex/plugs/arm64/build.ps1" }
if (-not (Test-Path $PePlugCdx))    { throw "PE plug not built. Run: codex/plugs/pe/build.ps1" }

# --- Phase 1: Compile source to IR ---
$IrFile  = Join-Path $OutDir 'arm64.ir'
$LogFile = Join-Path $OutDir 'compile-ir.log'
Write-Host "[boot-arm64] Phase 1: Compiling $Src to IR..."
& pwsh -NoProfile -File (Join-Path $Repo 'build' 'compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce -MemMB $MemMB
if ($LASTEXITCODE -ne 0) { throw "IR compile failed (exit $LASTEXITCODE); see $LogFile" }

# --- Phase 2: ARM64 codegen via plug ---
$WireFile = Join-Path $OutDir 'arm64.wire.bin'
Write-Host "[boot-arm64] Phase 2: ARM64 codegen..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\arm64\run.ps1') -IrInput $IrFile -Out $WireFile
if ($LASTEXITCODE -ne 0) { throw "ARM64 codegen failed (exit $LASTEXITCODE)" }

# --- Phase 3: PE plug (mode 2 = ARM64) ---
$PeFile = Join-Path $OutDir 'BOOTAA64.EFI'
Write-Host "[boot-arm64] Phase 3: Building ARM64 PE via plug (mode 2)..."

$wireBytes = [System.IO.File]::ReadAllBytes($WireFile)

# Skip serial preamble (find wire header)
$wireOff = 0
for ($wi = 0; $wi -lt [Math]::Min(64, $wireBytes.Length - 12); $wi++) {
    $cl = [BitConverter]::ToInt32($wireBytes, $wi)
    $dl = [BitConverter]::ToInt32($wireBytes, $wi + 4)
    $fc = [BitConverter]::ToInt32($wireBytes, $wi + 8)
    if ($cl -gt 0 -and $cl -lt 1000000 -and $dl -ge 0 -and $dl -lt 100000 -and $fc -gt 0 -and $fc -lt 10000) {
        $wireOff = $wi; break
    }
}
$cleanWire = New-Object byte[] ($wireBytes.Length - $wireOff)
[Array]::Copy($wireBytes, $wireOff, $cleanWire, 0, $cleanWire.Length)

# Build payload: [mode=2] [wire bytes]
$payload = New-Object byte[] (1 + $cleanWire.Length)
$payload[0] = 2
[Array]::Copy($cleanWire, 0, $payload, 1, $cleanWire.Length)

# Start TCP listener
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()

$stderrFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @('-kernel', $PePlugCdx, '-mem', '3072', '-headless') `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $listener.Pending()) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "PE plug did not connect within 30s" }
        Start-Sleep -Milliseconds 50
    }
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

    # Receive PE output — keep reading until connection closes
    $tcpStream.ReadTimeout = 60000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}

    if ($allBytes.Count -lt 512) { throw "PE output too small ($($allBytes.Count) bytes)" }
    $peArr = $allBytes.ToArray()
    # Fix inflated PE headers from plug bug with large binaries
    $maxExpected = $cleanWire.Length * 4
    if ($allBytes.Count -gt $maxExpected) {
        Write-Host "[boot-arm64] PE inflated ($($allBytes.Count) > $maxExpected), fixing headers..."
        # Find stub end by locating first wire code bytes in PE
        $stubEnd = 512
        for ($si = 512; $si -lt [Math]::Min($peArr.Length, 4096); $si += 4) {
            if ($peArr[$si] -eq $cleanWire[12] -and $peArr[$si+1] -eq $cleanWire[13] -and
                $peArr[$si+2] -eq $cleanWire[14] -and $peArr[$si+3] -eq $cleanWire[15]) {
                $stubEnd = $si; break
            }
        }
        $stubSize = $stubEnd - 512
        $codeLen = [BitConverter]::ToInt32($cleanWire, 0)
        $dataLen = [BitConverter]::ToInt32($cleanWire, 4)
        $totalCode = $stubSize + $codeLen + $dataLen
        $codeRaw = (($totalCode + 511) -band (-bnot 511))
        $codeSec = (($totalCode + 4095) -band (-bnot 4095))
        $relocRva = 4096 + $codeSec
        $imageSize = (($relocRva + 4096 + 4095) -band (-bnot 4095))
        # Patch PE headers
        [BitConverter]::GetBytes([int]$codeRaw).CopyTo($peArr, 128 + 28)  # SizeOfCode
        [BitConverter]::GetBytes([int]$imageSize).CopyTo($peArr, 128 + 80)  # SizeOfImage
        $secOff = 128 + 24 + 240  # .text section header
        [BitConverter]::GetBytes([int]$totalCode).CopyTo($peArr, $secOff + 8)  # VirtualSize
        [BitConverter]::GetBytes([int]$codeRaw).CopyTo($peArr, $secOff + 16)  # SizeOfRawData
        $sec2Off = $secOff + 40  # .reloc section header
        [BitConverter]::GetBytes([int]$relocRva).CopyTo($peArr, $sec2Off + 12)  # VirtualAddress
        [BitConverter]::GetBytes([int]($codeRaw + 512)).CopyTo($peArr, $sec2Off + 20)  # PointerToRawData
        # Also fix reloc RVA in optional header data directories
        [BitConverter]::GetBytes([int]$relocRva).CopyTo($peArr, 128 + 24 + 152)  # BaseReloc RVA
        # Build correct-sized output: headers + code + pad + reloc + pad
        $fixedSize = 512 + $codeRaw + 512
        $fixedPe = New-Object byte[] $fixedSize
        $copyLen = [Math]::Min(512 + $totalCode, $peArr.Length)
        [Array]::Copy($peArr, 0, $fixedPe, 0, $copyLen)
        # Write reloc dummy (8 bytes of zeros) at reloc section
        $fixedPe[$codeRaw + 512 + 4] = 8  # reloc block size = 8
        $peArr = $fixedPe
        Write-Host "[boot-arm64] Fixed: stub=$stubSize code=$codeLen data=$dataLen total=$totalCode pe=$fixedSize"
    }
    [System.IO.File]::WriteAllBytes($PeFile, $peArr)
    Write-Host "[boot-arm64] PE: $PeFile ($($peArr.Length) bytes)"

    $tcpClient.Close()
} finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
}

# --- Phase 4: Build GPT disk image ---
$ImgFile = Join-Path $OutDir 'codex-arm64.img'
Write-Host "[boot-arm64] Phase 4: Building GPT disk image..."
& pwsh -NoProfile -File (Join-Path $Repo 'build' 'build-arm64-img.ps1') -PeInput $PeFile -Out $ImgFile
if ($LASTEXITCODE -ne 0) { throw "Disk image build failed (exit $LASTEXITCODE)" }

if ($NoBoot) {
    Write-Host "[boot-arm64] Done (NoBoot). Image: $ImgFile"
    exit 0
}

# --- Phase 5: Boot in QEMU ---
Write-Host "[boot-arm64] Phase 5: Booting in QEMU aarch64..."
Write-Host "[boot-arm64] UART output on serial console. Press Ctrl+C to stop."

# Create varstore if it doesn't exist (64 MB, zeroed)
$VarStore = Join-Path $OutDir 'efi-varstore.img'
if (-not (Test-Path $VarStore)) {
    $fs = [System.IO.File]::Create($VarStore)
    $fs.SetLength(64 * 1024 * 1024)
    $fs.Close()
    Write-Host "[boot-arm64] Created EFI varstore: $VarStore"
}

$qemuArgs = @(
    '-machine', 'virt,gic-version=3',
    '-cpu', 'cortex-a72',
    '-m', '1024',
    '-drive', "if=pflash,format=raw,file=$UefiFw,readonly=on",
    '-drive', "if=pflash,format=raw,file=$VarStore",
    '-drive', "file=$ImgFile,format=raw,if=virtio",
    '-device', 'virtio-net-pci,netdev=net0,mac=52:54:00:12:34:56',
    '-netdev', 'user,id=net0,hostfwd=tcp::8080-:80',
    '-nographic'
)

& $QemuBin @qemuArgs
