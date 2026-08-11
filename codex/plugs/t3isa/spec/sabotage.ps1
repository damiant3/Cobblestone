# Negative controls for validate.ps1.
#
# A green check that has never been shown to go red is worth nothing (L-FALSIF).
# Each arm mutates one part of the encoding in a COPY of the assembler library,
# reruns the full validation against it, and reports how many programs it takes
# down. The arms are shaped, not generic: two of them leave survivors, and the
# survivors are named below because which programs survive is itself the
# evidence that the arm broke what it claims to break.
param(
  [string]$Work = (Join-Path $PSScriptRoot 'build-output')
)
$lib = Join-Path $PSScriptRoot 't3isa-assembler.ps1'
$val = Join-Path $PSScriptRoot 'validate.ps1'
$src = Get-Content $lib -Raw
if ((Get-ChildItem $Work -Filter *.t3s -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0) {
  throw "no .t3s corpus in $Work. Run build-corpus.ps1 first."
}
# The baseline is what the UNMUTATED library scores, not the corpus size. Those
# differed once: a .t3s whose .t3b manitc never wrote sat in the corpus and
# added a phantom casualty to every arm.
$baseline = [int](($(& $val -Work $Work -Quiet) -split '\s+')[0])
if ($baseline -eq 0) { throw "the unmutated library validates nothing; fix that before reading any arm." }
$total = $baseline

$arms = @(
  @{ name = 'TADD renumbered 1 -> 7'
     find = 'TADD=1;'; repl = 'TADD=7;'
     expect = 'every program: TADD is in all of them' }
  @{ name = 'CALLR renumbered 28 -> 23'
     find = 'CALLR=28'; repl = 'CALLR=23'
     expect = 'only programs using an indirect call' }
  @{ name = 'string table in declaration order'
     find = '[Array]::Sort($sorted, [StringComparer]::Ordinal)'; repl = ''
     expect = 'all but programs with fewer than ten string literals, where the two orders agree' }
  @{ name = 'TBRANCH counted as one word'
     find = "if (`$mn -eq 'TBRANCH') { `$wi += 3 }"; repl = "if (`$mn -eq 'TBRANCH') { `$wi += 1 }"
     expect = 'all but programs with no TBRANCH' }
)

Write-Output "negative controls: baseline is $baseline programs byte-identical"
Write-Output ""
$fired = 0
foreach ($arm in $arms) {
  if (-not $src.Contains($arm.find)) { Write-Output ("  {0,-38} ARM IS STALE: pattern not found in the library" -f $arm.name); continue }
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ("t3isa-sabotage-" + [Guid]::NewGuid().ToString('N') + ".ps1")
  $src.Replace($arm.find, $arm.repl) | Set-Content -LiteralPath $tmp -NoNewline
  $line = & $val -Work $Work -Lib $tmp -Quiet
  Remove-Item -LiteralPath $tmp -Force
  $parts = ($line -split '\s+')
  $survived = [int]$parts[0]
  $took = $total - $survived
  if ($took -gt 0) { $fired++ }
  $verdict = if ($took -gt 0) { "FIRES, takes $took of $total" } else { "DID NOT FIRE" }
  Write-Output ("  {0,-38} {1}" -f $arm.name, $verdict)
  Write-Output ("  {0,-38}   expected: {1}" -f '', $arm.expect)
}

Write-Output ""
$line = & $val -Work $Work -Quiet
$parts = ($line -split '\s+')
Write-Output ("restored library: {0} identical, {1} failed, {2} words" -f $parts[0], $parts[1], $parts[2])
Write-Output "arms that fired: $fired of $($arms.Count)"
exit ([int]($fired -ne $arms.Count -or [int]$parts[1] -gt 0))
