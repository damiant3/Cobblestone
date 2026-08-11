# Hold the CODEX encoder (T3IsaEncode.codex) to the same proof the PowerShell
# one passed: reassemble the external compiler's own .t3s listings and require
# the words to come out byte-identical to its .t3b, and the sidecar to match
# its .t3d as a set.
#
# The plug's T3-ASM mode exists for this. Reaching the encoder only through
# the emitter would make it an instrument reachable only through the thing it
# measures.
param(
  [string]$Work = (Join-Path $PSScriptRoot 'build-output'),
  [switch]$Quiet
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PlugDir = Split-Path $PSScriptRoot -Parent
$Repo = (Resolve-Path (Join-Path $PlugDir '..' '..' '..')).Path
. (Join-Path $Repo 'build\vm-config.ps1')
$PlugCdx = Join-Path $PlugDir 'build-output\t3isa-plug.cdx'
if (-not (Test-Path $PlugCdx)) { throw "MISSING: $PlugCdx. Run codex/plugs/t3isa/build.ps1" }
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'

# The transport is the limitation here, not the encoder. vm-config's table
# maps Unicode 0..255 only, so a character above that (their listings use an
# arrow) has no CCE byte, and one inside the range with no mapping (micro)
# would come back as [byte]$null = 0. A ZERO BYTE TERMINATES read-serial-cce,
# which truncates the input and reads as the encoder mangling everything
# downstream. Drop unmappable characters instead, and treat any program whose
# listing is not pure ASCII as sidecar-uncomparable rather than failing it.
function To-Cce([string]$s) {
  $out = [System.Collections.Generic.List[byte]]::new()
  foreach ($ch in $s.ToCharArray()) {
    $u = [int]$ch
    if ($u -eq 10) { $out.Add([byte]1); continue }   # CCE newline
    if ($u -eq 13) { continue }
    if ($u -ge 256) { continue }
    $c = $script:UnicodeToCce[$u]
    if ($null -eq $c -or [int]$c -eq 0) { continue }
    $out.Add([byte]$c)
  }
  , $out.ToArray()
}

function Invoke-Encoder([string]$asmPath) {
  $asm = [System.IO.File]::ReadAllText($asmPath)
  $hdr = To-Cce "T3-ASM`n"
  $body = To-Cce $asm
  $buf = New-Object byte[] ($hdr.Length + $body.Length + 1)
  [Buffer]::BlockCopy($hdr, 0, $buf, 0, $hdr.Length)
  [Buffer]::BlockCopy($body, 0, $buf, $hdr.Length, $body.Length)
  $buf[$buf.Length - 1] = 0
  $inF = [System.IO.Path]::GetTempFileName()
  $outF = [System.IO.Path]::GetTempFileName()
  $errF = [System.IO.Path]::GetTempFileName()
  [System.IO.File]::WriteAllBytes($inF, $buf)
  $p = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inF,'-output',$outF,'-mem','3072','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errF
  $p.WaitForExit(300000)
  if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; throw "timeout" }
  $raw = if (Test-Path $outF) { [System.IO.File]::ReadAllText($outF) } else { '' }
  Remove-Item $inF,$outF,$errF -Force -ErrorAction SilentlyContinue
  # The plug's first output line carries leading control bytes from the serial
  # framing, exactly as run.ps1 already accounts for. A reader that does not
  # strip them silently loses the FIRST line, which reads as the encoder
  # dropping an instruction: that cost a debugging cycle before the count of
  # emitted words was compared against the count of lines that matched.
  $raw -replace '^[\x00-\x1f]+', ''
}

$pass = 0; $fail = 0; $words = 0
foreach ($f in (Get-ChildItem $Work -Filter *.t3s -ErrorAction SilentlyContinue | Sort-Object Name)) {
  $stem = [IO.Path]::GetFileNameWithoutExtension($f.Name)
  $ref = Join-Path $Work "$stem.t3b"
  if (-not (Test-Path $ref)) { continue }

  $raw = Invoke-Encoder $f.FullName
  $lines = $raw -split "`r?`n"
  $errs = @($lines | Where-Object { $_ -match '^!ENCODE-ERROR' })
  if ($errs.Count -gt 0) {
    if (-not $Quiet) { Write-Output ("{0,-22} ENCODER REFUSED: {1}" -f $stem, $errs[0]) }
    $fail++; continue
  }
  $mine = @($lines | Where-Object { $_ -match '^W ' } | ForEach-Object { [long]($_.Substring(2).Trim()) })
  $side = @($lines | Where-Object { $_ -match '^S ' } | ForEach-Object { $_.Substring(2) })

  $refBytes = [IO.File]::ReadAllBytes($ref)
  if ($mine.Count * 8 -ne $refBytes.Length) {
    if (-not $Quiet) { Write-Output ("{0,-22} LENGTH {1} vs {2} words" -f $stem, $mine.Count, ($refBytes.Length/8)) }
    $fail++; continue
  }
  $bad = -1
  for ($i = 0; $i -lt $mine.Count; $i++) {
    if ($mine[$i] -ne [BitConverter]::ToInt64($refBytes, $i * 8)) { $bad = $i; break }
  }
  if ($bad -ge 0) {
    if (-not $Quiet) { Write-Output ("{0,-22} MISMATCH at word {1}" -f $stem, $bad) }
    $fail++; continue
  }

  $sideNote = 'no sidecar'
  $sidePath = Join-Path $Work "$stem.t3d"
  $null = $sidePath
  # Test the STRING BODIES, not the whole listing: every listing carries a
  # non-ASCII em-dash in its header comment, which cannot reach the sidecar,
  # and testing the file would skip every comparison for no reason.
  $asciiOnly = (-not (Test-Path $sidePath)) -or
               (-not ([regex]::IsMatch([System.IO.File]::ReadAllText($sidePath), '[^\x00-\x7F]')))
  if ((Test-Path $sidePath) -and $asciiOnly) {
    $refSide = @(Get-Content $sidePath | Where-Object { $_ -ne '' })
    $c = Compare-Object ($side | Sort-Object) ($refSide | Sort-Object)
    if ($null -ne $c) {
      if (-not $Quiet) { Write-Output ("{0,-22} words OK, SIDECAR DIFF ({1})" -f $stem, ($c | Measure-Object).Count) }
      $fail++; continue
    }
    $sideNote = "sidecar OK ($($refSide.Count))"
  } elseif (Test-Path $sidePath) {
    # The listing has non-ASCII text the transport cannot carry. The addresses
    # are still checked through the words; only the string BODIES are skipped.
    $sideNote = "sidecar skipped (non-ASCII text, transport-limited)"
  }
  $words += $mine.Count; $pass++
  if (-not $Quiet) { Write-Output ("{0,-22} IDENTICAL {1,6} words   {2}" -f $stem, $mine.Count, $sideNote) }
}

if ($Quiet) { Write-Output ("{0} {1} {2}" -f $pass, $fail, $words) }
else {
  Write-Output ""
  Write-Output "Codex encoder byte-identical: $pass    failed: $fail    words proven: $words"
}
exit ([int]($fail -gt 0 -or $pass -eq 0))
