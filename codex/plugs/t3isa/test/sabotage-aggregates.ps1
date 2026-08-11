# Negative controls for the aggregate half of the T3ISA plug.
#
# The gate's own control mutates an arithmetic instruction, which says nothing
# about whether record and variant LAYOUT is being compared. These arms mutate
# the layout decisions themselves, one at a time, in the emitter source, and
# rebuild the plug against each.
#
# The arms are shaped, not generic (L-FALSIF). Each predicts WHICH output rows
# move and which survive, and the survivors are the evidence: an arm that took
# every row would be consistent with a plug that simply stopped working. Two of
# the three below leave most rows standing, and one leaves a survivor that
# survives for an arithmetic reason rather than because the arm missed it.
#
# Requires the external toolchain, which is on this machine only.
param(
  [string]$Manitc = 'D:\Toolchain-Ternary\target-v13\release\manitc.exe'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$here    = $PSScriptRoot
$plugDir = Split-Path $here -Parent
$emitter = Join-Path $plugDir 'T3IsaEmitter.codex'
$src     = Join-Path $here 'aggregates.codex'
$outDir  = Join-Path $plugDir 'build-output'
$t3b     = Join-Path $outDir 'sabotage-agg.t3b'
if (-not (Test-Path $Manitc)) { throw "the oracle is not built: $Manitc" }

function Build-Plug {
  & pwsh -NoProfile -File (Join-Path $plugDir 'build.ps1') *> $null
  if ($LASTEXITCODE -ne 0) { throw "plug build failed" }
}
# Returns the program's output rows, or @('<refused or faulted>') when the plug
# would not produce an artifact. A refusal is a legitimate arm outcome and must
# not be confused with agreement.
function Get-Rows {
  & pwsh -NoProfile -File (Join-Path $plugDir 'run.ps1') -Src $src -Out $t3b *> $null
  if ($LASTEXITCODE -ne 0) { return @('<plug refused>') }
  $raw = & $Manitc run-t3 $t3b 2>&1 | Out-String
  $rows = @($raw -split "`r?`n" |
            Where-Object { $_ -notmatch '^\[T3ISA\] running' -and $_.Trim() -ne '' })
  if ($rows.Count -eq 0) { return @('<no output>') }
  return $rows
}
function Compare-Rows($baseRows, $armRows) {
  $moved = @()
  $n = [Math]::Max($baseRows.Count, $armRows.Count)
  for ($i = 0; $i -lt $n; $i++) {
    $b = if ($i -lt $baseRows.Count) { $baseRows[$i] } else { '<missing>' }
    $a = if ($i -lt $armRows.Count)  { $armRows[$i] }  else { '<missing>' }
    if ($b -ne $a) { $moved += "$b  ->  $a" }
  }
  return $moved
}

$arms = @(
  @{ name   = 'record fields stored by written position'
     find   = 't3-store-obj "R9" (list-at order i))'
     repl   = 't3-store-obj "R9" i)'
     expect = '1 row: only "point", built with its fields in the opposite order to their declaration. "manhattan" survives because addition commutes and "span" because Segment IS written in declaration order.' }
  @{ name   = 'variant tag read from offset one'
     find   = 'in let test = t3-load-obj "R23" 0'
     repl   = 'in let test = t3-load-obj "R23" 1'
     expect = '7 rows, and the shape of them is the point. "dot" SURVIVES: reading one word past a nullary object finds unwritten heap, which is zero, and Dot tag IS zero, so the wrong read coincides with the right answer. "line 11" then reads 11 as its tag, no branch claims 11, and the match fall-through TRAPS, which is why every row after it is missing rather than wrong. The three record rows are never reached. That trap firing here is the only sighting of the exhaustiveness backstop doing its job.' }
  @{ name   = 'constructor payload read from the tag slot'
     find   = '(acc & t3-load-obj "R9" (i + 1) & t3-store-slot c "R9" dst)'
     repl   = '(acc & t3-load-obj "R9" i & t3-store-slot c "R9" dst)'
     expect = '3 rows: exactly the constructors carrying a payload. Each bound name shifts down by one INDEPENDENTLY, so Rect 5 5 reads w from the tag slot and h from the first field, giving 2 and 5 rather than two equal values: 10, not the 100 an equal-sides reading would have left standing. The four Tri rows and "dot" survive for having no payload to misread, and the three record rows carry no tag to shift past.' }
  @{ name   = 'heap ceiling lowered to the heap base'
     find   = 't3-heap-ceiling = 49152'
     repl   = 't3-heap-ceiling = 32768'
     expect = 'every row: the first allocation of any size is already past the ceiling, so the guard traps before the first line is printed. A guard that has never been shown to fire is worth nothing, and nothing in the ordinary run comes near 16,384 words.' }
)

$backup = "$emitter.sabotage-backup"
Copy-Item -LiteralPath $emitter -Destination $backup -Force
$original = Get-Content -LiteralPath $emitter -Raw
$fired = 0
try {
  Build-Plug
  $base = Get-Rows
  Write-Output "baseline: $($base.Count) rows"
  $base | ForEach-Object { Write-Output "  $_" }
  Write-Output ""
  if ($base[0] -like '<*') { throw "the unmutated plug produces nothing; fix that before reading any arm." }

  foreach ($arm in $arms) {
    if (-not $original.Contains($arm.find)) {
      Write-Output ("ARM IS STALE: {0}" -f $arm.name)
      Write-Output ("  pattern not found in the emitter: {0}" -f $arm.find)
      continue
    }
    $original.Replace($arm.find, $arm.repl) | Set-Content -LiteralPath $emitter -NoNewline
    Build-Plug
    $rows  = @(Get-Rows)
    # @() on the assignment: a one-row difference otherwise unrolls to a bare
    # string and .Count is not a property of one.
    $moved = @(Compare-Rows $base $rows)
    if ($moved.Count -gt 0) { $fired++ }
    $verdict = if ($moved.Count -gt 0) { "FIRES, moves $($moved.Count) of $($base.Count) rows" } else { "DID NOT FIRE" }
    Write-Output ("{0}: {1}" -f $arm.name, $verdict)
    Write-Output ("  predicted: {0}" -f $arm.expect)
    $moved | ForEach-Object { Write-Output "    $_" }
    Write-Output ""
  }
}
finally {
  Set-Content -LiteralPath $emitter -Value $original -NoNewline
  Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
}

Build-Plug
$restored = @(Get-Rows)
$diff = @(Compare-Rows $base $restored)
Write-Output ("restored emitter: {0}" -f $(if ($diff.Count -eq 0) { "baseline reproduced, $($restored.Count) rows" } else { "STILL MUTATED, $($diff.Count) rows differ" }))
Write-Output "arms that fired: $fired of $($arms.Count)"
exit ([int]($fired -ne $arms.Count -or $diff.Count -ne 0))
