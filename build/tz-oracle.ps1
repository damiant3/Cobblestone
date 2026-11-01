# tz-oracle.ps1 -- the IANA zone answers from implementations we did not
# write, for comparison with codex/test/tz-* output.
#
# The oracle is Python's zoneinfo over the pip tzdata package at the same
# IANA release as codex/foreword/core/tzdata/<release>/, installed into
# -PyTarget (pip install --target <dir> tzdata==<version>). The script
# refuses when the installed release differs from -Release.
#
#   pwsh build/tz-oracle.ps1 -Release 2026f -PyTarget <dir> -Names -Codex <tz-parse output>
#     prints the zone and link names the two sides do not share, and exits 1
#     when there is any.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Release,
    [Parameter(Mandatory = $true)][string]$PyTarget,
    [string]$Python = 'D:\AI\DiffusionForge\system\python\python.exe',
    [switch]$Names,
    [switch]$Compiled,
    [switch]$Local,
    [switch]$Future,
    [string]$Codex = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:PYTHONPATH = $PyTarget
$have = (& $Python -c "import tzdata; print(tzdata.IANA_VERSION)").Trim()
if ($have -ne $Release) { Write-Host "REFUSED: oracle tzdata is $have, not $Release"; exit 2 }

if ($Names) {
    $py = @(& $Python -c "import zoneinfo; print('\n'.join(sorted(zoneinfo.available_timezones())))" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $ours = @(Get-Content -LiteralPath $Codex | ForEach-Object {
        if ($_ -match '^zone (\S+)$') { $Matches[1] } elseif ($_ -match '^link (\S+) -> ') { $Matches[1] }
    })
    $onlyPy = @($py | Where-Object { $_ -notin $ours })
    $onlyUs = @($ours | Where-Object { $_ -notin $py })
    Write-Host "oracle names $($py.Count), ours $($ours.Count), only in oracle $($onlyPy.Count), only in ours $($onlyUs.Count)"
    $onlyPy | ForEach-Object { Write-Host "  oracle only: $_" }
    $onlyUs | ForEach-Object { Write-Host "  ours only: $_" }
    if ($onlyPy.Count -gt 0 -or $onlyUs.Count -gt 0) { exit 1 }
    exit 0
}
if ($Compiled) {
    $ours = @{}
    foreach ($l in (Get-Content -LiteralPath $Codex)) { if ($l -match '^(\S+) (\d+) ([0-9a-f]{64})$') { $ours[$Matches[1]] = @($Matches[2], $Matches[3]) } }
    $zones = @($ours.Keys | Sort-Object)
    $script = Join-Path $PSScriptRoot 'tz-oracle.py'
    $hashes = & $Python -c "import sys,hashlib,runpy; g=runpy.run_path(r'$script', run_name='lib'); [print(n, len([x for x in g['listing'](n) if x.startswith('T ')]), hashlib.sha256(('\n'.join(g['listing'](n))+'\n').encode()).hexdigest()) for n in sys.argv[1:]]" @zones
    $bad = 0
    foreach ($h in $hashes) {
        $n, $count, $sha = $h -split ' '
        if ($ours[$n][0] -ne $count -or $ours[$n][1] -ne $sha) { $bad++; Write-Host "  differs: $n ours $($ours[$n][0]) oracle $count" }
    }
    Write-Host "zones compared $($zones.Count), oracle answered $(@($hashes).Count), differing $bad"
    if ($bad -gt 0 -or @($hashes).Count -ne $zones.Count) { exit 1 }
    exit 0
}if ($Future) {
    $ours = @{}
    foreach ($l in (Get-Content -LiteralPath $Codex)) { if ($l -match '^(\S+) (\d+) ([0-9a-f]{64})$') { $ours[$Matches[1]] = @($Matches[2], $Matches[3]) } }
    $zones = @($ours.Keys | Sort-Object)
    $script = Join-Path $PSScriptRoot 'tz-oracle.py'
    $hashes = & $Python -c "import sys,hashlib,runpy; g=runpy.run_path(r'$script', run_name='lib'); f=lambda n: [x for x in g['listing'](n, 4102444800) if x.startswith('T ') and int(x.split()[1]) >= 2145916800]; [print(n, len(f(n)), hashlib.sha256(''.join(x+'\n' for x in f(n)).encode()).hexdigest()) for n in sys.argv[1:]]" @zones
    $bad = 0
    foreach ($h in $hashes) {
        $n, $count, $sha = $h -split ' '
        if ($ours[$n][0] -ne $count -or $ours[$n][1] -ne $sha) { $bad++; Write-Host "  differs: $n ours $($ours[$n][0]) oracle $count" }
    }
    Write-Host "zones compared $($zones.Count), oracle answered $(@($hashes).Count), differing $bad"
    if ($bad -gt 0 -or @($hashes).Count -ne $zones.Count) { exit 1 }
    exit 0
}if ($Local) {
    $ours = @{}
    foreach ($l in (Get-Content -LiteralPath $Codex)) { if ($l -match '^(\S+) (\d+) ([0-9a-f]{64}) ') { $ours[$Matches[1]] = @($Matches[2], $Matches[3]) } }
    $zones = @($ours.Keys | Sort-Object)
    $script = Join-Path $PSScriptRoot 'tz-oracle.py'
    $hashes = & $Python -c "import sys,hashlib,runpy; g=runpy.run_path(r'$script', run_name='lib'); [print(n, len(g['local_lines'](n)), hashlib.sha256(''.join(x+'\n' for x in g['local_lines'](n)).encode()).hexdigest()) for n in sys.argv[1:]]" @zones
    $bad = 0
    foreach ($h in $hashes) {
        $n, $count, $sha = $h -split ' '
        if ($ours[$n][0] -ne $count -or $ours[$n][1] -ne $sha) { $bad++; Write-Host "  differs: $n ours $($ours[$n][0]) oracle $count" }
    }
    Write-Host "zones compared $($zones.Count), oracle answered $(@($hashes).Count), differing $bad"
    if ($bad -gt 0 -or @($hashes).Count -ne $zones.Count) { exit 1 }
    exit 0
}Write-Host 'nothing to do: pass -Names, -Compiled, -Local or -Future'
exit 2
