# Derive the numeric T3ISA opcode values, which the published reference does
# not state, by arithmetic from matched (.t3s, .t3b) pairs.
#
# The method: their compiler drops an assembly listing beside the binary, so
# each instruction line pairs with one word (three, for TBRANCH). Decoding the
# word into balanced-ternary fields and reading the top field off against the
# mnemonic on the line gives that mnemonic's opcode. Any two programs
# disagreeing about a mnemonic is a conflict and is reported. Their src/ is not
# read.
param([string]$Work = (Join-Path $PSScriptRoot 'build-output'))
. (Join-Path $PSScriptRoot 't3isa-assembler.ps1')

$map = @{}; $seen = @{}; $conflicts = 0; $totalAsm = 0; $files = 0
foreach ($f in (Get-ChildItem $Work -Filter *.t3s -ErrorAction SilentlyContinue | Sort-Object Name)) {
  $stem = [IO.Path]::GetFileNameWithoutExtension($f.Name)
  $binPath = Join-Path $Work "$stem.t3b"
  if (-not (Test-Path $binPath)) { continue }
  $bytes = [IO.File]::ReadAllBytes($binPath)
  $n = $bytes.Length / 8

  $asm = @(); $inData = $false
  foreach ($line in Get-Content $f.FullName) {
    $t = ($line -split ';')[0].Trim()
    if ($t -eq '') { continue }
    if ($t -match '^\.data:?$') { $inData = $true; continue }
    if ($t -match '^\.globals:?$') { $inData = $false; continue }
    if ($inData) { continue }
    while ($t -match '^([A-Za-z_][A-Za-z0-9_.:]*):\s*(.*)$') {
      $t = $Matches[2].Trim(); if ($t -eq '') { break }
    }
    if ($t -eq '') { continue }
    $asm += $t
  }

  $wi = 0
  foreach ($a in $asm) {
    $mn = ($a -split '\s+')[0].ToUpper()
    if ($wi -ge $n) { Write-Output "[$stem] ran out of words at '$a'"; break }
    # [string[]] is load-bearing: a one-element array unrolls to a scalar here,
    # and indexing a scalar string yields its first CHARACTER, so every
    # mnemonic would be derived as its initial letter.
    [string[]]$names = if ($mn -eq 'TBRANCH') { @('TBR_POS','TBR_ZERO','JUMP') } else { @($mn) }
    for ($k = 0; $k -lt $names.Count; $k++) {
      $d = Decode-Word ([BitConverter]::ToInt64($bytes, ($wi + $k) * 8))
      $nm = $names[$k]
      if ($map.ContainsKey($nm)) {
        if ($map[$nm] -ne $d.op) { Write-Output "[$stem] CONFLICT $nm : $($map[$nm]) vs $($d.op) at word $($wi+$k)"; $conflicts++ }
      } else { $map[$nm] = $d.op }
      $seen[$nm] = [int]$seen[$nm] + 1
    }
    $wi += $names.Count
  }
  $totalAsm += $asm.Count; $files++
  $tail = if ($wi -eq $n) { 'code words consumed exactly' } else { "consumed $wi of $n" }
  Write-Output ("{0,-22} words={1,-6} asm={2,-6} {3}" -f $stem, $n, $asm.Count, $tail)
}

Write-Output ""
Write-Output "programs: $files   instructions aligned: $totalAsm   conflicts: $conflicts"
Write-Output ""
Write-Output "opcode  mnemonic     occurrences"
$map.GetEnumerator() | Sort-Object Value | ForEach-Object { "{0,-7} {1,-12} {2}" -f $_.Value, $_.Key, $seen[$_.Key] }
$have = @($map.Values)
Write-Output ""
Write-Output "numbers still unclaimed in 0..28: $(((0..28) | Where-Object { $have -notcontains $_ }) -join ', ')"
$all = @('TADD','TSUB','TMUL','TDIV','TMOD','TNEG','TAND','TOR','TNOT','TSHI','TSHR','TMIN','TMAX','TCMP',
         'LOAD','STORE','TLIT','MOV','JUMP','TBR_POS','TBR_ZERO','TBR_NEG','CALL','CALLR','RET','SYSCALL','HALT','NOP')
Write-Output "mnemonics their front end never emits: $(($all | Where-Object { -not $map.ContainsKey($_) }) -join ', ')"
