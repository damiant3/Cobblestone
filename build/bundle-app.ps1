# bundle-app.ps1 -- Bundle a Codex application source file with all its transitive cited dependencies into a single .codex file
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Src,
    [Parameter(Mandatory=$true)]
    [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'quire-map.ps1')


$srcPath = (Resolve-Path $Src).Path
$rootLines = [System.IO.File]::ReadAllLines($srcPath)


try {
    $ordered = (Resolve-CiteOrder -RootLines $rootLines -Repo $Repo)
} catch {
    [Console]::Error.WriteLine(([string]'MISSING: ' + $_.Exception.Message))
    exit 3
}


$lines = (Format-CiteChapters -Ordered $ordered)

foreach ($l in $rootLines) {
    $lines.Add($l)
}
$lines.Add('')


$body = ([string]($lines -join "`n") + "`n")
[System.IO.File]::WriteAllText($Out, $body, ([System.Text.UTF8Encoding]::new($false)))
Write-Host ([string]([string]([string]([string]'[bundle-app] ' + $ordered.Count) + ' dependencies + root -> ') + $Out) + ([string]([string]' (' + $body.Length) + ' bytes)'))
