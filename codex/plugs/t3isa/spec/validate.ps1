# Prove the T3ISA encoding by reassembling their compiler's own .t3s listings
# and requiring the words to come out byte-identical to their .t3b, and the
# sidecar to match their .t3d as a set of addr:content lines.
#
# Exit code 0 only if every program matched. Run sabotage.ps1 to confirm this
# check can go red before resting anything on its green.
param(
  [string]$Work = (Join-Path $PSScriptRoot 'build-output'),
  [string]$Lib  = (Join-Path $PSScriptRoot 't3isa-assembler.ps1'),
  [switch]$Quiet
)
. $Lib

$pass = 0; $fail = 0; $words = 0
foreach ($f in (Get-ChildItem $Work -Filter *.t3s -ErrorAction SilentlyContinue | Sort-Object Name)) {
  $stem = [IO.Path]::GetFileNameWithoutExtension($f.Name)
  $ref = Join-Path $Work "$stem.t3b"
  if (-not (Test-Path $ref)) { continue }

  try { $a = Assemble $f.FullName }
  catch {
    if (-not $Quiet) { Write-Output ("{0,-22} THREW: {1}  on <{2}>" -f $stem, $_.Exception.Message, $script:lastInstr) }
    $fail++; continue
  }

  $refBytes = [IO.File]::ReadAllBytes($ref)
  $mine = New-Object byte[] ($a.words.Count * 8)
  for ($i = 0; $i -lt $a.words.Count; $i++) { [BitConverter]::GetBytes([long]$a.words[$i]).CopyTo($mine, $i * 8) }

  if ($mine.Length -ne $refBytes.Length) {
    if (-not $Quiet) { Write-Output ("{0,-22} LENGTH {1} vs {2} words" -f $stem, ($mine.Length/8), ($refBytes.Length/8)) }
    $fail++; continue
  }
  $diff = -1
  for ($i = 0; $i -lt $mine.Length; $i++) { if ($mine[$i] -ne $refBytes[$i]) { $diff = $i; break } }
  if ($diff -ge 0) {
    if (-not $Quiet) { Write-Output ("{0,-22} MISMATCH at word {1}" -f $stem, [int]($diff/8)) }
    $fail++; continue
  }

  $sideNote = 'no sidecar'
  $sidePath = Join-Path $Work "$stem.t3d"
  if (Test-Path $sidePath) {
    $refSide = @(Get-Content $sidePath | Where-Object { $_ -ne '' })
    $c = Compare-Object ($a.sidecar | Sort-Object) ($refSide | Sort-Object)
    if ($null -ne $c) {
      if (-not $Quiet) { Write-Output ("{0,-22} words OK, SIDECAR DIFF ({1} lines)" -f $stem, ($c | Measure-Object).Count) }
      $fail++; continue
    }
    $sideNote = "sidecar OK ($($refSide.Count))"
  }

  $words += $a.words.Count; $pass++
  if (-not $Quiet) { Write-Output ("{0,-22} IDENTICAL {1,6} words   {2}" -f $stem, $a.words.Count, $sideNote) }
}

if (-not $Quiet) {
  Write-Output ""
  Write-Output "byte-identical: $pass    failed: $fail    words proven: $words"
} else {
  Write-Output ("{0} {1} {2}" -f $pass, $fail, $words)
}
exit ([int]($fail -gt 0 -or $pass -eq 0))
