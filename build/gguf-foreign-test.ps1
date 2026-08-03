# Can Codex read a GGUF that Codex did not write?
#
# This is the direction that was missing. `codex/test/apps/bundled-agent` proves
# the bundle chain against a model this tree generates, and two halves written
# together agree whatever they both believe -- the BrotliBeatsOpus shape. This
# harness points the instrument the other way: real llama.cpp / Ollama model
# files, parsed by `Foreword chapter Gguf` in a guest, checked against an
# independent parse done here on the host.
#
# For each model it takes only the PREFIX up to the start of tensor data, which
# is the header, the metadata block and the tensor table -- everything a reader
# has to walk correctly to reach a tensor. That prefix is a few hundred KB even
# for a 22 GB model, because it is dominated by the tokenizer vocabulary: an
# array of tens of thousands of strings, which is exactly the metadata shape a
# generated fixture never has.
#
# Nothing third-party is committed. The models are found on the box and the
# expected answers are re-derived from them on every run, so this cannot rot
# into a stale constant.
#
#   pwsh build/gguf-foreign-test.ps1
#   pwsh build/gguf-foreign-test.ps1 -ModelDir D:\AI -Max 4
#   pwsh build/gguf-foreign-test.ps1 -Kernel build/output/Sut.cdx -KeepArtifacts
[CmdletBinding()]
param(
    [string]$ModelDir = 'D:\AI',
    [int]$Max = 4,
    [string]$Kernel = '',
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

$work = Join-Path $Repo 'build-output\gguf-foreign'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

# ---------------------------------------------------------- find real models

# Ollama stores GGUF as content-addressed blobs with no extension, so the file
# is identified by its magic rather than by its name.
Write-Host "[gguf-foreign] scanning $ModelDir for GGUF files..."
$candidates = @()
foreach ($f in (Get-ChildItem $ModelDir -Recurse -File -EA SilentlyContinue | Where-Object { $_.Length -gt 1MB })) {
    try {
        $fs = [System.IO.File]::OpenRead($f.FullName)
        $m = New-Object byte[] 4
        [void]$fs.Read($m, 0, 4); $fs.Close()
        if ([System.Text.Encoding]::ASCII.GetString($m) -eq 'GGUF') { $candidates += $f }
    } catch { }
}
if ($candidates.Count -eq 0) {
    Write-Host "FAIL: no GGUF file found under $ModelDir."
    Write-Host "  This harness needs a model produced by a FOREIGN implementation."
    Write-Host "  It fails rather than skips: a skip would read as coverage, and the"
    Write-Host "  whole claim here is that we can read somebody else's bytes."
    exit 1
}
$models = $candidates | Sort-Object Length | Select-Object -First $Max
Write-Host "[gguf-foreign] $($candidates.Count) found, testing $($models.Count)"

# --------------------------------------------------- the independent oracle

function Read-GgufPrefix ([string]$path) {
    $fs = [System.IO.File]::OpenRead($path)
    $cap = [Math]::Min($fs.Length, 64MB)
    $buf = New-Object byte[] $cap
    [void]$fs.Read($buf, 0, $cap); $fs.Close()

    $script:b = $buf; $script:o = 0
    function RU32 { $v = [BitConverter]::ToUInt32($script:b, $script:o); $script:o += 4; $v }
    function RU64 { $v = [BitConverter]::ToUInt64($script:b, $script:o); $script:o += 8; $v }
    function RStr { $n = [int](RU64); $s = [System.Text.Encoding]::UTF8.GetString($script:b, $script:o, $n); $script:o += $n; $s }
    function RVal([int]$t) {
        switch ($t) {
            0 { $script:o += 1 } 1 { $script:o += 1 } 2 { $script:o += 2 } 3 { $script:o += 2 }
            4 { $script:o += 4 } 5 { $script:o += 4 } 6 { $script:o += 4 } 7 { $script:o += 1 }
            10 { $script:o += 8 } 11 { $script:o += 8 } 12 { $script:o += 8 }
            8 { [void](RStr) }
            9 { $et = [int](RU32); $n = [int](RU64); for ($i = 0; $i -lt $n; $i++) { RVal $et } }
            default { throw "unknown GGUF value type $t" }
        }
    }

    $script:o = 4
    $ver = [int](RU32); $tc = [int](RU64); $kvc = [int](RU64)
    $arch = ''; $align = 32
    for ($i = 0; $i -lt $kvc; $i++) {
        $k = RStr; $t = [int](RU32)
        if ($k -eq 'general.architecture' -and $t -eq 8) { $arch = RStr }
        elseif ($k -eq 'general.alignment' -and $t -eq 4) { $align = [int](RU32) }
        else { RVal $t }
    }
    $tableOff = $script:o
    $first = ''
    for ($i = 0; $i -lt $tc; $i++) {
        $n = RStr
        if ($i -eq 0) { $first = $n }
        $nd = [int](RU32); for ($d = 0; $d -lt $nd; $d++) { [void](RU64) }
        [void](RU32); [void](RU64)
    }
    $dataStart = [int]([Math]::Ceiling($script:o / $align) * $align)
    return @{
        Version = $ver; Tensors = $tc; Kv = $kvc; Arch = $arch
        TableOff = $tableOff; First = $first; PrefixBytes = $dataStart
        Buf = $buf
    }
}

# ---------------------------------------------------------------- the guest

$guestSrc = Join-Path $work 'foreign-gguf.codex'
Set-Content -Path $guestSrc -Encoding utf8 -Value @'
Chapter: ForeignGgufProbe
  cites AI chapter Gguf
  cites Foreword chapter Fat16
  cites Foreword chapter Maybe

 Reads a real model file off the attached disk and reports what the header,
 the metadata walk and the tensor table say. The host compares every line
 against its own parse of the same bytes.

Section: Entry

  opening : [Console, Device.Block] Nothing
  opening = act
    vol <- fat16-boot-volume
    got <- fat16-read-bytes vol "AGENT.GGU"
    when got
      is None -> print-line-uni "no model"
      is Just (bs) -> report bs
  end

  report : List Integer -> [Console] Nothing
  report (bs) = act
    let h = gguf-parse-header bs
    in let ti = gguf-tensor-info-offset bs
    in act
      print-line-uni ("valid " & show (h.gh-valid))
      print-line-uni ("version " & show (h.gh-version))
      print-line-uni ("tensors " & show (h.gh-tensor-count))
      print-line-uni ("kv " & show (h.gh-metadata-count))
      print-line-uni ("arch " & gguf-metadata-text bs "general.architecture")
      print-line-uni ("table " & show ti)
      print-line-uni ("first " & (if ti < 0 then "-" else (gguf-parse-tensor-info bs ti).gti-name))
    end
  end
'@

$guestCdx = Join-Path $work 'foreign-gguf.cdx'
Write-Host "[gguf-foreign] compiling the guest..."
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') -Src $guestSrc -Out $guestCdx `
    -Log (Join-Path $work 'compile.log') -Kernel $Kernel 2>&1 | Out-Null
$rc = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($rc -ne 0 -or -not (Test-Path $guestCdx)) {
    Write-Host 'FAIL: guest compile failed'
    Get-Content (Join-Path $work 'compile.log') -TotalCount 20 -EA SilentlyContinue | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# ------------------------------------------------------------------- the runs

$stub = Join-Path $work 'pe.efi'
[System.IO.File]::WriteAllBytes($stub, ([byte[]](0x4D, 0x5A) + (New-Object byte[] 510)))
$man = Join-Path $work 'AGENT.MAN'
Set-Content -Path $man -Value "stub`n" -Encoding ascii

$fail = 0
$n = 0
foreach ($m in $models) {
    $n++
    $tag = "model$n"
    Write-Host ''
    Write-Host "--- $tag : $($m.Name.Substring(0, [Math]::Min(24, $m.Name.Length))) ($([math]::Round($m.Length/1MB,0)) MB) ---"
    $want = Read-GgufPrefix $m.FullName

    $ggu = Join-Path $work "$tag.ggu"
    $slice = New-Object byte[] $want.PrefixBytes
    [Array]::Copy($want.Buf, 0, $slice, 0, $want.PrefixBytes)
    [System.IO.File]::WriteAllBytes($ggu, $slice)

    # The image must hold the prefix; size it from the payload rather than
    # assuming the 8 MB default is enough.
    $sectors = [Math]::Max(16384, [int](($want.PrefixBytes / 512) * 1.4) + 4096)
    $img = Join-Path $work "$tag.img"
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-img.ps1') -PeInput $stub -Out $img `
        -Agent $ggu -AgentManifest $man -TotalSectors $sectors 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    if (-not (Test-Path $img)) { Write-Host "  FAIL: could not build the image"; $fail++; continue }

    $out = Join-Path $work "$tag.out"
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & (Join-Path $Repo 'tools\codex-vm.exe') -kernel $guestCdx -disk $img -headless -output $out -mem 3072 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    $got = @{}
    if (Test-Path $out) {
        foreach ($line in ((Get-Content $out -Raw) -replace '[^\x20-\x7E\r\n]', '') -split "`n") {
            $p = $line.Trim() -split ' ', 2
            if ($p.Count -eq 2) { $got[$p[0]] = $p[1].Trim() }
        }
    }

    function Check ([string]$key, [string]$want) {
        $g = if ($got.ContainsKey($key)) { $got[$key] } else { '<missing>' }
        if ($g -eq $want) { Write-Host ("  PASS  {0,-8} {1}" -f $key, $want) }
        else { Write-Host ("  FAIL  {0,-8} guest '{1}' host '{2}'" -f $key, $g, $want); $script:fail++ }
    }
    Check 'valid'   'True'
    Check 'version' "$($want.Version)"
    Check 'tensors' "$($want.Tensors)"
    Check 'kv'      "$($want.Kv)"
    Check 'arch'    $want.Arch
    Check 'table'   "$($want.TableOff)"
    Check 'first'   $want.First
}

Write-Host ''
if ($fail -gt 0) {
    Write-Host "gguf-foreign-test: $fail check(s) FAILED"
    Write-Host "artifacts kept in $work"
    exit 1
}
if (-not $KeepArtifacts) { Remove-Item -Recurse -Force $work -EA SilentlyContinue }
Write-Host "gguf-foreign-test: PASS ($($models.Count) foreign model(s))"
exit 0
