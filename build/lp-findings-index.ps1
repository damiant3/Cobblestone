# lp-findings-index.ps1 -- collect every finding from The Lost Paradise's
# parts into one index.
#
# Hand-written. There is no generator for this file.
#
#   pwsh build/lp-findings-index.ps1
#   pwsh build/lp-findings-index.ps1 -Out docs/PM/Active/Stories/TheLostParadise/F-findings-index.md
#
# WHY A SCRIPT. The index is refreshed every time a part lands, therefore a
# hand-compiled index is stale on the next landing and nothing would say so.
#
# THE PARTS DO NOT AGREE ON A FORMAT, and the script handles what the parts
# actually do rather than what a style rule says they should:
#
#   **RED-F1. statement**            (red, blu, val: the period inside the bold)
#   **FESTER-F1.** statement         (fester, root: the bold is the id alone)
#   ### REEK-F1                      (reek: a heading, statement follows)
#
# The evidence and falsifier markers disagree the same way, and all six known
# spellings are accepted:
#
#   Supporting evidence:      Falsifying evidence:      Falsifying evidence would be
#   Evidence:                 Falsifier:                *Falsified by:*
#
# WHAT IT CANNOT DO:
#
# 1. A finding whose id is CITED in another part looks like an anchor only if
#   the citation sits at the start of a line in one of the three shapes. The
#   script records the file each id was DEFINED in and reports an id defined
#   twice, which is the case a reader must be told about rather than have
#   silently merged.
# 2. The statement is taken as the text before the first evidence marker. A
#   part that states its evidence before its finding will have the two
#   swapped, and no check here can see that.
# 3. It cannot judge whether two findings agree. The duplicate and
#   contradiction table is written by hand and lives in the output file below
#   the generated tables.
# 4. Counting is by ANCHOR, not by id mentioned. The coverage line prints both
#   so a reader can see the difference.

[CmdletBinding()]
param(
    [string]$Dir = 'docs/PM/Active/Stories/TheLostParadise',
    [string]$Out = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

# The id may carry a lower-case suffix (BLU-F2a), and the anchor may carry a
# qualifier between the id and its punctuation (RED-F13 (corrected)., RED-F15
# IS WITHDRAWN AND REPLACED.). Both shapes appeared only once findings began
# to be CORRECTED, therefore a pattern without them silently under-reports the
# findings a reader most needs, which are the corrections (L-INSTRUMENT).
$idPat = '[A-Z]{3,7}-[A-Z]\d+[a-z]?'
$qual = '[^.,*\r\n]{0,60}'
$markers = '(?:Supporting evidence|Falsifying evidence|Evidence|Falsifier|\*Falsified by:\*|Falsified by)'

$parts = Get-ChildItem (Join-Path $Repo $Dir) -Filter '*.md' |
         Where-Object { $_.Name -notmatch '^(B|C|E|F)-' -and $_.Name -ne 'README.md' } |
         Sort-Object Name

$found = New-Object System.Collections.Generic.List[object]

foreach ($p in $parts) {
    $lines = [System.IO.File]::ReadAllLines($p.FullName)
    $section = ''
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -match '^#{2,4}\s+(.*)$') { $section = $Matches[1].Trim() }

        $id = ''
        if ($l -match "^\*\*($idPat)$qual[.,]\*\*\s*(.*)$")    { $id = $Matches[1]; $rest = $Matches[2] }
        elseif ($l -match "^\*\*($idPat)$qual[.,]\s*(.*)$")    { $id = $Matches[1]; $rest = $Matches[2] }
        elseif ($l -match "^#{3,4}\s+($idPat)\s*$")            { $id = $Matches[1]; $rest = '' }
        if ($id -eq '') { continue }

        # Gather the block: from here to the next anchor or the next heading.
        $buf = New-Object System.Collections.Generic.List[string]
        if ($rest) { $buf.Add($rest) }
        for ($j = $i + 1; $j -lt $lines.Count; $j++) {
            $n = $lines[$j]
            if ($n -match "^\*\*$idPat" -or $n -match "^#{2,4}\s") { break }
            $buf.Add($n)
        }
        $block = ($buf -join ' ') -replace '\s+', ' '
        $block = $block.Trim()

        # Statement is everything before the first evidence marker.
        $stmt = $block
        $ev = ''
        $fal = ''
        if ($block -match "^(.*?)\s*$markers") { $stmt = $Matches[1] }
        if ($block -match "(?:Supporting evidence|Evidence)\s*:?\s*(.*?)(?:\*?Falsif|$)") { $ev = $Matches[1] }
        if ($block -match "(?:Falsifying evidence|Falsifier|Falsified by:?\*?\*?)\s*:?\s*(.*)$") { $fal = $Matches[1] }

        $found.Add([pscustomobject]@{
            Id      = $id
            File    = $p.Name
            Line    = $i + 1
            Section = $section
            Stmt    = ($stmt -replace '\*\*', '' -replace '\|', '/').Trim()
            Ev      = ($ev   -replace '\*\*', '' -replace '\|', '/').Trim()
            Fal     = ($fal  -replace '\*\*', '' -replace '\|', '/').Trim()
        })
        $i = $j - 1
    }
}

