# check-source-cr-trigger.ps1 -- the Perforce change-content trigger that refuses
# a submit carrying a text-typed file with a carriage return that is not a line
# terminator. Server-side twin of build/check-source-cr.ps1 (the gate reader).
#
# Installed copy: D:\PerforceRoot\triggers\check-source-cr-trigger.ps1, named in
# `p4 triggers` as check-source-cr on change-content //Codex/.... The depot
# file is the source; re-copy it there after editing (PerforceProcess.md 4.8).
# Hand-written; no generator under codex/build/ emits this.
#
# WHY A TRIGGER. A stray CR reaches the depot as content, survives every
# line-end translation, and then (a) puts the CR inside a chapter NAME so every
# cite of it fails as a missing chapter (CDX3007, PR 133), and (b) makes git
# classify the file as binary under autocrlf, so the public mirror publishes it
# CRLF throughout while every other file is LF. Neither reader in the tree runs
# on every submit; the server does.
#
# WHICH FILES. Every file whose Perforce type is text-like (text, unicode, utf8,
# utf16 and their modifiers), which the typemap gives to 56 extensions. A data
# file that must carry a raw CR is typed binary instead; nothing in the tree
# needed one when this was widened (census of 9,549 files, 2026-09-08: only 30
# .failing sidecars carried a stray CR, all the \r\r\n shape, all fixed).
#
# p4 print on Windows writes the file in the client's line-end form, so the
# bytes read here are CRLF-terminated when translation applies and LF-terminated
# when it does not. Both forms are graded: in CRLF form a CR is bad unless it is
# exactly the CR of a CR LF pair with no CR before it; in LF form (no CR LF pair
# at all) any CR is bad; a file with both forms is bad as a whole.
param(
    [Parameter(Mandatory)][string]$Changelist,
    [Parameter(Mandatory)][string]$ServerPort
)

$ErrorActionPreference = 'Stop'
$p4 = @('-p', $ServerPort, '-u', 'damian', '-C', 'utf8')

function Stray-CrLine([byte[]]$b) {
    $lf = 0; $crlf = 0
    for ($i = 0; $i -lt $b.Length; $i++) {
        if ($b[$i] -eq 10) { $lf++; if ($i -gt 0 -and $b[$i - 1] -eq 13) { $crlf++ } }
    }
    $crlfForm = ($crlf -gt 0)
    $mixed = ($crlf -gt 0 -and $crlf -ne $lf)
    $line = 1
    for ($i = 0; $i -lt $b.Length; $i++) {
        if ($b[$i] -eq 10) { $line++; continue }
        if ($b[$i] -ne 13) { continue }
        if ($mixed) { return $line }
        if (-not $crlfForm) { return $line }
        $terminator = ($i + 1 -lt $b.Length) -and ($b[$i + 1] -eq 10) -and (($i -eq 0) -or ($b[$i - 1] -ne 13))
        if (-not $terminator) { return $line }
    }
    return 0
}

$bad = [System.Collections.Generic.List[string]]::new()
$tag = & p4 @p4 -ztag files "//Codex/...@=$Changelist" 2>&1
$file = $null; $action = $null; $type = $null
foreach ($l in @($tag) + @('... depotFile END')) {
    $l = [string]$l
    if ($l -like '... depotFile *') {
        if ($file -and $action -notlike 'delete' -and $action -notlike 'move/delete' -and $action -notlike 'purge' -and $type -match '^(text|unicode|utf8|utf16|ktext|kxtext|xtext)') {
            $tmp = [System.IO.Path]::GetTempFileName()
            try {
                & p4 @p4 print -q -o $tmp "$file@=$Changelist" | Out-Null
                $line = Stray-CrLine ([System.IO.File]::ReadAllBytes($tmp))
                if ($line -gt 0) { $bad.Add("$file line $line") }
            } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
        }
        $file = $l.Substring(13).Trim(); $action = $null; $type = $null
    } elseif ($l -like '... action *') { $action = $l.Substring(11).Trim() }
    elseif ($l -like '... type *') { $type = $l.Substring(9).Trim() }
}

if ($bad.Count -eq 0) { exit 0 }
Write-Output "check-source-cr: a text file in change $Changelist carries a carriage return that is not a line terminator."
Write-Output "In a .codex a CR at the end of a Chapter: header puts the CR in the chapter NAME, so every cites of it fails."
Write-Output "Strip the byte (PerforceProcess.md 4.8); the line terminator itself is not the problem."
foreach ($x in $bad) { Write-Output "  $x" }
exit 1
