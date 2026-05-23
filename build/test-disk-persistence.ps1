[CmdletBinding()]
param(
    [int]$PCore = 1,
    [int]$MaxRetries = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$buildDir = Join-Path $root 'build-output\bare-metal'
$diskDir  = Join-Path $root 'build-output\disk'
$diskImg  = Join-Path $diskDir 'persist-test.img'
$runDisk  = Join-Path $PSScriptRoot 'run-with-disk.ps1'
$compile  = Join-Path $PSScriptRoot 'compile.ps1'

if (-not (Test-Path $diskDir)) { New-Item -ItemType Directory -Path $diskDir -Force | Out-Null }

function Compile-Sample($src, $out) {
    $log = "$out.log"
    & pwsh -File $compile -Src $src -Out $out -Log $log -PCore $PCore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: compile $src (exit $LASTEXITCODE)"
        if (Test-Path $log) { Get-Content $log | Write-Host }
        exit 1
    }
}

# QEMU IDE PIO reads under WHPX are unreliable — the controller
# sometimes fails to initialize from the backing file on boot.
# ~40% of boots read correctly. We retry up to MaxRetries times.
function Run-Phase($kernel, $expect, $label) {
    for ($try = 1; $try -le $MaxRetries; $try++) {
        $out = & pwsh -File $runDisk -Kernel $kernel -Disk $diskImg -TimeoutSec 30 -PCore $PCore 2>&1
        $text = ($out | Out-String).Trim()
        if ($text -eq $expect) {
            Write-Host "  $label : PASS (attempt $try)"
            return
        }
    }
    Write-Host "  $label : FAIL (expected '$expect', got '$text' after $MaxRetries attempts)"
    exit 1
}

function Fresh-Disk { [System.IO.File]::WriteAllBytes($diskImg, [byte[]]::new(1048576)) }

$samples = @(
    @{ name = 'disk-facts-init'; src = 'disk-facts-init' },
    @{ name = 'disk-facts-load'; src = 'disk-facts-load' },
    @{ name = 'disk-facts-read'; src = 'disk-facts-read' },
    @{ name = 'disk-facts-multi'; src = 'disk-facts-multi' },
    @{ name = 'disk-facts-multi-load'; src = 'disk-facts-multi-load' }
)

foreach ($s in $samples) {
    $bin = Join-Path $buildDir "$($s.name).cdx"
    Write-Host "Compiling $($s.name)..."
    Compile-Sample (Join-Path $root "codex\test\$($s.src).codex") $bin
}

Write-Host ""
Write-Host "=== Test 1: single-fact roundtrip ==="
Fresh-Disk
Run-Phase (Join-Path $buildDir 'disk-facts-init.cdx') '1' 'init + write'
Run-Phase (Join-Path $buildDir 'disk-facts-load.cdx') '1' 'load count'
Run-Phase (Join-Path $buildDir 'disk-facts-read.cdx') 'hello' 'load content'

Write-Host ""
Write-Host "=== Test 2: multi-fact roundtrip ==="
Fresh-Disk
Run-Phase (Join-Path $buildDir 'disk-facts-multi.cdx') '3' 'write 3 facts'
Run-Phase (Join-Path $buildDir 'disk-facts-multi-load.cdx') '3 alpha beta gamma' 'load all 3'

Write-Host ""
Write-Host "PASS: all disk persistence tests verified"
Remove-Item -Force $diskImg -ErrorAction SilentlyContinue
exit 0
