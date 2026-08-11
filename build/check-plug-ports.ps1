# check-plug-ports.ps1 -- hold the guest and host halves of a plug's port together.
#
# A plug's TCP port is ONE fact written in TWO places: <Name>Plug.codex dials it
# in net-session-new, and the plug's run.ps1 listens on it. The NAT maps the
# guest's destination port straight to the host port, so if the two disagree the
# guest dials a port nobody is listening on and the run hangs until it times out
# with no diagnostic. That is the failure this exists to make impossible.
#
# build/plug-ports.ps1 is the authority. This checks both halves against it.
#
# Exit 1 on any mismatch, 0 when every plug agrees.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'plug-ports.ps1')

$bad = 0; $ok = 0; $seen = @{}
foreach ($name in ($script:PlugPorts.Keys | Sort-Object)) {
    $want = $script:PlugPorts[$name]
    if ($seen.ContainsKey($want)) {
        Write-Host ("  {0,-12} port {1} DUPLICATE, already assigned to {2}" -f $name, $want, $seen[$want]); $bad++
    }
    $seen[$want] = $name
    $dir = Join-Path $Repo "codex\plugs\$name"
    $guest = @(Get-ChildItem $dir -Filter '*Plug.codex' -File -ErrorAction SilentlyContinue |
               Where-Object { Select-String -Path $_.FullName -Pattern 'net-session-new' -Quiet })
    if ($guest.Count -eq 0) { Write-Host ("  {0,-12} NO GUEST SOURCE dialing a port" -f $name); $bad++; continue }
    foreach ($g in $guest) {
        $m = Select-String -Path $g.FullName -Pattern 'net-session-new\s+\S+\s+\S+\s+\S+\s+\d+\s+(\d+)\s'
        if (-not $m) { Write-Host ("  {0,-12} {1}: no readable port" -f $name, $g.Name); $bad++; continue }
        $got = [int]$m.Matches[0].Groups[1].Value
        if ($got -ne $want) { Write-Host ("  {0,-12} GUEST {1} dials {2}, table says {3}" -f $name, $g.Name, $got, $want); $bad++ }
        else { $ok++ }
    }
    $run = Join-Path $dir 'run.ps1'
    if (Test-Path $run) {
        $hits = @(Select-String -Path $run -Pattern '\b(\d{4})\b' -AllMatches |
                  ForEach-Object { $_.Matches } | ForEach-Object { [int]$_.Groups[1].Value } |
                  Where-Object { $_ -ge 9100 -and $_ -le 9199 } | Sort-Object -Unique)
        if ($hits.Count -eq 0) { Write-Host ("  {0,-12} run.ps1 names no plug port" -f $name); $bad++ }
        elseif ($hits.Count -gt 1 -or $hits[0] -ne $want) {
            Write-Host ("  {0,-12} HOST run.ps1 uses {1}, table says {2}" -f $name, ($hits -join ','), $want); $bad++
        } else { $ok++ }
    }
}
Write-Host ""
if ($bad -gt 0) { Write-Host "check-plug-ports: $bad MISMATCH, $ok agreed"; exit 1 }
Write-Host "check-plug-ports: OK ($ok halves agreed across $($script:PlugPorts.Count) plugs)"
exit 0
