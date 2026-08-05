# check-apps.ps1 -- Assert runtime invariants across every generated app page
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# Pages not produced by the widget pipeline
$Skip = @()


# name -> regex the page must match
$Invariants = [ordered]@{ 'html-shell' = '(?s)^<!(DOCTYPE|doctype) html'; 'click-dispatch' = '_wkOnClick'; 'state-runtime' = 'function state_get\('; 'render-loop' = 'function set_render\(|_render'; 'handler-binding' = 'register_handlers\('; 'theme-css' = 'function theme_to_css\('; 'audio-runtime' = 'function play_tone\('; 'input-channel' = 'register_input_handler|function _wkOnInput|on-input' }


$failed = 0
$checked = 0
foreach ($html in Get-ChildItem (Join-Path $Repo 'apps\*\web\*.html') -File) {
    $base = $html.BaseName
    # only check artifacts produced by the HTML plug pipeline
    $pages = @((Get-ChildItem $html.Directory.Parent.FullName -Filter '*Page.codex' -File | Where-Object { (($_.BaseName -replace 'WebPage$|Page$', '').ToLower() -eq $base) }))
    if ((@($pages).Count -eq 0)) {
        continue
    }
    if (($Skip -contains $base)) {
        continue
    }
    $txt = ([System.IO.File]::ReadAllText($html.FullName))
    $bad = @()
    foreach ($k in $Invariants.Keys) {
        if ((-not ($txt -match $Invariants[$k]))) {
            $bad += $k
        }
    }
    $lf = @(([regex]::Matches($txt, "`n"))).Count
    $crlf = @(([regex]::Matches($txt, "`r`n"))).Count
    if (($lf -ne $crlf)) {
        $bad += 'crlf-endings'
    }

    $checked++
    if ((@($bad).Count -gt 0)) {
        Write-Host ('FAIL {0,-12} {1}' -f $base, ($bad -join ', ')) -ForegroundColor Red
        $failed++
    } else {
        Write-Host ('ok   {0}' -f $base) -ForegroundColor Green
    }

}


Write-Host ''
if (($failed -gt 0)) {
    Write-Host ([string]([string]([string]$failed + ' of ') + $checked) + ' pages violate invariants') -ForegroundColor Red
    exit 1
} else {
    Write-Host ([string]([string]'all ' + $checked) + ' pages carry the shared runtime') -ForegroundColor Green
}
