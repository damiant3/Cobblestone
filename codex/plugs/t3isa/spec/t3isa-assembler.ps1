# T3ISA assembler, prototype. Library half: no side effects on dot-source.
#
# This is the executable specification of the T3ISA encoding that
# codex/plugs/t3isa's encoder chapter must reproduce. It was written from the
# external project's published docs/t3isa-reference.md, plus the numeric
# opcode values and field placement that document does not state, which were
# derived by arithmetic from matched (.t3s assembly, .t3b word) pairs emitted
# by their compiler. Their src/ was not read. See derive-opcodes.ps1 for the
# derivation and validate.ps1 for the proof.
#
# Assemble <path to .t3s> -> @{ words; sidecar; codeSize; labels }

$OP = @{
  TADD=1; TSUB=2; TMUL=3; TDIV=4; TMOD=5; TNEG=6
  TSHI=10; TSHR=11; TMIN=12; TMAX=13; TCMP=14
  LOAD=15; STORE=16; TLIT=17; MOV=18
  JUMP=20; CALL=21; RET=22; SYSCALL=24
  TBR_POS=25; TBR_ZERO=26; CALLR=28
}
# TAND, TOR, TNOT, TBR_NEG, HALT and NOP are absent on purpose: their front end
# emits none of them, so no number for them has been measured. 0, 7, 8, 9, 19,
# 23 and 27 are unclaimed. Do not guess. CALLR looked like it belonged at 23
# and measured 28. None of the six is needed: TAND/TOR/TNOT are min/max/negate
# by the spec's own definitions, and RET from main halts the emulator cleanly.

$P18 = 387420489L   # 3^18, opcode weight
$P13 = 1594323L     # 3^13, r1 weight and the wide-immediate modulus
$P8  = 6561L        # 3^8,  r2 weight
$P3  = 27L          # 3^3,  r3 weight

function Encode-Std([long]$op, [long]$r1, [long]$r2, [long]$r3, [long]$imm) {
  $op * $P18 + $r1 * $P13 + $r2 * $P8 + $r3 * $P3 + $imm
}
# The wide immediate is 13 trits, so it holds +/- (3^13-1)/2 = 797,161, far
# short of the 27-trit word's +/- 3,812,798,742,493. The encoding is a
# rem_euclid, so an out-of-range value does not fail, it WRAPS, and whoever
# emitted it gets a plausible wrong number instead of an error. Refuse:
# materialising a larger constant is the caller's job (TLIT, TSHI, TADD).
# This guard is what caught the emitter trying to TLIT a bound of 1,000,000.
$script:WideMax = 797161L
function Encode-Wide([long]$op, [long]$r1, [long]$imm) {
  if ($imm -gt $script:WideMax -or $imm -lt (0 - $script:WideMax)) {
    throw "wide immediate $imm is outside +/-$($script:WideMax) and would wrap"
  }
  $w = $imm % $P13; if ($w -lt 0) { $w += $P13 }
  $op * $P18 + $r1 * $P13 + $w
}
function Parse-Reg([string]$s) {
  if ($s -match '^[Rr](\d+)$') { return [long]$Matches[1] }
  throw "not a register: '$s'"
}

