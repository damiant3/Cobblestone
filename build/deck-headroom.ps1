# How much deck room does each unit ACTUALLY need, against what the
# derivation hands it?
#
#   pwsh build/deck-headroom.ps1                          # the whole corpus
#   pwsh build/deck-headroom.ps1 -List units.txt -Jobs 4
#   pwsh build/deck-headroom.ps1 -List units.txt -Top 40  # more of the table
#   pwsh build/deck-headroom.ps1 -Quire codex\build -WithSelf -MinMargin 1.25
#
# -MinMargin exits 1 when any unit's derived scale is less than n times what it
# requires, or when the kernel cannot answer the question at all. It asserts the
# RATIO rather than a point count because the tight cases have different
# denominators -- 43 against a floor of 64, 67 against a clamp of 100 -- and a
# point-count threshold passes the clamped one at any size. `build/build.ps1`
# runs it over `codex/build` plus the compiler's own unit.
#
# ProportionalDecks.md prescribes exactly this validation for anyone who
# changes deck-scale-min, deck-scale-margin or deck-scale-anchor, and until
# now it had no runner: the only instrument was bisecting -Decks by output
# equality, which is one VM boot per arm per unit and was therefore run over a
# handful of units and never repeated. This does the corpus in one -Measure
# run each.
#
# HOW ONE RUN REPLACES A FAN. `scaled-floor` computes a phase's room as
# `(floor - band) / 100 * pct` with the divide FIRST, so the room a phase has
# at scale s is exactly `trunc((floor - band) / 100) * s`, and the write-path
# guard fires when the phase's used bytes pass it. `-Measure` reports used per
# phase. Invert: the scale a phase requires is `ceil(used / perPoint)`, and the
# scale the unit requires is the max over its phases. A phase using less than
# `deck-min-workspace` is satisfied at ANY scale and is not counted.
#
# CHECK IS TWO CONSTRAINTS ON ONE DECK, and reading only the first is what made
# an earlier revision of this script 7 to 29 per cent low on every CHECK-bound
# unit. `compile-type-check` measures the deck at `check-metrics`, then keeps
# allocating on it -- `resolved-env`, `sorted-all0`, `sorted-et0` -- until it
# switches to the keep deck. Those are what `post-ov-bag` exists to catch, and
# `CHECK-RESOLVE` is that same deck at its true end. The two constraints differ
# because the two guards differ: the check body is stopped a guard band short
# of the ceiling (`deck-short-of check-ceiling demand-check-guard-band`) while
# the resolve tail is compared against the CEILING ITSELF (`__deck-pos >
# check-ceiling`), so the tail may spend the band and the body may not.
#
#   required = max( ceil(CHECK / perPoint), ceil((CHECK-RESOLVE - band) / perPoint) )
#
# HOW ACCURATE IT IS, against the expensive instrument rather than asserted.
# Bisected by output equality 2026-08-09 against seed A66E54F57CBAEBFD, five
# units, four CHECK-bound and one DESUGAR-bound:
#
#   build/output/Codex.codex            DESUGAR   model 67   bisected 67
#   codex/test/shell-build-keep         CHECK     model 30   bisected 30
#   codex/build/checkappsScript         CHECK     model 36   bisected 36
#   codex/build/lintunusedcitesScript   CHECK     model 39   bisected 39
#   codex/build/vmconfigScript          CHECK     model 43   bisected 43
#
# The last two are the ones that matter: the model was written from the source
# and PREDICTED 39 and 43 where bisection had only bracketed them to (38,40]
# and (42,44], and both predictions held. Two exact points would have been a
# calibration; two predictions are a model.
#
# CHECK's usage is scale-invariant, which is what lets one run stand for a fan:
# `CHECK` and `CHECK-RESOLVE` are byte-identical at scales 30, 31 and 64, and
# `CHECK` is byte-identical across seeds AEB5ED2B5043C7C1, 065D92E60292492D and
# A66E54F57CBAEBFD.
#
# THE OTHER LIMIT: -Measure runs no IR pipeline, so its LOWER is smaller than
# the one that ships. BuildSettings.codex calls LOWER the tightest deck in the
# compiler. A LOWER-bound row is understated and wants the expensive
# instrument: on foreword-all-compile it reads 131 against a bisected 137.
#
# WHEN A UNIT LOOKS TIGHT, bisect it for real -- compile at descending -Decks
# and compare the bytes against the derived default, which is what "requires"
# means here. A leg that compiles but produces DIFFERENT bytes is the silent
# under-reservation this whole area exists to catch, and it is the one outcome
# this script cannot see at all.
[CmdletBinding()]
param(
  [string]$List = '',
  [string]$Quire = '',
  [string]$Kernel = 'seed/Codex.cdx',
  [string]$Tag = 'corpus',
  [int]$Jobs = 4,
  [int]$TimeoutSec = 600,
  [int]$Top = 25,
  [double]$MinMargin = 0,
  [switch]$Plugs,
  [switch]$WithSelf,
  [switch]$Fresh
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot 'quire-map.ps1')
. (Join-Path $PSScriptRoot 'vm-config.ps1')

