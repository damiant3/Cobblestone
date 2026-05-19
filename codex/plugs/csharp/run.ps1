# Run the C# plug over a Codex source file and capture C# output.
#
#   <Codex source.codex>
#     │
#     ▼  build/test-compile.ps1 -Ir
#   <IR S-expression text>
#     │
#     ▼  csharp-plug.cdx (booted in QEMU, IR text on stdin via CCE)
#   <C# source on stdout via CCE>
#
# Usage:
#   plugs/csharp/run.ps1 -Src <source.codex> -Out <out.cs>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' 'codex.build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\csharp-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx — run plugs/csharp/build.ps1 first")
    exit 2
}

# ── CCE encode/decode tables ───────────────────────────────────────────
$script:CceToUnicode = @(
    0, 10, 32,
    48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
    101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
    108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
    118, 107, 106, 120, 113, 122,
    69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
    76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
    86, 75, 74, 88, 81, 90,
    46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
    43, 61, 42, 60, 62,
    47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
    94,
    36, 37,
    233, 232, 234, 235, 225, 224, 226, 228,
    243, 244, 246, 250, 252, 241, 231, 237
)

$script:UnicodeToCce = [byte[]]::new(256)
for ($i = 0; $i -lt 256; $i++) { $script:UnicodeToCce[$i] = 68 }
for ($i = 0; $i -lt $script:CceToUnicode.Length; $i++) {
    $u = $script:CceToUnicode[$i] % 256
    $script:UnicodeToCce[$u] = [byte]$i
}

function ConvertTo-Cce {
    param([byte[]]$Bytes)
    $out = [byte[]]::new($Bytes.Length)
    for ($i = 0; $i -lt $Bytes.Length; $i++) {
        $out[$i] = $script:UnicodeToCce[$Bytes[$i]]
    }
    return $out
}

function Read-CceStreamLine {
    param([System.IO.Stream]$Stream, [int]$TimeoutSec = 60)
    $buf = [System.Collections.Generic.List[byte]]::new()
    $one = [byte[]]::new(1)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $remainMs = [int][math]::Max(100, ($deadline - (Get-Date)).TotalMilliseconds)
        if ($Stream.CanTimeout) { $Stream.ReadTimeout = $remainMs }
        try { $n = $Stream.Read($one, 0, 1) } catch { return $null }
        if ($n -le 0) { return $null }
        if ($one[0] -eq 10) {
            $sb = [System.Text.StringBuilder]::new($buf.Count)
            foreach ($b in $buf) {
                if ($b -lt $script:CceToUnicode.Length) {
                    [void]$sb.Append([char]$script:CceToUnicode[$b])
                } else {
                    [void]$sb.Append([char]0xFFFD)
                }
            }
            return $sb.ToString().TrimEnd("`r")
        }
        $buf.Add($one[0])
    }
}

# ── Phase 1: Codex source -> IR text via main compiler ──────────────
$compileScript = Join-Path $Repo 'build\test-compile.ps1'
& $compileScript -Src $Src -Out $IrFile -Log $LogFile -Ir
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("FAIL: IR emit step exited $LASTEXITCODE; see $LogFile")
    exit 3
}
Write-Host "[csharp-run] IR: $((Get-Item $IrFile).Length) bytes"

# ── Phase 2: IR text -> C# via plug CDX (raw CCE on wire) ──────────
$run = Start-QemuRun -Kernel $PlugCdx -ConnectTimeoutSec 30 -MemMB 2048
if (-not $run) {
    [Console]::Error.WriteLine("FAIL: QEMU did not listen after 4 attempts")
    exit 4
}

try {
    $conn = $run.Conn
    if (-not (Read-QemuReady -Conn $conn -TimeoutSec 30)) {
        [Console]::Error.WriteLine("READY not received within 30s")
        exit 4
    }
    $stream = $conn.Data.GetStream()

    $irBytes = [System.IO.File]::ReadAllBytes($IrFile)
    $cceBytes = ConvertTo-Cce -Bytes $irBytes
    $stream.Write($cceBytes, 0, $cceBytes.Length)
    $stream.WriteByte(0)
    $stream.Flush()

    $lines = [System.Collections.Generic.List[string]]::new()
    while ($true) {
        $line = Read-CceStreamLine -Stream $stream -TimeoutSec 60
        if ($null -eq $line) { break }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            [Console]::Error.WriteLine("FAIL: $line")
            exit 5
        }
        if ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line"); continue }
        if ($line.StartsWith('HEAP:')) { break }
        $lines.Add($line)
    }
    $body = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText($Out, $body, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[csharp-run] OK: $Out ($($body.Length) bytes)"
    exit 0
} finally {
    if ($run) {
        Close-Qemu -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
