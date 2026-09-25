# check-annotation-targets.ps1 -- refuse an annotation whose `function:<name>`
# target no chapter defines, and a sidecar that mirrors no source file.
#
#   pwsh build/check-annotation-targets.ps1
#   pwsh build/check-annotation-targets.ps1 -Annotations <dir>   # sabotage arm
#
# Hand-maintained, like check-backlog-ids.ps1 beside it. Text only, starts no
# guest. An annotation reads as true while its subject is gone, so a reader takes
# a rationale for code that does not exist (L-ROWROT). `build/build.ps1` runs it on every gate.
#
# A sidecar `annotations/<path>.json` mirrors `<path>.codex`. A function target
# resolves when that chapter carries `^  <name>` followed by `:` or `(`, which is
# a definition's signature or equation line at column 2. Only `function:` targets
# are graded; `chapter:` and `section:` targets are not.
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$Annotations = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Annotations) { $Annotations = Join-Path $Root 'annotations' }
$Annotations = (Resolve-Path $Annotations).Path

$targets = 0
$findings = New-Object System.Collections.Generic.List[string]
foreach ($j in Get-ChildItem -Path $Annotations -Recurse -Filter *.json -File) {
    $rel = $j.FullName.Substring($Annotations.Length + 1)
    $src = Join-Path $Root ([IO.Path]::ChangeExtension($rel, '.codex'))
    if (-not (Test-Path -LiteralPath $src)) {
        $findings.Add("no source: annotations\$rel mirrors no $([IO.Path]::ChangeExtension($rel, '.codex'))")
        continue
    }
    $names = [regex]::Matches([IO.File]::ReadAllText($j.FullName), '"target"\s*:\s*"function:([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value }
    if (-not $names) { continue }
    $code = [IO.File]::ReadAllText($src)
    foreach ($n in $names) {
        $targets++
        if ($code -notmatch ('(?m)^  ' + [regex]::Escape($n) + '\s*[:(]')) {
            $findings.Add("unresolved: annotations\$rel names function:$n, which the chapter does not define")
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | ForEach-Object { Write-Host "  $_" }
    Write-Host "check-annotation-targets: $($findings.Count) finding(s) over $targets function target(s). Retarget each at the successor that still does what it says, or delete it; never sweep."
    exit 1
}
Write-Host "check-annotation-targets: $targets function target(s), every one defined in the chapter its sidecar mirrors."
exit 0
