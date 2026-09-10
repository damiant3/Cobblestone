# Assemble docs/PM/Active/Stories/TheLostParadise.md from its parts.
# The one formula for the report's body (root, 2026-09-09): the charter's
# own text (everything above the line "## 1. The accident") is kept as
# written, and every section below that line is REPLACED by the parts under
# TheLostParadise/, in the charter's order, each part's headings demoted one
# level so the report has one top heading. The parts stay the units of
# authorship and landing; this file is the report's body, regenerated
# whenever a part lands, never edited by hand below the line.
#
#   build/lp-assemble.ps1            # rewrite the report in place
#   build/lp-assemble.ps1 -Check     # print the part list, sizes and page count, write nothing
[CmdletBinding()]
param([switch]$Check)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$report = Join-Path $repo 'docs\PM\Active\Stories\TheLostParadise.md'
$dir = Join-Path $repo 'docs\PM\Active\Stories\TheLostParadise'
$order = @(
    @('01-the-accident.md',              '1. The accident'),
    @('02-timeline.md',                  '2. The history: the timeline'),
    @('03-sittings-ledger.md',           '2b. The history: every sitting'),
    @('04-the-nic.md',                   '3a. Technical analysis: the NIC'),
    @('05-the-medium.md',                '3b. Technical analysis: the medium'),
    @('06-the-ladder-and-the-images.md', '3c. Technical analysis: the ladder and the images'),
    @('07-the-beds.md',                  '3d. Technical analysis: the beds'),
    @('08-the-organization.md',          '4. Organizational analysis'),
    @('09-the-prompting.md',             '4b. Organizational analysis: the prompting'),
    @('10-findings.md',                  '5. Findings, root causes and contributing causes'),
    @('11-recommendations.md',           '6. Recommendations'),
    @('A-sittings-table.md',             'Appendix A. The sittings table'),
    @('B-cl-ledger.md',                  'Appendix B. The changelist ledger'),
    @('C-rulings-ledger.md',             'Appendix C. The rulings ledger, in Damian''s words'),
    @('D-lessons-crossref.md',           'Appendix D. The lessons against the record'),
    @('E-currentplan-metal-ledger.md',   'Appendix E. What CurrentPlan said about metal, revision by revision'),
    @('F-findings-index.md',             'Appendix F. The findings index')
)
$head = [IO.File]::ReadAllText($report)
$cut = $head.IndexOf("## 1. The accident")
if ($cut -lt 0) { throw "the charter has no '## 1. The accident' line to assemble below" }
$head = $head.Substring(0, $cut)
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append($head)
$total = 0; $missing = @()
foreach ($o in $order) {
    $p = Join-Path $dir $o[0]
    if (-not (Test-Path $p)) { $missing += $o[0]; [void]$sb.AppendLine("## $($o[1])`r`n`r`n*Part `$($o[0])` has not landed.*`r`n"); continue }
    $body = [IO.File]::ReadAllText($p)
    $total += $body.Length
    # drop the part's own top heading (the assembly's heading replaces it), demote the rest one level
    $lines = $body -split "`r?`n"
    $out = New-Object System.Collections.Generic.List[string]
    $first = $true
    foreach ($l in $lines) {
        if ($first -and $l -match '^# ') { $first = $false; continue }
        $first = $false
        if ($l -match '^(#{1,5}) ') { $out.Add('#' + $l) } else { $out.Add($l) }
    }
    [void]$sb.AppendLine("## $($o[1])"); [void]$sb.AppendLine()
    [void]$sb.AppendLine("*From `$($o[0])`.*"); [void]$sb.AppendLine()
    [void]$sb.AppendLine(($out -join "`r`n").TrimEnd()); [void]$sb.AppendLine()
}
$text = $sb.ToString()
$pages = [math]::Round($text.Length / 3000.0)   # about 3,000 characters per printed page of prose
Write-Host ("parts: {0} present, {1} missing ({2}); body {3:N0} characters, about {4} pages" -f ($order.Count - $missing.Count), $missing.Count, ($missing -join ', '), $total, $pages)
if ($Check) { return }
[IO.File]::WriteAllText($report, $text)
Write-Host "wrote $report"
