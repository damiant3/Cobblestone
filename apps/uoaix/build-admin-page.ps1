[CmdletBinding()]
param([switch]$Check)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('Chapter: AdminPage')
foreach ($entry in @(@('panel-login','admin-login.html'),@('panel-html','admin-panel.html'))) {
    $lines.Add('')
    $lines.Add("  $($entry[0]) : Text")
    $lines.Add("  $($entry[0]) =")
    $first = $true
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot ('web/' + $entry[1])))) {
        $escaped = $line.Replace('\','\\').Replace('"','\"')
        $prefix = if ($first) {'    '} else {'    & '}
        $lines.Add($prefix + '"' + $escaped + '\n"')
        $first = $false
    }
}
$expected = ($lines -join "`r`n") + "`r`n"
$target = Join-Path $PSScriptRoot 'AdminPage.codex'
if ($Check) {
    if (-not (Test-Path $target) -or [IO.File]::ReadAllText($target) -cne $expected) { throw 'AdminPage.codex differs from the HTML sources' }
    'Admin page source parity PASS'
} else { [IO.File]::WriteAllText($target,$expected,[Text.UTF8Encoding]::new($false)) }
