# coverage-report.ps1 -- function coverage of a program compiled under the `cover` mode flag.
#
# Inputs: the compile log (-Log of compile.ps1 -RawFlags cover), which carries one
# "CDX6015: [COVER] <slot> <name>" line per instrumented definition with its source
# span, and one or more run outputs holding "COVER:<slot>:<count>" blocks. A program
# prints a block at exit and at every __cover-dump call; counts are cumulative, so the
# LAST block of each output is that run's total. Several outputs (several runs of the
# same binary) are summed.
#
# Output: per chapter (source file) definitions hit / total, sorted by the never-run
# count, then the never-run list. -Filter keeps chapters whose path contains it.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Log,
    [Parameter(Mandatory)][string[]]$Output,
    [string]$Filter = '',
    [int]$Top = 40,
    [string]$Markdown = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$slots = @{}
foreach ($line in [IO.File]::ReadLines((Resolve-Path $Log).Path)) {
    if ($line -match '^(.*?):(\d+):\d+: info CDX6015: \[COVER\] (\d+) (\S+)') {
        $slots[[int]$matches[3]] = [pscustomobject]@{ File = $matches[1]; Line = [int]$matches[2]; Name = $matches[4]; Count = [long]0 }
    }
}
if ($slots.Count -eq 0) { Write-Host "coverage-report: no [COVER] lines in $Log (was it compiled with -RawFlags cover?)"; exit 1 }

$blocks = 0
foreach ($o in $Output) {
    $last = $null; $cur = $null
    foreach ($line in [IO.File]::ReadLines((Resolve-Path $o).Path)) {
        if ($line -match '^COVER-BEGIN:(\d+)') { $cur = @{} }
        elseif ($line -match '^COVER:(\d+):(\d+)' -and $null -ne $cur) { $cur[[int]$matches[1]] = [long]$matches[2] }
        elseif ($line -match '^COVER-END' -and $null -ne $cur) { $last = $cur; $cur = $null }
    }
    if ($null -eq $last) { Write-Host "coverage-report: no complete COVER block in $o"; continue }
    $blocks++
    foreach ($k in $last.Keys) { if ($slots.ContainsKey($k)) { $slots[$k].Count += $last[$k] } }
}
if ($blocks -eq 0) { Write-Host 'coverage-report: no run output carried a COVER block'; exit 1 }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$rows = @($slots.Values | Where-Object { $Filter -eq '' -or $_.File -like "*$Filter*" })
$chapters = @($rows | Group-Object File | ForEach-Object {
    $hit = @($_.Group | Where-Object { $_.Count -gt 0 }).Count
    [pscustomobject]@{ Chapter = $_.Name.Replace($root + '\', '').Replace('\', '/'); Hit = $hit; Total = $_.Count; Never = $_.Count - $hit }
} | Sort-Object Never, Chapter -Descending)
$hitAll = @($rows | Where-Object { $_.Count -gt 0 }).Count
$summary = 'coverage: {0} of {1} definitions entered ({2:N1}%), {3} chapters, {4} run(s)' -f $hitAll, $rows.Count, (100.0 * $hitAll / [math]::Max(1, $rows.Count)), $chapters.Count, $blocks
Write-Host $summary
$chapters | Select-Object -First $Top | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

if ($Markdown -ne '') {
    $md = New-Object System.Text.StringBuilder
    [void]$md.AppendLine("# Function coverage`n`n$summary`n")
    [void]$md.AppendLine('| chapter | hit | total | never run |')
    [void]$md.AppendLine('|---|---|---|---|')
    foreach ($c in $chapters) { [void]$md.AppendLine("| $($c.Chapter) | $($c.Hit) | $($c.Total) | $($c.Never) |") }
    [void]$md.AppendLine("`n## Never run`n")
    foreach ($g in ($rows | Where-Object { $_.Count -eq 0 } | Sort-Object File, Line | Group-Object File)) {
        [void]$md.AppendLine("- $($g.Name.Replace($root + '\', '').Replace('\', '/')): " + (($g.Group | ForEach-Object { "$($_.Name) ($($_.Line))" }) -join ', '))
    }
    [IO.File]::WriteAllText($Markdown, $md.ToString(), (New-Object Text.UTF8Encoding $false))
    Write-Host "coverage-report: wrote $Markdown"
}
exit 0
