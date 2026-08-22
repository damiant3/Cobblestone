# test-run.ps1 -- Boot a CDX kernel under codex-vm and capture filtered serial output
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Kernel,
    [Parameter(Mandatory=$true)]
    [string]$OutFile,
    [string]$StdinFile = '',
    [string]$KeysFile = '',
    [string]$DiskFile = '',
    [string]$Disk2File = '',
    [string]$VmArgsFile = '',
    [int]$Smp = 0,
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$sample = ([System.IO.Path]::GetFileNameWithoutExtension($Kernel))
$wallBudgetMs = 60000

Write-SweepLog "$sample run-start pcore=$PCore"


$outputFile = [System.IO.Path]::GetTempFileName()
$stderrFile = [System.IO.Path]::GetTempFileName()
$inputFile = $null


try {
    $vmArgs = @('-kernel', $Kernel, '-output', $outputFile, '-mem', '3072', '-headless')
    if (($StdinFile -and (Test-Path -PathType Leaf $StdinFile))) {
        $inputFile = [System.IO.Path]::GetTempFileName()
        $stdinBytes = [System.IO.File]::ReadAllBytes($StdinFile)
        [System.IO.File]::WriteAllBytes($inputFile, $stdinBytes)
        $vmArgs += @('-input', $inputFile)
    }
    if (($KeysFile -and (Test-Path -PathType Leaf $KeysFile))) {
        # Read directly: codex-vm only reads this file, so the read-only
        # attribute a depot sidecar carries after sync is no obstacle.
        $vmArgs += @('-keys-file', $KeysFile)
    }
    if (($DiskFile -and (Test-Path -PathType Leaf $DiskFile))) {
        # Copy to a writable temp image: depot sidecars are read-only after
        # sync (write tests would fail to open the disk), and codex-vm
        # flushes writes durably (a writable sidecar would be mutated).
        $diskWork = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllBytes($diskWork, [System.IO.File]::ReadAllBytes($DiskFile))
        $vmArgs += @('-disk', $diskWork)
    }
    if (($Disk2File -and (Test-Path -PathType Leaf $Disk2File))) {
        # The primary channel's slave. Copied to a writable temp for the same
        # reason as the master, and it matters more here: the case this exists
        # for is one drive writing to another.
        $disk2Work = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllBytes($disk2Work, [System.IO.File]::ReadAllBytes($Disk2File))
        $vmArgs += @('-disk2', $disk2Work)
    }
    if ($Smp -gt 1) {
        $vmArgs += @('-smp', "$Smp")
    }
    if (($VmArgsFile -and (Test-Path -PathType Leaf $VmArgsFile))) {
        # Whitespace-separated flags, '#' comments, blank lines ignored.
        foreach ($line in (Get-Content $VmArgsFile)) {
            $line = $line.Trim()
            if (-not $line -or $line.StartsWith('#')) {
                continue
            }
            $vmArgs += ($line -split '\s+')
        }
    }


    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    Write-SweepLog "$sample vm-pid=$($proc.Id)"

    if ((-not $proc.WaitForExit($wallBudgetMs))) {
        Write-SweepLog "$sample run-fail wall-budget-exceeded pid=$($proc.Id)"
        Stop-VmGraceful -ProcessId $proc.Id
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }


    if ((Test-Path -PathType Leaf $stderrFile)) {
        $vmErr = [System.IO.File]::ReadAllText($stderrFile)
        if (($vmErr -match 'DROPPED')) {
            [Console]::Error.WriteLine('codex-vm dropped guest serial bytes: the captured output is SHORT and any comparison against it is meaningless')
            [Console]::Error.WriteLine($vmErr)
            exit 1
        }
    }


    if (((-not (Test-Path -PathType Leaf $outputFile)) -or (Get-Item $outputFile).Length -eq 0)) {
        Write-SweepLog "$sample run-fail no-output"
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }


    $raw = [System.IO.File]::ReadAllText($outputFile) -replace "`r", '' -replace "^\x01", ''
    $allLines = $raw -split "`n"
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $allLines) {
        if ((($l.StartsWith('HEAP:') -or $l.StartsWith('WD:')) -or $l.StartsWith('STACK:'))) {
            continue
        } else {
            [void]$lines.Add($l)
        }
    }


    while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
    $body = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
    [System.IO.File]::WriteAllText($OutFile, $body, [System.Text.UTF8Encoding]::new($false))
    Write-SweepLog "$sample run-ok"
    exit 0

} finally {
    if (($proc -and (-not $proc.HasExited))) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force $outputFile, $stderrFile -ErrorAction SilentlyContinue
    if ($inputFile) {
        Remove-Item -Force $inputFile -ErrorAction SilentlyContinue
    }
}
