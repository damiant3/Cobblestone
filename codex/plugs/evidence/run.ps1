# Run the compliance evidence plug over one build's artifacts and write the
# evidence package (docs/Designs/Active/IoT/ComplianceEvidence.md):
#
#   codex/plugs/evidence/run.ps1 -Cdx build-output/foo.cdx -Log build-output/foo.log `
#       -Source build-output/foo-bundled.codex -Product foo -Board x86-64 -OutDir out
#
# Writes into -OutDir: Evidence.cdxe (canonical, byte-stable for identical
# inputs), Evidence.html (the reviewer page), SBOM.cdx.json (CycloneDX-shaped),
# Evidence.sha256 (the package hash, over Evidence.cdxe), Evidence.inputs.txt
# (exactly what the plug was fed), and Evidence.sig when -SigningKey names an
# Ed25519 seed file (32 bytes): the signature over the package hash, made by
# the same inline signer pattern build.ps1 uses for the compiler.
#
# What the plug is fed and what it is not. The plug runs in codex-vm off the
# serial ring, so it receives TEXT: the CDX's first 224 bytes as hex (the
# header the claims read), the whole file's SHA-256 computed here, the compile
# log's DIAGNOSTIC lines with the log's SHA-256 computed here, and the source
# manifest (one line per chapter with the chapter's SHA-256, computed here from
# the bundled source). The package names which hashes were computed on the
# host and which by the plug (`hashes.host=` and `hashes.plug=` rows), and
# Evidence.inputs.txt is exactly the text the plug was fed, so any hash that
# appears there was passed in; the plug hashes only what it holds (the header).
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Cdx,
    [string]$Log = '',
    # The bundled source the CDX was compiled from (build/bundle-app.ps1 output
    # or the compiler concat): split at `Chapter:` for the manifest.
    [string]$Source = '',
    [string]$Product = '',
    [string]$Board = '',
    # A punctual (WCET) report file, if the build produced one.
    [string]$Punctual = '',
    [string]$OutDir = '',
    [string]$SigningKey = '',
    # A disk image with a Codex fact-store partition (every build-img.ps1 image
    # carries one at the top of the medium): the package is recorded there as a
    # kind-50 fact whose content names the firmware header hash, the package
    # hash and the counts (FactIngest.codex), and Evidence.fact.txt says what
    # was written. The image is modified in place.
    [string]$FactImage = '',
    # The fact record's timestamp (seconds); the package itself carries none.
    # 0 means "now" from the host clock.
    [long]$Timestamp = 0,
    [int]$TimeoutSec = 300
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
. (Join-Path $Repo 'build' 'vm-config.ps1')
$PlugCdx = Join-Path $PSScriptRoot 'build-output\evidence-plug.cdx'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx (run codex/plugs/evidence/build.ps1)"); exit 2 }
if (-not (Test-Path -PathType Leaf $Cdx)) { [Console]::Error.WriteLine("MISSING: $Cdx"); exit 2 }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'build-output\package' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
if (-not $Product) { $Product = [IO.Path]::GetFileNameWithoutExtension($Cdx) }

function Sha([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLower() }
function ShaBytes([byte[]]$b) { $h = [Security.Cryptography.SHA256]::Create(); ([BitConverter]::ToString($h.ComputeHash($b)) -replace '-', '').ToLower() }

# ---- the input lines
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('EVIDENCE')
$lines.Add("product $Product")
$lines.Add("board $Board")
$cdxBytes = [IO.File]::ReadAllBytes($Cdx)
$lines.Add("cdx-size $($cdxBytes.Length)")
$lines.Add("cdx-file-sha256 $(Sha $Cdx)")
$hdrLen = [Math]::Min(224, $cdxBytes.Length)
$lines.Add("cdx-header " + (([BitConverter]::ToString($cdxBytes, 0, $hdrLen) -replace '-', '').ToLower()))
if ($Log -and (Test-Path -PathType Leaf $Log)) {
    $lines.Add("log-sha256 $(Sha $Log)")
    $logLines = @(Get-Content $Log)
    $lines.Add("log-lines $($logLines.Count)")
    foreach ($l in $logLines) {
        if ($l -match ' (error|warning|info) CDX\d+:' -or $l -match '^CODEGEN-|^(errors|warnings)') {
            # the plug counts by ` <sev> CDX<n>:`; the path before it and the
            # message after are the reviewer's, not the counter's, so both are
            # kept but the line is capped
            $t = $l -replace '[\x00-\x08\x0b-\x1f]', ' '
            if ($t.Length -gt 300) { $t = $t.Substring(0, 300) }
            $lines.Add("log $t")
        }
    }
} else {
    $lines.Add('log-lines 0')
}
if ($Punctual -and (Test-Path -PathType Leaf $Punctual)) { $lines.Add("punctual-sha256 $(Sha $Punctual)") }
if ($Source -and (Test-Path -PathType Leaf $Source)) {
    $src = [IO.File]::ReadAllText($Source)
    $lines.Add("manifest-sha256 $(Sha $Source)")
    $parts = [regex]::Split($src, '(?m)^(?=Chapter:)')
    foreach ($p in $parts) {
        if ($p -notmatch '^Chapter:\s*(.+?)\s*\r?\n') { continue }
        $name = $Matches[1] -replace '\s+', '_'
        $lines.Add("chapter $name $(ShaBytes ([Text.Encoding]::UTF8.GetBytes($p)))")
    }
}
$lines.Add('END')
$inputsFile = Join-Path $OutDir 'Evidence.inputs.txt'
[IO.File]::WriteAllText($inputsFile, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))