# Second pass: a finding defined only as a table row, which is what part 09
# does for its last two. Only ids that no anchor above already claimed are
# taken, so a part that BOTH states a finding and repeats it in a summary
# table is not counted twice.
$claimed = @{}
foreach ($x in $found) { $claimed[$x.Id] = $true }
foreach ($p in $parts) {
    $lines = [System.IO.File]::ReadAllLines($p.FullName)
    $section = ''
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^#{2,4}\s+(.*)$') { $section = $Matches[1].Trim() }
        if ($lines[$i] -notmatch "^\|\s*($idPat)\s*\|(.*)$") { continue }
        $id = $Matches[1]
        if ($claimed.ContainsKey($id)) { continue }
        $cells = ($Matches[2] -split '\|') | ForEach-Object { $_.Trim() }
        $claimed[$id] = $true
        $found.Add([pscustomobject]@{
            Id      = $id
            File    = $p.Name
            Line    = $i + 1
            Section = $section
            Stmt    = (($cells | Select-Object -First 1) -replace '\*\*','').Trim()
            Ev      = ''
            Fal     = (($cells | Select-Object -Last 1) -replace '\*\*','').Trim()
        })
    }
}

# Coverage: anchors found against ids mentioned anywhere.
Write-Host "lp-findings-index: $($parts.Count) parts read"
foreach ($p in $parts) {
    $ms = [regex]::Matches([System.IO.File]::ReadAllText($p.FullName), $idPat)
    $mentioned = @()
    foreach ($m in $ms) { $mentioned += $m.Value }
    $mentioned = @($mentioned | Sort-Object -Unique)
    $anchored = @($found | Where-Object { $_.File -eq $p.Name }).Count
    $flag = ''
    if ($anchored -lt $mentioned.Count) { $flag = '   (the difference is ids CITED here and defined elsewhere)' }
    Write-Host ("  {0,-34} anchors {1,3}   distinct ids mentioned {2,3}{3}" -f $p.Name, $anchored, $mentioned.Count, $flag)
}
$dupes = $found | Group-Object Id | Where-Object { $_.Count -gt 1 }
foreach ($d in $dupes) { Write-Host "  DEFINED TWICE: $($d.Name) in $(($d.Group.File) -join ', ')" }
Write-Host "lp-findings-index: $($found.Count) findings anchored"

if ($Out) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('| id | the finding | part | section | evidence | falsifier |')
    [void]$sb.AppendLine('|---|---|---|---|---|---|')
    function Clip([string]$s, [int]$n) { if ($s.Length -gt $n) { return $s.Substring(0, $n) } return $s }
    foreach ($x in ($found | Sort-Object { $_.Id -replace '\d+$','' }, { [int]([regex]::Match($_.Id,'\d+$').Value) })) {
        $row = '| ' + $x.Id + ' | ' + (Clip $x.Stmt 320) + ' | ' + ($x.File -replace '\.md$','') + ':' + $x.Line + ' | ' + (Clip $x.Section 60) + ' | ' + (Clip $x.Ev 200) + ' | ' + (Clip $x.Fal 200) + ' |'
        [void]$sb.AppendLine(($row -replace '[—–]', '--'))
    }
    [System.IO.File]::WriteAllText((Join-Path $Repo $Out), $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "lp-findings-index: wrote $Out"
}