$out = Join-Path $root "build-output\deck-headroom\$Tag"
if ($Fresh -and (Test-Path $out)) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null

# The floors, the bands and the derivation are READ from the source rather
# than copied here. Every count in this tree that was copied has gone stale,
# and a headroom report quoting a floor the compiler no longer uses would be
# worse than no report.
function Get-IntConst {
  param([string]$Path, [string]$Name)
  $m = Select-String -Path $Path -Pattern "^\s*$([regex]::Escape($Name))\s*:\s*Integer\s*=\s*(\d+)" | Select-Object -First 1
  if (-not $m) { throw "deck-headroom: constant '$Name' not found in $Path -- it was renamed or moved" }
  return [int64]$m.Matches[0].Groups[1].Value
}
$bs = Join-Path $root 'codex\compiler\Core\BuildSettings.codex'
$op = Join-Path $root 'codex\compiler\opening.codex'
$PHASE = [ordered]@{}
foreach ($p in @(
    @('LEX','demand-lex-floor','demand-lex-guard-band'),
    @('PARSE','demand-parse-scratch-floor','demand-parse-scratch-guard-band'),
    @('PARSE-KEEP','demand-parse-keep-floor','demand-parse-guard-band'),
    @('DESUGAR','demand-desugar-floor','demand-desugar-guard-band'),
    @('SCOPE','demand-scope-floor','demand-scope-guard-band'),
    @('CHECK','demand-check-floor','demand-check-guard-band'),
    @('LOWER','demand-lower-floor','demand-lower-guard-band'),
    @('RESOLVE','demand-resolve-floor','demand-resolve-guard-band'),
    @('LIFT','demand-lift-floor','demand-lift-guard-band'))) {
  $PHASE[$p[0]] = @((Get-IntConst $bs $p[1]), (Get-IntConst $bs $p[2]))
}
# CHECK-RESOLVE is the CHECK deck at its true end, so it takes CHECK's floor
# and band. It is the one phase whose guard compares against the ceiling rather
# than a band short of it, so the band is room it may spend: BANDFREE.
$PHASE['CHECK-RESOLVE'] = $PHASE['CHECK']
$BANDFREE = @('CHECK-RESOLVE')
$MINWS  = Get-IntConst $op 'deck-min-workspace'
$ANCHOR = Get-IntConst $op 'deck-scale-anchor'
$MARGIN = Get-IntConst $op 'deck-scale-margin'
$MINSC  = Get-IntConst $op 'deck-scale-min'
Write-Host "deck-headroom: anchor $ANCHOR, margin $MARGIN, floor $MINSC, min-workspace $MINWS"
Write-Host "deck-headroom: the linear term is consulted only between $([math]::Floor($MINSC * $ANCHOR / (100 * $MARGIN))) and $([math]::Floor($ANCHOR / $MARGIN)) bytes; below is the floor, above is the clamp"

function Derive([int64]$len) {
  $est = [math]::Floor($len * 100 * $MARGIN / $ANCHOR)
  if ($est -lt $MINSC) { return [int]$MINSC } elseif ($est -gt 100) { return 100 } else { return [int]$est }
}

