# run-plug-chain.ps1 -- Run a chain of plug CDXs sequentially piping output from each to the next
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$InFile,
    [Parameter(Mandatory=$true)]
    [string]$Output,
    [Parameter(Mandatory=$true)]
    [string[]]$Plugs,
    [int]$MemMB = 4096,
    [int]$TimeoutSec = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RunPlug = Join-Path $PSScriptRoot 'run-plug.ps1'

if ((-not (Test-Path -PathType Leaf $InFile))) {
    [Console]::Error.WriteLine("MISSING: $InFile")
    exit 2
}


foreach ($plug in $Plugs) {
    if ((-not (Test-Path -PathType Leaf $plug))) {
        [Console]::Error.WriteLine("MISSING plug CDX: $plug")
        exit 2
    }
}


$stage = $InFile
$temps = @()
for ($i = 0; $i -lt $Plugs.Count; $i++) {
    $isLast = ($i -eq ($Plugs.Count - 1))
    if ($isLast) {
        $dest = $Output
    } else {
        $dest = [System.IO.Path]::GetTempFileName()
        $temps += $dest
    }


    Write-Host "[chain] stage $($i + 1)/$($Plugs.Count): $($Plugs[$i])"
    & pwsh -NoProfile -File $RunPlug -Plug $Plugs[$i] -InFile $stage -Output $dest -MemMB $MemMB -TimeoutSec $TimeoutSec
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("FAIL: stage $($i + 1) ($($Plugs[$i])) exited $LASTEXITCODE")
        $temps | ForEach-Object { Remove-Item $_ -Force -ErrorAction SilentlyContinue }
        exit $LASTEXITCODE
    }


    $stage = $dest
}


$temps | ForEach-Object { Remove-Item $_ -Force -ErrorAction SilentlyContinue }
Write-Host "[chain] OK: $Output ($((Get-Item $Output).Length) bytes)"
exit 0