function Assemble([string]$path) {
  $raw = Get-Content $path

  # Pass 0: split code from .data, collect string literals.
  $codeLines = @(); $strLabels = @(); $strText = @{}; $inData = $false
  foreach ($line in $raw) {
    $t = $line
    if ($t -notmatch '\.string') { $t = ($t -split ';')[0] }
    $t = $t.Trim()
    if ($t -eq '') { continue }
    if ($t -match '^\.data:?$')    { $inData = $true;  continue }
    if ($t -match '^\.globals:?$') { $inData = $false; continue }
    if ($inData) {
      if ($t -match '^([A-Za-z_][A-Za-z0-9_.:]*):\s*\.string\s+"(.*)"\s*$') {
        $strLabels += $Matches[1]; $strText[$Matches[1]] = $Matches[2]
      }
      continue
    }
    $codeLines += $t
  }

  # Pass 1: label addresses. A label's address is the word index of the
  # instruction that follows it. TBRANCH occupies three words.
  $labels = @{}; $wi = 0; $instrs = @()
  foreach ($t in $codeLines) {
    $cur = $t
    while ($cur -match '^([A-Za-z_][A-Za-z0-9_.:]*):\s*(.*)$') {
      $labels[$Matches[1]] = $wi; $cur = $Matches[2].Trim()
      if ($cur -eq '') { break }
    }
    if ($cur -eq '') { continue }
    $instrs += $cur
    $mn = ($cur -split '\s+')[0].ToUpper()
    if ($mn -eq 'TBRANCH') { $wi += 3 } else { $wi += 1 }
  }
  $codeSize = $wi

  # String addresses are code_size + 1024 + i (reference section 3), where i is
  # the label's position in ORDINAL LEXICOGRAPHIC order of the label name --
  # str0, str1, str10, str11, ..., str2, str20 -- and not declaration order.
  # The reference does not say this; it was found by the artifact refusing to
  # match, and is a sorted-map ordering leaking out of their emitter.
  $sorted = [string[]]$strLabels
  [Array]::Sort($sorted, [StringComparer]::Ordinal)
  $strAddr = @{}
  for ($i = 0; $i -lt $sorted.Count; $i++) { $strAddr[$sorted[$i]] = $codeSize + 1024 + $i }

  $resolve = {
    param([string]$s)
    $v = $s.TrimStart('#')
    if ($v -match '^-?\d+$') { return [long]$v }
    if ($strAddr.ContainsKey($v)) { return [long]$strAddr[$v] }
    if ($labels.ContainsKey($v))  { return [long]$labels[$v] }
    throw "unresolved symbol '$v'"
  }.GetNewClosure()

  # Pass 2: encode.
  $words = New-Object 'System.Collections.Generic.List[long]'
  foreach ($t in $instrs) {
    $parts = $t -split '\s+', 2
    $mn = $parts[0].ToUpper()
    $rest = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    $ops = @()
    if ($rest.Trim() -ne '') { $ops = @(($rest -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) }
    $script:lastInstr = $t

    switch ($mn) {
      'RET'  { $words.Add((Encode-Std $OP.RET 0 0 0 0)); continue }
      'TBRANCH' {
        # Reads the register directly, not FLAGS, and expands to three words.
        $rc = Parse-Reg $ops[0]
        $words.Add((Encode-Wide $OP.TBR_POS  $rc (& $resolve $ops[1])))
        $words.Add((Encode-Wide $OP.TBR_ZERO $rc (& $resolve $ops[2])))
        $words.Add((Encode-Wide $OP.JUMP     0   (& $resolve $ops[3])))
        continue
      }
      'JUMP'     { $words.Add((Encode-Wide $OP.JUMP     0 (& $resolve $ops[0]))); continue }
      'CALL'     { $words.Add((Encode-Wide $OP.CALL     0 (& $resolve $ops[0]))); continue }
      'SYSCALL'  { $words.Add((Encode-Wide $OP.SYSCALL  0 (& $resolve $ops[0]))); continue }
      'CALLR'    { $words.Add((Encode-Std  $OP.CALLR (Parse-Reg $ops[0]) 0 0 0)); continue }
      'TLIT'     { $words.Add((Encode-Wide $OP.TLIT     (Parse-Reg $ops[0]) (& $resolve $ops[1]))); continue }
      'TBR_POS'  { $words.Add((Encode-Wide $OP.TBR_POS  (Parse-Reg $ops[0]) (& $resolve $ops[1]))); continue }
      'TBR_ZERO' { $words.Add((Encode-Wide $OP.TBR_ZERO (Parse-Reg $ops[0]) (& $resolve $ops[1]))); continue }
      'LOAD' {
        $rd = Parse-Reg $ops[0]
        if ($ops[1] -notmatch '^\[\s*([Rr]\d+)\s*\+\s*#(-?\d+)\s*\]$') { throw "bad LOAD operand '$($ops[1])'" }
        $words.Add((Encode-Std $OP.LOAD $rd (Parse-Reg $Matches[1]) 0 ([long]$Matches[2]))); continue
      }
      'STORE' {
        $rs = Parse-Reg $ops[0]
        if ($ops[1] -notmatch '^\[\s*([Rr]\d+)\s*\+\s*#(-?\d+)\s*\]$') { throw "bad STORE operand '$($ops[1])'" }
        $words.Add((Encode-Std $OP.STORE $rs (Parse-Reg $Matches[1]) 0 ([long]$Matches[2]))); continue
      }
      'MOV'  { $words.Add((Encode-Std $OP.MOV  (Parse-Reg $ops[0]) (Parse-Reg $ops[1]) 0 0)); continue }
      'TNEG' { $words.Add((Encode-Std $OP.TNEG (Parse-Reg $ops[0]) (Parse-Reg $ops[1]) 0 0)); continue }
      default {
        if (-not $OP.ContainsKey($mn)) { throw "unknown mnemonic '$mn'" }
        # Three-address: Rd, Ra, (Rb | #imm). A register third operand goes in
        # r3 with imm zero; an immediate goes in imm with r3 zero.
        $rd = Parse-Reg $ops[0]
        $ra = Parse-Reg $ops[1]
        $r3 = 0L; $imm = 0L
        if ($ops[2] -match '^#') { $imm = & $resolve $ops[2] } else { $r3 = Parse-Reg $ops[2] }
        # Both forms are legal: rhs = regs[r3] + imm (spec v1.3). This used to
        # refuse the register form of TSHI/TSHR, because measured 2026-08-09
        # the emulator read the imm field and ignored r3, so a register shift
        # was a silent multiply by 3^0. Re-measured 2026-08-10 against the
        # v1.3 emulator it answers 1594323 for a shift of 13 either way.
        # Refusing it now would reject a conforming program.
        $words.Add((Encode-Std $OP[$mn] $rd $ra $r3 $imm)); continue
      }
    }
  }

  # The .t3s literal escapes a quote as \" and a backslash as \\ ; the sidecar
  # carries both raw. \n stays escaped, and the emulator re-expands it
  # (reference section 9). One left-to-right pass, so \\" cannot double-unescape.
  $sidecar = @()
  foreach ($l in $strLabels) {
    $s = $strText[$l]; $o = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $s.Length; $i++) {
      if ($s[$i] -eq '\' -and $i + 1 -lt $s.Length -and ($s[$i+1] -eq '"' -or $s[$i+1] -eq '\')) {
        [void]$o.Append($s[$i+1]); $i++
      } else { [void]$o.Append($s[$i]) }
    }
    $sidecar += ("{0}:{1}" -f $strAddr[$l], $o.ToString())
  }

  [pscustomobject]@{ words = $words; sidecar = $sidecar; codeSize = $codeSize; labels = $labels }
}

# Decode a word into its five balanced-ternary fields, plus the wide reading.
function Get-BalancedDigit([long]$v, [long]$m) {
  $r = $v % $m; if ($r -lt 0) { $r += $m }
  if ($r -gt ($m - 1) / 2) { $r -= $m }
  [long]$r
}
function Decode-Word([long]$w) {
  $imm = Get-BalancedDigit $w 27;  $x = ($w - $imm) / 27
  $r3  = Get-BalancedDigit $x 243; $x = ($x - $r3) / 243
  $r2  = Get-BalancedDigit $x 243; $x = ($x - $r2) / 243
  $r1  = Get-BalancedDigit $x 243; $op = ($x - $r1) / 243
  $wd = $w - $op * $P18 - $r1 * $P13
  $wr = $wd % $P13; if ($wr -lt 0) { $wr += $P13 }
  if ($wr -gt ($P13 - 1) / 2) { $wr -= $P13 }
  [pscustomobject]@{ op = $op; r1 = $r1; r2 = $r2; r3 = $r3; imm = $imm; wide = $wr }
}
