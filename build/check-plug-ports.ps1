# check-plug-ports.ps1 -- hold the guest and host halves of a plug's port together.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
)

# A plug's TCP port is ONE fact written in TWO places: <Name>Plug.codex dials it
# in net-session-new, and the plug's run.ps1 listens on it. The NAT maps the
# guest's destination port straight to the host port, so if the two disagree the
# guest dials a port nobody is listening on and the run hangs until it times out
# with no diagnostic. That is the failure this exists to make impossible.
# 
# build/plug-ports.ps1 is the authority. This checks both halves against it.
# 
# Exit 1 on any mismatch, 0 when every plug agrees.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'plug-ports.ps1')

$bad = 0
$ok = 0
$seen = @{}


foreach ($name in ($script:PlugPorts.Keys | Sort-Object)) {
    $want = $script:PlugPorts[$name]
    if ($seen.ContainsKey($want)) {
        Write-Host ('  {0,-12} port {1} DUPLICATE, already assigned to {2}' -f $name, $want, $seen[$want])
        $bad++
    }
    $seen[$want] = $name

    $dir = (Join-Path $Repo ([string]'codex\plugs\' + $name))
    if ((Test-Path -PathType Container $dir)) {
        $guest = @((Get-ChildItem $dir -Filter '*Plug.codex' -File | Where-Object { ((Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue) -match 'net-session-new') }))
    } else {
        $guest = @()
    }
    if ((@($guest).Count -eq 0)) {
        Write-Host ('  {0,-12} NO GUEST SOURCE dialing a port' -f $name)
        $bad++
        continue
    }
    foreach ($g in $guest) {
        $hit = @((([System.IO.File]::ReadAllLines($g.FullName)) | Where-Object { ($_ -match 'net-session-new\s+\S+\s+\S+\s+\S+\s+\d+\s+(\d+)\s') } | Select-Object -First 1))
        if ((@($hit).Count -eq 0)) {
            Write-Host ('  {0,-12} {1}: no readable port' -f $name, $g.Name)
            $bad++
            continue
        }
        $line = $hit[0]
        if (($line -match 'net-session-new\s+\S+\s+\S+\s+\S+\s+\d+\s+(\d+)\s')) {
            $got = [int]$matches[1]
            if ((-not ($got -eq $want))) {
                Write-Host ('  {0,-12} GUEST {1} dials {2}, table says {3}' -f $name, $g.Name, $got, $want)
                $bad++
            } else {
                $ok++
            }
        } else {
            Write-Host ('  {0,-12} {1}: no readable port' -f $name, $g.Name)
            $bad++
        }
    }

    $run = (Join-Path $dir 'run.ps1')
    if ((Test-Path -PathType Leaf $run)) {
        $hitSet = @{}
        foreach ($mm in ([regex]::Matches((Get-Content $run -Raw -ErrorAction SilentlyContinue), '\b(\d{4})\b'))) {
            $v = [int]$mm.Groups[1].Value
            if ((($v -ge 9100) -and ($v -le 9199))) {
                $hitSet[$v] = $true
            }
        }
        $hits = @(($hitSet.Keys | Sort-Object))
        if ((@($hits).Count -eq 0)) {
            Write-Host ('  {0,-12} run.ps1 names no plug port' -f $name)
            $bad++
        } else {
            if (((@($hits).Count -gt 1) -or (-not ($hits[0] -eq $want)))) {
                Write-Host ('  {0,-12} HOST run.ps1 uses {1}, table says {2}' -f $name, ($hits -join ','), $want)
                $bad++
            } else {
                $ok++
            }
        }
    }

}


Write-Host ''
if (($bad -gt 0)) {
    Write-Host ([string]'check-plug-ports: ' + ([string]$bad + ([string]' MISMATCH, ' + ([string]$ok + ' agreed'))))
    exit 1
}
Write-Host ([string]'check-plug-ports: OK (' + ([string]$ok + ([string]' halves agreed across ' + ([string]$script:PlugPorts.Count + ' plugs)'))))
exit 0
