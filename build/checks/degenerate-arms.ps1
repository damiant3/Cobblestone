[CmdletBinding()]
param([string]$Repo = (Split-Path (Split-Path $PSScriptRoot)), [string]$Registry = '')
# L-DEGENERATE and L-SPECCEILING. Every function a crypto chapter exports that answers
# Boolean or Maybe is a trust decision until its registry row says otherwise, and a
# trust decision names the arm that feeds it each degenerate class and the exact
# .expected line that proves the refusal. Exit 1 on an unregistered function, a row
# naming nothing discovered, an arm that never calls its function, or a missing line.
# Limits: discovery is by chapter (a crypto root or a chapter citing one) and by
# result type; a trust decision outside codex/foreword, or one answering another
# type, is not seen. An `open` row is printed, not failed: it is a named gap.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path -LiteralPath $Repo).Path
if (-not $Registry) { $Registry = Join-Path $PSScriptRoot 'degenerate-arms.json' }
$reg = Get-Content -LiteralPath $Registry -Raw -Encoding utf8 | ConvertFrom-Json
$problems = [Collections.Generic.List[string]]::new()

$files = @(Get-ChildItem (Join-Path $Repo 'codex'), (Join-Path $Repo 'apps') -Recurse -Filter *.codex -File |
    Where-Object { $_.FullName -notmatch '[\\/](build-output|old)[\\/]' })
$text = @{}
foreach ($f in $files) { $text[$f.FullName] = [IO.File]::ReadAllText($f.FullName) }

$roots = @($reg.roots)
$rootRx = ($roots | ForEach-Object { [regex]::Escape($_) }) -join '|'
$foreword = @($files | Where-Object { $_.FullName -match '[\\/]codex[\\/]foreword[\\/]' })
foreach ($r in $roots) {
    if (-not ($foreword | Where-Object BaseName -eq $r)) { $problems.Add("root chapter not found: $r") }
}
$chapters = @($foreword | Where-Object {
    $_.BaseName -match "^($rootRx)$" -or $text[$_.FullName] -match "(?m)^\s*cites \w+ chapter ($rootRx)\s*$" })

$found = [ordered]@{}
foreach ($c in $chapters) {
    foreach ($m in [regex]::Matches($text[$c.FullName], '(?m)^  ([a-z][a-z0-9-]*) : [^\r\n]*-> (Boolean|Maybe\b[^\r\n]*)\s*$')) {
        $fn = $m.Groups[1].Value
        $rx = "(?<![a-z0-9-])$([regex]::Escape($fn))(?![a-z0-9-])"
        if ($files | Where-Object { $_.FullName -ne $c.FullName -and $text[$_.FullName] -match $rx } | Select-Object -First 1) {
            $found["$($c.BaseName).$fn"] = $fn
        }
    }
}

$rows = $reg.functions.PSObject.Properties
$armed = 0; $lines = 0; $waived = 0; $open = [Collections.Generic.List[string]]::new()
foreach ($key in $found.Keys) {
    if (-not $reg.functions.PSObject.Properties[$key]) { $problems.Add("unregistered trust decision: $key"); continue }
}
foreach ($row in $rows) {
    $key = $row.Name; $v = $row.Value
    if (-not $found.Contains($key)) { $problems.Add("registry row names nothing discovered: $key"); continue }
    $kinds = @('arm', 'waive', 'open' | Where-Object { $v.PSObject.Properties[$_] })
    if ($kinds.Count -ne 1) { $problems.Add("row needs exactly one of arm, waive, open: $key"); continue }
    switch ($kinds[0]) {
        'waive' { if (-not $v.waive) { $problems.Add("waive without a reason: $key") } else { $waived++ } }
        'open'  { if (-not $v.open) { $problems.Add("open without its owning row: $key") } else { $open.Add("OPEN ${key}: $($v.open)") } }
        'arm' {
            foreach ($arm in @($v.arm)) {
                $src = Join-Path $Repo "$($arm.test).codex"
                $exp = Join-Path $Repo "$($arm.test).expected"
                if (-not (Test-Path -LiteralPath $src) -or -not (Test-Path -LiteralPath $exp)) { $problems.Add("${key}: arm missing: $($arm.test)"); continue }
                $rx = "(?<![a-z0-9-])$([regex]::Escape($found[$key]))(?![a-z0-9-])"
                if ([IO.File]::ReadAllText($src) -notmatch $rx) { $problems.Add("${key}: $($arm.test) never names $($found[$key])") }
                $have = [Collections.Generic.HashSet[string]]::new([string[]]@(Get-Content -LiteralPath $exp | ForEach-Object { $_.TrimEnd() }))
                if (@($arm.lines).Count -eq 0) { $problems.Add("${key}: $($arm.test) lists no class line") }
                foreach ($l in @($arm.lines)) {
                    if ($have.Contains($l)) { $lines++ } else { $problems.Add("${key}: $($arm.test).expected lacks: $l") }
                }
            }
            $armed++
        }
    }
}

$open | ForEach-Object { $_ }
if ($problems.Count) { $problems | ForEach-Object { [Console]::Error.WriteLine($_) } }
"degenerate-arms: $($found.Count) trust decisions in $($chapters.Count) chapters; $armed armed ($lines class lines), $waived waived, $($open.Count) open, $($problems.Count) problems"
if ($problems.Count) { exit 1 }