# ---- CCE-encode for the ring (single-byte table; anything above 255 becomes '?')
$buf = [Collections.Generic.List[byte]]::new()
foreach ($l in $lines) {
    foreach ($ch in $l.ToCharArray()) {
        $u = [int]$ch
        if ($u -lt 256) { $buf.Add([byte]$script:UnicodeToCce[$u]) } else { $buf.Add([byte]$script:UnicodeToCce[63]) }
    }
    $buf.Add([byte]1)   # CCE newline
}
$buf.Add([byte]0)
$inputFile = [IO.Path]::GetTempFileName()
[IO.File]::WriteAllBytes($inputFile, $buf.ToArray())

# ---- run
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$outFile = [IO.Path]::GetTempFileName()
$errFile = [IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel', $PlugCdx, '-input', $inputFile, '-output', $outFile, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit($TimeoutSec * 1000) | Out-Null
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; [Console]::Error.WriteLine('FAIL: timeout'); exit 4 }
if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { '' }
    [Console]::Error.WriteLine('FAIL: no output')
    if ($err -match 'EXC') { [Console]::Error.WriteLine($err.Substring(0, [Math]::Min(400, $err.Length))) }
    exit 5
}
$raw = [IO.File]::ReadAllText($outFile) -replace "`r", ''
Remove-Item $inputFile, $outFile, $errFile -Force -ErrorAction SilentlyContinue

# ---- split the three documents
function Between([string]$text, [string]$a, [string]$b) {
    $i = $text.IndexOf($a); if ($i -lt 0) { return $null }
    $i += $a.Length
    $j = $text.IndexOf($b, $i); if ($j -lt 0) { return $null }
    return $text.Substring($i, $j - $i).TrimStart("`n").TrimEnd("`n") + "`n"
}
$cdxe = Between $raw "==CDXE==`n" "==HTML=="
$html = Between $raw "==HTML==`n" "==SBOM=="
$sbom = Between $raw "==SBOM==`n" "==END=="
if ($null -eq $cdxe -or $null -eq $html -or $null -eq $sbom) {
    [Console]::Error.WriteLine('FAIL: the plug did not emit all three documents; raw output follows')
    [Console]::Error.WriteLine($raw.Substring(0, [Math]::Min(600, $raw.Length)))
    exit 6
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutDir 'Evidence.cdxe'), $cdxe, $utf8)
[IO.File]::WriteAllText((Join-Path $OutDir 'Evidence.html'), $html, $utf8)
[IO.File]::WriteAllText((Join-Path $OutDir 'SBOM.cdx.json'), $sbom, $utf8)
$pkgHash = ShaBytes ($utf8.GetBytes($cdxe))
[IO.File]::WriteAllText((Join-Path $OutDir 'Evidence.sha256'), "$pkgHash  Evidence.cdxe`n", $utf8)

