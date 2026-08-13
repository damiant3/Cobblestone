# apply-annotations.ps1 -- Apply an audited annotation-extraction report: cut prose from source and write the sidecars
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$Report,
    [string]$Only,
    [string]$Author,
    [switch]$Apply,
    [switch]$List
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ((-not $Report)) {
    $Report = (Join-Path $Repo 'docs\Reports\AnnotationExtract.txt')
} else {
    $Report = ([System.IO.Path]::GetFullPath($Report))
}
if ((-not $Author)) {
    $Author = ($Repo -replace '^.*[\\/][^-\\/]*-', '').ToLower()
}
$HeadPat = '^@@ (.+?) \| ([0-9]+)-([0-9]+) \| (.+?) \| (.+?) \| (.*)$'
$today = (Get-Date).ToString('yyyy-MM-dd')

if ((-not (Test-Path -PathType Leaf $Report))) {
    Write-Host ([string]'no report at ' + $Report)
    exit 2
}
$script:recs = ([System.Collections.Generic.List[object]]::new())
$script:staleFiles = 0
$script:cutBlocks = 0
$script:cutLines = 0
$script:annWritten = 0
$script:filesChanged = 0


function Apply-File([string]$f) {
    $full = (Join-Path $Repo $f)
    if ((-not (Test-Path -PathType Leaf $full))) {
        Write-Host ([string]'  MISSING ' + $f)
        $script:staleFiles++
        return $false
    }
    $lines = ([System.IO.File]::ReadAllLines($full))
    $del = ([System.Collections.Generic.HashSet[int]]::new())
    $anns = ([System.Collections.Generic.List[string]]::new())
    $stale = $false
    foreach ($r in $script:recs) {
        if (($r.file -ne $f)) {
            continue
        }
        $ok = $true
        $x = 0
        while ((($x -lt @($r.body).Count) -and $ok)) {
            $idx = (($r.start + $x) - 1)
            if (($idx -ge @($lines).Count)) {
                $ok = $false
            } else {
                if (($lines[$idx] -ne $r.body[$x])) {
                    $ok = $false
                }
            }
            $x++
        }

        if ((-not $ok)) {
            Write-Host ([string]([string]([string]'  STALE ' + $f) + ' line ') + $r.start)
            $stale = $true
            continue
        }
        if (($r.verb -eq 'KEEP')) {
            continue
        }
        $x = 0
        while (($x -lt @($r.body).Count)) {
            [void]$del.Add((($r.start + $x) - 1))
            $x++
        }
        if (($r.verb -eq 'ANNOTATE')) {
            [void]$anns.Add((@('  {', ([string]([string]'    "target": "' + $r.target) + '",'), ([string]([string]'    "kind": "' + $r.kind) + '",'), ([string]([string]'    "author": "' + $Author) + '",'), ([string]([string]'    "date": "' + $today) + '",'), ([string]([string]'    "body": "' + (((($r.body -join ' ').Trim() -replace '\s+', ' ') -replace '\\', '\\') -replace '"', '\"')) + '",'), '    "thread": null', '  }') -join "`n"))
        }

    }
    if ($stale) {
        Write-Host ([string]'  REFUSED (stale report): ' + $f)
        $script:staleFiles++
        return $false
    }
    if ((@($del).Count -eq 0)) {
        return $false
    }
    $script:cutLines += @($del).Count
    $script:filesChanged++
    if ((-not $Apply)) {
        Write-Host ([string]([string]([string]'  would cut ' + @($del).Count) + ' lines from ') + $f)
        return $true
    }
    Set-ItemProperty $full -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    $kept = ([System.Collections.Generic.List[string]]::new())
    $blank = 0
    $x = 0
    while (($x -lt @($lines).Count)) {
        if ((-not $del.Contains($x))) {
            $ln = $lines[$x]
            if (($ln.Trim() -eq '')) {
                $blank++
            } else {
                $blank = 0
            }
            if (($blank -le 1)) {
                [void]$kept.Add($ln)
            }
        }
        $x++
    }

    [System.IO.File]::WriteAllLines($full, $kept)
    if ((@($anns).Count -gt 0)) {
        $side = (Join-Path $Repo (Join-Path 'annotations' ($f -replace '\.codex$', '.json')))
        $prior = ''
        if ((Test-Path -PathType Leaf $side)) {
            $prior = ((([System.IO.File]::ReadAllText($side)) -replace '^\s*\[', '') -replace '\]\s*$', '').Trim()
        }
        $joined = ($anns -join ',
')
        if ($prior) {
            $joined = ([string]([string]$prior + ',
') + $joined)
        }
        New-Item -ItemType Directory -Force (Split-Path $side) | Out-Null
        Set-Content -Path $side -Value (@('[', $joined, ']') -join "`n") -Encoding UTF8
        $script:annWritten += @($anns).Count
    }
    Write-Host ([string]([string]([string]'  cut ' + @($del).Count) + ' lines from ') + $f)
    return $true
}


$curFile = ''
$curStart = 0
$curStop = 0
$curTarget = ''
$curVerb = ''
$curKind = ''
$curBody = ([System.Collections.Generic.List[string]]::new())
foreach ($rl in ([System.IO.File]::ReadAllLines($Report))) {
    if (($rl -match $HeadPat)) {
        if ($curFile) {
            [void]$script:recs.Add([ordered]@{ 'file' = $curFile; 'start' = $curStart; 'stop' = $curStop; 'target' = $curTarget; 'verb' = $curVerb; 'kind' = $curKind; 'body' = $curBody })
        }

        $curFile = $matches[1]
        $curStart = [int]$matches[2]
        $curStop = [int]$matches[3]
        $curTarget = $matches[4]
        $vfull = $matches[5].Trim()
        $vparts = ($vfull -split ' ')
        $curVerb = $vparts[0]
        $curKind = $(if ((@($vparts).Count -gt 1)) { $vparts[1] } else { 'rationale' })
        $curBody = ([System.Collections.Generic.List[string]]::new())

        continue
    }
    if ($rl.StartsWith('> ')) {
        [void]$curBody.Add($rl.Substring(2))
    }
}
if ($curFile) {
    [void]$script:recs.Add([ordered]@{ 'file' = $curFile; 'start' = $curStart; 'stop' = $curStop; 'target' = $curTarget; 'verb' = $curVerb; 'kind' = $curKind; 'body' = $curBody })
}

Write-Host ([string]([string]'report: ' + @($script:recs).Count) + ' records')


$fileSet = ([System.Collections.Generic.HashSet[string]]::new())
foreach ($r in $script:recs) {
    if (($r.verb -eq 'KEEP')) {
        continue
    }
    if (($Only -and ($r.file -ne $Only))) {
        continue
    }
    [void]$fileSet.Add($r.file)
}
if ($List) {
    foreach ($f in $fileSet) {
        Write-Host $f
    }
    $sideSet = ([System.Collections.Generic.HashSet[string]]::new())
    foreach ($r in $script:recs) {
        if (($r.verb -ne 'ANNOTATE')) {
            continue
        }
        if (($Only -and ($r.file -ne $Only))) {
            continue
        }
        $sp = (Join-Path $Repo (Join-Path 'annotations' ($r.file -replace '\.codex$', '.json')))
        if ((Test-Path -PathType Leaf $sp)) {
            [void]$sideSet.Add($sp)
        }
    }
    foreach ($s in $sideSet) {
        Write-Host $s
    }
    exit 0
}
if ((-not $Apply)) {
    Write-Host 'DRY RUN -- nothing is written. Pass -Apply to mutate.'
}
foreach ($f in $fileSet) {
    [void](Apply-File $f)
}

Write-Host ([string]([string]([string]([string]'files ' + $script:filesChanged) + ', lines cut ') + $script:cutLines) + ([string]([string]', annotations ' + $script:annWritten) + ([string]', refused ' + $script:staleFiles)))


if ($Apply) {
    $auditSet = ([System.Collections.Generic.HashSet[string]]::new())
    foreach ($r in $script:recs) {
        if (($Only -and ($r.file -ne $Only))) {
            continue
        }
        [void]$auditSet.Add($r.file)
    }
    $ledger = (Join-Path $Repo (Join-Path 'annotations' 'AUDITED.txt'))
    $ledgerLines = ([System.Collections.Generic.List[string]]::new())
    if ((Test-Path -PathType Leaf $ledger)) {
        foreach ($ll in ([System.IO.File]::ReadAllLines($ledger))) {
            if (($ll.Trim() -eq '')) {
                continue
            }
            if ($auditSet.Contains(($ll -replace ' [^ ]+ [0-9-]+$', ''))) {
                continue
            }
            [void]$ledgerLines.Add($ll)
        }
    }
    foreach ($f in $auditSet) {
        [void]$ledgerLines.Add(([string]([string]([string]([string]$f + ' ') + $Author) + ' ') + $today))
    }
    [void]$ledgerLines.Sort()
    New-Item -ItemType Directory -Force (Split-Path $ledger) | Out-Null
    Set-Content -Path $ledger -Value ($ledgerLines -join '
') -Encoding UTF8
    Write-Host ([string]([string]'ledger: ' + @($auditSet).Count) + ' chapters recorded')
}