# The corpus, and its assembled unit lengths. A unit is every cited chapter
# followed by the source, which is what compile.ps1 sends and therefore what
# the compiler derives from; the source file's own length is a different and
# much smaller number.
if ($Plugs) {
  # A plug's compilation unit is its BUNDLE, assembled into build-output, and
  # every other mode here walks individual chapters and skips build-output on
  # purpose. So no plug unit was ever in this corpus, and the arm64 bundle ran
  # out of deck room with nothing reporting it (plugs 1.98): one field of type
  # `List IRDef` added to a record refused the whole plug with CDX9002 in
  # CHECK, no new loop and no new call site.
  #
  # The bundles are read off disk rather than rebuilt, because rebuilding 56 of
  # them to ask a question about deck room costs more than the question. That
  # makes staleness the hazard, so a plug whose newest chapter is newer than
  # its bundle is NAMED and skipped rather than measured quietly: a stale
  # bundle answers for the previous revision in either direction.
  # A PLUG THAT PASSES -Decks MUST BE MEASURED AT THAT SCALE, not at the
  # derived one, or this asks a question the build never asks. arm64 and riscv
  # build at -Decks 140 and do NOT fit the derivation; measured at derived they
  # overflow CHECK, write no deck records, and land in $NoDeckRecords -- so the
  # two bundles this mode exists for were the two it could not answer for, and
  # with -MinMargin they would have failed the gate for a scale nothing uses.
  # The plug's own build.ps1 is the single source of that number, so it is read
  # rather than restated here.
  $units = @()
  $missing = @()
  $stale = @()
  $script:UnitDecks = @{}
  foreach ($d in (Get-ChildItem (Join-Path $root 'codex\plugs') -Directory | Sort-Object Name)) {
    $plugBuild = Join-Path $d.FullName 'build.ps1'
    if (-not (Test-Path $plugBuild)) { continue }
    $bundle = Join-Path $d.FullName 'build-output\plug-source.codex'
    if (-not (Test-Path $bundle)) { $missing += $d.Name; continue }
    $newest = Get-ChildItem $d.FullName -Filter '*.codex' -File |
              Where-Object { $_.FullName -notmatch '\\build-output\\' } |
              Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTimeUtc -gt (Get-Item $bundle).LastWriteTimeUtc) { $stale += $d.Name; continue }
    $rel = $bundle.Substring($root.Length + 1)
    $m = [regex]::Match([System.IO.File]::ReadAllText($plugBuild), '-Decks\s+(\d+)')
    if ($m.Success) { $script:UnitDecks[$rel] = [int]$m.Groups[1].Value }
    $units += $rel
  }
  $overridden = @($script:UnitDecks.Keys)
  if ($overridden.Count -gt 0) {
    Write-Host "[deck-headroom] measured at the scale their build passes, not the derivation ($($overridden.Count)): $(($overridden | ForEach-Object { ($_ -split '\\')[2] + '=' + $script:UnitDecks[$_] }) -join ' ')" -ForegroundColor DarkGray
  }
  if ($missing.Count -gt 0) { Write-Host "[deck-headroom] no bundle, not measured ($($missing.Count)): $($missing -join ' ')" -ForegroundColor Yellow }
  if ($stale.Count -gt 0)   { Write-Host "[deck-headroom] bundle older than its source, not measured ($($stale.Count)): $($stale -join ' ')" -ForegroundColor Yellow }
  Write-Host "[deck-headroom] $($units.Count) plug bundle(s) in the corpus" -ForegroundColor DarkGray
} elseif ($Quire) {
  # A directory, not a list, so a unit added to the quire joins the check by
  # existing. The 2026-08-04 sweep certified a floor from a hand-written root
  # list and `codex/build` was not in it; a hand-written list of `codex/build`
  # would fail the same way one unit down.
  $units = @(Get-ChildItem (Join-Path $root $Quire) -Recurse -Filter '*.codex' -File |
             Where-Object { $_.FullName -notmatch '\\build-output\\' } |
             ForEach-Object { $_.FullName.Substring($root.Length + 1) })
  if ($WithSelf) {
    # The compiler's own unit is assembled rather than stored, so no directory
    # walk finds it, and it is the only unit in the tree past the clamp.
    $self = 'build\output\Codex.codex'
    & (Join-Path $PSScriptRoot 'concat-codex-self.ps1') -CodexDir codex/compiler -OutFile (Join-Path $root $self) | Out-Null
    $units += $self
  }
} elseif ($List) {
  $units = @(Get-Content $List | Where-Object { $_.Trim() -and -not $_.StartsWith('#') })
} else {
  # EVERY quire, discovered rather than listed. A hand-written root list is
  # how the 2026-08-04 sweep certified a floor of 32 that `codex/build` broke
  # both ways -- silently on one unit and with CDX9002 on three -- because the
  # Shell quire was not in it. A sweep cannot certify a floor for a quire it
  # does not enumerate, so this enumerates them all and a new one joins by
  # existing.
  $units = @()
  $roots = @('apps') + @(Get-ChildItem (Join-Path $root 'codex') -Directory |
                         Where-Object { $_.Name -ne 'compiler' } |
                         ForEach-Object { "codex\$($_.Name)" })
  foreach ($r in $roots) {
    $p = Join-Path $root $r
    if (Test-Path $p) {
      $units += (Get-ChildItem $p -Recurse -Filter '*.codex' -File |
                 Where-Object { $_.FullName -notmatch '\\build-output\\' } |
                 ForEach-Object { $_.FullName.Substring($root.Length + 1) })
    }
  }
  # The compiler's own unit is the row that matters most and it is the one
  # unit no directory walk finds, because it is assembled rather than stored.
  # It is also the only unit in the tree past the clamp, so it is the only one
  # whose margin the derivation cannot improve.
  $self = 'build\output\Codex.codex'
  & (Join-Path $PSScriptRoot 'concat-codex-self.ps1') -CodexDir codex/compiler -OutFile (Join-Path $root $self) | Out-Null
  $units += $self
}
Write-Host "deck-headroom: $($units.Count) units"

$embeddedPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
$items = foreach ($u in $units) {
  $len = 0
  try {
    $srcLines = [System.IO.File]::ReadAllLines((Join-Path $root $u))
    $seedSeen = @{}
    foreach ($line in $srcLines) { if ($line -match $embeddedPat) { $seedSeen["$($matches[1])::$($matches[2])"] = $true } }
    $ordered = Resolve-CiteOrder -RootLines $srcLines -Repo '.' -SeedSeen $seedSeen
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { $len += $l.Length + 1 }
    foreach ($l in $srcLines) { $len += $l.Length + 1 }
    $len += 1
  } catch { $len = 0 }
  $k = ($u -replace '[\\/:]', '_') -replace '\.codex$',''
  $ovr = 0
  if ((Test-Path variable:script:UnitDecks) -and $script:UnitDecks.ContainsKey($u)) { $ovr = $script:UnitDecks[$u] }
  $eff = if ($ovr -gt 0) { $ovr } else { Derive $len }
  [pscustomobject]@{ Unit = $u; Key = $k; Len = $len; Derived = $eff; Decks = $ovr
                     Log = (Join-Path $out "$k.log"); Bin = (Join-Path $out "$k.bin") }
}

# Separate processes, not ForEach-Object -Parallel: fresh runspaces race on
# the type-accelerator table at five or more concurrent legs and throw "An
# item with the same key has already been added. Key: decimal", which is a
# harness failure that reads exactly like a compile failure.
$compile = Join-Path $PSScriptRoot 'compile.ps1'
$todo = @($items | Where-Object { -not (Test-Path $_.Log) })
Write-Host "deck-headroom: $($todo.Count) to measure, $($items.Count - $todo.Count) cached"
# Admit only as many concurrent guests as the box can hold. This tool is the
# reason the check exists: at -Jobs 8 it read a contended run as "a plug
# bundle has grown into its deck reservation" and named a bundle that
# compiles clean alone (main 20381). A trimmed slot count costs wall time; an
# overcommitted one costs a wrong deck verdict.
$Jobs = Get-VmAdmittedSlots -Slots $Jobs -What 'deck-headroom'
$running = @(); $n = 0
foreach ($it in $todo) {
  while (@($running | Where-Object { -not $_.HasExited }).Count -ge $Jobs) { Start-Sleep -Milliseconds 300 }
  $cArgs = @('-NoProfile','-File',$compile,'-Src',$it.Unit,'-Out',$it.Bin,'-Log',$it.Log,
             '-Kernel',$Kernel,'-Measure','-TimeoutSec',"$TimeoutSec")
  if ($it.Decks -gt 0) { $cArgs += @('-Decks', "$($it.Decks)") }
  $proc = Start-Process -FilePath 'pwsh' -WindowStyle Hidden -PassThru `
    -RedirectStandardError ($it.Log + '.err') `
    -ArgumentList $cArgs
  # The exit code was launched and thrown away, so the only evidence left about
  # a unit that produced nothing was the absence of records -- which is the same
  # absence whatever the cause. compile.ps1 answers 4 for a compile that ran and
  # refused (measured on a forced -Decks 8 overflow) and 2 or 3 when the VM
  # never got going, and that is the difference this tool could not previously
  # see. Keep it beside the unit rather than in a parallel array.
  $it | Add-Member -NotePropertyName Proc -NotePropertyValue $proc -Force
  $running += $proc
  $n++
  if ($n % 200 -eq 0) { Write-Host "  launched $n/$($todo.Count)" }
}
foreach ($p in $running) { $p.WaitForExit() }

