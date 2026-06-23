# Helper invoked by test.ps1. Boots a CDX kernel under codex-vm.exe,
# captures serial output, filters HEAP/WD/STACK lines, writes to OutFile.
#
# Uses memory-mapped I/O: -kernel, -output, optionally -input for stdin.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Kernel,
    [Parameter(Mandatory=$true)] [string]$OutFile,
    [string]$StdinFile = '',
    [string]$DiskFile = '',
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$sample = [System.IO.Path]::GetFileNameWithoutExtension($Kernel)
$wallBudgetMs = 60000

Write-SweepLog "$sample run-start pcore=$PCore"

$outputFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()
$inputFile = $null

try {
    $vmArgs = @('-kernel', $Kernel, '-output', $outputFile, '-mem', '3072', '-headless')
    if ($StdinFile -and (Test-Path -PathType Leaf $StdinFile)) {
        $inputFile = [System.IO.Path]::GetTempFileName()
        $stdinBytes = [System.IO.File]::ReadAllBytes($StdinFile)
        [System.IO.File]::WriteAllBytes($inputFile, $stdinBytes)
        $vmArgs += @('-input', $inputFile)
    }
    if ($DiskFile -and (Test-Path -PathType Leaf $DiskFile)) {
        $vmArgs += @('-disk', $DiskFile)
    }

    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs `
        -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    Write-SweepLog "$sample vm-pid=$($proc.Id)"

    if (-not $proc.WaitForExit($wallBudgetMs)) {
        Write-SweepLog "$sample run-fail wall-budget-exceeded pid=$($proc.Id)"
        Stop-VmGraceful -ProcessId $proc.Id
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }

    if (-not (Test-Path $outputFile) -or (Get-Item $outputFile).Length -eq 0) {
        Write-SweepLog "$sample run-fail no-output"
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }

    $raw = [System.IO.File]::ReadAllText($outputFile) -replace "`r", '' -replace "^\x01", ''
    $allLines = $raw -split "`n"
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $allLines) {
        if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:')) { continue }
        $lines.Add($l)
    }
    while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
    $body = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
    [System.IO.File]::WriteAllText($OutFile, $body, [System.Text.UTF8Encoding]::new($false))
    Write-SweepLog "$sample run-ok"
    exit 0
} finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force $outputFile, $stderrFile -ErrorAction SilentlyContinue
    if ($inputFile) { Remove-Item -Force $inputFile -ErrorAction SilentlyContinue }
}
