# guard-sabotage.ps1 -- show each compiler-internal guard firing (COMPILER-143).
#
# Root's ruling 2026-10-29: no fault-injection flag in the shipped compiler.
# A guard that a correct front end never reaches is graded by SABOTAGE
# (L-SABOTAGE): patch one line of a scratch concatenation of the compiler so
# a valid program reaches the guard, compile that scratch compiler with the
# kernel, compile an arm program with it, and require the guard's CDX code in
# the log. The same arm compiled by an UNPATCHED scratch compiler built from
# the same concatenation must not carry the code (the control). A row with
# no patch (9003, 6011) is a guard a valid program already reaches under its
# flags, so the unpatched compile is required to carry the code instead. A report row
# is a diagnostic that only reports (4030, 6015, 9016), shown reached under
# its flag.
#
# The `proves` column says which claim a row makes. `routing`: the patch
# forces the guard's condition, so the row proves the guard's diagnostic and
# halt are reached, not that its condition detects anything. `detection`: the
# patch corrupts the guard's INPUT (9007 under-marks the entry parameters,
# 9008 routes call-crossing intervals to caller-saved registers) and the
# unmodified check finds it. 9006's detection sabotage (lir-pmv-pick ignoring
# a blocked move) needs a conflicting parameter move no arm produces, so that
# row routes.
#
# The arms compile with -Passes none where the default pipeline would fold
# or inline the subject away, and the comparison arms are punctual (the LIR
# selector declines punctual definitions) with variable operands, because
# the LIR path and an immediate operand never reach emit-comparison.
#
# Nothing this script builds is a seed. Every artifact stays under -OutDir and
# the tree is never edited. A patch whose text no longer occurs exactly once
# in the concatenation reports PATCH-MISS: the guard's code moved, and the row
# below is updated by hand, never by loosening the match.
#
# Cost, measured 2026-10-29: about 150 s for all 22 rows, serial, one compile
# guest at a time (a scratch compiler builds in about 7 s on a warm box).

#
# Usage:
#   build/guard-sabotage.ps1                       # every guard
#   build/guard-sabotage.ps1 -Only 9010,9012       # named codes
[CmdletBinding()]
param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string[]]$Only = @(),
    [string]$OutDir = 'build-output\guard-sabotage'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Compile = Join-Path $Repo 'build\compile.ps1'