# ---- sign, when a key is given: the same inline signer shape as build.ps1
if ($SigningKey) {
    if (-not (Test-Path -PathType Leaf $SigningKey)) { [Console]::Error.WriteLine("MISSING: -SigningKey $SigningKey"); exit 7 }
    $keyBytes = [IO.File]::ReadAllBytes($SigningKey)
    $hashBytes = [Security.Cryptography.SHA256]::Create().ComputeHash($utf8.GetBytes($cdxe))
    $keyList = ($keyBytes | ForEach-Object { $_.ToString() }) -join ', '
    $hashList = ($hashBytes | ForEach-Object { $_.ToString() }) -join ', '
    $signSrc = Join-Path $OutDir 'evidence-sign-inline.codex'
    $signSrcText = @"
Chapter: EvidenceSignInline
  cites Foreword chapter Console
  cites Foreword chapter Ed25519
  cites Foreword chapter Sha512
Section: Helpers
  bytes-to-hex : List Integer, Integer, Integer, Text -> Text
  bytes-to-hex (bs) (i) (len) (acc) =
    if i >= len then acc
    else bytes-to-hex bs (i + 1) len (acc & byte-hex (list-at bs i))
  byte-hex : Integer -> Text
  byte-hex (b) = nib-hex (b / 16) & nib-hex (b - (b / 16) * 16)
  nib-hex : Integer -> Text
  nib-hex (n) = if n < 10 then show n else if n == 10 then "a" else if n == 11 then "b" else if n == 12 then "c" else if n == 13 then "d" else if n == 14 then "e" else "f"
Section: Body
  opening : [Console] Nothing = act
    let key = [$keyList]
    in let hash = [$hashList]
    in let pub = ed25519-public-key key
    in let sig = ed25519-sign key pub hash
    in act
      print-line-uni ("pub " & bytes-to-hex pub 0 32 "")
      print-line-uni ("sig " & bytes-to-hex sig 0 64 "")
    end
  end
"@
    [IO.File]::WriteAllText($signSrc, $signSrcText, $utf8)
    $signCdx = Join-Path $OutDir 'evidence-sign.cdx'
    $signLog = Join-Path $OutDir 'evidence-sign.log'
    $signOut = Join-Path $OutDir 'evidence-sign.out'
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $signSrc -Out $signCdx -Log $signLog 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: signer compile; see $signLog"); exit 8 }
    & pwsh -NoProfile -File (Join-Path $Repo 'build\test-run.ps1') -Kernel $signCdx -OutFile $signOut 2>&1 | Out-Null
    $sigLines = @(Get-Content $signOut -ErrorAction SilentlyContinue)
    $pub = ($sigLines | Where-Object { $_ -like 'pub *' } | Select-Object -First 1)
    $sig = ($sigLines | Where-Object { $_ -like 'sig *' } | Select-Object -First 1)
    if (-not $pub -or -not $sig) { [Console]::Error.WriteLine('FAIL: signer produced no signature'); exit 8 }
    [IO.File]::WriteAllText((Join-Path $OutDir 'Evidence.sig'), "sha256 $pkgHash`n$pub`n$sig`n", $utf8)
    Remove-Item $signSrc, $signCdx, $signLog, $signOut -Force -ErrorAction SilentlyContinue
}
$claimed = ([regex]::Matches($cdxe, 'status=claimed')).Count
$total = ([regex]::Matches($cdxe, '^claim ', 'Multiline')).Count

# ---- record the package in a fact store
if ($FactImage) {
    if (-not (Test-Path -PathType Leaf $FactImage)) { [Console]::Error.WriteLine("MISSING: -FactImage $FactImage"); exit 9 }
    $ingest = Join-Path $PSScriptRoot 'build-output\fact-ingest.cdx'
    if (-not (Test-Path $ingest)) { [Console]::Error.WriteLine("MISSING: $ingest (run codex/plugs/evidence/build.ps1)"); exit 9 }
    $hdrHash = ([regex]::Match($cdxe, '(?m)^binary\.header-hash=([0-9a-f]*)')).Groups[1].Value
    $fileHash = ([regex]::Match($cdxe, '(?m)^binary\.file-sha256=([0-9a-f]*)')).Groups[1].Value
    $ts = if ($Timestamp -gt 0) { $Timestamp } else { [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() }
    $content = "compliance-evidence product=$Product board=$Board firmware=$hdrHash file=$fileHash package=$pkgHash claimed=$claimed of=$total$(if ($SigningKey) { ' signed=y' } else { ' signed=n' })"
    $fbuf = [Collections.Generic.List[byte]]::new()
    foreach ($l in @("timestamp $ts", "content $content", 'END')) {
        foreach ($ch in $l.ToCharArray()) { $u = [int]$ch; if ($u -lt 256) { $fbuf.Add([byte]$script:UnicodeToCce[$u]) } else { $fbuf.Add([byte]$script:UnicodeToCce[63]) } }
        $fbuf.Add([byte]1)
    }
    $fbuf.Add([byte]0)
    $fin = [IO.Path]::GetTempFileName(); [IO.File]::WriteAllBytes($fin, $fbuf.ToArray())
    $fout = [IO.Path]::GetTempFileName(); $ferr = [IO.Path]::GetTempFileName()
    Set-ItemProperty $FactImage -Name IsReadOnly -Value $false
    $fp = Start-Process -FilePath $vmBin -ArgumentList @('-kernel', $ingest, '-input', $fin, '-output', $fout, '-disk', (Resolve-Path $FactImage).Path, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $ferr
    $fp.WaitForExit($TimeoutSec * 1000) | Out-Null
    if (-not $fp.HasExited) { Stop-Process -Id $fp.Id -Force; [Console]::Error.WriteLine('FAIL: ingest timeout'); exit 10 }
    $fres = @(if (Test-Path $fout) { Get-Content $fout | Where-Object { $_ -like 'FACT *' } | Select-Object -First 1 })
    foreach ($tmp in @($fin, $fout, $ferr)) { if (Test-Path $tmp) { [IO.File]::Delete($tmp) } }
    $fline = if ($fres.Count -gt 0) { $fres[0] } else { 'FACT refused no answer from the ingest program' }
    [IO.File]::WriteAllText((Join-Path $OutDir 'Evidence.fact.txt'), "image $FactImage`n$fline`ncontent $content`n", $utf8)
    if (-not $fline.StartsWith('FACT ok')) { [Console]::Error.WriteLine("FAIL: $fline"); exit 11 }
    Write-Host "[evidence] $fline"
}
Write-Host "[evidence] $OutDir : $claimed of $total requirements claimed against an artifact; package sha256 $pkgHash$(if ($SigningKey) { ' (signed)' } else { '' })"
exit 0
