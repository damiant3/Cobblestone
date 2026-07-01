# measure-survey.ps1 - profile per-phase deck USAGE for a workload, to inform
# survey heuristics. Resolves cites like compile.ps1, runs TEXT mode, extracts
# WD:PM deck-usage per phase, and reports density metrics over the full concat.
#
# Usage: measure-survey.ps1 -Src <source.codex> [-Survey "check-mul:60,..."]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [int]$MemMB = 3072,
    [string]$Survey = '',
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $Repo

. (Join-Path $PSScriptRoot 'quire-map.ps1')
$citePat = New-CitePattern -ExtraQuires @('Codex')

# Index codex/compiler chapters (Codex quire) by chapter Title -> file path,
# since they live in subdirs (Core/Emit/IR/Semantics/Syntax/Types/Ast).
$codexIndex = @{}
foreach ($f in Get-ChildItem (Join-Path $Repo 'codex\compiler') -Recurse -Filter *.codex) {
    foreach ($l in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($l -match '^Chapter:\s*(.+?)\s*$') { $codexIndex[$matches[1].Trim()] = $f.FullName; break }
    }
}
$srcLines = [System.IO.File]::ReadAllLines($Src)
$seedSeen = @{}
foreach ($line in $srcLines) {
    if ($line -match '^Chapter:\s*(\w+)--(.+?)\s*$') { $seedSeen["$($matches[1])::$($matches[2])"] = $true }
}
$ordered = Resolve-CiteOrder -RootLines $srcLines -Repo $Repo -Pattern $citePat -SeedSeen $seedSeen -OnMissing skip `
    -PathOverride { param($quire, $name) if ($quire -eq 'Codex') { $codexIndex[$name] } else { $null } }
$sb = [System.Text.StringBuilder]::new(2097152)
$mode = 'TEXT'
if ($Survey) { $mode = "$mode survey=$Survey" }
[void]$sb.Append("$mode`n")
$bodySb = [System.Text.StringBuilder]::new(2097152)
foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$bodySb.Append($l + "`n") }
foreach ($line in $srcLines) { [void]$bodySb.Append($line + "`n") }
$body = $bodySb.ToString()
[void]$sb.Append($body); [void]$sb.Append([char]4)

# Density metrics over the full concatenated body
$S = $body.Length
$records  = ([regex]::Matches($body, '=\s*(mutable\s+)?record\s*\{')).Count
$variants = ([regex]::Matches($body, '(?m)^\s+\|\s')).Count            # variant ctor lines
$fields   = ([regex]::Matches($body, '(?m)^\s+[a-z][\w-]*\s*:\s')).Count # record-field-ish lines
$arrows   = ([regex]::Matches($body, '->')).Count
$whens    = ([regex]::Matches($body, '(?m)\bwhen\b')).Count

$vm = Join-Path $Repo 'tools\codex-vm.exe'
$in = [System.IO.Path]::GetTempFileName(); $out = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($in, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$kern = if ($Kernel) { $Kernel } else { Join-Path $Repo 'seed\Codex.cdx' }
& $vm -kernel $kern -input $in -output $out -mem $MemMB -headless 2>&1 | Out-Null
$o = [System.IO.File]::ReadAllText($out)
Remove-Item $in,$out -Force -EA SilentlyContinue
Pop-Location

$name = [System.IO.Path]::GetFileNameWithoutExtension($Src)
Write-Host ("WORKLOAD={0}  S={1}  records={2}  variant-ctors={3}  fields={4}  arrows={5}  whens={6}" -f $name,$S,$records,$variants,$fields,$arrows,$whens)
$halted = $o -match 'CODEGEN-HALTED|CDX9002|Deck overflow'
if ($halted) { Write-Host "  *** HALTED/OVERFLOW (survey too low or compile error) ***" }
$o -split "`n" | Where-Object { $_ -match 'WD:PM-' } | ForEach-Object {
    if ($_ -match 'WD:PM-(\w+):deck-origin=(\d+),deck-end=(\d+),deck-usage=(\d+),bivy-hwm=(\d+),bivy-usage=(\d+)') {
        $phase=$matches[1]; $du=[long]$matches[4]; $bu=[long]$matches[6]
        Write-Host ("  {0,-8} deck-usage={1,12:N0}  bivy-usage={2,12:N0}  deck/S={3,7:N1}  bivy/S={4,7:N1}" -f $phase,$du,$bu,($du/$S),($bu/$S))
    }
}