$script:MissingResolve = @()
$script:NoDeckRecords = @()
$script:Unmeasured = @()
# A unit with no DECK records is TWO answers in one bucket and only one of them
# is about decks. THE DISCRIMINATOR IS NOT THE DIAGNOSTIC: measured 2026-08-27,
# a real overflow (-Decks 8 on the rust bundle, exit 4) writes a 287-byte log
# carrying PHASE-h0..h-post-emit and EMIT-BYTES:0 and NO `CDX9002` anywhere,
# because -Measure suppresses diagnostics. Keying on CDX9002 was written here
# first and would have downgraded every genuine overflow to the bucket below.
# So the question this asks is whether the compiler RAN: phase records and no
# deck records is a unit that got far enough to refuse, and that is the deck
# answer this tool exists to give. No phase records at all is a measurement
# that never happened -- eight concurrent VMs at 3072 MB on a 15.8 GB box die
# instantly leaving an empty log, and the failing set moves run to run (2 units,
# then 4, then 0, same tree), which is why it must not be reported as a grown
# reservation: the rust bundle so named compiles alone at exit 0.
# HONEST LIMIT: the overflow arm above is measured. The empty-log arm is the
# shape of codex-vm dying (build.ps1's own build-cdx-fail.log came back 5 bytes
# the same day) and was NOT captured from a contention run, because contention
# could not be reproduced on demand.
$rows = foreach ($it in $items) {
  if (-not (Test-Path $it.Log)) { $script:Unmeasured += $it.Unit; continue }
  # -Raw on a zero-byte file answers $null, and [regex]::Matches($null,..) throws
  # "Value cannot be null" -- which takes the whole run down rather than
  # reporting the unit. A dead VM is exactly how a zero-byte log gets here.
  $text = Get-Content $it.Log -Raw
  if ($null -eq $text) { $text = '' }
  $decks = [regex]::Matches($text, 'DECK-\d+:phase=(?<p>[A-Z-]+) origin=\d+ end=\d+ used=(?<u>\d+)')
  if ($decks.Count -eq 0) {
    $code = if ($it.PSObject.Properties['Proc'] -and $it.Proc) { $it.Proc.ExitCode } else { 'cached' }
    $ph = ([regex]::Matches($text, 'PHASE-h')).Count
    $ev = "$($it.Unit)  [exit=$code, log=$($text.Length)b, phases=$ph]"
    # Ran to a refusal is a deck answer. Anything else is not, and the evidence
    # travels with the unit so the reader does not have to take either on trust.
    if ($ph -gt 0 -and ($code -eq 4 -or $code -eq 'cached')) { $script:NoDeckRecords += $ev }
    else { $script:Unmeasured += $ev }
    continue
  }
  $req = 1; $bind = ''; $worst = 0
  $sawResolve = $false
  foreach ($m in $decks) {
    $p = $m.Groups['p'].Value; $u = [int64]$m.Groups['u'].Value
    if (-not $PHASE.Contains($p)) { continue }
    if ($p -eq 'CHECK-RESOLVE') { $sawResolve = $true; $u = $u - $PHASE[$p][1] }
    if ($u -le $MINWS) { continue }
    $r = [int][math]::Ceiling($u / [math]::Floor(($PHASE[$p][0] - $PHASE[$p][1]) / 100))
    if ($r -gt $req) { $req = $r; $bind = $p; $worst = $u }
  }
  $script:MissingResolve += @(if (-not $sawResolve) { 1 } else { 0 })
  [pscustomobject]@{
    Unit = $it.Unit; Len = $it.Len; Derived = $it.Derived; Required = $req
    Binding = $bind; Used = $worst; Margin = [math]::Round($it.Derived / $req, 2)
  }
}
$rows = @($rows)
$csv = Join-Path $out 'headroom.csv'
$rows | Export-Csv -NoTypeInformation -Path $csv

