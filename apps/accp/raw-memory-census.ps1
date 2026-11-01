[CmdletBinding()]
param([string]$Repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path, [switch]$Write)
# AccpInformationFlow section 3 and L-CENSUS. Every builtin in the compiler's table is
# either on the conduit's raw-memory list (AccpFlow.codex, afl-raw-memory) or on the
# reviewed address-free list in raw-memory-census.json. Exit 1 on a table builtin on
# neither list, on both, or a listed name the table no longer holds. -Write rewrites
# the reviewed list as the table minus the raw list, for use after a review only.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$table = [IO.File]::ReadAllText((Join-Path $Repo 'codex/compiler/Types/Builtins.codex'))
$names = @([regex]::Matches($table, 'bs-name = "([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$flow = [IO.File]::ReadAllText((Join-Path $Repo 'apps/accp/AccpFlow.codex'))
$block = [regex]::Match($flow, '(?s)  afl-raw-memory = \[(.*?)\r?\n  \]')
if (-not $block.Success) { throw 'afl-raw-memory list not found in AccpFlow.codex' }
$raw = @([regex]::Matches($block.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$regFile = Join-Path $PSScriptRoot 'raw-memory-census.json'
if ($Write) {
    $free = @($names | Where-Object { $raw -notcontains $_ })
    [ordered]@{ 'address-free' = $free } | ConvertTo-Json | Set-Content -LiteralPath $regFile -Encoding utf8
}
$reviewed = @((Get-Content -LiteralPath $regFile -Raw -Encoding utf8 | ConvertFrom-Json).'address-free')
$problems = [Collections.Generic.List[string]]::new()
foreach ($n in $names) {
    $r = $raw -contains $n; $f = $reviewed -contains $n
    if ($r -and $f) { $problems.Add("on both lists: $n") }
    elseif (-not $r -and -not $f) { $problems.Add("unclassified builtin: $n") }
}
foreach ($n in @($raw + $reviewed)) { if ($names -notcontains $n) { $problems.Add("listed, not in the table: $n") } }
if (@($raw | Group-Object | Where-Object Count -gt 1).Count) { $problems.Add('duplicate raw-memory entry') }
# Rung 1 reads an effect label off a name's IR type, and a builtin whose table type is
# None or a bare EffectfulTy prints no row there, so each must be named by AccpFlowIr's
# afi-nullary or afi-unmodelled. True, False and Nothing are values, not operations.
$fir = [IO.File]::ReadAllText((Join-Path $Repo 'apps/accp/AccpFlowIr.codex'))
$fnBody = { param($name) $m = [regex]::Match($fir, "(?s)  $name \(n\) =(.*?)\r?\n\r?\n"); if (-not $m.Success) { throw "$name not found in AccpFlowIr.codex" }; @([regex]::Matches($m.Groups[1].Value, 'n == "([^"]+)"') | ForEach-Object { $_.Groups[1].Value }) }
$named = @((& $fnBody 'afi-nullary') + (& $fnBody 'afi-unmodelled'))
$rowless = @([regex]::Matches($table, '(?s)bs-name = "([^"]+)",(?:(?!bs-name).)*?bs-type = (None|Just \(EffectfulTy)') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notin 'True', 'False', 'Nothing' })
foreach ($n in $rowless) { if ($named -notcontains $n) { $problems.Add("row-less builtin unnamed by AccpFlowIr: $n") } }
foreach ($n in $named) { if ($names -notcontains $n) { $problems.Add("AccpFlowIr names a builtin the table no longer holds: $n") } }
$problems | ForEach-Object { [Console]::Error.WriteLine($_) }
"raw-memory census: $($names.Count) builtins; $($raw.Count) raw memory, $($reviewed.Count) address-free; $($rowless.Count) row-less, all named by AccpFlowIr unless listed; $($problems.Count) problems"
if ($problems.Count) { exit 1 }
