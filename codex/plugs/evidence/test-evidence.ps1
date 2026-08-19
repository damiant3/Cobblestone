# The evidence plug's arms, including the ones that must refuse:
#
#   codex/plugs/evidence/test-evidence.ps1            # build the plug, then every arm
#   codex/plugs/evidence/test-evidence.ps1 -SkipBuild
#
#   self       the plug's own build as the subject (its CDX, build.log, bundled
#              source): three documents, EVIDENCE1..END, every requirement in
#              the four catalogs has one claim line, some claimed, the summary
#              rows count them, SBOM lists every chapter
#   stable     the same inputs run twice give a byte-identical Evidence.cdxe
#              (ComplianceEvidence.md constraint 1)
#   no-log     no -Log: log.present=n and every compile-log / effect-types
#              claim is not-claimed with the reason; binary claims unchanged
#   not-cdx    a text file as -Cdx: binary.present=n, every cdx-* claim is
#              not-claimed, no claim names a hash it does not have
#   dirty-log  a log with an injected `error CDX2031:` line: effect-types
#              flips to not-claimed while compile-log stays claimed with
#              errors=1 in its note (the checker's refusal is what the claim
#              cites, so a red build must not read as a clean one)
[CmdletBinding()]
param([switch]$SkipBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $Here '..' '..' '..')).Path
$Work = Join-Path ([IO.Path]::GetTempPath()) ("evidence-test-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

if (-not $SkipBuild) {
    & pwsh -NoProfile -File (Join-Path $Here 'build.ps1')
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: plug build'; exit 1 }
}
$cdx = Join-Path $Here 'build-output\evidence-plug.cdx'
$log = Join-Path $Here 'build-output\build.log'
$src = Join-Path $Here 'build-output\plug-source.codex'
foreach ($f in @($cdx, $log, $src)) { if (-not (Test-Path $f)) { Write-Host "FAIL: $f missing (build first)"; exit 1 } }

function Run-Plug([string]$name, [string[]]$extra) {
    $out = Join-Path $Work $name
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    $a = @('-OutDir', $out, '-Product', 'evidence-plug', '-Board', 'x86-64') + $extra
    & pwsh -NoProfile -File (Join-Path $Here 'run.ps1') @a | Out-Null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}
function Cdxe([string]$dir) { if ($dir -and (Test-Path (Join-Path $dir 'Evidence.cdxe'))) { return @(Get-Content (Join-Path $dir 'Evidence.cdxe')) } else { return @() } }
function Count([string[]]$rows, [string]$pattern) { return @($rows | Where-Object { $_ -match $pattern }).Count }

$expected = [ordered]@{ self = 'three docs, 61 claims, some claimed'; stable = 'byte-identical rerun'; 'no-log' = 'log claims not-claimed'; 'not-cdx' = 'cdx claims not-claimed'; 'dirty-log' = 'effect-types not-claimed, errors counted'; board = 'the residual adjusts by SoC'; 'fact-ingest' = 'the package lands as a fact, once'; 'no-store' = 'a diskless image refuses' }
$actual = [ordered]@{}

# self
$d = Run-Plug 'self' @('-Cdx', $cdx, '-Log', $log, '-Source', $src)
$rows = Cdxe $d
if (-not $d) { $actual['self'] = '(run failed)' }
elseif (-not (Test-Path (Join-Path $d 'Evidence.html')) -or -not (Test-Path (Join-Path $d 'SBOM.cdx.json'))) { $actual['self'] = 'a document is missing' }
elseif ($rows[0] -ne 'EVIDENCE1' -or $rows[-1] -ne 'END') { $actual['self'] = "framing: first [$($rows[0])] last [$($rows[-1])]" }
else {
    $claims = Count $rows '^claim '
    $claimed = Count $rows '^claim .* status=claimed '
    $chapters = Count $rows '^chapter '
    $sbomComps = ([regex]::Matches((Get-Content (Join-Path $d 'SBOM.cdx.json') -Raw), '"type": "file"')).Count
    $sum = Count $rows '^summary reg='
    if ($claims -ne 61) { $actual['self'] = "claims=$claims (catalogs hold 61)" }
    elseif ($claimed -lt 1) { $actual['self'] = 'nothing claimed against the plug''s own build' }
    elseif ($sum -ne 4) { $actual['self'] = "summary rows=$sum" }
    elseif ($chapters -lt 2 -or $sbomComps -ne $chapters) { $actual['self'] = "manifest chapters=$chapters sbom components=$sbomComps" }
    elseif ((Count $rows '^binary.present=y') -ne 1 -or (Count $rows '^log.present=y') -ne 1) { $actual['self'] = 'presence rows wrong' }
    else { $actual['self'] = $expected['self'] }
}

# stable
$d2 = Run-Plug 'self2' @('-Cdx', $cdx, '-Log', $log, '-Source', $src)
if ($d -and $d2) {
    $h1 = (Get-FileHash (Join-Path $d 'Evidence.cdxe')).Hash; $h2 = (Get-FileHash (Join-Path $d2 'Evidence.cdxe')).Hash
    $actual['stable'] = if ($h1 -eq $h2) { $expected['stable'] } else { 'the two runs differ' }
} else { $actual['stable'] = '(run failed)' }

# no-log
$d3 = Run-Plug 'nolog' @('-Cdx', $cdx, '-Source', $src)
$r3 = Cdxe $d3
if (-not $d3) { $actual['no-log'] = '(run failed)' }
elseif ((Count $r3 '^log.present=n') -ne 1) { $actual['no-log'] = 'log.present not n' }
elseif ((Count $r3 '^claim .* status=claimed artifact=log:') -ne 0) { $actual['no-log'] = 'a log claim survived without a log' }
elseif ((Count $r3 '^claim .* status=claimed artifact=cdx:') -lt 1) { $actual['no-log'] = 'binary claims went missing too' }
else { $actual['no-log'] = $expected['no-log'] }

# not-cdx
$d4 = Run-Plug 'notcdx' @('-Cdx', $log, '-Log', $log, '-Source', $src)
$r4 = Cdxe $d4
if (-not $d4) { $actual['not-cdx'] = '(run failed)' }
elseif ((Count $r4 '^binary.present=n') -ne 1) { $actual['not-cdx'] = 'binary.present not n' }
elseif ((Count $r4 '^claim .* status=claimed artifact=(cdx|hash):') -ne 0) { $actual['not-cdx'] = 'a binary claim survived without a CDX' }
elseif ((Count $r4 '^claim .* status=claimed artifact=log:') -lt 1) { $actual['not-cdx'] = 'log claims went missing too' }
else { $actual['not-cdx'] = $expected['not-cdx'] }

# dirty-log
$dirty = Join-Path $Work 'dirty.log'
$dl = @(Get-Content $log) + @('D:\x\Foo.codex:12:3: error CDX2031: an effect performed without being declared')
[IO.File]::WriteAllText($dirty, (($dl -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
$d5 = Run-Plug 'dirty' @('-Cdx', $cdx, '-Log', $dirty, '-Source', $src)
$r5 = Cdxe $d5
if (-not $d5) { $actual['dirty-log'] = '(run failed)' }
elseif ((Count $r5 '^log.errors=1') -ne 1) { $actual['dirty-log'] = "log.errors row: $($r5 | Where-Object { $_ -like 'log.errors=*' })" }
elseif ((Count $r5 '^log.CDX2031=1') -ne 1) { $actual['dirty-log'] = 'CDX2031 not counted' }
else {
    $eff = @($r5 | Where-Object { $_ -match '^claim .* status=claimed .*note="the effect checker passed' })
    $cl = @($r5 | Where-Object { $_ -match '^claim .* status=claimed artifact=log:.*errors=1 ' })
    if ($eff.Count -ne 0) { $actual['dirty-log'] = 'effect-types still claimed on a red log' }
    elseif ($cl.Count -lt 1) { $actual['dirty-log'] = 'compile-log claim does not carry errors=1' }
    else { $actual['dirty-log'] = $expected['dirty-log'] }
}

# board: the per-board residual adjusts by SoC. ESP32-C6 anchors flash
# encryption at the hardware; STM32F4 does not; an unnamed board is "unknown"
# rather than assumed either way. The claims must not change with the board.
# Run-Plug fixes -Board x86-64; these need their own board, so call run.ps1
# directly rather than through it.
function Run-Board([string]$name, [string]$board) {
    $out = Join-Path $Work $name
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    & pwsh -NoProfile -File (Join-Path $Here 'run.ps1') -OutDir $out -Product 'evidence-plug' -Board $board -Cdx $cdx -Log $log -Source $src | Out-Null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}
$bEsp = Run-Board 'esp' 'esp32-c6'
$bStm = Run-Board 'stm' 'stm32f4'
$bUnk = Run-Board 'unk' 'mystery-soc'
$rE = Cdxe $bEsp; $rS = Cdxe $bStm; $rU = Cdxe $bUnk
if (-not $bEsp -or -not $bStm -or -not $bUnk) { $actual['board'] = '(run failed)' }
elseif ((Count $rE '^board\.flash-encryption=yes') -ne 1) { $actual['board'] = 'esp32-c6 flash encryption not anchored' }
elseif ((Count $rS '^board\.flash-encryption=no') -ne 1) { $actual['board'] = 'stm32f4 flash encryption not left to the manufacturer' }
elseif ((Count $rU '^board\.known=n') -ne 1) { $actual['board'] = 'an unknown board was not reported unknown' }
else {
    # the claims are identical regardless of board (only the board.* lines move)
    $claimsE = @($rE | Where-Object { $_ -match '^claim ' })
    $claimsS = @($rS | Where-Object { $_ -match '^claim ' })
    if (($claimsE -join "`n") -ne ($claimsS -join "`n")) { $actual['board'] = 'the board changed a claim, which it must not' }
    else { $actual['board'] = $expected['board'] }
}

# fact-ingest: the package lands in a fact store image as one kind-50 fact,
# and a second run of the same package does not double it (idempotent by
# content). A GPT image with a fact partition is what build/build-img.ps1
# makes; build/boot/diag.img is one, carried in the depot.
$diag = Join-Path $Repo 'build\boot\diag.img'
if (-not (Test-Path $diag)) { $actual['fact-ingest'] = '(skipped: build/boot/diag.img missing)'; $actual['no-store'] = '(skipped)' }
else {
    $img = Join-Path $Work 'facts.img'
    Copy-Item $diag $img -Force; Set-ItemProperty $img -Name IsReadOnly -Value $false
    $c1 = $c2 = ''
    foreach ($n in 1, 2) {
        $o = Join-Path $Work "fi$n"
        & pwsh -NoProfile -File (Join-Path $Here 'run.ps1') -Cdx $cdx -Log $log -Source $src -Product evidence-plug -Board x86-64 -OutDir $o -FactImage $img -Timestamp 1755500000 *> $null
        $line = if (Test-Path (Join-Path $o 'Evidence.fact.txt')) { (Get-Content (Join-Path $o 'Evidence.fact.txt') | Where-Object { $_ -like 'FACT *' } | Select-Object -First 1) } else { '' }
        if ($n -eq 1) { $c1 = $line } else { $c2 = $line }
    }
    if (-not $c1.StartsWith('FACT ok') -or $c1 -notmatch 'count=1 head=3') { $actual['fact-ingest'] = "first write: $c1" }
    elseif ($c1 -match 'already present') { $actual['fact-ingest'] = 'first write reported already present' }
    elseif (-not $c2.StartsWith('FACT ok') -or $c2 -notmatch 'already present' -or $c2 -notmatch 'count=1 ') { $actual['fact-ingest'] = "second write not idempotent: $c2" }
    else { $actual['fact-ingest'] = $expected['fact-ingest'] }

    # no-store: a PARTITIONED image with no fact partition. An MBR signature at
    # 0x1FE with no GPT facts partition resolves the region to refuse (base < 0),
    # disk-load is unusable, and the write refuses by name. (A raw, unpartitioned
    # image is a legitimate whole-disk store and would NOT refuse, which is why
    # the arm partitions it.)
    $bad2 = Join-Path $Work 'nostore.img'
    $nb = New-Object byte[] 65536; $nb[510] = 0x55; $nb[511] = 0xAA
    [IO.File]::WriteAllBytes($bad2, $nb); Set-ItemProperty $bad2 -Name IsReadOnly -Value $false
    $o = Join-Path $Work 'nostore'
    $out = & pwsh -NoProfile -File (Join-Path $Here 'run.ps1') -Cdx $cdx -Log $log -Source $src -Product evidence-plug -Board x86-64 -OutDir $o -FactImage $bad2 -Timestamp 1755500000 2>&1
    $ec = $LASTEXITCODE
    $fl = if (Test-Path (Join-Path $o 'Evidence.fact.txt')) { (Get-Content (Join-Path $o 'Evidence.fact.txt') | Where-Object { $_ -like 'FACT *' } | Select-Object -First 1) } else { '' }
    if ($ec -eq 0) { $actual['no-store'] = 'a diskless image did not fail the run' }
    elseif ($fl -notmatch 'refused') { $actual['no-store'] = "no refusal recorded: $fl" }
    else { $actual['no-store'] = $expected['no-store'] }
}

$bad = 0
Write-Host ''
Write-Host 'arm        expected                                  actual'
foreach ($k in $expected.Keys) {
    $ok = ($actual[$k] -eq $expected[$k]); if (-not $ok) { $bad++ }
    Write-Host ("{0,-10} {1,-40}  {2}" -f $k, $expected[$k], $(if ($ok) { 'ok' } else { "MISMATCH: $($actual[$k])" }))
}
Write-Host ''
if ($bad -gt 0) { Write-Host "EVIDENCE PLUG: $bad arm(s) disagree"; exit 1 }
Write-Host 'Evidence plug: every arm answered as it should.'
exit 0
