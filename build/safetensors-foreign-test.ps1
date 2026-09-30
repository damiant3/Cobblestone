# Can Codex read a safetensors header that Codex did not write?
#
# `Foreword chapter SafeTensors` parses the 8-byte length and the JSON header of
# a real model, here a 6.46 GB SDXL checkpoint whose data offsets run past 4 GB,
# in a guest, and every line it prints is checked against an independent parse
# of the same bytes done on the host with ConvertFrom-Json.
#
# Only the PREFIX is handed over: 8 bytes of length and the header itself. A
# guest holds bytes as a list at about 8 bytes per byte, so the tensor data
# never crosses; what this grades is everything a reader must get right to
# find a tensor, the offsets above 2^32 included.
#
# Nothing third-party is committed; the expected answers are re-derived from
# the model on every run.
#
#   pwsh build/safetensors-foreign-test.ps1
#   pwsh build/safetensors-foreign-test.ps1 -Model <path.safetensors> -Kernel build/output/Sut.cdx
[CmdletBinding()]
param(
    [string]$Model = 'D:\AI\DiffusionForge\webui\models\Stable-diffusion\dreamshaperXL_lightningDPMSDE.safetensors',
    [string]$Kernel = '',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path $Model)) { Write-Host "FAIL: no model at $Model. This harness grades a FOREIGN file and fails rather than skips."; exit 1 }

$work = Join-Path $Repo 'build-output\safetensors-foreign'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

# --------------------------------------------------- the independent oracle

$fs = [System.IO.File]::OpenRead($Model)
$fileLen = $fs.Length
$lenBytes = New-Object byte[] 8
[void]$fs.Read($lenBytes, 0, 8)
$hlen = [BitConverter]::ToUInt64($lenBytes, 0)
if ($hlen -le 0 -or $hlen -gt 100MB) { $fs.Close(); Write-Host "FAIL: implausible header length $hlen"; exit 1 }
$prefix = New-Object byte[] (8 + $hlen)
[Array]::Copy($lenBytes, $prefix, 8)
$got = 0
while ($got -lt $hlen) { $n = $fs.Read($prefix, 8 + $got, $hlen - $got); if ($n -le 0) { break }; $got += $n }
$fs.Close()
$header = [System.Text.Encoding]::UTF8.GetString($prefix, 8, $hlen) | ConvertFrom-Json
$names = @($header.PSObject.Properties | Where-Object { $_.Name -ne '__metadata__' })
$f16 = @($names | Where-Object { $_.Value.dtype -eq 'F16' }).Count
$last = $names | Sort-Object { [int64]$_.Value.data_offsets[1] } | Select-Object -Last 1
$maxEnd = [int64]$last.Value.data_offsets[1]
$dataLen = $fileLen - 8 - [int64]$hlen
Write-Host "[st-foreign] $([System.IO.Path]::GetFileName($Model)): $fileLen bytes, header $hlen, $($names.Count) tensors, data ends at $maxEnd of $dataLen"
if ($maxEnd -ne $dataLen) { Write-Host 'FAIL: the host parse does not account for the whole data section; the oracle is wrong'; exit 1 }
$want = [ordered]@{
    valid     = 'True'
    header    = "$hlen"
    tensors   = "$($names.Count)"
    f16       = "$f16"
    f8e4m3    = "$(@($names | Where-Object { $_.Value.dtype -eq 'F8_E4M3' }).Count)"
    f8e5m2    = "$(@($names | Where-Object { $_.Value.dtype -eq 'F8_E5M2' }).Count)"
    first     = $names[0].Name
    maxend    = "$maxEnd"
    last      = $last.Name
    laststart = "$([int64]$last.Value.data_offsets[0])"
    lastshape = (@($last.Value.shape) -join 'x')
}

# ---------------------------------------------------------------- the guest

$guestSrc = Join-Path $work 'foreign-safetensors.codex'
Set-Content -Path $guestSrc -Encoding utf8 -Value @'
Chapter: ForeignSafeTensorsProbe
  cites AI chapter SafeTensors
  cites Foreword chapter Fat16
  cites Foreword chapter Maybe