Write-Host ""
# The remainder used to be asserted as "chapters that are not entry points",
# and for a walk over ordinary chapters that is usually true. It is not a
# cause this script establishes: a unit is dropped when its measure log
# carries no DECK records at all, and an entry point whose run produced none
# lands in the same bucket. arm64's and riscv's plug bundles do exactly that,
# so naming them is the difference between a corpus that omits two units and
# a corpus that says which two (plugs 1.98).
Write-Host "measured $($rows.Count) of $($items.Count) units"
if ($script:NoDeckRecords.Count -gt 0) {
  Write-Host "ran but wrote no deck records, so the margin is below 1 ($($script:NoDeckRecords.Count)):"
  foreach ($u in $script:NoDeckRecords) { Write-Host "  $u" }
}
if ($script:Unmeasured.Count -gt 0) {
  Write-Host "measurement did not happen, so the margin is UNKNOWN ($($script:Unmeasured.Count)):"
  foreach ($u in $script:Unmeasured) { Write-Host "  $u" }
}
Write-Host ""
Write-Host "where the derivation comes from:"
Write-Host ("  floor ($MINSC)  {0,6}" -f @($rows | Where-Object { $_.Derived -eq $MINSC }).Count)
Write-Host ("  linear band     {0,6}" -f @($rows | Where-Object { $_.Derived -gt $MINSC -and $_.Derived -lt 100 }).Count)
Write-Host ("  clamp (100)     {0,6}" -f @($rows | Where-Object { $_.Derived -eq 100 }).Count)
Write-Host ""
Write-Host "binding phase:"
$rows | Group-Object Binding | Sort-Object Count -Descending | ForEach-Object {
  Write-Host ("  {0,-12} {1,6}" -f $(if ($_.Name) { $_.Name } else { '(under 2 MB)' }), $_.Count) }
Write-Host ""
$missing = @($script:MissingResolve | Where-Object { $_ -eq 1 }).Count
if ($missing -gt 0) {
  Write-Host "  WARNING: $missing of $(@($rows).Count) units reported no CHECK-RESOLVE deck."
  Write-Host "           That kernel predates the second CHECK measurement, so every"
  Write-Host "           CHECK-bound row below is 7 to 29 per cent LOW. Pass a -Kernel that"
  Write-Host "           has it before quoting any margin."
}
Write-Host ""
Write-Host "the $Top tightest, by margin of derived over required:"
Write-Host ("  {0,7} {1,8} {2,8} {3,9}  {4,-10} {5}" -f 'margin','derived','required','unitlen','binding','unit')
$rows | Sort-Object Margin | Select-Object -First $Top | ForEach-Object {
  Write-Host ("  {0,7} {1,8} {2,8} {3,9}  {4,-10} {5}" -f $_.Margin, $_.Derived, $_.Required, $_.Len, $_.Binding, $_.Unit) }
