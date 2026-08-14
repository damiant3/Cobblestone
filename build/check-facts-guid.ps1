# check-facts-guid.ps1 -- The fact-store partition type GUID must agree across all three writers
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
)

# The fact-store partition type GUID exists in three places and they must
# agree. A runner, not a promise: a copied constant is exactly the divergence
# Foreword chapter FactLog exists to prevent, and the three copies are here
# because the alternatives are worse -- the IMG plug is its own compilation
# unit and citing Gpt would drag Device.Block into an image writer, and
# build/build-img.ps1 is PowerShell and cites nothing.
# 
#   codex/foreword/core/Gpt.codex        gpt-codex-facts-guid   (the reader)
#   codex/plugs/img/GptWriter.codex      gpt-codex-facts-guid   (the plug)
#   build/build-img.ps1                  a [byte[]] literal     (the shipped stick)
# 
# A disagreement is silent and expensive: the stick is written with one type
# and the guest looks for another, so the store finds no region, refuses every
# write, and reports "0 disk facts" forever with nothing anywhere saying why.
# 
# Exit 1 on disagreement. Wired into build/build.ps1.


$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path


function Fail([string]$msg) {
    Write-Host ([string]'FAIL: ' + $msg)
    exit 1
}

# the two Codex sources: a decimal list on the gpt-codex-facts-guid line
function Get-CodexGuid([string]$relPath) {
    $path = (Join-Path $Repo $relPath)
    if ((-not (Test-Path -PathType Leaf $path))) {
        & 'Fail' ([string]$relPath + ' is missing')
    }
    $hits = @((([System.IO.File]::ReadAllLines($path)) | Where-Object { ($_ -match 'gpt-codex-facts-guid\s*:\s*List Integer\s*=\s*\[') } | Select-Object -First 1))
    if ((@($hits).Count -eq 0)) {
        & 'Fail' ([string]$relPath + ' declares no gpt-codex-facts-guid')
    }
    $line = $hits[0]
    if ((-not ($line -match '\[([^\]]*)\]'))) {
        & 'Fail' ([string]$relPath + ' : could not read the byte list')
    }
    $bytes = @((($matches[1] -split ',') | ForEach-Object { [int]$_.Trim() }))
    if ((-not (@($bytes).Count -eq 16))) {
        & 'Fail' ([string]([string]([string]$relPath + ' : GUID has ') + @($bytes).Count) + ' bytes, want 16')
    }
    return ($bytes -join ',')
}

# the PowerShell writer: a hex [byte[]] literal on the marked line
function Get-Ps1Guid([string]$relPath) {
    $path = (Join-Path $Repo $relPath)
    if ((-not (Test-Path -PathType Leaf $path))) {
        & 'Fail' ([string]$relPath + ' is missing')
    }
    $hits = @((([System.IO.File]::ReadAllLines($path)) | Where-Object { ($_ -match 'WBytes \$fsOff \(\[byte\[\]\]@\(') } | Select-Object -First 1))
    if ((@($hits).Count -eq 0)) {
        & 'Fail' ([string]$relPath + ' writes no fact-store type GUID')
    }
    $line = $hits[0]
    if ((-not ($line -match '@\(([^\)]*)\)'))) {
        & 'Fail' ([string]$relPath + ' : could not read the byte list')
    }
    $bytes = @((($matches[1] -split ',') | ForEach-Object { [int]([string]'0x' + ($_.Trim() -replace '^0x', '')) }))
    if ((-not (@($bytes).Count -eq 16))) {
        & 'Fail' ([string]([string]([string]$relPath + ' : GUID has ') + @($bytes).Count) + ' bytes, want 16')
    }
    return ($bytes -join ',')
}


$paths = @('codex/foreword/core/Gpt.codex', 'codex/plugs/img/GptWriter.codex', 'build/build-img.ps1')
$guids = @((Get-CodexGuid 'codex/foreword/core/Gpt.codex'), (Get-CodexGuid 'codex/plugs/img/GptWriter.codex'), (Get-Ps1Guid 'build/build-img.ps1'))
$reference = $guids[0]
$bad = @()
for ($i = 0; ($i -lt @($guids).Count); $i++) {
    $sp = $paths[$i]
    $sb = $guids[$i]
    if (($sb -ne $reference)) {
        $bad += ([string]([string]$sp + ': ') + $sb)
    }
}


if ((@($bad).Count -gt 0)) {
    Write-Host 'FAIL: the fact-store partition type GUID disagrees across writers.'
    Write-Host ([string]([string]([string]'  ' + $paths[0]) + ': ') + $reference)
    foreach ($b in $bad) {
        Write-Host ([string]'  ' + $b)
    }
    exit 1
}
Write-Host ([string]([string]'check-facts-guid: OK (' + @($guids).Count) + ' sources agree)')
exit 0
