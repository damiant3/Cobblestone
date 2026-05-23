# Test the bare-metal exception handler output.
# Compiles crashing samples, boots them with a short timeout,
# and verifies the serial output contains the expected dump format.
[CmdletBinding()]
param(
    [string]$CodexCdx = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CodexCdx) { $CodexCdx = Join-Path $Repo 'seed\Codex.cdx' }
$compile = Join-Path $PSScriptRoot 'compile.ps1'
$outDir = Join-Path $Repo 'build-output\exc-test'
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$stage0Dir = Join-Path $Repo 'build-output\bare-metal'
New-Item -ItemType Directory -Force -Path $stage0Dir | Out-Null
Copy-Item -Force $CodexCdx (Join-Path $stage0Dir 'Codex.cdx')

$samples = @(
    @{ Name = 'exc-div-zero';    Pattern = '!EXC=';          NeedStack = $true },
    @{ Name = 'exc-null-read';  Pattern = '!EXC=';          NeedStack = $true },
    @{ Name = 'exc-gpf';        Pattern = '!EXC=';          NeedStack = $true },
    @{ Name = 'exc-stack-heap'; Pattern = 'OUT OF MEMORY';  NeedStack = $false }
)

$pass = 0
$fail = 0

foreach ($s in $samples) {
    $src = Join-Path $Repo "codex\test\$($s.Name).codex"
    $cdx = Join-Path $outDir "$($s.Name).cdx"
    $log = Join-Path $outDir "$($s.Name).log"

    Write-Host -NoNewline "$($s.Name): "

    & pwsh -NoProfile -File $compile -Src $src -Out $cdx -Log $log 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL (compile)"
        $fail++
        continue
    }

    $run = Start-VmRun -Kernel $cdx -ConnectTimeoutSec 5 -MemMB 2048
    if (-not $run) {
        Write-Host "FAIL (vm start)"
        $fail++
        continue
    }

    $output = ''
    try {
        $dataStream = $run.Conn.Data.GetStream()
        $ctrlStream = $run.Conn.Ctrl.GetStream()
        $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
        $readBuf = New-Object byte[] 4096
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            $gotData = $false
            if ($dataStream.DataAvailable) {
                if ($dataStream.CanTimeout) { $dataStream.ReadTimeout = 2000 }
                try { $n = $dataStream.Read($readBuf, 0, $readBuf.Length) } catch { $n = 0 }
                if ($n -gt 0) { for ($i = 0; $i -lt $n; $i++) { $allBytes.Add($readBuf[$i]) }; $gotData = $true }
            }
            if ($ctrlStream.DataAvailable) {
                if ($ctrlStream.CanTimeout) { $ctrlStream.ReadTimeout = 2000 }
                try { $n = $ctrlStream.Read($readBuf, 0, $readBuf.Length) } catch { $n = 0 }
                if ($n -gt 0) { for ($i = 0; $i -lt $n; $i++) { $allBytes.Add($readBuf[$i]) }; $gotData = $true }
            }
            if (-not $gotData) { Start-Sleep -Milliseconds 100; continue }
            $partial = [System.Text.Encoding]::UTF8.GetString($allBytes.ToArray())
            if ($partial.Contains('S[0000000000000070]') -or $partial.Contains('OUT OF MEMORY')) { break }
        }
        $output = [System.Text.Encoding]::UTF8.GetString($allBytes.ToArray())
    } finally {
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }

    [System.IO.File]::WriteAllText((Join-Path $outDir "$($s.Name).out"), $output)

    $hasPattern = $output.Contains($s.Pattern)
    $dumpLines = @($output -split "`n" | Where-Object { $_ -match '^S\[' }).Count
    $stackOk = if ($s.NeedStack) { $dumpLines -ge 14 } else { $true }

    if ($hasPattern -and $stackOk) {
        Write-Host "PASS (pattern=$hasPattern stack=$dumpLines)"
        $pass++
    } else {
        Write-Host "FAIL (pattern=$hasPattern stack=$dumpLines)"
        Write-Host "  output: $($output.Substring(0, [math]::Min(200, $output.Length)))"
        $fail++
    }
}

Write-Host ""
Write-Host "Exception handler tests: pass=$pass fail=$fail"
if ($fail -gt 0) { exit 1 }
exit 0