Write-Host ""
Write-Host "-> $csv"

# The gate arm. Three ways to fail, and the second two are the ones that stop
# this from being a check that passes because it never ran: a kernel with no
# CHECK-RESOLVE deck understates every CHECK-bound row by 7 to 29 per cent, and
# an empty row set is not a clean tree.
if ($MinMargin -gt 0) {
  Write-Host ""
  # A UNIT WITH NO DECK RECORDS IS THE ONE THIS TOOL EXISTS TO CATCH, and it
  # used to pass silently. Measured 2026-08-27 on the arm64 and riscv plug
  # bundles: at the derived scale each refuses with
  # `CDX9002: Deck overflow in CHECK; deck floor exceeded`, and the overflow
  # aborts CHECK BEFORE any DECK record is written, so the measure log is
  # empty. `-Measure` reports neither the records nor the diagnostic, so the
  # tool saw an empty log and skipped the unit -- a check that stops asking
  # reports exactly what one that asks and agrees reports (L-CAPABILITY-LOST).
  # The header has always said -MinMargin fails "when the kernel cannot answer
  # the question at all"; this is that clause, honored.
  if ($script:NoDeckRecords.Count -gt 0) {
    Write-Host "FAIL: $($script:NoDeckRecords.Count) unit(s) ran and wrote no deck records, so their margin is below 1:"
    foreach ($u in $script:NoDeckRecords) { Write-Host "  $u" }
    Write-Host "  Compile one without -Measure to see the diagnostic; -Measure suppresses it."
    exit 1
  }
  # A measurement that did not happen is not a verdict about decks, and saying
  # it is sends the reader to inspect a reservation that is fine. It still
  # fails: a check that stops asking reports what one that asks and agrees
  # reports (L-CAPABILITY-LOST).
  if ($script:Unmeasured.Count -gt 0) {
    Write-Host "FAIL: $($script:Unmeasured.Count) unit(s) produced NO compiler output, so nothing about their margin was measured:"
    foreach ($u in $script:Unmeasured) { Write-Host "  $u" }
    Write-Host "  This is not a deck verdict. The commonest cause is contention: -Jobs $Jobs at 3072 MB"
    Write-Host "  each does not fit every box, and codex-vm dies leaving an empty log. Re-run, and"
    Write-Host "  compare -Jobs 1 before believing any unit here has a deck problem."
    exit 1
  }
  if ($missing -gt 0) {
    Write-Host "FAIL: $missing of $($rows.Count) units reported no CHECK-RESOLVE deck, so no margin here can be believed."
    exit 1
  }
  if ($rows.Count -eq 0) {
    Write-Host "FAIL: measured no units. Nothing was checked."
    exit 1
  }
  # Recomputed from the two integers rather than read from the Margin column,
  # which is rounded to two places for the table.
  $scored = foreach ($r in $rows) { [pscustomobject]@{ Row = $r; M = [double]$r.Derived / [double]$r.Required } }
  $under = @($scored | Where-Object { $_.M -lt $MinMargin } | Sort-Object M)
  if ($under.Count -gt 0) {
    Write-Host "FAIL: $($under.Count) unit(s) below a margin of $MinMargin."
    $under | ForEach-Object { Write-Host ("  margin {0,5:0.00}  derived {1,4}  needs {2,4}  {3}  {4}" -f $_.M, $_.Row.Derived, $_.Row.Required, $_.Row.Binding, $_.Row.Unit) }
    exit 1
  }
  $worst = $scored | Sort-Object M | Select-Object -First 1
  Write-Host ("deck-headroom: OK, tightest margin {0:0.00} (needs {1} of {2}, {3}) over {4} units, floor {5} required" -f `
    $worst.M, $worst.Row.Required, $worst.Row.Derived, $worst.Row.Unit, $rows.Count, $MinMargin)
}