Section: Entry

  opening : [Console, Device.Block] Nothing
  opening = act
    vol <- fat16-boot-volume
    got <- fat16-read-bytes vol "AGENT.GGU"
    when got
      is None -> print-line-uni "no header"
      is Just (bs) -> report (st-parse-file bs)
  end

  count-dtype : List StTensorMeta, Integer, Integer, Integer -> Integer
  count-dtype (ts) (d) (i) (acc) =
    if i >= list-length ts then acc
    else let t = list-at ts i
    in count-dtype ts d (i + 1) (if t.stm-dtype == d then acc + 1 else acc)

  last-ending : List StTensorMeta, Integer, Integer, Integer -> Integer
  last-ending (ts) (i) (best) (best-end) =
    if i >= list-length ts then best
    else let t = list-at ts i
    in if t.stm-offset-end > best-end then last-ending ts (i + 1) i (t.stm-offset-end)
    else last-ending ts (i + 1) best best-end

  report : SafeTensorFile -> [Console] Nothing
  report (f) = act
    let ts = f.st-tensors
    in let b = last-ending ts 0 (0 - 1) (0 - 1)
    in act
      print-line-uni ("valid " & show (f.st-valid))
      print-line-uni ("header " & show (f.st-header-len))
      print-line-uni ("tensors " & show (f.st-tensor-count))
      print-line-uni ("f16 " & show (count-dtype ts st-dtype-f16 0 0))
      print-line-uni ("f8e4m3 " & show (count-dtype ts st-dtype-f8-e4m3 0 0))
      print-line-uni ("f8e5m2 " & show (count-dtype ts st-dtype-f8-e5m2 0 0))
      print-line-uni ("first " & first-name ts)
      print-line-uni ("maxend " & last-field ts b 0)
      print-line-uni ("last " & last-field ts b 1)
      print-line-uni ("laststart " & last-field ts b 2)
      print-line-uni ("lastshape " & last-field ts b 3)
    end
  end

  first-name : List StTensorMeta -> Text
  first-name (ts) =
    if list-length ts == 0 then "-"
    else let t = list-at ts 0
    in t.stm-name

  last-field : List StTensorMeta, Integer, Integer -> Text
  last-field (ts) (b) (which) =
    if b < 0 then "-"
    else let t = list-at ts b
    in if which == 0 then show (t.stm-offset-end)
    else if which == 1 then t.stm-name
    else if which == 2 then show (t.stm-offset-start)
    else st-format-shape (t.stm-shape) 0 (t.stm-ndim) ""
'@

$guestCdx = Join-Path $work 'foreign-safetensors.cdx'
Write-Host '[st-foreign] compiling the guest...'
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') -Src $guestSrc -Out $guestCdx -Log (Join-Path $work 'compile.log') -Kernel $Kernel 2>&1 | Out-Null
$ErrorActionPreference = $prev
if (-not (Test-Path $guestCdx)) {
    Write-Host 'FAIL: guest compile failed'
    Select-String -Path (Join-Path $work 'compile.log') -Pattern 'error CDX' | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
    exit 1
}

# ------------------------------------------------------------------- the run

$payload = Join-Path $work 'header.bin'
[System.IO.File]::WriteAllBytes($payload, $prefix)
$stub = Join-Path $work 'pe.efi'
[System.IO.File]::WriteAllBytes($stub, ([byte[]](0x4D, 0x5A) + (New-Object byte[] 510)))
$man = Join-Path $work 'AGENT.MAN'
Set-Content -Path $man -Value "stub`n" -Encoding ascii
$sectors = [Math]::Max(16384, [int](($prefix.Length / 512) * 1.4) + 4096)
$img = Join-Path $work 'header.img'
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-img.ps1') -PeInput $stub -Out $img -Agent $payload -AgentManifest $man -TotalSectors $sectors 2>&1 | Out-Null
$ErrorActionPreference = $prev
if (-not (Test-Path $img)) { Write-Host 'FAIL: could not build the image'; exit 1 }

$out = Join-Path $work 'guest.out'
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& (Join-Path $Repo 'tools\codex-vm.exe') -kernel $guestCdx -disk $img -headless -output $out -mem 3072 2>&1 | Out-Null
$ErrorActionPreference = $prev
$gotKv = @{}
if (Test-Path $out) {
    foreach ($line in ((Get-Content $out -Raw) -replace '[^\x20-\x7E\r\n]', '') -split "`n") {
        $p = $line.Trim() -split ' ', 2
        if ($p.Count -eq 2) { $gotKv[$p[0]] = $p[1].Trim() }
    }
}

$fail = 0
foreach ($k in $want.Keys) {
    $g = if ($gotKv.ContainsKey($k)) { $gotKv[$k] } else { '<missing>' }
    if ($g -eq $want[$k]) { Write-Host ("  PASS  {0,-10} {1}" -f $k, $want[$k]) }
    else { Write-Host ("  FAIL  {0,-10} guest '{1}' host '{2}'" -f $k, $g, $want[$k]); $fail++ }
}
Write-Host ''
if ($fail -gt 0) { Write-Host "safetensors-foreign-test: $fail check(s) FAILED; artifacts kept in $work"; exit 1 }
if (-not $KeepArtifacts) { Remove-Item -Recurse -Force $work -EA SilentlyContinue }
Write-Host 'safetensors-foreign-test: PASS'
exit 0
