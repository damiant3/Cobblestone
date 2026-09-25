# Every state word a diagnostic stage can answer has a verdict row, and every
# stage the ladder names has a verdict function (DiagnosticStick.md, "The
# report: what needs to happen"). Static, no build: it reads the stage
# chapters' declared vocabularies and the ladder's verdict functions.
#
#   build/check-diag-verdicts.ps1            # exit 0 when every word has a row
#
# The convention it enforces, and which the ladder relies on at runtime too:
#   - a stage chapter build/boot/diag/Diag*.codex declares
#       <tag>-states : List Text = ["a", "b", ...]
#     naming every state word its <tag>-run can return;
#   - Diag.codex names the stage in dg-stage-name as `if i == N then "<name>"`
#     and routes it in dg-stage-run as `dsm-run c` (the tag before -run) and in
#     dg-verdict-stage as `dg-verdict-<name> s`;
#   - dg-verdict-<name> carries `if s == "<word>" then` for every declared word.
# A stage whose vocabulary is not declared, a name with no verdict function, or
# a word with no row is reported by file and name and fails the check. The
# ladder's runtime fallback prints "no verdict row for <stage> <word>" on the
# glass, so a miss here is visible on a boot as well; this catches it before.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Dir = Join-Path $Repo 'build\boot\diag'
$ladder = Get-Content (Join-Path $Dir 'Diag.codex') -Raw
$bad = 0

# stage number -> name, from dg-stage-name
$names = @{}
$m = [regex]::Match($ladder, 'dg-stage-name \(i\) =(.*?)\r?\n\r?\n', 'Singleline')
if (-not $m.Success) { Write-Host 'FAIL: dg-stage-name not found in Diag.codex'; exit 1 }
foreach ($mm in [regex]::Matches($m.Groups[1].Value, 'i == (\d+) then "([a-z0-9-]+)"')) { $names[[int]$mm.Groups[1].Value] = $mm.Groups[2].Value }
# stage number -> run tag, from dg-stage-run
$tags = @{}
$m = [regex]::Match($ladder, 'dg-stage-run \(c\) \(i\) =(.*?)\r?\n\r?\n', 'Singleline')
if (-not $m.Success) { Write-Host 'FAIL: dg-stage-run not found in Diag.codex'; exit 1 }
foreach ($mm in [regex]::Matches($m.Groups[1].Value, 'i == (\d+) then ([a-z0-9]+)-run c')) { $tags[[int]$mm.Groups[1].Value] = $mm.Groups[2].Value }
$count = [int]([regex]::Match($ladder, 'dg-stage-count : Integer = (\d+)').Groups[1].Value)

# declared vocabularies: tag -> words, from every stage chapter
$vocab = @{}
foreach ($f in Get-ChildItem $Dir -Filter 'Diag*.codex' -File) {
    $src = Get-Content $f.FullName -Raw
    foreach ($mm in [regex]::Matches($src, '([a-z0-9]+)-states : List Text = \[([^\]]*)\]')) {
        $words = @([regex]::Matches($mm.Groups[2].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        $vocab[$mm.Groups[1].Value] = @{ words = $words; file = $f.Name }
    }
}

for ($i = 1; $i -le $count; $i++) {
    if (-not $names.ContainsKey($i)) { Write-Host "FAIL: stage $i has no name in dg-stage-name"; $bad++; continue }
    if (-not $tags.ContainsKey($i)) { Write-Host "FAIL: stage ${i} ($($names[$i])) is not routed in dg-stage-run"; $bad++; continue }
    $name = $names[$i]; $tag = $tags[$i]
    if (-not $vocab.ContainsKey($tag)) { Write-Host "FAIL: stage ${name}: no ${tag}-states : List Text = [...] in any build/boot/diag/Diag*.codex"; $bad++; continue }
    $vm = [regex]::Match($ladder, "dg-verdict-$name \(s\) =(.*?)\r?\n\r?\n", 'Singleline')
    if (-not $vm.Success) { Write-Host "FAIL: stage ${name}: no dg-verdict-$name in Diag.codex"; $bad++; continue }
    if ($ladder -notmatch "if i == $i then dg-verdict-$name s") { Write-Host "FAIL: stage ${name}: dg-verdict-stage does not route $i to dg-verdict-$name"; $bad++ }
    $body = $vm.Groups[1].Value
    foreach ($w in $vocab[$tag].words) {
        if ($body -notmatch ('if s == "' + [regex]::Escape($w) + '" then')) {
            Write-Host "FAIL: stage ${name}: state `"$w`" ($($vocab[$tag].file)) has no row in dg-verdict-$name"; $bad++
        }
    }
    # words with a row but not declared: the vocabulary is the contract, so a
    # row for an undeclared word is a stale row or a missing declaration
    foreach ($mm in [regex]::Matches($body, 'if s == "([^"]+)" then')) {
        $w = $mm.Groups[1].Value
        if ($vocab[$tag].words -notcontains $w) { Write-Host "FAIL: stage ${name}: dg-verdict-$name has a row for `"$w`" that $($vocab[$tag].file) does not declare"; $bad++ }
    }
    Write-Host ("  {0,-8} {1,-6} {2} states, verdict rows present" -f $name, $tag, $vocab[$tag].words.Count)
}
if ($bad -gt 0) { Write-Host "check-diag-verdicts: $bad problem(s)"; exit 1 }
Write-Host "check-diag-verdicts: OK ($count stages)"
exit 0