$Concat = Join-Path $Repo 'build\concat-codex-self.ps1'
if (-not [IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $Repo $Kernel }
if (-not (Test-Path -PathType Leaf $Kernel)) { throw "Kernel not found: $Kernel" }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $Repo $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$arms = @{
    basic = @'
Chapter: C143Basic
  cites Foreword chapter Console

Section: Body
  sum-to : Integer, Integer -> Integer
  sum-to (n) (acc) = if n == 0 then acc else sum-to (n - 1) (acc + n)

  mix : Integer, Integer, Integer -> Integer
  mix (a) (b) (c) = a * b + c

  flip : Integer -> Integer
  flip (x) = -(x)

  fib : Integer -> Integer
  fib (n) = if n < 2 then n else fib (n - 1) + fib (n - 2)

  swap-sub : Integer, Integer, Integer -> Integer
  swap-sub (a) (b) (c) = mix c a b - mix b c a

  cmp-text : Text, Integer -> Text
  cmp-text (s) (n) =
    let lt = n < 3
    in let eq = n == 3
    in if lt then s else if eq then "eq" else "gt"

  count-down : Text, Integer -> Integer
  count-down (s) (n) = if n == 0 then text-length s else count-down s (n - 1)

  punctual below : Integer, Integer -> Boolean
  below (a) (b) = a < b

  punctual same : Integer, Integer -> Boolean
  same (a) (b) = a == b

  punctual bump : Integer -> Integer
  bump (n) = n + 1

  opening : [Console] Nothing = act
    print-line-uni (show (sum-to 10 0))
    print-line-uni (show (mix 2 3 4))
    print-line-uni (show (fib 10))
    print-line-uni (show (swap-sub 1 2 3))
    print-line-uni (show (flip 7))
    print-line-uni (show (bump 1))
    print-line-uni (show (below (mix 1 1 1) (mix 2 2 2)))
    print-line-uni (show (same (mix 1 1 1) (mix 2 2 2)))
    print-line-uni (cmp-text "lt" 2)
    print-line-uni (show (count-down "abc" 5))
    print-line-uni (if mix 1 1 1 < 3 then "lt" else "ge")
  end
Page 1
'@
    wcet = @'
Chapter: C143Wcet
  cites Foreword chapter Console

Section: Body
  punctual 1 over : Integer -> Integer
  over (n) = n * 3 + 1

  opening : [Console] Nothing = act
    print-line-uni (show (over 4))
  end
Page 1
'@
    fork = @'
Chapter: C143Fork
  cites Foreword chapter Console

Section: Body
  fresh : Integer -> Integer
  fresh (n) =
    let t = fork (\x -> n)
    in await t

  opening : [Console] Nothing = act
    print-line-uni (show (fresh 3))
  end
Page 1
'@
    rows = @'
Chapter: C143Rows
  cites Foreword chapter Console

Section: Body
  apply-row : (Integer -> [e] Integer), Integer -> [e] Integer
  apply-row (f) (x) = f x

  logged-apply : (Integer -> [Console, e] Integer), Integer -> [Console, e] Integer
  logged-apply (f) (x) = f x

  double : Integer -> Integer
  double (n) = n * 2

  shout : Integer -> [Console] Integer
  shout (n) = act
    print-line-uni ("shout " & show n)
    n * 2
  end

  opening : [Console] Nothing = act
    print-line-uni ("pure " & show (apply-row double 21))
    r <- logged-apply shout 21
    print-line-uni ("logged " & show r)
  end
Page 1
'@
}

# One row per guard: the arm it compiles, compile.ps1 switches, and the one
# patch (old text, which must occur exactly once, and its replacement).
# A newline in a patch matches CRLF or LF.
$guards = @(
    @{ code = '2000'; arm = 'basic'; flags = @(); site = 'emit-expr-dispatch IrError arm, reached through IrNegate'
       old = 'is IrNegate (operand) (ty) (sp) -> emit-negate st operand'
       new = 'is IrNegate (operand) (ty) (sp) -> emit-expr st (IrError "c143 sabotage" ty sp)' }
    @{ code = '2042'; arm = 'fork'; flags = @(); site = 'st-refuse-hosted'
       old = "st-refuse-hosted (st) (name) (sp) =`n    if st-hosted st then"
       new = "st-refuse-hosted (st) (name) (sp) =`n    if True then" }
    @{ code = '2091'; arm = 'rows'; flags = @(); site = 'unify-row-open-open'
       old = 'in if row-occurs st2 (a.tail-id) bind-a 0 | row-occurs st2 (b.tail-id) bind-b 0 then'
       new = 'in if True then' }
    @{ code = '6012'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'emit-wcet-check'
       old = "emit-wcet-check (st) (def) (insn-count) =`n    if insn-count < 0 then"
       new = "emit-wcet-check (st) (def) (insn-count) =`n    if insn-count >= 0 then" }
    @{ code = '9001'; arm = 'basic'; flags = @(); site = 'emit-fuel-exhausted'
       old = 'emit-fuel-exhausted (st) = st.emit-work >= max-emit-work'
       new = 'emit-fuel-exhausted (st) = st.emit-work >= 0' }
    @{ code = '9003'; arm = 'basic'; flags = @('-EscapeCheck'); site = 'check-escape-invariant, reached unpatched under -EscapeCheck'
       old = $null; new = $null }
    @{ code = '9005'; arm = 'basic'; flags = @(); site = 'emit-all-defs accumulator capacity'
       old = "    else if accum-at-capacity st then`n      st-add-error st cdx-accum-overflow"
       new = "    else if True then`n      st-add-error st cdx-accum-overflow" }
    @{ code = '9006'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'LIR entry-move verifier (pmv-viol)'
       old = '    in let bad = pmv-viol file assign 0 np'
       new = '    in let bad = 0' }
    @{ code = '9007'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'lir-emit-try structural verifier'
       old = '    in let pv = lir-mark-params (lir-make-vec 0 nv []) 0 (f.nparams)'
       new = '    in let pv = lir-mark-params (lir-make-vec 0 nv []) 0 (f.nparams - 1)'; proves = 'detection' }
    @{ code = '9008'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'lir-emit-try allocation verifier'
       old = '       then lir-scan-cross tgt iv st ex'
       new = '       then lir-scan-nocross tgt iv st ex callpos hints divpos'; proves = 'detection' }
    @{ code = '9009'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'tail-call shuffle verifier'
       old = "    in let st0 = if text-length viol == 0 then st`n    else`n      st-add-error st cdx-tco-shuffle-violation"
       new = "    in let st0 = if False then st`n    else`n      st-add-error st cdx-tco-shuffle-violation" }
    @{ code = '9010'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'emit-comparison operand check'
       old = '    let st0 = if cmp-answers-operand st operand-ty then st'
       new = '    let st0 = if False then st' }
    @{ code = '9012'; arm = 'basic'; flags = @('-Passes', 'none'); site = 'emit-ord-comparison Text arm'
       old = "    else when resolved`n     is TextTy | CharTy ->`n      let st0 = st-add-error st cdx-order-compare-on-text"
       new = "    else when CharTy`n     is TextTy | CharTy ->`n      let st0 = st-add-error st cdx-order-compare-on-text" }
    @{ code = '9013'; arm = 'basic'; flags = @(); site = 'chapter image serial-ring check'
       old = 'in let over-ring = if load-base == bare-metal-load-addr then content-len > ring-room else False'
       new = 'in let over-ring = if load-base == bare-metal-load-addr then True else False' }
    @{ code = '3007'; arm = 'basic'; flags = @(); site = 'ChapterScoper cite presence, the cited name corrupted before lookup'
       old = 'in if bare-name-present bare-cache (cite-key (cite.chapter-name.value)) 0 (list-length bare-cache)'
       new = 'in if bare-name-present bare-cache (cite-key (cite.chapter-name.value) & "x") 0 (list-length bare-cache)'; proves = 'detection' }
    @{ code = '4030'; arm = 'basic'; flags = @('-Passes', 'occ-report'); site = 'occ-report pass'
       old = $null; new = $null; proves = 'report' }
    @{ code = '6011'; arm = 'wcet'; flags = @('-Passes', 'none'); site = 'emit-wcet-check over a punctual 1 budget'
       old = $null; new = $null; proves = 'detection' }
    @{ code = '6015'; arm = 'basic'; flags = @('-RawFlags', 'cover'); site = 'cover-slot info under the cover flag'
       old = $null; new = $null; proves = 'report' }
    @{ code = '9002'; arm = 'self'; flags = @('-Decks', '5'); site = 'deck floor, the compiler''s own unit under -Decks 5 with growth refused'
       old = "  deck-grow-fits (base) (next) (headroom) =`n    if hosted-kind /= 0 then False"
       new = "  deck-grow-fits (base) (next) (headroom) =`n    if True then False"; proves = 'detection' }
    @{ code = '9015'; arm = 'basic'; flags = @('-Passes', 'ir-check'); site = 'ir-check, user definitions dropped from its scope'
       old = '    in let s1 = ir-check-add-def-names s0 (ch.defs) 0 (list-length (ch.defs))'
       new = '    in let s1 = s0'; proves = 'detection' }
    @{ code = '9016'; arm = 'basic'; flags = @('-IrUni', '-Passes', 'none', '-RawFlags', 'fidelity'); site = 'IR fidelity summary under the fidelity flag'
       old = $null; new = $null; proves = 'report' }
    @{ code = '9014'; arm = 'basic'; flags = @(); site = 'LOWER-KEEP deck-grown note'
       old = 'deck-grown-notes "LOWER-KEEP" (lw.keep-height) (lw.keep-grown) []'
       new = 'deck-grown-notes "LOWER-KEEP" (lw.keep-height) 1 []' }
)
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if ($Only.Count -gt 0) { $guards = @($guards | Where-Object { $Only -contains $_.code }) }
if ($guards.Count -eq 0) { throw "No guard matches -Only $($Only -join ',')" }

function Invoke-Compile([string]$Src, [string]$Out, [string]$Log, [string]$With, [string[]]$Extra) {
    if (Test-Path $Out) { Remove-Item -Force $Out }
    if (Test-Path $Log) { Remove-Item -Force $Log }
    $a = @('-NoProfile', '-File', $Compile, '-Src', $Src, '-Out', $Out, '-Log', $Log, '-Kernel', $With) + $Extra
    & pwsh @a *> $null
    return (Test-Path -PathType Leaf $Out)
}

function Has-Code([string]$Log, [string]$Code) {
    if (-not (Test-Path $Log)) { return $false }
    return [bool](Select-String -Path $Log -Pattern "CDX$Code\b" -Quiet)
}

$base = Join-Path $OutDir 'base.codex'
& pwsh -NoProfile -File $Concat -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $base *> $null
if (-not (Test-Path $base)) { throw 'concat-codex-self produced no unit' }
$baseText = [IO.File]::ReadAllText($base)
Write-Host "kernel: $Kernel [$((Get-FileHash $Kernel).Hash.Substring(0,16))]"

$control = Join-Path $OutDir 'control.cdx'
if (-not (Invoke-Compile $base $control (Join-Path $OutDir 'control.log') $Kernel @('-Repl', '-MemMB', '3072'))) {
    throw "The unpatched scratch compiler did not build; read $OutDir\control.log"
}
foreach ($name in $arms.Keys) {
    [IO.File]::WriteAllText((Join-Path $OutDir "arm-$name.codex"), ($arms[$name] -replace "`r`n", "`n") + "`n")
}

$rows = @()
foreach ($g in $guards) {
    $code = $g.code
    if ($null -eq $g.old) {
        $armSrc = if ($g.arm -eq 'self') { $base } else { Join-Path $OutDir "arm-$($g.arm).codex" }
        $ctlLog = Join-Path $OutDir "arm-$code-control.log"
        [void](Invoke-Compile $armSrc (Join-Path $OutDir "arm-$code-control.cdx") $ctlLog $control $g.flags)
        $rows += [pscustomobject][ordered]@{ code = "CDX$code"; site = $g.site; proves = $(if ($g.ContainsKey('proves')) { $g.proves } else { 'detection' }); patch = 'none'; build = '-'
            fired = if (Has-Code $ctlLog $code) { 'FIRED' } else { 'SILENT' }; control = 'clean' }
        continue
    }
    $pattern = [regex]::Escape($g.old) -replace '\\n', '\r?\n'
    $hits = [regex]::Matches($baseText, $pattern)
    $row = [ordered]@{ code = "CDX$code"; site = $g.site; proves = $(if ($g.ContainsKey('proves')) { $g.proves } else { 'routing' }); patch = 'ok'; build = '-'; fired = '-'; control = '-' }
    if ($hits.Count -ne 1) {
        $row.patch = "PATCH-MISS ($($hits.Count))"
        $rows += [pscustomobject]$row; continue
    }
    $m = $hits[0]
    $nl = if ($m.Value.Contains("`r`n")) { "`r`n" } else { "`n" }
    $patched = $baseText.Substring(0, $m.Index) + ($g.new -replace "`n", $nl) + $baseText.Substring($m.Index + $m.Length)
    $src = Join-Path $OutDir "sab-$code.codex"
    [IO.File]::WriteAllText($src, $patched)
    $cdx = Join-Path $OutDir "sab-$code.cdx"
    if (-not (Invoke-Compile $src $cdx (Join-Path $OutDir "sab-$code.log") $Kernel @('-Repl', '-MemMB', '3072'))) {
        $row.build = 'BUILD-FAIL'
        $rows += [pscustomobject]$row; continue
    }
    $row.build = 'ok'
    $armSrc = if ($g.arm -eq 'self') { $base } else { Join-Path $OutDir "arm-$($g.arm).codex" }
    $armLog = Join-Path $OutDir "arm-$code-sab.log"
    [void](Invoke-Compile $armSrc (Join-Path $OutDir "arm-$code-sab.cdx") $armLog $cdx $g.flags)
    $row.fired = if (Has-Code $armLog $code) { 'FIRED' } else { 'SILENT' }
    $ctlLog = Join-Path $OutDir "arm-$code-control.log"
    $ctlBuilt = Invoke-Compile $armSrc (Join-Path $OutDir "arm-$code-control.cdx") $ctlLog $control $g.flags
    $row.control = if (Has-Code $ctlLog $code) { 'DIRTY' } elseif ($ctlBuilt) { 'clean' } else { 'CONTROL-FAIL' }
    $rows += [pscustomobject]$row
}

$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$bad = @($rows | Where-Object { ($_.patch -ne 'ok' -and $_.patch -ne 'none') -or ($_.patch -eq 'ok' -and $_.build -ne 'ok') -or $_.fired -ne 'FIRED' -or $_.control -ne 'clean' })
if ($bad.Count -gt 0) { Write-Host "guard-sabotage: $($bad.Count) of $($rows.Count) guard(s) not shown firing against a clean control"; exit 1 }
Write-Host "guard-sabotage: all $($rows.Count) guard(s) fired; every control clean"
